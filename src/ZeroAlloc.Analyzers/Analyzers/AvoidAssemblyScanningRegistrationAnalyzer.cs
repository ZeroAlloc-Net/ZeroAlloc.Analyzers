using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace ZeroAlloc.Analyzers;

/// <summary>
/// Reports calls to well-known registration APIs that discover types by scanning assemblies with
/// reflection, which trimming and Native AOT break. Matches on the assembly that declares the
/// called method and on the method name, not on type names, because these libraries have moved
/// their extensions between types and packages across major versions. Stands down per call only
/// when the SDK's AOT analyzer is on and the API carries a trim or dynamic-code annotation, since
/// the SDK then reports it already.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class AvoidAssemblyScanningRegistrationAnalyzer : DiagnosticAnalyzer
{
    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticIds.AvoidAssemblyScanningRegistration,
        "Avoid assembly-scanning registration",
        "'{0}' registers types by scanning assemblies with reflection, which trimming and Native AOT break; ZeroAlloc.{1} registers them with a source generator instead",
        DiagnosticCategories.Aot,
        DiagnosticSeverity.Info,
        isEnabledByDefault: true);

    private static readonly string[] MediatRAssemblies = ["MediatR", "MediatR.Extensions.Microsoft.DependencyInjection"];
    private static readonly string[] AutoMapperAssemblies = ["AutoMapper", "AutoMapper.Extensions.Microsoft.DependencyInjection"];

    // The one place to add a library. OnlyWithAssemblyOrTypeParameter limits a method to the
    // overloads that take an assembly or a type, leaving the config-only overloads alone.
    private static readonly (string[] Assemblies, string[] Methods, bool OnlyWithAssemblyOrTypeParameter, string Package)[] ScanningApis =
    [
        (["Scrutor"], ["Scan"], false, "Inject"),
        (MediatRAssemblies,
            ["RegisterServicesFromAssembly", "RegisterServicesFromAssemblies", "RegisterServicesFromAssemblyContaining"],
            false, "Mediator"),
        (MediatRAssemblies, ["AddMediatR"], true, "Mediator"),
        (["FluentValidation.DependencyInjectionExtensions"],
            ["AddValidatorsFromAssembly", "AddValidatorsFromAssemblies", "AddValidatorsFromAssemblyContaining"],
            false, "Validation"),
        (AutoMapperAssemblies, ["AddAutoMapper"], true, "Mapping"),
    ];

    private static readonly string[] TrimAnnotations = ["RequiresUnreferencedCodeAttribute", "RequiresDynamicCodeAttribute"];

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [Rule];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(static start =>
        {
            var sdkAnalyzerOn = AotHelper.IsSdkAotAnalyzerEnabled(start.Options);
            start.RegisterOperationAction(
                context => AnalyzeInvocation(context, sdkAnalyzerOn),
                OperationKind.Invocation);
        });
    }

    private static void AnalyzeInvocation(OperationAnalysisContext context, bool sdkAnalyzerOn)
    {
        var method = ((IInvocationOperation)context.Operation).TargetMethod;
        var assemblyName = method.ContainingAssembly?.Identity.Name;
        if (assemblyName is null)
            return;

        foreach (var (assemblies, methods, onlyWithAssemblyOrTypeParameter, package) in ScanningApis)
        {
            if (Array.IndexOf(methods, method.Name) < 0 || Array.IndexOf(assemblies, assemblyName) < 0)
                continue;

            if (onlyWithAssemblyOrTypeParameter && !HasAssemblyOrTypeParameter(method))
                continue;

            // The SDK's own analyzer reports annotated APIs, so reporting them as well would double up.
            if (sdkAnalyzerOn && IsTrimAnnotated(method))
                return;

            context.ReportDiagnostic(Diagnostic.Create(
                Rule,
                context.Operation.Syntax.GetLocation(),
                $"{method.ContainingType.Name}.{method.Name}",
                package));
            return;
        }
    }

    private static bool HasAssemblyOrTypeParameter(IMethodSymbol method)
    {
        foreach (var parameter in method.Parameters)
        {
            if (IsAssemblyOrTypeShape(parameter.Type))
                return true;
        }

        return false;
    }

    private static bool IsAssemblyOrTypeShape(ITypeSymbol type)
    {
        if (type is IArrayTypeSymbol array)
            return IsAssemblyOrType(array.ElementType);

        return type is INamedTypeSymbol { IsGenericType: true, TypeArguments.Length: 1 } named
            && named.OriginalDefinition.SpecialType == SpecialType.System_Collections_Generic_IEnumerable_T
            ? IsAssemblyOrType(named.TypeArguments[0])
            : IsAssemblyOrType(type);
    }

    private static bool IsAssemblyOrType(ITypeSymbol type)
        => type.SpecialType == SpecialType.None
            && type.ContainingType is null
            && ((type.Name == "Assembly" && IsNamespace(type, "System", "Reflection"))
                || (type.Name == "Type" && IsNamespace(type, "System")));

    private static bool IsNamespace(ITypeSymbol type, params string[] parts)
    {
        var ns = type.ContainingNamespace;
        for (var i = parts.Length - 1; i >= 0; i--)
        {
            if (ns is null || ns.Name != parts[i])
                return false;

            ns = ns.ContainingNamespace;
        }

        return ns is { IsGlobalNamespace: true };
    }

    private static bool IsTrimAnnotated(IMethodSymbol method)
        => HasTrimAnnotation(method.GetAttributes())
            || HasTrimAnnotation(method.ContainingType.GetAttributes());

    // Compared by name so the attribute types need not resolve in the compilation.
    private static bool HasTrimAnnotation(ImmutableArray<AttributeData> attributes)
    {
        foreach (var attribute in attributes)
        {
            var attributeClass = attribute.AttributeClass;
            if (attributeClass is not null
                && Array.IndexOf(TrimAnnotations, attributeClass.Name) >= 0
                && IsNamespace(attributeClass, "System", "Diagnostics", "CodeAnalysis"))
            {
                return true;
            }
        }

        return false;
    }
}
