using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace ZeroAlloc.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class AvoidParamsInLoopsAnalyzer : DiagnosticAnalyzer
{
    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticIds.AvoidParamsInLoops,
        "Avoid params calls in loops",
        "params call to '{0}' inside a loop allocates its params collection on every iteration",
        DiagnosticCategories.Linq,
        DiagnosticSeverity.Info,
        isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [Rule];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(static compilationContext =>
        {
            var compilation = compilationContext.Compilation;

            // .NET 8+: the compiler backs params spans with a stack-allocated inline array.
            var hasInlineArrays = compilation.GetTypeByMetadataName(
                "System.Runtime.CompilerServices.InlineArrayAttribute") is not null;

            // .NET 7+: constant ReadOnlySpan<T> of wider primitives is read from static data.
            var hasCreateSpan = compilation.GetTypeByMetadataName(
                "System.Runtime.CompilerServices.RuntimeHelpers")?.GetMembers("CreateSpan").Length > 0;

            compilationContext.RegisterOperationAction(
                operationContext => AnalyzeInvocation(operationContext, hasInlineArrays, hasCreateSpan),
                OperationKind.Invocation);
        });
    }

    private static void AnalyzeInvocation(OperationAnalysisContext context, bool hasInlineArrays, bool hasCreateSpan)
    {
        var invocation = (IInvocationOperation)context.Operation;

        // Check if inside a loop first (cheap syntactic check)
        if (!IsInsideLoop(invocation.Syntax))
            return;

        foreach (var argument in invocation.Arguments)
        {
            // Only arguments in expanded form make the compiler build the collection.
            // Passing an existing array or collection (normal form) allocates nothing new.
            if (argument.ArgumentKind is not (ArgumentKind.ParamArray or ArgumentKind.ParamCollection))
                continue;

            if (AllocatesPerCall(argument, hasInlineArrays, hasCreateSpan))
                context.ReportDiagnostic(Diagnostic.Create(Rule, invocation.Syntax.GetLocation(), invocation.TargetMethod.Name));

            return;
        }
    }

    private static bool AllocatesPerCall(IArgumentOperation argument, bool hasInlineArrays, bool hasCreateSpan)
    {
        var elements = GetElements(argument.Value);
        var parameterType = argument.Parameter?.Type;

        if (parameterType is IArrayTypeSymbol)
            return elements.Length > 0; // An empty params array is Array.Empty<T>().

        if (parameterType is INamedTypeSymbol { IsGenericType: true } named
            && named.ConstructedFrom.ToDisplayString() is "System.Span<T>" or "System.ReadOnlySpan<T>")
        {
            if (elements.Length == 0 || hasInlineArrays)
                return false;

            // Without inline arrays the compiler falls back to new T[], except for constant
            // ReadOnlySpan<T> data of primitives, which it reads from the assembly's static data.
            var isReadOnly = named.Name == "ReadOnlySpan";
            return !(isReadOnly && IsStaticData(named.TypeArguments[0], elements, hasCreateSpan));
        }

        // Other params collections (List<T>, IEnumerable<T>, ...) are heap objects. An empty one is
        // only free when the compiler can hand out a cached empty array for an interface type.
        return elements.Length > 0 || parameterType?.TypeKind != TypeKind.Interface;
    }

    private static ImmutableArray<IOperation> GetElements(IOperation value) => value switch
    {
        IArrayCreationOperation array => array.Initializer?.ElementValues ?? ImmutableArray<IOperation>.Empty,
        ICollectionExpressionOperation collection => collection.Elements,
        _ => ImmutableArray<IOperation>.Empty,
    };

    private static bool IsStaticData(ITypeSymbol elementType, ImmutableArray<IOperation> elements, bool hasCreateSpan)
    {
        foreach (var element in elements)
        {
            if (!element.ConstantValue.HasValue)
                return false;
        }

        return elementType.SpecialType switch
        {
            SpecialType.System_Boolean or SpecialType.System_Byte or SpecialType.System_SByte => true,
            SpecialType.System_Char or SpecialType.System_Int16 or SpecialType.System_UInt16
                or SpecialType.System_Int32 or SpecialType.System_UInt32
                or SpecialType.System_Int64 or SpecialType.System_UInt64
                or SpecialType.System_Single or SpecialType.System_Double => hasCreateSpan,
            _ => false,
        };
    }

    private static bool IsInsideLoop(SyntaxNode node)
    {
        var current = node.Parent;
        while (current != null)
        {
            if (current is ForStatementSyntax
                or ForEachStatementSyntax
                or WhileStatementSyntax
                or DoStatementSyntax)
            {
                return true;
            }

            // Stop at method/lambda boundaries
            if (current is MethodDeclarationSyntax
                or LocalFunctionStatementSyntax
                or LambdaExpressionSyntax)
            {
                return false;
            }

            current = current.Parent;
        }

        return false;
    }
}
