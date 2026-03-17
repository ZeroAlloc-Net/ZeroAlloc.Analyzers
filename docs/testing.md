---
id: testing
title: Testing with Analyzers
slug: /docs/testing
description: Suppress warnings in tests, write diagnostic unit tests with Roslyn test helpers, and verify TFM-gated rules.
sidebar_position: 17
---

# Testing with Analyzers

This page covers two distinct concerns: suppressing ZeroAlloc.Analyzers warnings in your own test projects that intentionally use allocation-heavy patterns, and writing Roslyn diagnostic tests that verify ZeroAlloc.Analyzers rules fire (or do not fire) on specific code patterns.

---

## Suppressing warnings in test code

Test helpers, fixture setup, and scenario-building code often intentionally use patterns that ZeroAlloc.Analyzers would flag. A `List<string>` built in a loop for a test fixture, a `Dictionary` that is not frozen because it is populated dynamically — these are correct for the purpose of the test.

### Suppress a single occurrence with a pragma

```csharp
#pragma warning disable ZA0107 // pre-sizing not needed: test fixture with unknown count
var items = new List<string>();
foreach (var name in testNames)
    items.Add(name);
#pragma warning restore ZA0107
```

Always include a short justification comment after the rule ID. It helps reviewers understand why the suppression is intentional rather than accidental.

### Suppress a method with [SuppressMessage]

```csharp
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Performance.Collections",
    "ZA0601:AvoidLinqInLoops",
    Justification = "Test helper — readability takes priority over allocation performance.")]
private static IEnumerable<int> BuildTestSequence(int count)
    => Enumerable.Range(0, count).Select(i => i * i).ToList();
```

The first argument is the diagnostic category and the second is `"<RuleId>:<Title>"`. The title portion after the colon is optional but helps audit tools.

### Suppress all rules for test files via .editorconfig

The most scalable approach for test projects is to use `.editorconfig` to suppress specific rules across all test files:

```ini
# .editorconfig at the repository root (or test project root)

[**/*Tests.cs]
dotnet_diagnostic.ZA0601.severity = none
dotnet_diagnostic.ZA0107.severity = none

[**/*Fixture.cs]
dotnet_diagnostic.ZA0201.severity = none
```

This keeps the suppressions out of source code and makes them auditable in a single file. See the [Configuration guide](configuration.md) for the full reference on `.editorconfig` scoping.

---

## Writing Roslyn diagnostic tests

ZeroAlloc.Analyzers' own test suite uses `Microsoft.CodeAnalysis.CSharp.Analyzer.Testing` and `Microsoft.CodeAnalysis.CSharp.CodeFix.Testing` from the `Microsoft.CodeAnalysis.Testing` NuGet family. These packages provide a fluent API for writing in-process Roslyn compilation tests.

### Adding the test packages

```xml
<PackageReference Include="Microsoft.CodeAnalysis.CSharp.Analyzer.Testing" Version="1.1.2" />
<PackageReference Include="Microsoft.CodeAnalysis.CSharp.CodeFix.Testing" Version="1.1.2" />
<PackageReference Include="Microsoft.CodeAnalysis.CSharp" Version="4.14.0" />
```

### The verifier helpers

ZeroAlloc.Analyzers ships two thin verifier wrappers in its test project that you can copy to your own test projects:

**`CSharpAnalyzerVerifier<TAnalyzer>`** — verifies that an analyzer produces (or does not produce) expected diagnostics:

```csharp
// VerifyAnalyzerAsync: asserts that the listed diagnostics ARE reported
await CSharpAnalyzerVerifier<MyAnalyzer>.VerifyAnalyzerAsync(source, "net8.0", expected);

// VerifyNoDiagnosticAsync: asserts that NO diagnostics are reported
await CSharpAnalyzerVerifier<MyAnalyzer>.VerifyNoDiagnosticAsync(source, "net6.0");
```

**`CSharpCodeFixVerifier<TAnalyzer, TCodeFix>`** — verifies that a code fix transforms the source correctly:

```csharp
await CSharpCodeFixVerifier<MyAnalyzer, MyCodeFix>.VerifyCodeFixAsync(
    source: originalSource,
    fixedSource: expectedFixedSource,
    diagnosticId: "ZA0109",
    targetFramework: "net8.0");
```

Both verifiers inject the TFM into the compilation via a global config entry (`build_property.TargetFramework`), which is the same mechanism the real MSBuild integration uses. This means TFM-gating works correctly in tests with no special setup beyond passing the `targetFramework` string.

---

## Verifying a warning IS raised

Use the `{|#N:..|}` markup syntax to annotate the expected diagnostic location inside the test source string:

```csharp
[Fact]
public async Task ReadonlyDictField_InitializedInConstructor_Reports()
{
    var source = """
        using System.Collections.Generic;

        class C
        {
            private readonly Dictionary<string, int> {|#0:_lookup|};

            C() { _lookup = new() { ["a"] = 1 }; }

            int Get(string key) => _lookup[key];
        }
        """;

    var expected = CSharpAnalyzerVerifier<UseFrozenDictionaryAnalyzer>
        .Diagnostic(DiagnosticIds.UseFrozenDictionary)
        .WithLocation(0)
        .WithArguments("_lookup");

    await CSharpAnalyzerVerifier<UseFrozenDictionaryAnalyzer>
        .VerifyAnalyzerAsync(source, "net8.0", expected);
}
```

The `{|#0:_lookup|}` markup marks the span that should be underlined by the diagnostic. `WithLocation(0)` matches the marker index. `WithArguments(...)` matches the format arguments in the rule's message template.

If the analyzer reports a diagnostic at the wrong location or with wrong arguments, the test fails with a detailed diff.

---

## Verifying a warning is NOT raised

Call `VerifyNoDiagnosticAsync` with code that should be clean. The test fails if any diagnostic is reported:

```csharp
[Fact]
public async Task MutableDict_WithAddCalls_NoDiagnostic()
{
    var source = """
        using System.Collections.Generic;

        class C
        {
            private readonly Dictionary<string, int> _lookup = new();

            void AddItem(string key, int value) => _lookup.Add(key, value);
        }
        """;

    await CSharpAnalyzerVerifier<UseFrozenDictionaryAnalyzer>
        .VerifyNoDiagnosticAsync(source);
}
```

No-false-positive tests are as important as positive tests. They verify that the analyzer does not fire on valid, intentional uses of a pattern.

---

## TFM-specific testing

To confirm a rule is gated off on one TFM and on for another, write two complementary tests:

```csharp
[Fact]
public async Task FrozenDict_OnNet8_Reports()
{
    var source = """
        using System.Collections.Generic;
        class C
        {
            private readonly Dictionary<string, int> {|#0:_map|} = new() { ["a"] = 1 };
            int Get(string k) => _map[k];
        }
        """;

    var expected = CSharpAnalyzerVerifier<UseFrozenDictionaryAnalyzer>
        .Diagnostic(DiagnosticIds.UseFrozenDictionary)
        .WithLocation(0)
        .WithArguments("_map");

    await CSharpAnalyzerVerifier<UseFrozenDictionaryAnalyzer>
        .VerifyAnalyzerAsync(source, "net8.0", expected);
}

[Fact]
public async Task FrozenDict_OnNet6_NoDiagnostic()
{
    var source = """
        using System.Collections.Generic;
        class C
        {
            private readonly Dictionary<string, int> _map = new() { ["a"] = 1 };
            int Get(string k) => _map[k];
        }
        """;

    // FrozenDictionary requires net8.0 — rule must be silent on net6.0
    await CSharpAnalyzerVerifier<UseFrozenDictionaryAnalyzer>
        .VerifyNoDiagnosticAsync(source, "net6.0");
}
```

For rules with an upper-bound TFM gate (such as ZA0801, which fires only below `net7.0`), reverse the test pair: assert the diagnostic fires on `net6.0` and does not fire on `net7.0`.

### Passing custom reference assemblies

When a test requires types that exist only in specific SDK versions (such as `FrozenDictionary<,>` from `net8.0`), pass the appropriate `ReferenceAssemblies` instance to the verifier overload:

```csharp
await CSharpAnalyzerVerifier<UseFrozenDictionaryAnalyzer>.VerifyAnalyzerAsync(
    source,
    targetFramework: "net8.0",
    referenceAssemblies: ReferenceAssemblies.Net.Net80,
    expected);
```

`ReferenceAssemblies.Net.Net80` is provided by the `Microsoft.CodeAnalysis.CSharp.Analyzer.Testing` package and downloads the correct reference assemblies on first use (cached in the NuGet package cache). The default verifier overload already uses `Net80` as its baseline; override it only when testing rules that target older TFMs and need the correct API surface for that target.

---

## Running the tests

ZeroAlloc.Analyzers uses xUnit as the test framework. Run all tests from the solution root:

```bash
dotnet test tests/ZeroAlloc.Analyzers.Tests
```

All analyzer and code-fix tests run in process — no separate compilation step or MSBuild invocation is required. Tests complete in seconds even for large rule suites because the Roslyn test framework reuses the compiler infrastructure across test cases.
