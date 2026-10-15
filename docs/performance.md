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
- **Analysis work** — walking syntax trees, resolving symbols, and reporting diagnostics. In a `dotnet build`, this runs over the whole project every time the compiler runs. Changing one file reruns the compiler for the whole project, and the analyzers with it: the measurements below show the same analyzer time for a one-file edit as for a full rebuild. Only a build in which no input changed skips the compiler, and the analyzers, entirely.

For most projects, analyzer overhead is a small fraction of total build time compared to the C# compiler's own semantic analysis and code generation work.

---

## Measured overhead

Measured 2026-10-15 with ZeroAlloc.Analyzers 1.11.0 on a sample project: 40 copies of six source
files, 240 files and about 18,000 lines, written to trigger a broad mix of rules. Every figure is
relative to the same project built without the package.

> Build time is shown as a percentage of the same project's build without the analyzer, because
> the measuring host is a shared VM; absolute timings are not published.

| Scenario | Build | Overhead, median | Overhead, IQR | ZA analyzer time, share of build |
|---|---|---:|---:|---:|
| `netstandard2.0` | full | +10.5% | +2.9% to +19.1% | 12.6% |
| `netstandard2.0` | one-file edit | +23.0% | +18.3% to +28.8% | 14.8% |
| `netstandard2.0` | no-op | -3.9% | -6.3% to +11.6% | 0.0% |
| `net8.0` | full | +2.1% | -2.0% to +6.6% | 6.0% |
| `net8.0` | one-file edit | +2.9% | +1.0% to +8.7% | 6.5% |
| `net8.0` | no-op | +5.2% | -11.1% to +10.5% | 0.0% |
| `net8.0` + `netstandard2.0` | full | +4.3% | +1.5% to +7.2% | 14.1% |
| `net8.0` + `netstandard2.0` | one-file edit | +5.4% | +3.8% to +9.7% | 18.8% |
| `net8.0` + `netstandard2.0` | no-op | +2.2% | -4.4% to +5.6% | 0.0% |
| `net8.0`, ZA0607 and ZA0502 off | full | +1.4% | -0.2% to +4.5% | 5.8% |
| `net8.0`, ZA0607 and ZA0502 off | one-file edit | +3.8% | -1.2% to +5.7% | 5.7% |
| `net8.0`, ZA0607 and ZA0502 off | no-op | +0.0% | -4.4% to +5.7% | 0.0% |

How to read it:

- **Overhead** is, for each of 15 interleaved pairs of builds, the build with the package divided
  by the build without it, minus one. The table gives the median and the interquartile range of
  those 15 pairs.
- **ZA analyzer time, share of build** is the time the compiler reports for the ZeroAlloc.Analyzers
  assembly under `-p:ReportAnalyzer=true`, as a share of the median build without the package. The
  compiler sums that time across its analysis threads, so it can exceed the wall-clock overhead.
- A **full** build is `dotnet build --no-incremental`. A **one-file edit** appends a comment to one
  file and runs `dotnet build`. A **no-op** build runs `dotnet build` with nothing changed.
- Every build starts its own compiler, with `-p:UseSharedCompilation=false`, so that compiler
  servers shared with other builds on the machine cannot skew it. That startup is part of the
  baseline. With a warm compiler server the baseline is shorter, so the same analyzer time is a
  larger share of it.

What it shows:

- **The overhead is a few percent on `net8.0`** and largest on `netstandard2.0`, where the .NET SDK's
  own analyzers do not run, so ZeroAlloc's work is a bigger share of a smaller build.
- **A one-file edit costs as much analyzer time as a full rebuild.** The compiler reruns for the
  whole project, so every file is analyzed again.
- **A no-op build costs nothing.** The compiler does not run, ZeroAlloc reports 0 analyzer time, and
  the remaining differences are noise, within each row's IQR.
- **Disabling ZA0607 and ZA0502 made no difference this measurement can resolve.** Analyzer time
  went from 6.0% to 5.8% of the build, and the wall-clock overhead stayed within the noise of the
  plain `net8.0` row.

The IQRs are wide because the host is a shared virtual machine. The medians are indicative; the
analyzer-time column is the steadier figure.

Environment: .NET SDK 10.0.402, Debian 13 on Linux 6.12, a KVM virtual machine on a shared host
with an Intel Core Ultra 7 255H. The run was pinned to four vCPUs.

### Reproducing

The harness is [`tools/build-overhead/measure.sh`](https://github.com/ZeroAlloc-Net/ZeroAlloc.Analyzers/blob/main/tools/build-overhead/measure.sh).
It builds the sample in a temporary directory outside the repository, once without the package
and once with it from NuGet, and alternates the two so drift on the machine hits both alike:

```bash
taskset -c 2-5 tools/build-overhead/measure.sh -n 15 -k 40 -v 1.11.0
```

`-n` is the number of build pairs per cell, `-k` the number of copies of the sample sources, and
`-v` the package version. The script prints the table above and keeps the raw per-build CSV. It
needs the .NET 10 SDK, python3 and network access to NuGet; the 12 cells took about 10 minutes here.

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

- A `net8.0` build runs 59 of the 64 rules, or 62 with the Configuration.Binder, Options and assembly-scanning library packages. ZA0211 needs .NET 9, and ZA0110 needs C# 13, while `net8.0` defaults to C# 12. ZA1707 needs the Microsoft.Extensions.Configuration.Binder 8.0 or later package and C# 12, and ZA1708 needs the Microsoft.Extensions.Options 8.0 or later package and C# 8, so both run only where those packages are referenced. ZA1710 runs only where Scrutor, MediatR, FluentValidation or AutoMapper is referenced. ZA1706 needs net8.0. A rule counts as running when it can report with no extra packages at the target's default language version.
- A `net6.0` build automatically skips ZA0101, ZA0102, ZA0104, ZA0205, ZA0701, ZA0801, and ZA1001 — any rule whose minimum TFM is higher than `net6.0`.
- A `netstandard2.0` build skips all TFM-gated rules and runs 41 of the 64 rules, or 44 with the System.Memory and assembly-scanning library packages: those with no TFM requirement that are also not gated on a language version or an API the target lacks. ZA1402, ZA1503 and ZA1709 have no TFM gate, so they run on every target. ZA0303 needs `ArrayPool<T>` from System.Buffers and ZA0304 needs `ReadOnlySpan<T>` from System.Memory, which brings System.Buffers with it. ZA1710 has no TFM gate, so it runs on `netstandard2.0` wherever Scrutor, MediatR, FluentValidation or AutoMapper is referenced. ZA1707 and ZA1708 do not run there even with their packages, because `netstandard2.0` defaults to C# 7.3, below their C# 12 and C# 8 gates.

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

The recommended approach is to never fully disable ZeroAlloc.Analyzers in CI. The per-rule TFM gating ensures you only pay for rules that are actionable for your target, and a build in which nothing changed does not run the analyzers at all.
