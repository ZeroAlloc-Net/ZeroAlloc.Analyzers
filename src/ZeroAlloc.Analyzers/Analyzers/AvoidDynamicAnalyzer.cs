using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace ZeroAlloc.Analyzers;

/// <summary>
/// Reports dynamic dispatch, which binds at run time through the C# runtime binder and so
/// needs reflection and runtime code generation. Stands down when the SDK's own AOT analyzer
/// is enabled.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class AvoidDynamicAnalyzer : DiagnosticAnalyzer
{
    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticIds.AvoidDynamic,
        "Avoid dynamic dispatch",
        "'dynamic' dispatch uses the runtime binder, which is not supported under Native AOT",
        DiagnosticCategories.Aot,
        DiagnosticSeverity.Info,
        isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [Rule];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(static start =>
        {
            if (AotHelper.IsSdkAotAnalyzerEnabled(start.Options))
                return;

            start.RegisterOperationAction(
                Analyze,
                OperationKind.DynamicInvocation,
                OperationKind.DynamicMemberReference,
                OperationKind.DynamicIndexerAccess,
                OperationKind.DynamicObjectCreation,
                OperationKind.Binary,
                OperationKind.Unary,
                OperationKind.CompoundAssignment,
                OperationKind.Increment,
                OperationKind.Decrement,
                OperationKind.Conversion,
                OperationKind.Await);

            // Disposing a dynamic resource binds its conversion to IDisposable at run time, but
            // Roslyn exposes no conversion operation for it. Using is not a dispatch operation in
            // IsDynamicDispatch, because then the outermost-only walk would hide its whole body.
            start.RegisterOperationAction(AnalyzeUsing, OperationKind.Using, OperationKind.UsingDeclaration);
        });
    }

    private static void AnalyzeUsing(OperationAnalysisContext context)
    {
        var resources = context.Operation switch
        {
            IUsingOperation usingOperation => usingOperation.Resources,
            IUsingDeclarationOperation declaration => declaration.DeclarationGroup,
            _ => null,
        };

        if (resources is IVariableDeclarationGroupOperation group)
        {
            foreach (var declaration in group.Declarations)
            {
                foreach (var declarator in declaration.Declarators)
                {
                    if (IsDynamic(declarator.Symbol.Type))
                        context.ReportDiagnostic(Diagnostic.Create(Rule, declarator.Syntax.GetLocation()));
                }
            }
        }
        else if (resources is not null && IsDynamic(resources.Type))
        {
            context.ReportDiagnostic(Diagnostic.Create(Rule, resources.Syntax.GetLocation()));
        }
    }

    // True when the operation sits in the resource of a using whose resource is dynamic: the
    // expression of using (d.Open()), or the initializer of a dynamic declarator in a using
    // declaration. The using report covers it, as in using (d.Open() ?? c).
    private static bool IsInDynamicUsingResource(IOperation operation)
    {
        IVariableDeclaratorOperation? declarator = null;
        var child = operation;
        for (var parent = operation.Parent; parent is not null; child = parent, parent = parent.Parent)
        {
            if (parent is IVariableDeclaratorOperation current)
                declarator = current;

            var isResource = parent switch
            {
                IUsingOperation usingOperation => usingOperation.Resources == child,
                IUsingDeclarationOperation => true,
                _ => (bool?)null,
            };

            if (isResource is { } found)
            {
                if (!found)
                    return false;

                return child is IVariableDeclarationGroupOperation
                    ? declarator is not null && IsDynamic(declarator.Symbol.Type)
                    : IsDynamic(child.Type);
            }
        }

        return false;
    }

    private static void Analyze(OperationAnalysisContext context)
    {
        if (!IsDynamicDispatch(context.Operation))
            return;

        // Report only the outermost dynamic operation, so d.A.B(c) gives one diagnostic.
        for (var parent = context.Operation.Parent; parent is not null; parent = parent.Parent)
        {
            if (IsDynamicDispatch(parent))
                return;
        }

        // A dynamic using resource is outermost; AnalyzeUsing reports it.
        if (IsInDynamicUsingResource(context.Operation))
            return;

        context.ReportDiagnostic(Diagnostic.Create(Rule, context.Operation.Syntax.GetLocation()));
    }

    // Operations the compiler turns into a call to the C# runtime binder.
    private static bool IsDynamicDispatch(IOperation operation) => operation switch
    {
        IDynamicInvocationOperation or IDynamicMemberReferenceOperation
            or IDynamicIndexerAccessOperation or IDynamicObjectCreationOperation => true,
        IBinaryOperation binary => IsDynamic(binary.LeftOperand.Type) || IsDynamic(binary.RightOperand.Type),
        IUnaryOperation unary => IsDynamic(unary.Operand.Type),
        ICompoundAssignmentOperation compound => IsDynamic(compound.Target.Type) || IsDynamic(compound.Value.Type),
        IIncrementOrDecrementOperation increment => IsDynamic(increment.Target.Type),
        // Awaiting a dynamic value binds GetAwaiter, IsCompleted and GetResult at run time.
        IAwaitOperation awaitOperation => IsDynamic(awaitOperation.Operation.Type),
        // Converting a dynamic value to a static type binds the conversion at run time;
        // converting to object or dynamic does not, and neither does an 'as' type test.
        IConversionOperation conversion => IsDynamic(conversion.Operand.Type)
            && !conversion.IsTryCast
            && !IsDynamic(conversion.Type)
            && conversion.Type?.SpecialType != SpecialType.System_Object,
        _ => false,
    };

    private static bool IsDynamic(ITypeSymbol? type) => type?.TypeKind == TypeKind.Dynamic;
}
