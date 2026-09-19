using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace ZeroAlloc.Analyzers;

/// <summary>
/// Detects when a non-readonly member is invoked on a value type received as
/// <c>in</c>, <c>ref readonly</c>, or a <c>readonly</c> field, which causes the
/// compiler to silently create a defensive copy.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class AvoidDefensiveCopyAnalyzer : DiagnosticAnalyzer
{
    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticIds.AvoidDefensiveCopy,
        "Avoid defensive copies on readonly value types",
        "Calling non-readonly member '{0}' on '{1}' causes a defensive copy",
        DiagnosticCategories.Boxing,
        DiagnosticSeverity.Info,
        isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [Rule];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(
            AnalyzeNode,
            SyntaxKind.InvocationExpression,
            SyntaxKind.SimpleMemberAccessExpression);
    }

    private static void AnalyzeNode(SyntaxNodeAnalysisContext context)
    {
        switch (context.Node)
        {
            case InvocationExpressionSyntax invocation:
                AnalyzeInvocation(context, invocation);
                break;
            case MemberAccessExpressionSyntax memberAccess:
                AnalyzePropertyAccess(context, memberAccess);
                break;
        }
    }

    private static void AnalyzeInvocation(
        SyntaxNodeAnalysisContext context,
        InvocationExpressionSyntax invocation)
    {
        var memberAccess = invocation.Expression as MemberAccessExpressionSyntax;
        if (memberAccess == null)
            return;

        var receiverSymbol = context.SemanticModel.GetSymbolInfo(memberAccess.Expression, context.CancellationToken).Symbol;
        if (receiverSymbol == null)
            return;

        if (!IsReadonlyReceiver(receiverSymbol, out string receiverName))
            return;

        var calledMethod = context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken).Symbol as IMethodSymbol;
        if (calledMethod == null || calledMethod.IsReadOnly)
            return;

        // An extension method invoked in reduced form is still a static call with the receiver
        // passed by value, and compiles to the same IL as the explicit static form — which this
        // analyzer already leaves alone. The copy is declared in the signature rather than
        // inserted by the compiler to protect a readonly value, so it is not a defensive copy.
        // `ref this` and `in this` receivers are a different matter and keep reporting. See #49.
        if (IsByValueExtensionReceiver(calledMethod))
            return;

        context.ReportDiagnostic(
            Diagnostic.Create(Rule, invocation.GetLocation(), calledMethod.Name, receiverName));
    }

    /// <summary>
    /// True when the method is an extension whose <c>this</c> parameter is passed by value.
    /// <see cref="IMethodSymbol.ReducedFrom"/> is non-null only for the reduced form, which is
    /// the form that was being misdiagnosed; the explicit static call is not a member access on
    /// the receiver and never reached this analyzer.
    /// </summary>
    private static bool IsByValueExtensionReceiver(IMethodSymbol method)
    {
        if (!method.IsExtensionMethod)
            return false;

        // ReducedFrom carries the original signature including the `this` parameter; the reduced
        // symbol hides it behind the receiver.
        var original = method.ReducedFrom ?? method;
        if (original.Parameters.Length == 0)
            return false;

        return original.Parameters[0].RefKind == RefKind.None;
    }

    private static void AnalyzePropertyAccess(
        SyntaxNodeAnalysisContext context,
        MemberAccessExpressionSyntax memberAccess)
    {
        // Skip if this member access is the target of an invocation — handled by AnalyzeInvocation
        if (memberAccess.Parent is InvocationExpressionSyntax)
            return;

        var receiverSymbol = context.SemanticModel.GetSymbolInfo(memberAccess.Expression, context.CancellationToken).Symbol;
        if (receiverSymbol == null)
            return;

        if (!IsReadonlyReceiver(receiverSymbol, out string receiverName))
            return;

        var accessedSymbol = context.SemanticModel.GetSymbolInfo(memberAccess, context.CancellationToken).Symbol;

        if (accessedSymbol is IPropertySymbol property)
        {
            var getter = property.GetMethod;
            if (getter == null || getter.IsReadOnly)
                return;

            context.ReportDiagnostic(
                Diagnostic.Create(Rule, memberAccess.GetLocation(), property.Name, receiverName));
        }
    }

    private static bool IsReadonlyReceiver(ISymbol receiverSymbol, out string receiverName)
    {
        receiverName = receiverSymbol.Name;

        if (receiverSymbol is IParameterSymbol param)
        {
            if (param.RefKind == RefKind.In && param.Type.IsValueType)
                return true;
        }
        else if (receiverSymbol is ILocalSymbol local)
        {
            if (local.RefKind == RefKind.In && local.Type.IsValueType)
                return true;
        }
        else if (receiverSymbol is IFieldSymbol field)
        {
            if (field.IsReadOnly && field.Type.IsValueType)
                return true;
        }

        return false;
    }
}
