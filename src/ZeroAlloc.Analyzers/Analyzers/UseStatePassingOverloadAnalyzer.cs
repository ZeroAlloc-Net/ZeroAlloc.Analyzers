using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
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

    // Each API, the names of the callback parameters whose lambdas can capture, the name of the state
    // parameter its state-passing overload takes, and the code fix that applies to it, if any.
    // Adding an API here is all it takes to cover it.
    private static readonly (string Type, string Method, string[] Callbacks, string StateParameter, string? Fix)[] ApiTable =
    [
        ("System.Collections.Concurrent.ConcurrentDictionary`2", "GetOrAdd", ["valueFactory"], "factoryArgument", GetOrAddFix),
        ("System.Collections.Concurrent.ConcurrentDictionary`2", "AddOrUpdate", ["addValueFactory", "updateValueFactory"], "factoryArgument", null),
        ("System.Threading.CancellationToken", "Register", ["callback"], "state", null),
        ("System.Threading.CancellationToken", "UnsafeRegister", ["callback"], "state", null),
        ("System.Threading.ThreadPool", "QueueUserWorkItem", ["callBack"], "state", QueueUserWorkItemFix),
        ("System.Threading.ThreadPool", "UnsafeQueueUserWorkItem", ["callBack"], "state", null),
        ("System.Threading.Tasks.TaskFactory", "StartNew", ["action", "function"], "state", null),
        ("System.String", "Create", ["action"], "state", null),
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
        foreach (var (typeName, methodName, callbacks, stateParameter, fix) in ApiTable)
        {
            if (compilation.GetTypeByMetadataName(typeName) is not { } type)
                continue;

            var overloads = type.GetMembers(methodName).OfType<IMethodSymbol>().ToImmutableArray();

            // Only APIs whose state-passing overload exists in this compilation.
            if (!overloads.Any(m => m.Parameters.Any(p => p.Name == stateParameter)))
                continue;

            // The QueueUserWorkItem fix needs the generic overload with a typed state, added in .NET Core 3.0.
            var usableFix = fix == QueueUserWorkItemFix && !overloads.Any(m => m.IsGenericMethod) ? null : fix;
            builder.Add(new Api(type, methodName, callbacks, usableFix));
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
            if (argument.Parameter is not { } parameter
                || !api.Callbacks.Contains(parameter.Name)
                || argument.Value is not IDelegateCreationOperation { Target: IAnonymousFunctionOperation lambda }
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
    // plus whether it uses 'this'. Constants are not captured, and a primary-constructor parameter
    // used outside the constructor's own initializers is a field of 'this' in disguise.
    private static Captured Captures(IAnonymousFunctionOperation lambda)
    {
        var span = lambda.Syntax.Span;
        var variables = ImmutableArray.CreateBuilder<ISymbol>();
        var capturesThis = false;

        foreach (var operation in lambda.Body.Descendants())
        {
            ISymbol? symbol = operation switch
            {
                ILocalReferenceOperation { Local.IsConst: false } local => local.Local,
                IParameterReferenceOperation parameter => parameter.Parameter,
                _ => null,
            };

            // Calling a local function carries an implicit receiver; whether the local function itself
            // uses 'this' is its own business.
            if (operation is IInstanceReferenceOperation { ReferenceKind: InstanceReferenceKind.ContainingTypeInstance } instance
                && !IsLocalFunctionReceiver(instance))
            {
                capturesThis = true;
            }

            if (symbol is IParameterSymbol { ContainingSymbol: IMethodSymbol { MethodKind: MethodKind.Constructor } constructor }
                && constructor.DeclaringSyntaxReferences.Any(r => r.GetSyntax() is TypeDeclarationSyntax)
                && !IsInsideInitializer(lambda))
            {
                capturesThis = true;
                continue;
            }

            if (symbol is not null
                && !symbol.DeclaringSyntaxReferences.Any(r => span.Contains(r.Span))
                && !variables.Contains(symbol, SymbolEqualityComparer.Default))
            {
                variables.Add(symbol);
            }
        }

        return new Captured(variables.ToImmutable(), capturesThis);
    }

    private static bool IsLocalFunctionReceiver(IInstanceReferenceOperation instance) => instance.Parent switch
    {
        IInvocationOperation invocation => invocation.TargetMethod.MethodKind == MethodKind.LocalFunction,
        IMethodReferenceOperation reference => reference.Method.MethodKind == MethodKind.LocalFunction,
        _ => false,
    };

    // Field and property initializers and the base-type arguments run inside the primary constructor,
    // where its parameters are real parameters.
    private static bool IsInsideInitializer(IAnonymousFunctionOperation lambda)
    {
        foreach (var node in lambda.Syntax.Ancestors())
        {
            switch (node)
            {
                case EqualsValueClauseSyntax { Parent: VariableDeclaratorSyntax { Parent.Parent: BaseFieldDeclarationSyntax } }:
                case EqualsValueClauseSyntax { Parent: PropertyDeclarationSyntax }:
                case PrimaryConstructorBaseTypeSyntax:
                    return true;
                case MemberDeclarationSyntax:
                    return false;
            }
        }

        return false;
    }

    // The code fix applies only when the rewrite keeps the meaning: one captured variable that is
    // never written, an implicitly typed lambda, and the overload shape the fix knows how to rewrite.
    private static string? FixFor(Api api, IInvocationOperation invocation, IArgumentOperation argument, IAnonymousFunctionOperation lambda, Captured captures)
    {
        if (api.Fix is null
            || captures.This
            || captures.Variables.Length != 1
            || lambda.Syntax.SyntaxTree.Options is not CSharpParseOptions { LanguageVersion: var languageVersion }
            || languageVersion.MapSpecifiedToEffectiveVersion() < LanguageVersion.CSharp9
            || captures.Variables[0] is not (ILocalSymbol { IsRef: false } or IParameterSymbol { RefKind: RefKind.None })
            || IsMutableStruct(captures.Variables[0])
            || !IsImplicitlyTyped(lambda.Syntax)
            || CallsCapturingLocalFunction(lambda)
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

    // A state argument is a copy: for a struct that could change, a closure shares the one variable.
    private static bool IsMutableStruct(ISymbol variable)
    {
        var type = variable switch
        {
            ILocalSymbol local => local.Type,
            IParameterSymbol parameter => parameter.Type,
            _ => null,
        };

        return type is { IsValueType: true, SpecialType: SpecialType.None, TypeKind: not TypeKind.Enum }
            && !type.IsReadOnly;
    }

    // A static lambda cannot call a local function that captures, and a non-static one may.
    private static bool CallsCapturingLocalFunction(IAnonymousFunctionOperation lambda)
    {
        foreach (var operation in lambda.Body.Descendants())
        {
            var target = operation switch
            {
                IInvocationOperation invocation => invocation.TargetMethod,
                IMethodReferenceOperation reference => reference.Method,
                _ => null,
            };

            if (target is { MethodKind: MethodKind.LocalFunction, IsStatic: false })
                return true;
        }

        return false;
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

            if (reference.Syntax.Parent is RefExpressionSyntax)
                return true;

            switch (reference.Parent)
            {
                case ISimpleAssignmentOperation assignment when assignment.Target == reference:
                case ICompoundAssignmentOperation compound when compound.Target == reference:
                case ICoalesceAssignmentOperation coalesce when coalesce.Target == reference:
                case IIncrementOrDecrementOperation:
                case IAddressOfOperation:
                case IArgumentOperation { Parameter.RefKind: RefKind.Ref or RefKind.Out or RefKind.In }:
                    return true;
                case ITupleOperation tuple when IsDeconstructionTarget(tuple):
                    return true;
            }
        }

        return false;
    }

    // A tuple nested in the target of a deconstruction assigns every variable it holds.
    private static bool IsDeconstructionTarget(ITupleOperation tuple)
    {
        IOperation top = tuple;
        while (top.Parent is ITupleOperation parent)
            top = parent;

        return top.Parent is IDeconstructionAssignmentOperation deconstruction && deconstruction.Target == top;
    }

    private sealed class Api(INamedTypeSymbol type, string method, string[] callbacks, string? fix)
    {
        public INamedTypeSymbol Type { get; } = type;

        public string Method { get; } = method;

        public string[] Callbacks { get; } = callbacks;

        public string? Fix { get; } = fix;
    }

    private sealed class Captured(ImmutableArray<ISymbol> variables, bool capturesThis)
    {
        public ImmutableArray<ISymbol> Variables { get; } = variables;

        public bool This { get; } = capturesThis;
    }
}
