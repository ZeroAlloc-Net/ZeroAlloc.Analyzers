using System.Collections.Concurrent;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace ZeroAlloc.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class PreferParamsSpanAnalyzer : DiagnosticAnalyzer
{
    public const string ExposedProperty = "Exposed";

    private const string ExposedSuffix = "; it is visible outside the assembly, so add an overload instead of changing it";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticIds.PreferParamsSpan,
        "Declare params as ReadOnlySpan<T>",
        "params parameter '{0}' of '{1}' can be 'params ReadOnlySpan<{2}>' so callers do not allocate{3}",
        DiagnosticCategories.Collections,
        DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        customTags: WellKnownDiagnosticTags.CompilationEnd);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [Rule];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.Analyze);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(static start =>
        {
            if (start.Compilation is not CSharpCompilation { LanguageVersion: >= LanguageVersion.CSharp13 })
                return;

            var readOnlySpan = start.Compilation.GetTypeByMetadataName("System.ReadOnlySpan`1");

            // A params span only avoids the allocation through an inline array, which needs .NET 8.
            var inlineArray = start.Compilation.GetTypeByMetadataName("System.Runtime.CompilerServices.InlineArrayAttribute");
            if (readOnlySpan is null || inlineArray is null)
                return;

            // Friend assemblies can call internal methods, where this rule cannot see the call.
            var hasFriendAssemblies = start.Compilation.Assembly.GetAttributes().Any(a =>
                a.AttributeClass is { Name: "InternalsVisibleToAttribute" } attribute
                && attribute.ContainingNamespace.ToDisplayString() == "System.Runtime.CompilerServices");

            var expressionTreeBase = start.Compilation.GetTypeByMetadataName("System.Linq.Expressions.LambdaExpression");

            var candidates = new ConcurrentDictionary<IMethodSymbol, bool>(SymbolEqualityComparer.Default);

            // Methods whose parameter type is fixed by a method group, an interface or a caller.
            var excluded = new ConcurrentDictionary<IMethodSymbol, bool>(SymbolEqualityComparer.Default);

            // A method group converted to a delegate fixes the parameter type.
            start.RegisterOperationAction(context =>
            {
                var reference = (IMethodReferenceOperation)context.Operation;
                excluded[reference.Method.OriginalDefinition] = true;
            }, OperationKind.MethodReference);

            // A caller that passes a covariant array or null, or an expression tree, cannot call the span form.
            start.RegisterOperationAction(context =>
            {
                var operation = context.Operation;
                var (target, arguments) = operation switch
                {
                    IInvocationOperation invocation => (invocation.TargetMethod, invocation.Arguments),
                    IObjectCreationOperation creation when creation.Constructor is not null => (creation.Constructor, creation.Arguments),
                    _ => default,
                };

                if (target is null || target.Parameters.IsEmpty || !target.Parameters[target.Parameters.Length - 1].IsParams)
                    return;

                if (IsInExpressionTree(operation, expressionTreeBase) || PassesArrayOfOtherType(arguments))
                    excluded[target.OriginalDefinition] = true;
            }, OperationKind.Invocation, OperationKind.ObjectCreation);

            // An interface member can be implemented by a method a base class declares.
            start.RegisterSymbolAction(context =>
            {
                var type = (INamedTypeSymbol)context.Symbol;
                foreach (var iface in type.AllInterfaces)
                {
                    foreach (var member in iface.GetMembers().OfType<IMethodSymbol>())
                    {
                        if (type.FindImplementationForInterfaceMember(member) is IMethodSymbol implementation)
                            excluded[implementation.OriginalDefinition] = true;
                    }
                }
            }, SymbolKind.NamedType);

            start.RegisterOperationBlockAction(context =>
            {
                if (context.OwningSymbol is IMethodSymbol method
                    && IsCandidate(method)
                    && !method.DeclaringSyntaxReferences.Any(reference => GeneratedCode.IsGenerated(reference.SyntaxTree))
                    && UsesAreSpanCompatible(context.OperationBlocks, method.Parameters[method.Parameters.Length - 1], readOnlySpan))
                {
                    candidates[method] = true;
                }
            });

            start.RegisterCompilationEndAction(context =>
            {
                foreach (var method in candidates.Keys)
                {
                    if (excluded.ContainsKey(method))
                        continue;

                    var parameter = method.Parameters[method.Parameters.Length - 1];
                    var exposed = IsVisibleOutsideAssembly(method, hasFriendAssemblies);
                    var properties = ImmutableDictionary<string, string?>.Empty
                        .Add(ExposedProperty, exposed ? "true" : "false");

                    context.ReportDiagnostic(Diagnostic.Create(
                        Rule,
                        parameter.Locations[0],
                        properties,
                        parameter.Name,
                        method.Name,
                        ((IArrayTypeSymbol)parameter.Type).ElementType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
                        exposed ? ExposedSuffix : string.Empty));
                }
            });
        });
    }

    private static bool IsCandidate(IMethodSymbol method)
    {
        if (method.MethodKind is not (MethodKind.Ordinary or MethodKind.Constructor)
            || method.Parameters.IsEmpty
            || method.Parameters[method.Parameters.Length - 1] is not { IsParams: true, Type: IArrayTypeSymbol { Rank: 1 } })
        {
            return false;
        }

        // A primary constructor parameter is captured by member bodies this rule does not scan.
        if (method.MethodKind == MethodKind.Constructor
            && method.DeclaringSyntaxReferences.Any(reference => reference.GetSyntax() is TypeDeclarationSyntax))
        {
            return false;
        }

        // The signature is fixed by another declaration, or a span parameter is not allowed.
        if (method.IsOverride || method.IsVirtual || method.IsAbstract || method.IsExtern
            || method.IsAsync || method.IsIterator
            || method.PartialDefinitionPart is not null || method.PartialImplementationPart is not null
            || !method.ExplicitInterfaceImplementations.IsEmpty)
        {
            return false;
        }

        return true;
    }

    private static bool UsesAreSpanCompatible(ImmutableArray<IOperation> blocks, IParameterSymbol parameter, INamedTypeSymbol readOnlySpan)
    {
        foreach (var block in blocks)
        {
            foreach (var reference in block.Descendants().OfType<IParameterReferenceOperation>())
            {
                if (!SymbolEqualityComparer.Default.Equals(reference.Parameter, parameter))
                    continue;

                // A span cannot be captured by a lambda or local function.
                for (var current = reference.Parent; current is not null; current = current.Parent)
                {
                    if (current is IAnonymousFunctionOperation or ILocalFunctionOperation)
                        return false;
                }

                if (!ReadOnlySpanUse.IsCompatible(reference, readOnlySpan))
                    return false;
            }
        }

        return true;
    }

    // Normal form with an array whose type is not exactly the parameter type: a covariant array, or null.
    private static bool PassesArrayOfOtherType(ImmutableArray<IArgumentOperation> arguments)
    {
        foreach (var argument in arguments)
        {
            if (argument.Parameter is not { IsParams: true } parameter || argument.ArgumentKind != ArgumentKind.Explicit)
                continue;

            var value = argument.Value;
            while (value is IConversionOperation { IsImplicit: true } conversion)
                value = conversion.Operand;

            if (value.Type is null || !SymbolEqualityComparer.Default.Equals(value.Type, parameter.Type))
                return true;
        }

        return false;
    }

    // An expression tree cannot call a method with a params span.
    private static bool IsInExpressionTree(IOperation operation, INamedTypeSymbol? lambdaExpression)
    {
        if (lambdaExpression is null)
            return false;

        for (var current = operation.Parent; current is not null; current = current.Parent)
        {
            if (current is IAnonymousFunctionOperation { Parent: IConversionOperation { Type: { } type } })
            {
                for (var baseType = type; baseType is not null; baseType = baseType.BaseType)
                {
                    if (SymbolEqualityComparer.Default.Equals(baseType, lambdaExpression))
                        return true;
                }
            }
        }

        return false;
    }

    private static bool IsVisibleOutsideAssembly(ISymbol symbol, bool hasFriendAssemblies)
    {
        for (var current = symbol; current is not null and not INamespaceSymbol; current = current.ContainingSymbol)
        {
            var visible = current.DeclaredAccessibility is Accessibility.Public or Accessibility.Protected or Accessibility.ProtectedOrInternal
                || hasFriendAssemblies && current.DeclaredAccessibility is Accessibility.Internal or Accessibility.ProtectedAndInternal;
            if (!visible)
                return false;
        }

        return true;
    }
}
