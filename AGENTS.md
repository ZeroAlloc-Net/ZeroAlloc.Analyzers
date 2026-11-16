# AGENTS.md: ZeroAlloc.Analyzers

Roslyn analyzers and code fixes that flag allocations and NativeAOT breakers (64 rules, IDs ZA01xx to ZA17xx). It ships as one NuGet development dependency, `ZeroAlloc.Analyzers`, with no runtime assembly. Consumers are .NET projects on any target framework.

## Layout

- `src/ZeroAlloc.Analyzers` and `src/ZeroAlloc.Analyzers.CodeFixes` are both `netstandard2.0` and `IsPackable=false`. Neither is published on its own.
- `src/ZeroAlloc.Analyzers.Package` is the only packable project. It bundles both DLLs under `analyzers/dotnet/cs` and the props under `build` and `buildTransitive`. Check what ships through this project.
- `tests/ZeroAlloc.Analyzers.Tests` references the analyzers twice: as a library for type access and as an analyzer, so the rules also run on the test code.
- `tools/build-overhead` is a benchmark harness for `docs/performance.md`. It is not part of the solution build.
- `AnalyzerReleases.Shipped.md` and `AnalyzerReleases.Unshipped.md` are release tracking. A new rule needs an entry in the Unshipped file; the release PR moves it to Shipped.

## Build and test

Prerequisites: SDK 11.0.100 (`global.json`, rollForward `latestMinor`). The test project targets `net9.0` and `net11.0`, so both the .NET 9 and the .NET 11 runtimes must be installed.

CI job `build` in `ci.yml`:

```bash
dotnet tool restore
VERSION=$(dotnet dotnet-gitversion /output json /showvariable SemVer)
dotnet restore
dotnet build --no-restore -c Release -p:Version=$VERSION
dotnet test --no-build -c Release --verbosity normal --logger "trx;LogFilePrefix=test-results" --collect "XPlat Code Coverage" --settings coverage.runsettings --results-directory TestResults
dotnet pack src/ZeroAlloc.Analyzers.Package/ZeroAlloc.Analyzers.Package.csproj -c Release --no-build -p:Version=$VERSION
```

Fast inner loop, verified locally, no CI job runs it:

```bash
dotnet build -c Release -p:Version=1.2.3
dotnet test --no-build -c Release --filter "FullyQualifiedName~ZA0105"
```

## Conventions and gotchas

- `TreatWarningsAsErrors` is on for the whole repo, and the analyzer projects run their own rules on themselves. A new warning in repo code fails the build.
- Pack fails with ZAVERSION001 or ZAVERSION002 unless the version is passed as `-p:Version` to every command that builds, including the `dotnet build` before `pack --no-build`. Do not work around the guard.
- Rules read the consumer's target framework through `CompilerVisibleProperty` items in `buildTransitive/ZeroAlloc.Analyzers.props` and `TfmHelper`. A rule that suggests an API newer than the consumer's target must stand down on older targets. A new MSBuild property a rule reads needs an entry in that props file.
- Test through `CSharpAnalyzerVerifier` and `CSharpCodeFixVerifier` in `tests/.../Verifiers`, passing the target framework the rule is gated on. Cover both the reporting case and a no-diagnostic case. Test files are named `ZAxxxx_<RuleName>Tests.cs`; IDs live in `DiagnosticIds.cs` and categories in `DiagnosticCategories.cs`.
- Analyzers call `ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None)` and `EnableConcurrentExecution()`. Filter syntactically before touching the semantic model.
- The `System.Security.Cryptography.Pkcs` pin in the test project is a CVE override with a removal condition in its comment. Leave it until that condition holds.
- Contributor walk-throughs: `docs/contributing/adding-a-rule.md`, `architecture.md`, `tfm-awareness.md`.

<!-- org-core:start sha256=c42b83cb821d67a5547aafe02bbf9d3f36a7d19379a5d378bc38d452b7db8451 (generated from docker-nuc scripts/agents-md/org-core-zeroalloc.md; do not edit here) -->
## Org rules

These rules hold in every ZeroAlloc-Net repo. Each library makes two promises: it works under
NativeAOT and trimming, and its hot path allocates nothing. The 2026-10-11 sweep found 16 of 28 repos
allocating on the hot path, most with docs claiming 0 B.

### Hot path

The operation a consumer runs repeatedly: `Send`, a cache hit, `Validate`, `Map`, `Serialize`, a
resilience call, a state transition, an append, a worker poll, including the idle poll. Startup and
registration are not hot path. For a source generator, the hot path is the code it emits: judge the
snapshots and the generated output.

### Allocation traps the sweep found

| Pattern | Instead |
|---|---|
| `async ValueTask` that only awaits and returns | `return new ValueTask<T>(inner.XAsync(ct));` |
| `Task.WhenAll` over tasks that already completed | check `IsCompletedSuccessfully`, await only what is pending |
| `TagList` or log `params object[]` built before `Enabled`/`IsEnabled` | check first, or `LoggerMessage.Define` |
| event args allocated before checking for subscribers | copy the handler to a local, check, then allocate |
| `new Serializer()` / `new Utf8JsonWriter()` per call | cache stateless instances, reuse writers with `Reset` |
| `GetString()` / `ToString()` round-trips in JSON converters | parse and format through spans |
| `ToArray()` / `ToList()` copies between layers | pass `ReadOnlyMemory<T>` or spans, copy once at most |
| LINQ in emitted code | emit `for` loops |
| `yield` iterator behind a public `GetEnumerator` | public struct enumerator, from the first release |
| `Enum.IsDefined(typeof(E), v)`, `decimal.GetBits(v)` | `Enum.IsDefined<E>`, `v.Scale` |
| configuration parsed or read per call | parse once and cache |
| an `AsyncLocal` write per call | pass state explicitly |
| a capturing lambda or method-group delegate per call | cache the delegate in a field, or a static lambda |
| a per-key object that is never removed | remove on release; otherwise it is a leak |

Inherent allocations are fine when documented: the consumer's own result, an exception on a failure
path, a one-time cache fill.

### Tests and benchmarks

- Every hot path has an allocation gate test (`AllocationGate` from ZeroAlloc.TestHelpers) that measures
  the whole public call, including any state setup.
- Budgets are exact. If a path allocates, assert that number and comment what it is.
- Test the generated code, not a hand-written stand-in, and cover every shape: each attribute, nested
  collections, and the asynchronous case with a fake that returns pending tasks.
- Benchmarks run in CI through the shared `benchmark-smoke.yml` and target the library's LTS
  frameworks. The benchmark body allocates nothing besides the library itself. Run locally with
  `--inProcess`.
- Publish exact allocations and same-run ratios against a `Baseline`, never absolute timings. Run with
  `--launchCount 3`, pin CPUs with `taskset`, give the spread, and label every table.

### NativeAOT and trimming

- Packable runtime projects, and projects consumers publish, set `IsAotCompatible=true` on modern
  target frameworks. Analyzers and source generators target `netstandard2.0`.
- No reflection or runtime code generation on a supported path. An opt-in reflection fallback carries
  honest `[RequiresUnreferencedCode]` / `[RequiresDynamicCode]`. Every suppression must be true;
  remove dead ones.
- The AOT smoke sample publishes with `TreatWarningsAsErrors=true`. If a third-party warning really
  blocks it, exclude only that code with `WarningsNotAsErrors` and name the assembly.
- The smoke runs the hot path of every package in the repo. A package without a smoke has unknown AOT
  status. Adapters run against real services in CI service containers; publish-only is a last resort.
  App and template smokes make authenticated requests.

### Docs and shipping

- Docs match measurements. After a hot-path change, re-measure and update `docs/performance.md` and the
  README. Never write "0 B" without a test that asserts it.
- New public API is `feat:`, even for a performance change. Public API changes are additive: never add
  an optional parameter to a shipped signature. A breaking end state goes in a `next-major` issue.
- A behaviour change gets a `## Behaviour change` section in the PR body.
- Conventional Commits with release-please. Subjects start lowercase, body lines stay within 100
  characters, and a body never contains nested parentheses: release-please drops the whole commit.
- Local packs use `9.9.9-local.<sha>`. After a release, verify the package on NuGet.
- Fix review findings in the PR; file an issue only for what cannot go in it.

Full rules, template wiring lessons and the reasons behind each rule: https://github.com/ZeroAlloc-Net/.github/blob/main/AGENTS.md
<!-- org-core:end -->
