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

            // Methods whose array callers could bind to another overload once the array needs a
            // conversion. The advice for an exposed method keeps the array overload, so this only
            // blocks the code fix's signature change.
            var arrayCallRebinds = new ConcurrentDictionary<IMethodSymbol, bool>(SymbolEqualityComparer.Default);

            // Whether a source method with this name has a params array, so a call may need checking.
            var paramsArrayNames = new ConcurrentDictionary<string, bool>(StringComparer.Ordinal);

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

                if (target is null)
                    return;

                if (IsParams(target) && (IsInExpressionTree(operation, expressionTreeBase) || PassesArrayOfOtherType(arguments)))
                    excluded[target.OriginalDefinition] = true;

                if (operation.SemanticModel is { } model
                    && MayInvolveParamsArray(target, start.Compilation, paramsArrayNames, context.CancellationToken))
                {
                    CheckMethodGroup(operation, target, arguments, model, readOnlySpan, excluded, arrayCallRebinds, context.CancellationToken);
                }
            }, OperationKind.Invocation, OperationKind.ObjectCreation);

            // A collection expression calls Add for each element without an operation for the call.
            start.RegisterOperationAction(context =>
            {
                if (((ICollectionExpressionOperation)context.Operation).Elements.IsEmpty)
                    return;

                ExcludeParamsArrayMethodsNamed("Add", start.Compilation, excluded, context.CancellationToken);
            }, OperationKind.CollectionExpression);

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

                    if (!exposed && arrayCallRebinds.ContainsKey(method) || HasConflictingOverload(method, readOnlySpan))
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

    // An overload that is itself params, or that the new signature could duplicate, conflicts even
    // without a visible call: callers in other assemblies see it too.
    private static bool HasConflictingOverload(IMethodSymbol method, INamedTypeSymbol readOnlySpan)
    {
        // Constructors are not inherited, so only the containing type's own constructors are overloads.
        for (var type = method.ContainingType; type is not null; type = type.BaseType)
        {
            foreach (var member in type.GetMembers(method.Name))
            {
                if (member is IMethodSymbol overload
                    && !SymbolEqualityComparer.Default.Equals(overload, method)
                    && Conflicts(overload, method, readOnlySpan))
                {
                    return true;
                }
            }

            if (method.MethodKind == MethodKind.Constructor)
                break;
        }

        return false;
    }

    private static bool Conflicts(IMethodSymbol overload, IMethodSymbol method, INamedTypeSymbol readOnlySpan)
    {
        // Between two expanded params forms, the tie-break depends on the collection type.
        if (IsParams(overload))
            return true;

        // The new signature could duplicate an overload that already ends in a span.
        var count = method.Parameters.Length;
        return overload.Parameters.Length == count
            && overload.Parameters[count - 1].Type is INamedTypeSymbol last
            && SymbolEqualityComparer.Default.Equals(last.OriginalDefinition, readOnlySpan);
    }

    private static bool IsParams(IMethodSymbol method) =>
        !method.Parameters.IsEmpty && method.Parameters[method.Parameters.Length - 1].IsParams;

    // A call needs checking when a params array method could be in its method group. Constructors
    // are only overloaded within their type; other methods are found by name across the source.
    private static bool MayInvolveParamsArray(
        IMethodSymbol target,
        Compilation compilation,
        ConcurrentDictionary<string, bool> paramsArrayNames,
        CancellationToken cancellationToken)
    {
        if (target.MethodKind == MethodKind.Constructor)
            return target.ContainingType.InstanceConstructors.Any(HasParamsArray);

        return paramsArrayNames.GetOrAdd(target.Name, name =>
            compilation.GetSymbolsWithName(name, SymbolFilter.Member, cancellationToken)
                .OfType<IMethodSymbol>()
                .Any(HasParamsArray));
    }

    // The new signature changes overload resolution only for calls it competes for: calls where two
    // params methods tie-break on their collection types, calls that bound elsewhere with an argument
    // in its params slot that converts to the span, and calls that pass it an array, which then needs
    // a conversion. The method group is what the compiler chose from, including extension methods in
    // other static classes and members of derived types.
    private static void CheckMethodGroup(
        IOperation operation,
        IMethodSymbol target,
        ImmutableArray<IArgumentOperation> arguments,
        SemanticModel model,
        INamedTypeSymbol readOnlySpan,
        ConcurrentDictionary<IMethodSymbol, bool> excluded,
        ConcurrentDictionary<IMethodSymbol, bool> arrayCallRebinds,
        CancellationToken cancellationToken)
    {
        // An implicit call, such as the Add of a collection initializer, takes the syntax of something
        // else, like the element expression, so its own arguments cannot be read from the syntax.
        var (group, syntaxArguments) = operation.IsImplicit
            ? (ImmutableArray<ISymbol>.Empty, null)
            : MethodGroupOf(operation.Syntax, model, cancellationToken);

        // A constructor competes only with its type's other constructors.
        var methods = target.MethodKind == MethodKind.Constructor
            ? target.ContainingType.InstanceConstructors
            : group.OfType<IMethodSymbol>().ToImmutableArray();

        // A call whose argument syntax is unknown, or whose group does not hold the call's own target,
        // cannot be mapped to a params slot. Such a call can bind to an extension method in any static
        // class, so every params array method with the name is excluded.
        if (syntaxArguments is not { } argumentList
            || !methods.Any(method => SymbolEqualityComparer.Default.Equals(Definition(method), Definition(target))))
        {
            if (target.MethodKind == MethodKind.Constructor)
            {
                foreach (var constructor in target.ContainingType.InstanceConstructors.Where(HasParamsArray))
                    excluded[constructor.OriginalDefinition] = true;
            }
            else
            {
                ExcludeParamsArrayMethodsNamed(target.Name, model.Compilation, excluded, cancellationToken);
            }

            return;
        }

        if (methods.Length < 2)
            return;

        var targetDefinition = Definition(target);
        var paramsMethods = methods.Count(IsParams);

        // The argument of a call that passes an array to the target's params parameter.
        var arrayArgument = HasParamsArray(target)
            ? arguments.FirstOrDefault(argument =>
                argument.Parameter is { IsParams: true } && argument.ArgumentKind == ArgumentKind.Explicit)?.Syntax as ArgumentSyntax
            : null;

        foreach (var method in methods)
        {
            var definition = Definition(method);
            var isTarget = SymbolEqualityComparer.Default.Equals(definition, targetDefinition);

            if (HasParamsArray(method)
                && (paramsMethods > 1
                    || !isTarget && ParamsSlotConvertsToSpan(method, argumentList, model, readOnlySpan, cancellationToken)))
            {
                excluded[definition] = true;
            }

            if (!isTarget
                && arrayArgument is not null
                && AcceptsArray(method, target.Parameters[target.Parameters.Length - 1].Type, arrayArgument, argumentList, model.Compilation))
            {
                arrayCallRebinds[targetDefinition] = true;
            }
        }
    }

    private static (ImmutableArray<ISymbol> Group, SeparatedSyntaxList<ArgumentSyntax>? Arguments) MethodGroupOf(
        SyntaxNode syntax,
        SemanticModel model,
        CancellationToken cancellationToken) => syntax switch
    {
        InvocationExpressionSyntax invocation =>
            (model.GetMemberGroup(invocation.Expression, cancellationToken), invocation.ArgumentList.Arguments),
        BaseObjectCreationExpressionSyntax { ArgumentList: { } list } => (ImmutableArray<ISymbol>.Empty, list.Arguments),
        ConstructorInitializerSyntax initializer => (ImmutableArray<ISymbol>.Empty, initializer.ArgumentList.Arguments),
        PrimaryConstructorBaseTypeSyntax baseType => (ImmutableArray<ISymbol>.Empty, baseType.ArgumentList.Arguments),
        _ => (ImmutableArray<ISymbol>.Empty, null),
    };

    // An implicit call can bind to an extension method in any static class, so every source method
    // with the name and a params array is excluded.
    private static void ExcludeParamsArrayMethodsNamed(
        string name,
        Compilation compilation,
        ConcurrentDictionary<IMethodSymbol, bool> excluded,
        CancellationToken cancellationToken)
    {
        foreach (var method in compilation.GetSymbolsWithName(name, SymbolFilter.Member, cancellationToken).OfType<IMethodSymbol>())
        {
            if (HasParamsArray(method))
                excluded[method.OriginalDefinition] = true;
        }
    }

    // A reduced extension method in a member group stands for the static method it was reduced from.
    private static IMethodSymbol Definition(IMethodSymbol method) => (method.ReducedFrom ?? method).OriginalDefinition;

    // The argument a params method would take in normal form converts to its span. A type parameter
    // element could match anything, so it counts.
    private static bool ParamsSlotConvertsToSpan(
        IMethodSymbol method,
        SeparatedSyntaxList<ArgumentSyntax> arguments,
        SemanticModel model,
        INamedTypeSymbol readOnlySpan,
        CancellationToken cancellationToken)
    {
        var parameter = method.Parameters[method.Parameters.Length - 1];
        var slot = ArgumentFor(parameter, arguments);
        if (slot is null)
            return false;

        var element = ((IArrayTypeSymbol)parameter.Type).ElementType;
        if (ContainsTypeParameter(element))
            return true;

        cancellationToken.ThrowIfCancellationRequested();
        return model.ClassifyConversion(slot.Expression, readOnlySpan.Construct(element)).IsImplicit;
    }

    // The overload could take the array the call passes to the target's params parameter, by
    // position or by name. A params overload is left to the tie-break check, and one that takes
    // fewer arguments than the call passes cannot compete.
    private static bool AcceptsArray(
        IMethodSymbol overload,
        ITypeSymbol arrayType,
        ArgumentSyntax arrayArgument,
        SeparatedSyntaxList<ArgumentSyntax> arguments,
        Compilation compilation)
    {
        if (IsParams(overload) || overload.Parameters.Length < arguments.Count)
            return false;

        var name = arrayArgument.NameColon?.Name.Identifier.ValueText;
        var index = arguments.IndexOf(arrayArgument);
        var parameter = name is not null
            ? overload.Parameters.FirstOrDefault(p => p.Name == name)
            : index >= 0 && index < overload.Parameters.Length ? overload.Parameters[index] : null;

        return parameter is not null && Accepts(parameter.Type, arrayType, compilation);
    }

    // In normal form the params argument is named after the parameter, or is the last positional one.
    private static ArgumentSyntax? ArgumentFor(IParameterSymbol parameter, SeparatedSyntaxList<ArgumentSyntax> arguments)
    {
        foreach (var argument in arguments)
        {
            if (argument.NameColon?.Name.Identifier.ValueText == parameter.Name)
                return argument;
        }

        return arguments.Count == parameter.Ordinal + 1 && arguments[parameter.Ordinal].NameColon is null
            ? arguments[parameter.Ordinal]
            : null;
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

        // A ref struct's instance method gets 'this' by ref, so unless it is readonly it can store the span.
        if (call is IInvocationOperation { Instance.Type.IsRefLikeType: true, TargetMethod.IsReadOnly: false })
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
