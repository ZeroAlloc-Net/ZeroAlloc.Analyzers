# Batch 3 PR 3 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship the four NativeAOT rules: ZA1706 (library is not marked `IsAotCompatible`), ZA1707 (use the configuration-binding source generator), ZA1708 (use `[OptionsValidator]` instead of `ValidateDataAnnotations`) and ZA1709 (avoid `dynamic` dispatch).

**Architecture:** Each rule is one `DiagnosticAnalyzer` in `src/ZeroAlloc.Analyzers/Analyzers/`, built on `IOperation`. ZA1707 to ZA1709 stand down through `AotHelper.IsSdkAotAnalyzerEnabled`, exactly as ZA1701 to ZA1705 do. ZA1706 reports once per compilation, with no source location. MSBuild properties reach the analyzers through `CompilerVisibleProperty` items in the package's `buildTransitive` props.

**Tech Stack:** C# with `LangVersion` latest, Roslyn 5.9, xUnit with `Microsoft.CodeAnalysis.CSharp.Analyzer.Testing` 1.1.4. Tests pull the `Microsoft.Extensions.*` packages through `ReferenceAssemblies.AddPackages`, as the ZA0401 tests do.

**Spec:** `docs/plans/2026-10-09-zero-alloc-aot-batch-3-design.md`, section "NativeAOT rules". Issues #82, #83, #84 and #85.

## Global Constraints

- The analyzer project targets `netstandard2.0` with `TreatWarningsAsErrors`, `Nullable`, `ImplicitUsings` and `EnforceExtendedAnalyzerRules` on.
  - Do not add explicit `using System.Linq;`, `using System.Collections.Generic;` or `using System.Threading.Tasks;`.
  - Do not use `record` types.
- A descriptor reported from a compilation-end action must pass `customTags: WellKnownDiagnosticTags.CompilationEnd`.
- Every new descriptor needs a `DiagnosticIds` constant, plus a row appended to the `### New Rules` table in `src/ZeroAlloc.Analyzers/AnalyzerReleases.Unshipped.md`. If the file holds only its two header comment lines, add the table header first.
- Analyze through `IOperation`. Gate on BCL and package APIs by checking that their symbol exists.
- All four rules are `Info`.
- Keep each file's existing line endings. Do not touch the line endings of lines you don't edit.
- Make one `feat:` commit per rule.
  - Body lines must be 100 characters or fewer; commitlint enforces this in CI.
  - Bodies must not contain nested parentheses.
  - End each commit with a `Co-Authored-By:` trailer naming the model that wrote it.
- Build and test from the repo root: `dotnet test tests/ZeroAlloc.Analyzers.Tests -c Release`

## File map

| File | Responsibility | Task |
|---|---|---|
| `src/ZeroAlloc.Analyzers/AotHelper.cs` | MSBuild property helpers, shared by all AOT rules | 1 |
| `src/ZeroAlloc.Analyzers.Package/buildTransitive/ZeroAlloc.Analyzers.props` | `CompilerVisibleProperty` items | 1, 2 |
| `tests/ZeroAlloc.Analyzers.Tests/Verifiers/CSharpAnalyzerVerifier.cs` | Overload that takes MSBuild properties and an output kind | 1 |
| `src/ZeroAlloc.Analyzers/Analyzers/MarkLibraryAotCompatibleAnalyzer.cs` | ZA1706 | 1 |
| `src/ZeroAlloc.Analyzers/Analyzers/UseConfigurationBindingGeneratorAnalyzer.cs` | ZA1707 | 2 |
| `src/ZeroAlloc.Analyzers/Analyzers/UseOptionsValidatorGeneratorAnalyzer.cs` | ZA1708 | 3 |
| `src/ZeroAlloc.Analyzers/Analyzers/AvoidDynamicAnalyzer.cs` | ZA1709 | 4 |
| `docs/rules/aot.md`, `docs/getting-started.md` | Rule sections and rows | 1–4 |
| `README.md`, `docs/getting-started.md`, `docs/performance.md`, design doc | Counts and refinements | 5 |

## Refinements to the spec made by this plan

Task 5 writes these into the design doc.

1. **ZA1706 also stands down when `IsAotCompatible` is set to any value, including `false`.** An explicit `false` is a deliberate opt-out. Only an unset property means "never decided".
2. **ZA1706 registers no callbacks** for an app, a test project, or a target framework older than net8.0. It checks those conditions in its compilation-start action, so the README's "registers zero callbacks" claim stays true.
3. **ZA1709 covers every operation that goes through the runtime binder,** not just the four the spec lists. That includes binary and unary operators, compound assignments, increments and decrements on `dynamic` operands, and conversions from `dynamic` to a type other than `object` or `dynamic`. `int n = d.Count;` binds the conversion at run time too. The outermost-only rule still applies, so each expression gets one diagnostic.

---

### Task 1: ZA1706 — library is not marked IsAotCompatible (#82)

**Files:**
- Create: `src/ZeroAlloc.Analyzers/Analyzers/MarkLibraryAotCompatibleAnalyzer.cs`
- Create: `tests/ZeroAlloc.Analyzers.Tests/ZA1706_MarkLibraryAotCompatibleTests.cs`
- Modify: `src/ZeroAlloc.Analyzers/AotHelper.cs`, `src/ZeroAlloc.Analyzers.Package/buildTransitive/ZeroAlloc.Analyzers.props`, `tests/ZeroAlloc.Analyzers.Tests/Verifiers/CSharpAnalyzerVerifier.cs`, `src/ZeroAlloc.Analyzers/DiagnosticIds.cs`, `src/ZeroAlloc.Analyzers/AnalyzerReleases.Unshipped.md`, `docs/rules/aot.md`, `docs/getting-started.md`

**Interfaces:**
- Produces:
  - `AotHelper.IsBuildPropertyTrue(AnalyzerOptions options, string name)` returns `bool`. It is true when `build_property.<name>` equals `true`, ignoring case.
  - `AotHelper.IsBuildPropertySet(AnalyzerOptions options, string name)` returns `bool`. It is true when `build_property.<name>` exists and is not empty or whitespace.
  - `CSharpAnalyzerVerifier<TAnalyzer>.VerifyAnalyzerWithPropertiesAsync(string source, ReferenceAssemblies referenceAssemblies, IReadOnlyDictionary<string, string> buildProperties, DiagnosticResult[] expected, OutputKind outputKind = OutputKind.DynamicallyLinkedLibrary)` returns `Task`.
  - `DiagnosticIds.MarkLibraryAotCompatible = "ZA1706"`.

- [ ] **Step 1: Add the ID constant**

In `DiagnosticIds.cs`, after `public const string AvoidTypeLoadingByName = "ZA1705";`:

```csharp
    public const string MarkLibraryAotCompatible = "ZA1706";
```

- [ ] **Step 2: Add the property helpers to AotHelper**

Replace the body of `src/ZeroAlloc.Analyzers/AotHelper.cs` with the following. `IsSdkAotAnalyzerEnabled` keeps its behaviour; its private `IsTrue` becomes the public `IsBuildPropertyTrue`.

```csharp
using Microsoft.CodeAnalysis.Diagnostics;

namespace ZeroAlloc.Analyzers;

internal static class AotHelper
{
    /// <summary>
    /// True when the project has opted into Native AOT or marked itself AOT-compatible.
    /// In that case the .NET SDK already enables the official AOT analyzer (IL3050+),
    /// so ZeroAlloc's ZA17xx rules stand down to avoid double-reporting.
    /// </summary>
    public static bool IsSdkAotAnalyzerEnabled(AnalyzerOptions options)
        => IsBuildPropertyTrue(options, "PublishAot")
            || IsBuildPropertyTrue(options, "IsAotCompatible")
            || IsBuildPropertyTrue(options, "EnableAotAnalyzer");

    /// <summary>True when the MSBuild property is visible to the analyzer and set to <c>true</c>.</summary>
    public static bool IsBuildPropertyTrue(AnalyzerOptions options, string name)
        => options.AnalyzerConfigOptionsProvider.GlobalOptions.TryGetValue($"build_property.{name}", out var value)
            && value.Equals("true", StringComparison.OrdinalIgnoreCase);

    /// <summary>True when the MSBuild property is visible to the analyzer and set to any non-empty value.</summary>
    public static bool IsBuildPropertySet(AnalyzerOptions options, string name)
        => options.AnalyzerConfigOptionsProvider.GlobalOptions.TryGetValue($"build_property.{name}", out var value)
            && !string.IsNullOrWhiteSpace(value);
}
```

- [ ] **Step 3: Make IsTestProject visible to analyzers**

In `src/ZeroAlloc.Analyzers.Package/buildTransitive/ZeroAlloc.Analyzers.props`, add this after the `EnableAotAnalyzer` item:

```xml
    <!-- Lets ZA1706 skip test projects, which are never published with AOT. -->
    <CompilerVisibleProperty Include="IsTestProject" />
```

- [ ] **Step 4: Add the verifier overload**

In `tests/ZeroAlloc.Analyzers.Tests/Verifiers/CSharpAnalyzerVerifier.cs`, add this method to `CSharpAnalyzerVerifier<TAnalyzer>`, after the existing `VerifyNoDiagnosticAsync` overloads:

```csharp
    /// <summary>
    /// Analyzes with the given MSBuild properties visible to the analyzer, as the package's
    /// buildTransitive props make them, and with the given output kind.
    /// </summary>
    public static async Task VerifyAnalyzerWithPropertiesAsync(
        string source,
        ReferenceAssemblies referenceAssemblies,
        IReadOnlyDictionary<string, string> buildProperties,
        DiagnosticResult[] expected,
        OutputKind outputKind = OutputKind.DynamicallyLinkedLibrary)
    {
        var test = new CSharpAnalyzerTest<TAnalyzer, DefaultVerifier>
        {
            TestCode = source,
            ReferenceAssemblies = referenceAssemblies,
        };

        test.TestState.OutputKind = outputKind;

        var properties = string.Join("\n", buildProperties.Select(p => $"build_property.{p.Key} = {p.Value}"));
        test.TestState.AnalyzerConfigFiles.Add(("/.globalconfig", $"is_global = true\n{properties}\n"));

        test.ExpectedDiagnostics.AddRange(expected);
        await test.RunAsync();
    }
```

- [ ] **Step 5: Write the failing tests**

Create `tests/ZeroAlloc.Analyzers.Tests/ZA1706_MarkLibraryAotCompatibleTests.cs`:

```csharp
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Testing;
using ZeroAlloc.Analyzers.Tests.Verifiers;

namespace ZeroAlloc.Analyzers.Tests;

public class ZA1706_MarkLibraryAotCompatibleTests
{
    private const string Library = """
        public class Widget
        {
        }
        """;

    private const string App = """
        class Program
        {
            static void Main() { }
        }
        """;

    private static DiagnosticResult Expected() =>
        CSharpAnalyzerVerifier<MarkLibraryAotCompatibleAnalyzer>.Diagnostic(DiagnosticIds.MarkLibraryAotCompatible);

    private static Dictionary<string, string> Properties(string targetFramework, params (string Name, string Value)[] extra)
    {
        var properties = new Dictionary<string, string> { ["TargetFramework"] = targetFramework };
        foreach (var (name, value) in extra)
            properties[name] = value;
        return properties;
    }

    [Theory]
    [InlineData("net8.0")]
    [InlineData("net9.0")]
    public async Task Net8OrLaterLibraryWithoutAotProperty_Reports(string targetFramework)
    {
        await CSharpAnalyzerVerifier<MarkLibraryAotCompatibleAnalyzer>.VerifyAnalyzerWithPropertiesAsync(
            Library, ReferenceAssemblies.Net.Net80, Properties(targetFramework), [Expected()]);
    }

    [Theory]
    [InlineData("netstandard2.0")]
    [InlineData("net6.0")]
    [InlineData("net472")]
    public async Task OlderTargetFramework_NoDiagnostic(string targetFramework)
    {
        // IsAotCompatible only means something from net8.0, and setting it earlier produces an SDK warning.
        await CSharpAnalyzerVerifier<MarkLibraryAotCompatibleAnalyzer>.VerifyAnalyzerWithPropertiesAsync(
            Library, ReferenceAssemblies.Net.Net80, Properties(targetFramework), []);
    }

    [Theory]
    [InlineData("IsAotCompatible", "true")]
    [InlineData("IsAotCompatible", "false")]
    [InlineData("PublishAot", "true")]
    [InlineData("EnableAotAnalyzer", "true")]
    [InlineData("IsTestProject", "true")]
    public async Task AlreadyDecidedOrTestProject_NoDiagnostic(string name, string value)
    {
        // An explicit IsAotCompatible=false is a deliberate opt-out, so it also stands the rule down.
        await CSharpAnalyzerVerifier<MarkLibraryAotCompatibleAnalyzer>.VerifyAnalyzerWithPropertiesAsync(
            Library, ReferenceAssemblies.Net.Net80, Properties("net8.0", (name, value)), []);
    }

    [Fact]
    public async Task Application_NoDiagnostic()
    {
        await CSharpAnalyzerVerifier<MarkLibraryAotCompatibleAnalyzer>.VerifyAnalyzerWithPropertiesAsync(
            App, ReferenceAssemblies.Net.Net80, Properties("net8.0"), [], OutputKind.ConsoleApplication);
    }
}
```

- [ ] **Step 6: Run the tests to verify they fail**

Run: `dotnet test tests/ZeroAlloc.Analyzers.Tests -c Release --filter "FullyQualifiedName~ZA1706"`
Expected: build error CS0246, `MarkLibraryAotCompatibleAnalyzer` not found.

- [ ] **Step 7: Write the analyzer**

Create `src/ZeroAlloc.Analyzers/Analyzers/MarkLibraryAotCompatibleAnalyzer.cs`:

```csharp
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace ZeroAlloc.Analyzers;

/// <summary>
/// Reports a net8.0+ library that never decided on IsAotCompatible. Until it does, the SDK's
/// trim and AOT analyzers are off, so AOT hazards surface only when an app publishes with it.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class MarkLibraryAotCompatibleAnalyzer : DiagnosticAnalyzer
{
    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticIds.MarkLibraryAotCompatible,
        "Mark libraries as AOT-compatible",
        "Library does not set IsAotCompatible, so the SDK's trim and AOT analyzers are off; set it for net8.0 and later target frameworks",
        DiagnosticCategories.Aot,
        DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        customTags: WellKnownDiagnosticTags.CompilationEnd);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [Rule];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(static start =>
        {
            // Decide up front, so apps, test projects and older frameworks register nothing.
            if (!AppliesTo(start.Compilation, start.Options))
                return;

            start.RegisterCompilationEndAction(static end => end.ReportDiagnostic(Diagnostic.Create(Rule, Location.None)));
        });
    }

    private static bool AppliesTo(Compilation compilation, AnalyzerOptions options) =>
        compilation.Options.OutputKind == OutputKind.DynamicallyLinkedLibrary
        && !AotHelper.IsBuildPropertyTrue(options, "IsTestProject")
        && TfmHelper.TryGetTfm(options, out var tfm)
        && TfmHelper.IsNet8OrLater(tfm)
        && !AotHelper.IsSdkAotAnalyzerEnabled(options)
        // An explicit IsAotCompatible=false is a decision too; only an unset property is reported.
        && !AotHelper.IsBuildPropertySet(options, "IsAotCompatible");
}
```

- [ ] **Step 8: Add the release-tracking row**

Append to the `### New Rules` table in `AnalyzerReleases.Unshipped.md`. If the file holds only its two header comment lines, first add `### New Rules`, a blank line, then the header row `Rule ID | Category                | Severity | Notes` and the separator `--------|-------------------------|----------|----------------------------------------`.

```
ZA1706  | Performance.Aot         | Info     | MarkLibraryAotCompatibleAnalyzer
```

- [ ] **Step 9: Run the tests to verify they pass**

Run: `dotnet test tests/ZeroAlloc.Analyzers.Tests -c Release --filter "FullyQualifiedName~ZA1706"`
Expected: PASS, 11 tests. Then run the full suite once; the `AotHelper` refactor must leave every ZA1701 to ZA1705 test green.

- [ ] **Step 10: Document the rule**

Append to `docs/rules/aot.md`, after the ZA1705 suppression block:

````markdown

---

## ZA1706 — Mark libraries as AOT-compatible {#za1706}

> **Severity**: Info | **Min TFM**: net8.0 | **Code fix**: No

### Why

The SDK's trim and AOT analyzers (`IL2xxx`, `IL3xxx`) only run when a project sets `IsAotCompatible`, `PublishAot` or `EnableAotAnalyzer`. A library that sets none of them can ship code that breaks under Native AOT, and nothing warns until an app publishes with it. Setting `IsAotCompatible` turns those analyzers on for the library itself, so AOT problems surface where they can be fixed.

The rule reports once per project, with no source location, for a net8.0 or later library that has never set `IsAotCompatible`. It stays silent for apps, test projects and older target frameworks. An explicit `<IsAotCompatible>false</IsAotCompatible>` also silences it, because that is a decision. The diagnostic shows on build and in full-solution analysis.

### Fix

```xml
<!-- In a multi-targeted library, set it only where it applies -->
<PropertyGroup Condition="$([MSBuild]::IsTargetFrameworkCompatible('$(TargetFramework)', 'net8.0'))">
  <IsAotCompatible>true</IsAotCompatible>
</PropertyGroup>
```

### Suppression

The diagnostic has no source location, so `#pragma` cannot silence it. Record the decision instead:

```xml
<IsAotCompatible>false</IsAotCompatible>
```

or turn the rule off in `.editorconfig`: `dotnet_diagnostic.ZA1706.severity = none`.
````

In `docs/getting-started.md`, add after the ZA1704 row in the Native AOT table:

```markdown
| [ZA1706](rules/aot.md#za1706) | Mark libraries as AOT-compatible | Info | net8.0 |
```

- [ ] **Step 11: Commit**

```bash
git add src tests docs/rules/aot.md docs/getting-started.md
git commit -m "feat: add ZA1706 to report libraries that never set IsAotCompatible" -m "Closes #82" -m "Co-Authored-By: <your model> <noreply@anthropic.com>"
```

---

### Task 2: ZA1707 — use the configuration-binding source generator (#83)

**Files:**
- Create: `src/ZeroAlloc.Analyzers/Analyzers/UseConfigurationBindingGeneratorAnalyzer.cs`
- Create: `tests/ZeroAlloc.Analyzers.Tests/ZA1707_UseConfigurationBindingGeneratorTests.cs`
- Modify: `src/ZeroAlloc.Analyzers.Package/buildTransitive/ZeroAlloc.Analyzers.props`, `DiagnosticIds.cs`, `AnalyzerReleases.Unshipped.md`, `docs/rules/aot.md`, `docs/getting-started.md`

**Interfaces:**
- Consumes:
  - `AotHelper.IsSdkAotAnalyzerEnabled(AnalyzerOptions)` and `AotHelper.IsBuildPropertyTrue(AnalyzerOptions, string)`, from Task 1.
  - `CSharpAnalyzerVerifier<T>.VerifyAnalyzerWithPropertiesAsync(...)`, from Task 1.
- Produces: `DiagnosticIds.UseConfigurationBindingGenerator = "ZA1707"`.

- [ ] **Step 1: Add the ID constant**

After `MarkLibraryAotCompatible = "ZA1706";`:

```csharp
    public const string UseConfigurationBindingGenerator = "ZA1707";
```

- [ ] **Step 2: Make EnableConfigurationBindingGenerator visible**

In `buildTransitive/ZeroAlloc.Analyzers.props`, after the `IsTestProject` item:

```xml
    <!-- Lets ZA1707 stand down once the configuration-binding source generator is on. -->
    <CompilerVisibleProperty Include="EnableConfigurationBindingGenerator" />
```

- [ ] **Step 3: Write the failing tests**

Create `tests/ZeroAlloc.Analyzers.Tests/ZA1707_UseConfigurationBindingGeneratorTests.cs`:

```csharp
using Microsoft.CodeAnalysis.Testing;
using ZeroAlloc.Analyzers.Tests.Verifiers;

namespace ZeroAlloc.Analyzers.Tests;

public class ZA1707_UseConfigurationBindingGeneratorTests
{
    private static readonly ReferenceAssemblies WithConfiguration = ReferenceAssemblies.Net.Net80.AddPackages([
        new PackageIdentity("Microsoft.Extensions.Configuration.Binder", "8.0.0"),
        new PackageIdentity("Microsoft.Extensions.Options.ConfigurationExtensions", "8.0.0")]);

    private static string Binding(string statement) => $$"""
        using Microsoft.Extensions.Configuration;
        using Microsoft.Extensions.DependencyInjection;

        class Settings
        {
            public string Name { get; set; } = "";
        }

        class C
        {
            void M(IConfiguration config, IServiceCollection services, Settings settings)
            {
                {{statement}}
            }
        }
        """;

    private static DiagnosticResult Expected(string api) =>
        CSharpAnalyzerVerifier<UseConfigurationBindingGeneratorAnalyzer>
            .Diagnostic(DiagnosticIds.UseConfigurationBindingGenerator)
            .WithLocation(0)
            .WithArguments(api);

    [Theory]
    [InlineData("{|#0:config.Bind(settings)|};", "ConfigurationBinder.Bind")]
    [InlineData("_ = {|#0:config.Get<Settings>()|};", "ConfigurationBinder.Get")]
    [InlineData("_ = {|#0:config.Get(typeof(Settings))|};", "ConfigurationBinder.Get")]
    [InlineData("_ = {|#0:config.GetValue<int>(\"Port\")|};", "ConfigurationBinder.GetValue")]
    [InlineData("{|#0:services.Configure<Settings>(config)|};", "OptionsConfigurationServiceCollectionExtensions.Configure")]
    [InlineData("{|#0:services.AddOptions<Settings>().Bind(config)|};", "OptionsBuilderConfigurationExtensions.Bind")]
    [InlineData("{|#0:services.AddOptions<Settings>().BindConfiguration(\"Settings\")|};", "OptionsBuilderConfigurationExtensions.BindConfiguration")]
    public async Task ReflectionBinding_Reports(string statement, string api)
    {
        await CSharpAnalyzerVerifier<UseConfigurationBindingGeneratorAnalyzer>
            .VerifyAnalyzerAsync(Binding(statement), "net8.0", WithConfiguration, Expected(api));
    }

    [Theory]
    [InlineData("EnableConfigurationBindingGenerator")]
    [InlineData("PublishAot")]
    [InlineData("IsAotCompatible")]
    public async Task GeneratorOnOrSdkAnalyzerOn_NoDiagnostic(string property)
    {
        // PublishAot turns the generator on by itself, and IsAotCompatible turns on the SDK's IL2026.
        await CSharpAnalyzerVerifier<UseConfigurationBindingGeneratorAnalyzer>.VerifyAnalyzerWithPropertiesAsync(
            Binding("config.Bind(settings);"),
            WithConfiguration,
            new Dictionary<string, string> { ["TargetFramework"] = "net8.0", [property] = "true" },
            []);
    }

    [Fact]
    public async Task ConfigureWithDelegate_NoDiagnostic()
    {
        // Configure(Action<T>) lives in OptionsServiceCollectionExtensions and binds nothing.
        await CSharpAnalyzerVerifier<UseConfigurationBindingGeneratorAnalyzer>.VerifyAnalyzerAsync(
            Binding("services.Configure<Settings>(s => s.Name = \"x\");"), "net8.0", WithConfiguration);
    }
}
```

- [ ] **Step 4: Run the tests to verify they fail**

Run: `dotnet test tests/ZeroAlloc.Analyzers.Tests -c Release --filter "FullyQualifiedName~ZA1707"`
Expected: build error CS0246, `UseConfigurationBindingGeneratorAnalyzer` not found.

- [ ] **Step 5: Write the analyzer**

Create `src/ZeroAlloc.Analyzers/Analyzers/UseConfigurationBindingGeneratorAnalyzer.cs`:

```csharp
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace ZeroAlloc.Analyzers;

/// <summary>
/// Reports reflection-based configuration binding while the configuration-binding source
/// generator is off. Stands down when the SDK's own AOT analyzer is enabled.
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

            var builder = ImmutableArray.CreateBuilder<(INamedTypeSymbol Type, string[] Methods)>();
            foreach (var (typeName, methods) in BindingApis)
            {
                if (start.Compilation.GetTypeByMetadataName(typeName) is { } type)
                    builder.Add((type, methods));
            }

            if (builder.Count == 0)
                return;

            var apis = builder.ToImmutable();
            start.RegisterOperationAction(context => AnalyzeInvocation(context, apis), OperationKind.Invocation);
        });
    }

    private static void AnalyzeInvocation(OperationAnalysisContext context, ImmutableArray<(INamedTypeSymbol Type, string[] Methods)> apis)
    {
        var method = ((IInvocationOperation)context.Operation).TargetMethod;
        foreach (var (type, methods) in apis)
        {
            if (SymbolEqualityComparer.Default.Equals(method.ContainingType, type) && Array.IndexOf(methods, method.Name) >= 0)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    Rule,
                    context.Operation.Syntax.GetLocation(),
                    $"{type.Name}.{method.Name}"));
                return;
            }
        }
    }
}
```

- [ ] **Step 6: Add the release-tracking row**

```
ZA1707  | Performance.Aot         | Info     | UseConfigurationBindingGeneratorAnalyzer
```

- [ ] **Step 7: Run the tests to verify they pass**

Run: `dotnet test tests/ZeroAlloc.Analyzers.Tests -c Release --filter "FullyQualifiedName~ZA1707"`
Expected: PASS, 11 tests.

- [ ] **Step 8: Document the rule**

Append to `docs/rules/aot.md`:

````markdown

---

## ZA1707 — Use the configuration-binding source generator {#za1707}

> **Severity**: Info | **Min TFM**: Any | **Code fix**: No

### Why

`ConfigurationBinder.Bind`, `Get` and `GetValue`, and the options helpers `Configure<T>(IConfiguration)`, `Bind` and `BindConfiguration`, walk your options type with reflection. That needs the type's members preserved from trimming, and it is slower than generated code. Since .NET 8, the configuration-binding source generator intercepts those calls and replaces them with generated code. Turning it on is one MSBuild property; the calls themselves stay the same.

`PublishAot` turns the generator on automatically, so the rule stays silent there, and it also stays silent once the SDK's own AOT analyzer is enabled.

### Fix

```xml
<PropertyGroup>
  <EnableConfigurationBindingGenerator>true</EnableConfigurationBindingGenerator>
</PropertyGroup>
```

### Suppression

```csharp
#pragma warning disable ZA1707
// or in .editorconfig: dotnet_diagnostic.ZA1707.severity = none
```
````

In `docs/getting-started.md`, add after the ZA1706 row:

```markdown
| [ZA1707](rules/aot.md#za1707) | Use the configuration-binding source generator | Info | Any |
```

- [ ] **Step 9: Commit**

```bash
git add src tests docs/rules/aot.md docs/getting-started.md
git commit -m "feat: add ZA1707 to suggest the configuration-binding source generator" -m "Closes #83" -m "Co-Authored-By: <your model> <noreply@anthropic.com>"
```

---

### Task 3: ZA1708 — use [OptionsValidator] instead of ValidateDataAnnotations (#84)

**Files:**
- Create: `src/ZeroAlloc.Analyzers/Analyzers/UseOptionsValidatorGeneratorAnalyzer.cs`
- Create: `tests/ZeroAlloc.Analyzers.Tests/ZA1708_UseOptionsValidatorGeneratorTests.cs`
- Modify: `DiagnosticIds.cs`, `AnalyzerReleases.Unshipped.md`, `docs/rules/aot.md`, `docs/getting-started.md`

**Interfaces:**
- Consumes: `AotHelper.IsSdkAotAnalyzerEnabled` and `CSharpAnalyzerVerifier<T>.VerifyAnalyzerWithPropertiesAsync`, both from Task 1.
- Produces: `DiagnosticIds.UseOptionsValidatorGenerator = "ZA1708"`.

- [ ] **Step 1: Add the ID constant**

After `UseConfigurationBindingGenerator = "ZA1707";`:

```csharp
    public const string UseOptionsValidatorGenerator = "ZA1708";
```

- [ ] **Step 2: Write the failing tests**

Create `tests/ZeroAlloc.Analyzers.Tests/ZA1708_UseOptionsValidatorGeneratorTests.cs`:

```csharp
using Microsoft.CodeAnalysis.Testing;
using ZeroAlloc.Analyzers.Tests.Verifiers;

namespace ZeroAlloc.Analyzers.Tests;

public class ZA1708_UseOptionsValidatorGeneratorTests
{
    private static readonly ReferenceAssemblies WithOptions = ReferenceAssemblies.Net.Net80.AddPackages([
        new PackageIdentity("Microsoft.Extensions.Options.DataAnnotations", "8.0.0")]);

    private const string Source = """
        using Microsoft.Extensions.DependencyInjection;

        class Settings
        {
            public string Name { get; set; } = "";
        }

        class C
        {
            void M(IServiceCollection services)
            {
                {|#0:services.AddOptions<Settings>().ValidateDataAnnotations()|};
            }
        }
        """;

    [Fact]
    public async Task ValidateDataAnnotations_Reports()
    {
        var expected = CSharpAnalyzerVerifier<UseOptionsValidatorGeneratorAnalyzer>
            .Diagnostic(DiagnosticIds.UseOptionsValidatorGenerator)
            .WithLocation(0);

        await CSharpAnalyzerVerifier<UseOptionsValidatorGeneratorAnalyzer>
            .VerifyAnalyzerAsync(Source, "net8.0", WithOptions, expected);
    }

    [Theory]
    [InlineData("PublishAot")]
    [InlineData("IsAotCompatible")]
    public async Task SdkAotAnalyzerOn_NoDiagnostic(string property)
    {
        // With the SDK's AOT analyzer on, IL2026 already reports ValidateDataAnnotations.
        await CSharpAnalyzerVerifier<UseOptionsValidatorGeneratorAnalyzer>.VerifyAnalyzerWithPropertiesAsync(
            Source.Replace("{|#0:", string.Empty).Replace("|};", ";"),
            WithOptions,
            new Dictionary<string, string> { ["TargetFramework"] = "net8.0", [property] = "true" },
            []);
    }
}
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet test tests/ZeroAlloc.Analyzers.Tests -c Release --filter "FullyQualifiedName~ZA1708"`
Expected: build error CS0246, `UseOptionsValidatorGeneratorAnalyzer` not found.

- [ ] **Step 4: Write the analyzer**

Create `src/ZeroAlloc.Analyzers/Analyzers/UseOptionsValidatorGeneratorAnalyzer.cs`:

```csharp
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace ZeroAlloc.Analyzers;

/// <summary>
/// Reports ValidateDataAnnotations, which validates options with reflection, and points at the
/// [OptionsValidator] source generator. Stands down when the SDK's own AOT analyzer is enabled.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class UseOptionsValidatorGeneratorAnalyzer : DiagnosticAnalyzer
{
    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticIds.UseOptionsValidatorGenerator,
        "Use a source-generated options validator",
        "'ValidateDataAnnotations' validates with reflection; use an [OptionsValidator] source-generated validator instead",
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

            var extensions = start.Compilation.GetTypeByMetadataName(
                "Microsoft.Extensions.DependencyInjection.OptionsBuilderDataAnnotationsExtensions");
            if (extensions is null)
                return;

            start.RegisterOperationAction(context =>
            {
                var method = ((IInvocationOperation)context.Operation).TargetMethod;
                if (method.Name == "ValidateDataAnnotations"
                    && SymbolEqualityComparer.Default.Equals(method.ContainingType, extensions))
                {
                    context.ReportDiagnostic(Diagnostic.Create(Rule, context.Operation.Syntax.GetLocation()));
                }
            }, OperationKind.Invocation);
        });
    }
}
```

- [ ] **Step 5: Add the release-tracking row**

```
ZA1708  | Performance.Aot         | Info     | UseOptionsValidatorGeneratorAnalyzer
```

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test tests/ZeroAlloc.Analyzers.Tests -c Release --filter "FullyQualifiedName~ZA1708"`
Expected: PASS, 3 tests.

- [ ] **Step 7: Document the rule**

Append to `docs/rules/aot.md`:

````markdown

---

## ZA1708 — Use a source-generated options validator {#za1708}

> **Severity**: Info | **Min TFM**: Any | **Code fix**: No

### Why

`ValidateDataAnnotations()` reads your options type's `[Required]`, `[Range]` and other attributes with reflection every time the options are validated. That is not trim-safe, and it costs reflection at startup. Since .NET 8, the `[OptionsValidator]` source generator writes the same checks as plain code: declare a `partial` validator class for your options type and register it instead.

### Before

```csharp
// ❌ reflection over the attributes at validation time
services.AddOptions<SmtpOptions>()
    .BindConfiguration("Smtp")
    .ValidateDataAnnotations();
```

### After

```csharp
// ✓ the generator writes the validation code
[OptionsValidator]
public partial class SmtpOptionsValidator : IValidateOptions<SmtpOptions>;

services.AddOptions<SmtpOptions>().BindConfiguration("Smtp");
services.AddSingleton<IValidateOptions<SmtpOptions>, SmtpOptionsValidator>();
```

### Suppression

```csharp
#pragma warning disable ZA1708
// or in .editorconfig: dotnet_diagnostic.ZA1708.severity = none
```
````

In `docs/getting-started.md`, add after the ZA1707 row:

```markdown
| [ZA1708](rules/aot.md#za1708) | Use a source-generated options validator | Info | Any |
```

- [ ] **Step 8: Commit**

```bash
git add src tests docs/rules/aot.md docs/getting-started.md
git commit -m "feat: add ZA1708 to suggest OptionsValidator over ValidateDataAnnotations" -m "Closes #84" -m "Co-Authored-By: <your model> <noreply@anthropic.com>"
```

---

### Task 4: ZA1709 — avoid dynamic dispatch (#85)

**Files:**
- Create: `src/ZeroAlloc.Analyzers/Analyzers/AvoidDynamicAnalyzer.cs`
- Create: `tests/ZeroAlloc.Analyzers.Tests/ZA1709_AvoidDynamicTests.cs`
- Modify: `DiagnosticIds.cs`, `AnalyzerReleases.Unshipped.md`, `docs/rules/aot.md`, `docs/getting-started.md`

**Interfaces:**
- Consumes: `AotHelper.IsSdkAotAnalyzerEnabled` and `CSharpAnalyzerVerifier<T>.VerifyAnalyzerWithPropertiesAsync`, both from Task 1.
- Produces: `DiagnosticIds.AvoidDynamic = "ZA1709"`.

- [ ] **Step 1: Add the ID constant**

After `UseOptionsValidatorGenerator = "ZA1708";`:

```csharp
    public const string AvoidDynamic = "ZA1709";
```

- [ ] **Step 2: Write the failing tests**

Create `tests/ZeroAlloc.Analyzers.Tests/ZA1709_AvoidDynamicTests.cs`:

```csharp
using Microsoft.CodeAnalysis.Testing;
using ZeroAlloc.Analyzers.Tests.Verifiers;

namespace ZeroAlloc.Analyzers.Tests;

public class ZA1709_AvoidDynamicTests
{
    private static string WithDynamic(string body) => $$"""
        using System;

        class C
        {
            object M(dynamic d, object c)
            {
                {{body}}
            }
        }
        """;

    private static DiagnosticResult Expected() =>
        CSharpAnalyzerVerifier<AvoidDynamicAnalyzer>.Diagnostic(DiagnosticIds.AvoidDynamic).WithLocation(0);

    [Theory]
    [InlineData("return {|#0:d.Name|};")]
    [InlineData("return {|#0:d.Compute(1, 2)|};")]
    [InlineData("return {|#0:d[0]|};")]
    [InlineData("return {|#0:d.A.B(c)|};")]
    [InlineData("return {|#0:d + 1|};")]
    [InlineData("int n = {|#0:d.Count|}; return n;")]
    [InlineData("{|#0:d.Count++|}; return null;")]
    [InlineData("{|#0:Console.WriteLine(d)|}; return null;")]
    [InlineData("return {|#0:new Uri(d)|};")]
    public async Task DynamicDispatch_ReportsOncePerExpression(string body)
    {
        // Each case is one expression; nested dynamic operations report only at the outermost one.
        await CSharpAnalyzerVerifier<AvoidDynamicAnalyzer>.VerifyAnalyzerAsync(WithDynamic(body), "net8.0", Expected());
    }

    [Theory]
    [InlineData("dynamic x = new object(); return x;")]
    [InlineData("dynamic x = c; x = 5; return x;")]
    [InlineData("return d;")]
    public async Task DynamicWithoutDispatch_NoDiagnostic(string body)
    {
        // Storing into, assigning or returning a dynamic value does not call the runtime binder.
        await CSharpAnalyzerVerifier<AvoidDynamicAnalyzer>.VerifyNoDiagnosticAsync(WithDynamic(body), "net8.0");
    }

    [Theory]
    [InlineData("PublishAot")]
    [InlineData("IsAotCompatible")]
    public async Task SdkAotAnalyzerOn_NoDiagnostic(string property)
    {
        await CSharpAnalyzerVerifier<AvoidDynamicAnalyzer>.VerifyAnalyzerWithPropertiesAsync(
            WithDynamic("return d.Name;"),
            ReferenceAssemblies.Net.Net80,
            new Dictionary<string, string> { ["TargetFramework"] = "net8.0", [property] = "true" },
            []);
    }
}
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet test tests/ZeroAlloc.Analyzers.Tests -c Release --filter "FullyQualifiedName~ZA1709"`
Expected: build error CS0246, `AvoidDynamicAnalyzer` not found.

- [ ] **Step 4: Write the analyzer**

Create `src/ZeroAlloc.Analyzers/Analyzers/AvoidDynamicAnalyzer.cs`:

```csharp
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
        "'dynamic' dispatch uses the runtime binder, which is not supported under NativeAOT",
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
                OperationKind.Conversion);
        });
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
        // Converting a dynamic value to a static type binds the conversion at run time;
        // converting to object or dynamic does not.
        IConversionOperation conversion => IsDynamic(conversion.Operand.Type)
            && !IsDynamic(conversion.Type)
            && conversion.Type?.SpecialType != SpecialType.System_Object,
        _ => false,
    };

    private static bool IsDynamic(ITypeSymbol? type) => type?.TypeKind == TypeKind.Dynamic;
}
```

- [ ] **Step 5: Add the release-tracking row**

```
ZA1709  | Performance.Aot         | Info     | AvoidDynamicAnalyzer
```

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test tests/ZeroAlloc.Analyzers.Tests -c Release --filter "FullyQualifiedName~ZA1709"`
Expected: PASS, 14 tests.

- [ ] **Step 7: Document the rule**

Append to `docs/rules/aot.md`:

````markdown

---

## ZA1709 — Avoid dynamic dispatch {#za1709}

> **Severity**: Info | **Min TFM**: Any | **Code fix**: No

### Why

Every operation on a `dynamic` value is bound at run time by the C# runtime binder. That covers member access, method calls, indexers, operators, and conversions back to a static type. The binder inspects the object with reflection and generates code on the fly, which Native AOT cannot do and which trimming breaks. Dynamic dispatch is also far slower than a static call and allocates on every operation.

The rule reports each expression that dispatches dynamically once, at its outermost dynamic operation. Declaring, storing or returning a `dynamic` value is not reported, because none of that calls the binder.

### Before

```csharp
// ❌ every line goes through the runtime binder
dynamic response = JsonConvert.DeserializeObject(json);
string name = response.user.name;
```

### After

```csharp
// ✓ a typed model binds at compile time
var response = JsonSerializer.Deserialize(json, AppJsonContext.Default.Response);
string name = response.User.Name;
```

### Suppression

```csharp
#pragma warning disable ZA1709
// or in .editorconfig: dotnet_diagnostic.ZA1709.severity = none
```
````

In `docs/getting-started.md`, add after the ZA1708 row:

```markdown
| [ZA1709](rules/aot.md#za1709) | Avoid dynamic dispatch | Info | Any |
```

- [ ] **Step 8: Commit**

```bash
git add src tests docs/rules/aot.md docs/getting-started.md
git commit -m "feat: add ZA1709 to report dynamic dispatch" -m "Closes #85" -m "Co-Authored-By: <your model> <noreply@anthropic.com>"
```

---

### Task 5: Rule counts, AOT overview and spec refinements

**Files:**
- Modify: `README.md`, `docs/getting-started.md`, `docs/performance.md`, `docs/rules/aot.md`, `docs/plans/2026-10-09-zero-alloc-aot-batch-3-design.md`

- [ ] **Step 1: Update rule counts**

The four rules take the total from 59 to 63. The getting-started mermaid graph counts enabled-by-default rules, so Native AOT goes from `ZA17xx (4 rules)` to `ZA17xx (8 rules)`. Activity per target:

| Rule | net8.0 | bare netstandard2.0 | Reason |
|---|---|---|---|
| ZA1706 | active | not active | Gated on net8.0 |
| ZA1707 | active | not active | Its package APIs exist only with the Microsoft.Extensions packages |
| ZA1708 | active | not active | Its package APIs exist only with the Microsoft.Extensions packages |
| ZA1709 | active | active | No gate |

So:
- `README.md` and `docs/getting-started.md`: every `59 rules` becomes `63 rules`.
- `README.md` documentation table, Native AOT row: `Expression.Compile, Reflection.Emit, runtime generics, reflection serializers, name-based loading, AOT-compatible libraries, configuration binding, options validation, dynamic`.
- `README.md` performance table and `docs/performance.md`:
  - `57 of 59` becomes `61 of 63`.
  - `40 of 59, or 42 with the System.Memory package` becomes `41 of 63, or 43 with the System.Memory package`.
  - The multi-TFM row `57 / 40 per TFM, or 57 / 42 with System.Memory` becomes `61 / 41 per TFM, or 61 / 43 with System.Memory`.
  - `55 of 59` becomes `59 of 63`.
  - In the `docs/performance.md` prose, add that ZA1707 and ZA1708 run only where the Microsoft.Extensions configuration and options packages are referenced, and that ZA1706 needs net8.0.
  - Leave the timing columns unchanged.

Run: `grep -rnE "59 rules|of 59|57 / 40|ZA17xx \(4 rules\)" README.md docs --include=*.md | grep -v "docs/plans/"`
Expected: no output.

- [ ] **Step 2: Update the AOT rules intro**

In `docs/rules/aot.md`, add after the "Several existing ZeroAlloc rules already steer toward AOT-friendly code" paragraph:

```markdown
ZA1706 to ZA1709 cover the next layer: a library that has not switched the SDK analyzers on at all (ZA1706), and the reflection-based helpers in `Microsoft.Extensions` that have a source-generated replacement (ZA1707 and ZA1708), plus `dynamic` (ZA1709).
```

- [ ] **Step 3: Record the refinements in the design doc**

In `docs/plans/2026-10-09-zero-alloc-aot-batch-3-design.md`:
- Under **ZA1706 → Detection**, add: `An explicit IsAotCompatible=false also stands the rule down, because it is a decision; only an unset property is reported. The conditions are checked at compilation start, so apps, test projects and older target frameworks register no callbacks.`
- Under **ZA1709 → Detection**, add: `Besides the four dynamic operation kinds, binary and unary operators, compound assignments, increments and decrements on dynamic operands, and conversions from dynamic to a type other than object or dynamic, are reported too, because each goes through the runtime binder. The outermost-only rule applies across all of them.`

- [ ] **Step 4: Run the full suite and build**

Run: `dotnet build -c Release && dotnet test tests/ZeroAlloc.Analyzers.Tests -c Release`
Expected: build succeeds with 0 warnings, and all tests pass.

- [ ] **Step 5: Commit**

```bash
git add README.md docs
git commit -m "docs: update rule counts for the batch 3 AOT rules and record their refinements" -m "Co-Authored-By: <your model> <noreply@anthropic.com>"
```

Push and PR are done by the controller after the final review. The PR body carries:

```
BEGIN_COMMIT_OVERRIDE
feat: add ZA1706 to report libraries that never set IsAotCompatible
feat: add ZA1707 to suggest the configuration-binding source generator
feat: add ZA1708 to suggest OptionsValidator over ValidateDataAnnotations
feat: add ZA1709 to report dynamic dispatch
END_COMMIT_OVERRIDE
```
