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
            var attributeBase = start.Compilation.GetTypeByMetadataName("System.Attribute");

            var candidates = new ConcurrentDictionary<IMethodSymbol, bool>(SymbolEqualityComparer.Default);

            // Methods whose parameter type is fixed by a method group, an interface or a caller.
            var excluded = new ConcurrentDictionary<IMethodSymbol, bool>(SymbolEqualityComparer.Default);

            // Methods some caller passes an array to, in the params position.
            var arrayCalls = new ConcurrentDictionary<IMethodSymbol, bool>(SymbolEqualityComparer.Default);

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

                if (arguments.Any(argument => argument.Parameter is { IsParams: true } && argument.ArgumentKind == ArgumentKind.Explicit))
                    arrayCalls[target.OriginalDefinition] = true;
            }, OperationKind.Invocation, OperationKind.ObjectCreation);

            // An interface member can be implemented by a method a base class declares.
            start.RegisterSymbolAction(context =>
            {
                var type = (INamedTypeSymbol)context.Symbol;
                foreach (var iface in type.AllInterfaces)
                {
                    foreach (var member in iface.GetMembers().OfType<IMethodSymbol>())
                    {
                        // Only a method with a params array can be a candidate, so skip the costly lookup otherwise.
                        if (!HasParamsArray(member))
                            continue;

                        if (type.FindImplementationForInterfaceMember(member) is IMethodSymbol implementation)
                            excluded[implementation.OriginalDefinition] = true;
                    }
                }
            }, SymbolKind.NamedType);

            start.RegisterOperationBlockAction(context =>
            {
                if (context.OwningSymbol is IMethodSymbol method
                    && IsCandidate(method, attributeBase)
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

                    // The advice for an exposed method keeps the array overload, which a caller passing
                    // an array still binds to, so only the changed signature can rebind such a call.
                    var checkArrayCalls = !exposed && arrayCalls.ContainsKey(method);
                    if (HasConflictingOverload(method, checkArrayCalls, context.Compilation, readOnlySpan))
                        continue;
                    var properties = ImmutableDictionary<string, string?>.Empty
                        .Add(ExposedProperty, exposed ? "true" : "false");

                    context.ReportDiagnostic(Diagnostic.Create(
                        Rule,
                        parameter.Locations[0],
                        properties,
                        parameter.Name,
                        method.MethodKind == MethodKind.Constructor ? method.ContainingType.Name : method.Name,
                        ((IArrayTypeSymbol)parameter.Type).ElementType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
                        exposed ? ExposedSuffix : string.Empty));
                }
            });
        });
    }

    private static bool HasParamsArray(IMethodSymbol method) =>
        !method.Parameters.IsEmpty
        && method.Parameters[method.Parameters.Length - 1] is { IsParams: true, Type: IArrayTypeSymbol { Rank: 1 } };

    private static bool IsCandidate(IMethodSymbol method, INamedTypeSymbol? attributeBase)
    {
        if (method.MethodKind is not (MethodKind.Ordinary or MethodKind.Constructor) || !HasParamsArray(method))
            return false;

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

        // A params span is implicitly scoped, so it cannot leave the method through the return value,
        // a ref or out parameter of a ref struct type, or the implicit ref 'this' of a ref struct.
        if (method.ReturnsByRef || method.ReturnsByRefReadonly || method.ReturnType.IsRefLikeType
            || !method.IsStatic && method.ContainingType.IsRefLikeType
            || method.Parameters.Any(p => p.RefKind is RefKind.Ref or RefKind.Out && p.Type.IsRefLikeType))
        {
            return false;
        }

        // Attribute arguments cannot bind to a span parameter.
        return !InheritsFrom(method.ContainingType, attributeBase);
    }

    // A call that passes separate arguments compares element conversions, which the new signature
    // does not change. An overload can only clash with the new signature, tie-break differently as
    // another params method, or win or tie a call that passes an array once the array needs a
    // conversion to the span.
    private static bool HasConflictingOverload(
        IMethodSymbol method,
        bool checkArrayCalls,
        Compilation compilation,
        INamedTypeSymbol readOnlySpan)
    {
        // Extension overloads in other static classes compete for the call and cannot be enumerated.
        if (method.IsExtensionMethod && checkArrayCalls)
            return true;

        // Constructors are not inherited, so only the containing type's own constructors are overloads.
        for (var type = method.ContainingType; type is not null; type = type.BaseType)
        {
            foreach (var member in type.GetMembers(method.Name))
            {
                if (member is IMethodSymbol overload
                    && !SymbolEqualityComparer.Default.Equals(overload, method)
                    && Conflicts(overload, method, checkArrayCalls, compilation, readOnlySpan))
                {
                    return true;
                }
            }

            if (method.MethodKind == MethodKind.Constructor)
                break;
        }

        return false;
    }

    private static bool Conflicts(
        IMethodSymbol overload,
        IMethodSymbol method,
        bool checkArrayCalls,
        Compilation compilation,
        INamedTypeSymbol readOnlySpan)
    {
        var count = method.Parameters.Length;
        var parameters = overload.Parameters;

        // Between two expanded params forms, the tie-break depends on the collection type.
        if (!parameters.IsEmpty && parameters[parameters.Length - 1].IsParams)
            return true;

        // The new signature could duplicate an overload that already ends in a span.
        if (parameters.Length == count
            && parameters[count - 1].Type is INamedTypeSymbol last
            && SymbolEqualityComparer.Default.Equals(last.OriginalDefinition, readOnlySpan))
        {
            return true;
        }

        // An overload that cannot take as many arguments never competes for an array call.
        if (!checkArrayCalls || parameters.Length < count)
            return false;

        // The array reaches the overload by position, or by name as a named argument.
        var paramsParameter = method.Parameters[count - 1];
        return parameters.Any(parameter =>
            (parameter.Ordinal == count - 1 || parameter.Name == paramsParameter.Name)
            && Accepts(parameter.Type, paramsParameter.Type, compilation));
    }

    // Type inference can make a type parameter accept anything, so it counts as accepting.
    private static bool Accepts(ITypeSymbol target, ITypeSymbol arrayType, Compilation compilation) =>
        ContainsTypeParameter(target)
        || ContainsTypeParameter(arrayType)
        || compilation.ClassifyCommonConversion(arrayType, target).IsImplicit;

    private static bool ContainsTypeParameter(ITypeSymbol type) => type switch
    {
        ITypeParameterSymbol => true,
        IArrayTypeSymbol array => ContainsTypeParameter(array.ElementType),
        INamedTypeSymbol named => named.TypeArguments.Any(ContainsTypeParameter),
        _ => false,
    };

    private static bool InheritsFrom(INamedTypeSymbol? type, INamedTypeSymbol? baseType)
    {
        if (baseType is null)
            return false;

        for (var current = type; current is not null; current = current.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(current, baseType))
                return true;
        }

        return false;
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

                if (!ReadOnlySpanUse.IsCompatible(reference, readOnlySpan) || SpanMayEscape(reference))
                    return false;
            }
        }

        return true;
    }

    // After the fix the span is scoped to the method. A conversion of the array to a span is kept
    // only when every value that can carry the span is used up within one expression.
    private static bool SpanMayEscape(IParameterReferenceOperation reference) =>
        reference.Parent is IConversionOperation conversion && SpanValueMayEscape(conversion);

    // 'value' is a ref struct that may carry the scoped span. It is used up when it is read, iterated,
    // or passed by value or by 'in' to a call whose result cannot carry it, or whose ref struct result
    // is itself used up. Stored in a local or field, returned, or reachable through another ref or out
    // argument or a by-ref return, the scoped span could escape.
    private static bool SpanValueMayEscape(IOperation value)
    {
        switch (value.Parent)
        {
            case IForEachLoopOperation:
                return false;
            case IConversionOperation conversion:
                return conversion.Type is not { IsRefLikeType: true } ? true : SpanValueMayEscape(conversion);
            case IPropertyReferenceOperation property when property.Instance == value:
                return property.Property.ReturnsByRef
                    || property.Type is { IsRefLikeType: true } && SpanValueMayEscape(property);
            case IInvocationOperation invocation when invocation.Instance == value:
                return CallResultMayEscape(invocation, invocation.Arguments);
            case IArgumentOperation { Parameter.RefKind: RefKind.None or RefKind.In } argument:
                return argument.Parent switch
                {
                    IInvocationOperation invocation => CallResultMayEscape(invocation, invocation.Arguments),
                    IObjectCreationOperation creation => CallResultMayEscape(creation, creation.Arguments),
                    _ => true,
                };
            default:
                return true;
        }
    }

    private static bool CallResultMayEscape(IOperation call, ImmutableArray<IArgumentOperation> arguments)
    {
        if (call is IInvocationOperation { TargetMethod: { ReturnsByRef: true } or { ReturnsByRefReadonly: true } })
            return true;

        if (arguments.Any(other => other.Parameter is { RefKind: RefKind.Ref or RefKind.Out, Type.IsRefLikeType: true }))
            return true;

        return call.Type is { IsRefLikeType: true } && SpanValueMayEscape(call);
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
