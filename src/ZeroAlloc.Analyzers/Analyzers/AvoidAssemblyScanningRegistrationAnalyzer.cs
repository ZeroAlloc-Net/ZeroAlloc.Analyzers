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
/// when the SDK analyzer that reports the API's annotation is on: the trim analyzer for
/// [RequiresUnreferencedCode], the AOT analyzer for [RequiresDynamicCode].
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class AvoidAssemblyScanningRegistrationAnalyzer : DiagnosticAnalyzer
{
    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticIds.AvoidAssemblyScanningRegistration,
        "Avoid assembly-scanning registration",
        "'{0}' registers types by scanning assemblies with reflection, which trimming and Native AOT break; {1}",
        DiagnosticCategories.Aot,
        DiagnosticSeverity.Info,
        isEnabledByDefault: true);

    private static readonly string[] MediatRAssemblies = ["MediatR", "MediatR.Extensions.Microsoft.DependencyInjection"];
    private static readonly string[] AutoMapperAssemblies = ["AutoMapper", "AutoMapper.Extensions.Microsoft.DependencyInjection"];

    private const string InjectHint = "ZeroAlloc.Inject registers services at compile time with a source generator";
    private const string MediatorHint = "ZeroAlloc.Mediator 6.2 or later registers its handlers at compile time from the generated AddMediator()";
    private const string ValidationHint = "ZeroAlloc.Validation registers validators at compile time with a source generator";
    private const string MappingHint = "ZeroAlloc.Mapping generates mappers at compile time, with no registration needed";

    // The one place to add a library. OnlyWithAssemblyOrTypeParameter limits a method to the
    // overloads that take an assembly or a type, leaving the config-only overloads alone. The hint
    // is the message's advice, which differs per package because the replacements work differently.
    private static readonly (string[] Assemblies, string[] Methods, bool OnlyWithAssemblyOrTypeParameter, string Hint)[] ScanningApis =
    [
        (["Scrutor"], ["Scan"], false, InjectHint),
        (MediatRAssemblies,
            ["RegisterServicesFromAssembly", "RegisterServicesFromAssemblies", "RegisterServicesFromAssemblyContaining"],
            false, MediatorHint),
        (MediatRAssemblies, ["AddMediatR"], true, MediatorHint),
        (["FluentValidation.DependencyInjectionExtensions"],
            ["AddValidatorsFromAssembly", "AddValidatorsFromAssemblies", "AddValidatorsFromAssemblyContaining"],
            false, ValidationHint),
        (AutoMapperAssemblies, ["AddAutoMapper"], true, MappingHint),
    ];

    private const string RequiresUnreferencedCode = "RequiresUnreferencedCodeAttribute";
    private const string RequiresDynamicCode = "RequiresDynamicCodeAttribute";

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [Rule];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(static start =>
        {
            // IL2026 comes from the trim analyzer and IL3050 from the AOT analyzer, and
            // EnableAotAnalyzer alone turns on only the second, so each is tracked on its own.
            var trimAnalyzerOn = AotHelper.IsSdkTrimAnalyzerEnabled(start.Options);
            var aotAnalyzerOn = AotHelper.IsSdkAotAnalyzerEnabled(start.Options);
            start.RegisterOperationAction(
                context => AnalyzeInvocation(context, trimAnalyzerOn, aotAnalyzerOn),
                OperationKind.Invocation);
        });
    }

    private static void AnalyzeInvocation(OperationAnalysisContext context, bool trimAnalyzerOn, bool aotAnalyzerOn)
    {
        var method = ((IInvocationOperation)context.Operation).TargetMethod;
        var assemblyName = method.ContainingAssembly?.Identity.Name;
        if (assemblyName is null)
            return;

        foreach (var (assemblies, methods, onlyWithAssemblyOrTypeParameter, hint) in ScanningApis)
        {
            if (Array.IndexOf(methods, method.Name) < 0 || Array.IndexOf(assemblies, assemblyName) < 0)
                continue;

            if (onlyWithAssemblyOrTypeParameter && !HasAssemblyOrTypeParameter(method))
                continue;

            // The SDK's own analyzer reports annotated APIs, so reporting them as well would double up.
            if ((trimAnalyzerOn && HasAnnotation(method, RequiresUnreferencedCode))
                || (aotAnalyzerOn && HasAnnotation(method, RequiresDynamicCode)))
            {
                return;
            }

            context.ReportDiagnostic(Diagnostic.Create(
                Rule,
                context.Operation.Syntax.GetLocation(),
                $"{method.ContainingType.Name}.{method.Name}",
                hint));
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

    private static bool HasAnnotation(IMethodSymbol method, string attributeName)
        => HasAttribute(method.GetAttributes(), attributeName)
            || HasAttribute(method.ContainingType.GetAttributes(), attributeName);

    // Compared by name so the attribute types need not resolve in the compilation.
    private static bool HasAttribute(ImmutableArray<AttributeData> attributes, string attributeName)
    {
        foreach (var attribute in attributes)
        {
            var attributeClass = attribute.AttributeClass;
            if (attributeClass is not null
                && attributeClass.Name == attributeName
                && IsNamespace(attributeClass, "System", "Diagnostics", "CodeAnalysis"))
            {
                return true;
            }
        }

        return false;
    }
}
