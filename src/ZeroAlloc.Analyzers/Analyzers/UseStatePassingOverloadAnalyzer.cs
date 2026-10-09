using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace ZeroAlloc.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class UseStatePassingOverloadAnalyzer : DiagnosticAnalyzer
{
    public const string FixProperty = "Fix";
    public const string CapturedProperty = "Captured";
    public const string GetOrAddFix = "GetOrAdd";
    public const string QueueUserWorkItemFix = "QueueUserWorkItem";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticIds.UseStatePassingOverload,
        "Use the state-passing overload instead of a capturing lambda",
        "Lambda passed to '{0}' captures {1}; use the overload that passes state so the lambda can be static",
        DiagnosticCategories.Delegates,
        DiagnosticSeverity.Info,
        isEnabledByDefault: true);

    // Each API, the name of the state parameter its state-passing overload takes, and the code fix
    // that applies to it, if any. Adding an API here is all it takes to cover it.
    private static readonly (string Type, string Method, string StateParameter, string? Fix)[] ApiTable =
    [
        ("System.Collections.Concurrent.ConcurrentDictionary`2", "GetOrAdd", "factoryArgument", GetOrAddFix),
        ("System.Collections.Concurrent.ConcurrentDictionary`2", "AddOrUpdate", "factoryArgument", null),
        ("System.Threading.CancellationToken", "Register", "state", null),
        ("System.Threading.CancellationToken", "UnsafeRegister", "state", null),
        ("System.Threading.ThreadPool", "QueueUserWorkItem", "state", QueueUserWorkItemFix),
        ("System.Threading.ThreadPool", "UnsafeQueueUserWorkItem", "state", QueueUserWorkItemFix),
        ("System.Threading.Tasks.TaskFactory", "StartNew", "state", null),
        ("System.String", "Create", "state", null),
    ];

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [Rule];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(static start =>
        {
            var apis = ResolveApis(start.Compilation);
            if (apis.IsEmpty)
                return;

            start.RegisterOperationAction(context => AnalyzeInvocation(context, apis), OperationKind.Invocation);
        });
    }

    private static ImmutableArray<Api> ResolveApis(Compilation compilation)
    {
        var builder = ImmutableArray.CreateBuilder<Api>();
        foreach (var (typeName, methodName, stateParameter, fix) in ApiTable)
        {
            if (compilation.GetTypeByMetadataName(typeName) is not { } type)
                continue;

            var overloads = type.GetMembers(methodName).OfType<IMethodSymbol>().ToImmutableArray();

            // Only APIs whose state-passing overload exists in this compilation.
            if (!overloads.Any(m => m.Parameters.Any(p => p.Name == stateParameter)))
                continue;

            // The QueueUserWorkItem fix needs the generic overload with a typed state, added in .NET Core 3.0.
            var usableFix = fix == QueueUserWorkItemFix && !overloads.Any(m => m.IsGenericMethod) ? null : fix;
            builder.Add(new Api(type, methodName, usableFix));
        }

        return builder.ToImmutable();
    }

    private static void AnalyzeInvocation(OperationAnalysisContext context, ImmutableArray<Api> apis)
    {
        var invocation = (IInvocationOperation)context.Operation;
        var method = invocation.TargetMethod;
        var api = apis.FirstOrDefault(a => a.Method == method.Name
            && SymbolEqualityComparer.Default.Equals(method.ContainingType.OriginalDefinition, a.Type));
        if (api is null)
            return;

        foreach (var argument in invocation.Arguments)
        {
            if (argument.Value is not IDelegateCreationOperation { Target: IAnonymousFunctionOperation lambda }
                || lambda.Symbol.IsStatic)
            {
                continue;
            }

            var captures = Captures(lambda);
            if (captures.Variables.IsEmpty && !captures.This)
                continue;

            var names = captures.Variables.Select(v => $"'{v.Name}'");
            if (captures.This)
                names = names.Prepend("'this'");

            var properties = ImmutableDictionary<string, string?>.Empty;
            if (FixFor(api, invocation, argument, lambda, captures) is { } fix)
            {
                properties = properties
                    .Add(FixProperty, fix)
                    .Add(CapturedProperty, captures.Variables[0].Name);
            }

            context.ReportDiagnostic(Diagnostic.Create(
                Rule,
                lambda.Syntax.GetLocation(),
                properties,
                $"{method.ContainingType.Name}.{method.Name}",
                string.Join(", ", names)));
        }
    }

    // Locals and parameters used inside the lambda but declared outside it, in order of first use,
    // plus whether it uses 'this'.
    private static Captured Captures(IAnonymousFunctionOperation lambda)
    {
        var span = lambda.Syntax.Span;
        var variables = ImmutableArray.CreateBuilder<ISymbol>();
        var capturesThis = false;

        foreach (var operation in lambda.Body.Descendants())
        {
            ISymbol? symbol = operation switch
            {
                ILocalReferenceOperation local => local.Local,
                IParameterReferenceOperation parameter => parameter.Parameter,
                _ => null,
            };

            if (operation is IInstanceReferenceOperation { ReferenceKind: InstanceReferenceKind.ContainingTypeInstance })
                capturesThis = true;

            if (symbol is not null
                && !symbol.DeclaringSyntaxReferences.Any(r => span.Contains(r.Span))
                && !variables.Contains(symbol, SymbolEqualityComparer.Default))
            {
                variables.Add(symbol);
            }
        }

        return new Captured(variables.ToImmutable(), capturesThis);
    }

    // The code fix applies only when the rewrite keeps the meaning: one captured variable that is
    // never written, an implicitly typed lambda, and the overload shape the fix knows how to rewrite.
    private static string? FixFor(Api api, IInvocationOperation invocation, IArgumentOperation argument, IAnonymousFunctionOperation lambda, Captured captures)
    {
        if (api.Fix is null
            || captures.This
            || captures.Variables.Length != 1
            || captures.Variables[0] is not (ILocalSymbol { IsRef: false } or IParameterSymbol { RefKind: RefKind.None })
            || !IsImplicitlyTyped(lambda.Syntax)
            || IsWrittenAnywhere(invocation, captures.Variables[0]))
        {
            return null;
        }

        var method = invocation.TargetMethod;
        return api.Fix switch
        {
            GetOrAddFix when method.Parameters.Length == 2 && argument.Parameter?.Name == "valueFactory" => GetOrAddFix,
            QueueUserWorkItemFix when method.Parameters.Length == 1 && !UsesOwnParameter(lambda) => QueueUserWorkItemFix,
            _ => null,
        };
    }

    private static bool IsImplicitlyTyped(SyntaxNode lambda) => lambda switch
    {
        SimpleLambdaExpressionSyntax => true,
        ParenthesizedLambdaExpressionSyntax parenthesized => parenthesized.ParameterList.Parameters.All(p => p.Type is null),
        _ => false,
    };

    private static bool UsesOwnParameter(IAnonymousFunctionOperation lambda) =>
        lambda.Body.Descendants().OfType<IParameterReferenceOperation>()
            .Any(p => SymbolEqualityComparer.Default.Equals(p.Parameter.ContainingSymbol, lambda.Symbol));

    // A closure sees writes made after it is created; a state argument is a snapshot. Any write to the
    // variable in the method body makes the rewrite unsafe.
    private static bool IsWrittenAnywhere(IOperation operation, ISymbol variable)
    {
        var root = operation;
        while (root.Parent is not null)
            root = root.Parent;

        foreach (var reference in root.Descendants())
        {
            var referenced = reference switch
            {
                ILocalReferenceOperation local => local.Local,
                IParameterReferenceOperation parameter => (ISymbol)parameter.Parameter,
                _ => null,
            };

            if (referenced is null || !SymbolEqualityComparer.Default.Equals(referenced, variable))
                continue;

            switch (reference.Parent)
            {
                case ISimpleAssignmentOperation assignment when assignment.Target == reference:
                case ICompoundAssignmentOperation compound when compound.Target == reference:
                case ICoalesceAssignmentOperation coalesce when coalesce.Target == reference:
                case IIncrementOrDecrementOperation:
                case IArgumentOperation { Parameter.RefKind: RefKind.Ref or RefKind.Out }:
                case ITupleOperation { Parent: IDeconstructionAssignmentOperation }:
                    return true;
            }
        }

        return false;
    }

    private sealed class Api(INamedTypeSymbol type, string method, string? fix)
    {
        public INamedTypeSymbol Type { get; } = type;

        public string Method { get; } = method;

        public string? Fix { get; } = fix;
    }

    private sealed class Captured(ImmutableArray<ISymbol> variables, bool capturesThis)
    {
        public ImmutableArray<ISymbol> Variables { get; } = variables;

        public bool This { get; } = capturesThis;
    }
}
