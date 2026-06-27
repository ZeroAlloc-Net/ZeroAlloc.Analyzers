using System.Collections.Generic;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace ZeroAlloc.Analyzers;

/// <summary>
/// Flags reflection-based serializers (XmlSerializer, DataContractSerializer, BinaryFormatter,
/// …) that are not trim- or AOT-safe. Suggests a source-generated serializer instead. Stands
/// down when the SDK's own AOT analyzer is already enabled. JSON is covered separately by
/// ZA1001 (UseJsonSourceGeneration).
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class AvoidReflectionSerializersAnalyzer : DiagnosticAnalyzer
{
    private static readonly HashSet<string> ReflectionSerializers = new()
    {
        "System.Xml.Serialization.XmlSerializer",
        "System.Runtime.Serialization.DataContractSerializer",
        "System.Runtime.Serialization.Json.DataContractJsonSerializer",
        "System.Runtime.Serialization.Formatters.Binary.BinaryFormatter",
    };

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticIds.AvoidReflectionSerializers,
        "Avoid reflection-based serializers",
        "'{0}' uses reflection-based serialization that is not trim- or AOT-safe — use a source-generated serializer instead",
        DiagnosticCategories.Aot,
        DiagnosticSeverity.Info,
        isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [Rule];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterCompilationStartAction(compilationContext =>
        {
            if (AotHelper.IsSdkAotAnalyzerEnabled(compilationContext.Options))
                return;

            compilationContext.RegisterSyntaxNodeAction(AnalyzeObjectCreation, SyntaxKind.ObjectCreationExpression);
        });
    }

    private static void AnalyzeObjectCreation(SyntaxNodeAnalysisContext context)
    {
        var creation = (ObjectCreationExpressionSyntax)context.Node;

        if (context.SemanticModel.GetSymbolInfo(creation, context.CancellationToken).Symbol
            is not IMethodSymbol constructor)
            return;

        var type = constructor.ContainingType;
        if (!ReflectionSerializers.Contains(type.ToDisplayString()))
            return;

        context.ReportDiagnostic(Diagnostic.Create(Rule, creation.GetLocation(), type.Name));
    }
}
