using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace ZeroAlloc.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class UseUtf8StringLiteralAnalyzer : DiagnosticAnalyzer
{
    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticIds.UseUtf8StringLiteral,
        "Use a UTF-8 string literal",
        "Use the UTF-8 literal {0}u8 instead of encoding a constant string on every call",
        DiagnosticCategories.Strings,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [Rule];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(static start =>
        {
            if (start.Compilation is not CSharpCompilation { LanguageVersion: >= LanguageVersion.CSharp11 })
                return;

            var encoding = start.Compilation.GetTypeByMetadataName("System.Text.Encoding");
            var readOnlySpan = start.Compilation.GetTypeByMetadataName("System.ReadOnlySpan`1");
            if (encoding is null || readOnlySpan is null)
                return;

            start.RegisterOperationAction(
                context => AnalyzeInvocation(context, encoding, readOnlySpan),
                OperationKind.Invocation);
        });
    }

    private static void AnalyzeInvocation(OperationAnalysisContext context, INamedTypeSymbol encoding, INamedTypeSymbol readOnlySpan)
    {
        var invocation = (IInvocationOperation)context.Operation;
        var method = invocation.TargetMethod;
        if (method.Name != "GetBytes"
            || method.Parameters.Length != 1
            || method.Parameters[0].Type.SpecialType != SpecialType.System_String
            || !SymbolEqualityComparer.Default.Equals(method.ContainingType, encoding))
        {
            return;
        }

        if (invocation.Instance is not IPropertyReferenceOperation { Property: { Name: "UTF8", IsStatic: true } property }
            || !SymbolEqualityComparer.Default.Equals(property.ContainingType, encoding))
        {
            return;
        }

        if (invocation.Arguments[0].Value.ConstantValue is not { HasValue: true, Value: string text } || !IsWellFormedUtf16(text))
            return;

        // Only a result that becomes a ReadOnlySpan<byte> can take the literal unchanged.
        // Kept as a byte[], the literal would need .ToArray() and gains nothing.
        if (invocation.Parent is not IConversionOperation { IsImplicit: true, Type: INamedTypeSymbol target }
            || !SymbolEqualityComparer.Default.Equals(target.OriginalDefinition, readOnlySpan)
            || target.TypeArguments[0].SpecialType != SpecialType.System_Byte)
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(
            Rule,
            invocation.Syntax.GetLocation(),
            SymbolDisplay.FormatLiteral(text, quote: true)));
    }

    // A u8 literal does not compile for text with an unpaired surrogate.
    private static bool IsWellFormedUtf16(string text)
    {
        for (var i = 0; i < text.Length; i++)
        {
            if (char.IsHighSurrogate(text[i]))
            {
                if (i + 1 >= text.Length || !char.IsLowSurrogate(text[i + 1]))
                    return false;
                i++;
            }
            else if (char.IsLowSurrogate(text[i]))
            {
                return false;
            }
        }

        return true;
    }
}
