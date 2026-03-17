---
id: performance
title: Build Performance
slug: /docs/performance
description: How ZeroAlloc.Analyzers minimizes build-time overhead through incremental analysis and TFM-aware rule gating.
sidebar_position: 16
---

# Build Performance

ZeroAlloc.Analyzers is designed to add negligible overhead to your build. This page explains how the analyzer minimizes its impact and how to configure your builds for the fastest possible iteration cycle.

---

## How Roslyn analyzers affect build time

Every Roslyn diagnostic analyzer runs inside the compiler's analyzer host during the compilation phase. The cost of running analyzers falls into two categories:

- **Analyzer host overhead** — loading the analyzer assembly, calling `Initialize`, and registering syntax/symbol callbacks. This happens once per compilation. ZeroAlloc.Analyzers registers all its callbacks in a single `RegisterCompilationStartAction` per analyzer, so this cost is proportional to the number of analyzer types (43 analyzers), not the number of rules.
- **Analysis work** — walking syntax trees, resolving symbols, and reporting diagnostics. This runs on every compilation unit that changes. The Roslyn compiler caches unchanged compilation units; analyzers only re-run on files that the compiler already needs to reprocess.

For most projects, analyzer overhead is a small fraction of total build time compared to the C# compiler's own semantic analysis and code generation work.

---

## ZeroAlloc.Analyzers' approach to minimizing overhead

### TFM gating at initialization time

Each TFM-gated analyzer checks the target framework once during `RegisterCompilationStartAction` and skips registering callbacks entirely when the TFM is ineligible:

```csharp
context.RegisterCompilationStartAction(compilationContext =>
{
    if (!TfmHelper.TryGetTfm(compilationContext.Options, out var tfm)
        || !TfmHelper.IsNet8OrLater(tfm))
        return; // no callbacks registered — zero overhead for this TFM

    compilationContext.RegisterSymbolAction(ctx => AnalyzeField(ctx), SymbolKind.Field);
});
```

When the rule does not apply to the current TFM, the analyzer produces zero diagnostics and zero per-file analysis work. There are no null checks, no skipped-but-called callbacks, and no diagnostic suppression — the callback simply does not exist for that compilation.

### TFM value read from CompilerVisibleProperty

The TFM is read from a `build_property.TargetFramework` global config entry, which is populated by `ZeroAlloc.Analyzers.props` via a `CompilerVisibleProperty` MSBuild item:

```xml
<ItemGroup>
  <CompilerVisibleProperty Include="TargetFramework" />
</ItemGroup>
```

This is a zero-cost mechanism: the value is passed to the analyzer as part of the existing global config infrastructure and does not require any additional file I/O or process communication.

### Symbol-level analysis, not syntax-level where possible

Analyzers that check type declarations (such as `UseFrozenDictionaryAnalyzer`) register at the `SymbolKind.Field` level rather than walking every `SyntaxNode` in the file. Symbol-level callbacks are triggered by the compiler only after it has already built the symbol graph, meaning the analyzer gets a pre-filtered view of the code and does not need to implement its own filtering logic over raw syntax trees.

Analyzers that must inspect syntax patterns (such as loop-body analysis for ZA0601) register `RegisterSyntaxNodeAction` for `InvocationExpression` nodes, performing a lightweight syntactic walk up to the nearest enclosing loop rather than entire files.

---

## Multi-TFM builds

When your project uses `<TargetFrameworks>` (plural), MSBuild invokes the compiler once per TFM. ZeroAlloc.Analyzers runs independently in each compilation with the correct TFM value injected via the `.props` file.

This means:

- A `net8.0` build runs all 43 rules.
- A `net6.0` build automatically skips ZA0101, ZA0102, ZA0104, ZA0205, ZA0701, ZA0801, and ZA1001 — any rule whose minimum TFM is higher than `net6.0`.
- A `netstandard2.0` build skips all TFM-gated rules and runs only the 29 rules whose minimum TFM is `Any`.

For CI pipelines that build multiple TFMs in parallel, the per-TFM cost is independent: each TFM compilation gets exactly the rule set that applies to it, with no wasted work.

---

## Tips for fast builds

### EnforceCodeStyleInBuild

The `<EnforceCodeStyleInBuild>` MSBuild property controls whether `.editorconfig`-defined code style rules (IDE\* diagnostics) run during `dotnet build`. ZeroAlloc.Analyzers rules are not IDE-style rules — they are standard Roslyn diagnostics and always run during build regardless of this setting.

If you set `<EnforceCodeStyleInBuild>false</EnforceCodeStyleInBuild>`, you reduce the load from built-in IDE analyzers but ZeroAlloc rules still fire. Combining `<EnforceCodeStyleInBuild>false</EnforceCodeStyleInBuild>` with severity suppression of ZA `Info`-level rules in your local `.editorconfig` gives the fastest local build experience while keeping `Warning`-level enforcement active.

### Running analyzers only in CI

If you want zero analyzer overhead during local iteration and full analysis only in CI, use an MSBuild condition to disable all analyzers locally:

```xml
<!-- Directory.Build.props -->
<PropertyGroup Condition="'$(CI)' != 'true'">
  <RunAnalyzers>false</RunAnalyzers>
</PropertyGroup>
```

Most CI systems (GitHub Actions, Azure DevOps, GitLab CI) set `CI=true` automatically. With `RunAnalyzers` set to `false`, Roslyn does not load or execute any analyzer assemblies, including ZeroAlloc.Analyzers.

Alternatively, disable only ZeroAlloc rules while keeping other analyzer packages active:

```ini
# .editorconfig (local override — do not commit)
[*.cs]
dotnet_analyzer_diagnostic.category-Performance.severity = none
```

Place this in a `.editorconfig` that is git-ignored for local use only.

### Per-rule severity tuning to suppress expensive rules in debug builds

`Info`-severity rules do not appear in build output but still run analysis work. If specific rules are slow on your codebase (such as ZA0607, which performs a method-scoped syntactic scan grouping all foreach statements over the same IEnumerable local to detect multiple enumeration), you can demote them further or disable them for non-CI builds:

```xml
<!-- Directory.Build.props -->
<PropertyGroup Condition="'$(Configuration)' == 'Debug' and '$(CI)' != 'true'">
  <!-- Disable the heavier data-flow rules during local Debug builds -->
  <NoWarn>$(NoWarn);ZA0607;ZA0502</NoWarn>
</PropertyGroup>
```

This keeps the rules active in Release and CI builds where correctness matters, while keeping local Debug cycles fast.

### Parallel builds

Roslyn analyzers run concurrently by default when `context.EnableConcurrentExecution()` is called in `Initialize`. All ZeroAlloc.Analyzers call `EnableConcurrentExecution()`, so they participate in parallel analysis across compilation units. Ensure your build environment does not artificially limit thread count (`/maxcpucount` in MSBuild) if build time is a concern.

---

## When to disable in CI vs local development

| Scenario | Recommendation |
|---|---|
| Local `Debug` fast iteration | Keep `Warning`-severity rules; suppress `Info` via `.editorconfig` |
| Local full check before PR | Run with default settings — mirrors what CI will see |
| CI pull request validation | Full analysis; consider promoting key rules to `error` |
| CI release build | Full analysis with `TreatWarningsAsErrors` for zero-tolerance enforcement |
| Generated code directories | Silence all rules via `.editorconfig` glob pattern |
| Legacy/unmaintained project | Disable the package entirely via `<RunAnalyzers>false</RunAnalyzers>` in a local override |

The recommended approach is to never fully disable ZeroAlloc.Analyzers in CI. The per-rule TFM gating ensures you only pay for rules that are actionable for your target, and the incremental Roslyn caching ensures unchanged files are never re-analyzed.
