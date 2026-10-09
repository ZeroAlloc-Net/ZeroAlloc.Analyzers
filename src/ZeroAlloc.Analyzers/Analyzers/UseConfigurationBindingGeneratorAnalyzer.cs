using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace ZeroAlloc.Analyzers;

/// <summary>
/// Reports reflection-based configuration binding while the configuration-binding source
/// generator is off. Stands down when the SDK's own AOT analyzer is enabled, and below C# 12,
/// where the generator cannot use interceptors and emits error SYSLIB1102.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class UseConfigurationBindingGeneratorAnalyzer : DiagnosticAnalyzer
{
    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticIds.UseConfigurationBindingGenerator,
        "Use the configuration-binding source generator",
        "'{0}' binds configuration with reflection; set <EnableConfigurationBindingGenerator>true</EnableConfigurationBindingGenerator> to generate the binding code",
        DiagnosticCategories.Aot,
        DiagnosticSeverity.Info,
        isEnabledByDefault: true);

    // The binding entry points the source generator intercepts.
    private static readonly (string Type, string[] Methods)[] BindingApis =
    [
        ("Microsoft.Extensions.Configuration.ConfigurationBinder", ["Bind", "Get", "GetValue"]),
        ("Microsoft.Extensions.DependencyInjection.OptionsBuilderConfigurationExtensions", ["Bind", "BindConfiguration"]),
        ("Microsoft.Extensions.DependencyInjection.OptionsConfigurationServiceCollectionExtensions", ["Configure"]),
    ];

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [Rule];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(static start =>
        {
            if (AotHelper.IsSdkAotAnalyzerEnabled(start.Options)
                || AotHelper.IsBuildPropertyTrue(start.Options, "EnableConfigurationBindingGenerator"))
            {
                return;
            }

            // The generator intercepts calls, which needs C# 12; below it the build fails with SYSLIB1102.
            if (start.Compilation is not CSharpCompilation { LanguageVersion: >= LanguageVersion.CSharp12 })
                return;

            var builder = ImmutableArray.CreateBuilder<(INamedTypeSymbol Type, string[] Methods)>();
            foreach (var (typeName, methods) in BindingApis)
            {
                // The generator ships with Microsoft.Extensions.Configuration.Binder 8.0.
                if (start.Compilation.GetTypeByMetadataName(typeName) is { } type
                    && type.ContainingAssembly.Identity.Version.Major >= 8)
                {
                    builder.Add((type, methods));
                }
            }

            if (builder.Count == 0)
                return;

            var apis = builder.ToImmutable();
            start.RegisterOperationAction(context => AnalyzeInvocation(context, apis), OperationKind.Invocation);
        });
    }

    private static void AnalyzeInvocation(OperationAnalysisContext context, ImmutableArray<(INamedTypeSymbol Type, string[] Methods)> apis)
    {
        var invocation = (IInvocationOperation)context.Operation;
        var method = invocation.TargetMethod;
        foreach (var (type, methods) in apis)
        {
            if (SymbolEqualityComparer.Default.Equals(method.ContainingType, type) && Array.IndexOf(methods, method.Name) >= 0)
            {
                // The generator only replaces calls whose target type is known at compile time
                // and that its generated code can name; it warns SYSLIB1104 for the others.
                if (HasRuntimeOnlyTarget(invocation, method)
                    || BoundType(invocation, method) is { } bound && !GeneratorCanBind(bound))
                {
                    return;
                }

                context.ReportDiagnostic(Diagnostic.Create(
                    Rule,
                    context.Operation.Syntax.GetLocation(),
                    $"{type.Name}.{method.Name}"));
                return;
            }
        }
    }

    private static bool HasRuntimeOnlyTarget(IInvocationOperation invocation, IMethodSymbol method)
    {
        foreach (var argument in invocation.Arguments)
        {
            var value = Unwrap(argument.Value);
            if (argument.Parameter is not { } parameter)
                continue;

            if (parameter.Type.ToDisplayString() == "System.Type" && value is not ITypeOfOperation)
                return true;

            if (method.Name == "Bind" && parameter.Name == "instance" && value.Type?.SpecialType == SpecialType.System_Object)
                return true;
        }

        return false;
    }

    // The type the call binds: the method's type argument, the typeof operand passed as the
    // System.Type argument, or the static type of the instance that Bind fills.
    private static ITypeSymbol? BoundType(IInvocationOperation invocation, IMethodSymbol method)
    {
        if (method.TypeArguments.Length == 1)
            return method.TypeArguments[0];

        foreach (var argument in invocation.Arguments)
        {
            if (argument.Parameter is not { } parameter)
                continue;

            var value = Unwrap(argument.Value);
            if (value is ITypeOfOperation typeOf)
                return typeOf.TypeOperand;

            if (method.Name == "Bind" && parameter.Name == "instance")
                return value.Type;
        }

        return null;
    }

    // False when the bound type contains an open type parameter, or when the generated code,
    // which lives outside the type, cannot name the type or one of its parts.
    private static bool GeneratorCanBind(ITypeSymbol type)
    {
        switch (type)
        {
            case ITypeParameterSymbol:
                return false;
            case IArrayTypeSymbol array:
                return GeneratorCanBind(array.ElementType);
            case INamedTypeSymbol named:
                for (var current = named; current is not null; current = current.ContainingType)
                {
                    if (IsHiddenFromGeneratedCode(current.DeclaredAccessibility))
                        return false;

                    foreach (var typeArgument in current.TypeArguments)
                    {
                        if (!GeneratorCanBind(typeArgument))
                            return false;
                    }
                }

                return true;
            default:
                return !IsHiddenFromGeneratedCode(type.DeclaredAccessibility);
        }
    }

    private static bool IsHiddenFromGeneratedCode(Accessibility accessibility) =>
        accessibility is Accessibility.Private or Accessibility.Protected or Accessibility.ProtectedAndInternal;

    private static IOperation Unwrap(IOperation operation)
    {
        while (operation is IConversionOperation { IsImplicit: true } conversion)
            operation = conversion.Operand;
        return operation;
    }
}
