# Design: Zero-Alloc and NativeAOT Analyzers (Batch 3)

**Date:** 2026-10-09
**Source inspiration:** issue #70 (ZA0602 and C# 13 params collections), the .NET 8/9 BCL span APIs, and AOT gaps the SDK's IL2xxx/IL3xxx analyzers only report once AOT is switched on

## Context

Batch 3 adds 13 rules: 9 allocation rules and 4 NativeAOT rules. A rule is only in scope if the SDK's built-in analyzers don't already report it. That excludes CA1830, CA1835, CA1836, CA1851, CA1861, CA1862 and CA1870, and the trim and AOT warnings that appear once `IsAotCompatible` or `PublishAot` is on.

A 14th idea is out of scope here: flagging assembly-scanning registration and pointing adopters at ZeroAlloc.Inject, Mediator, Validation or Mapping. Whether a general-purpose analyzer should recommend ZeroAlloc packages needs a maintainer decision, so it is tracked as its own issue.

## Conventions for every rule in this batch

- **Analysis model.** Rules read `IOperation`, not syntax plus argument counts. #70 showed why: syntax-level heuristics misreport anything the compiler lowers differently.
- **API gates use symbols, not TFM strings.** A rule that needs a BCL API checks that the symbol exists in the compilation, for example `MemoryExtensions.Split(ReadOnlySpan<char>, char)` or `RuntimeHelpers.CreateSpan`. Language gates use `LanguageVersion`.
- **Hot path** means inside a `for`, `foreach`, `while` or `do` loop, using the same walk as ZA0602's `IsInsideLoop`. It stops at method, local function and lambda boundaries.
- **AOT stand-down.** ZA1707 to ZA1709 report nothing when `AotHelper.IsSdkAotAnalyzerEnabled` is true, because the SDK reports IL2026/IL3050 for the same code by then.
- **Severity.** Rules default to Info. Warning is only for code that is wrong with no trade-off, and those rules are narrowed until that holds (ZA0210, ZA0303).

## ID registry

| ID | Name | Category | Severity | Code fix |
|---|---|---|---|---|
| ZA0110 | `PreferParamsSpan` | Performance.Collections | Info | Non-exposed methods only |
| ZA0111 | `AvoidConcurrentDictionarySnapshot` | Performance.Collections | Info | Yes |
| ZA0210 | `UseUtf8StringLiteral` | Performance.Strings | Warning | Yes |
| ZA0211 | `UseSpanSplit` | Performance.Strings | Info | No |
| ZA0212 | `UseTryParse` | Performance.Strings | Info | No |
| ZA0303 | `ReturnRentedArray` | Performance.Memory | Warning | No |
| ZA0304 | `UseReadOnlySpanForConstantTable` | Performance.Memory | Info | Yes |
| ZA1402 | `UseStatePassingOverload` | Performance.Delegates | Info | Single captured local or parameter |
| ZA1503 | `ImplementEquatableOnStructKey` | Performance.ValueTypes | Info | No |
| ZA1706 | `MarkLibraryAotCompatible` | Performance.Aot | Info | No |
| ZA1707 | `UseConfigurationBindingGenerator` | Performance.Aot | Info | No |
| ZA1708 | `UseOptionsValidatorGenerator` | Performance.Aot | Info | No |
| ZA1709 | `AvoidDynamic` | Performance.Aot | Info | No |

ZA0207 stays unused because nothing records why it was skipped. ZA1103 stays reserved, as `DiagnosticIds.cs` documents.

---

## Allocation rules

### ZA0110 — `PreferParamsSpan`

**Problem:** every expanded call to a `params T[]` method allocates an array. Since C# 13, `params ReadOnlySpan<T>` is backed by an inline array on .NET 8+ and allocates nothing (#70).

**Detection:**
1. A method declared in source has a `params T[]` parameter.
2. The compilation's `LanguageVersion` is 13 or later.
3. Skip overrides, explicit and implicit interface implementations, `extern` methods, and methods whose array **escapes**. The array escapes if it is assigned, stored, returned, captured by a lambda or local function, passed to a parameter typed `T[]`, `object` or an interface, or used through an array-only member (anything except the indexer, `Length` and `foreach`).

**Message:**
- For non-exposed methods: `"params parameter '{0}' can be declared as 'params ReadOnlySpan<{1}>' so callers do not allocate"`
- For methods visible outside the assembly: `"Add a 'params ReadOnlySpan<{1}>' overload of '{2}' so callers do not allocate; changing '{0}' itself is a binary breaking change"`

**Code fix:** only for methods not visible outside the assembly. It changes the parameter type in place. Exposed methods get no fix, because an overload's body is the author's call.

### ZA0111 — `AvoidConcurrentDictionarySnapshot`

**Problem:** every read of `ConcurrentDictionary<TKey, TValue>.Keys` or `.Values` takes every lock and copies the contents into a new collection. Enumerating the dictionary itself takes no locks and makes no copy.

**Detection:** `.Keys` or `.Values` of a `ConcurrentDictionary<,>` is the collection of a `foreach`, or the source of a LINQ call. A snapshot assigned to a local or field and read later is not reported, because taking a consistent snapshot may be the intent.

**Message:** `"'{0}' copies the dictionary under all locks; enumerate the dictionary and use '{1}' instead"`

**Code fix:** for `foreach`, enumerate the dictionary and replace uses of the loop variable with `.Key` or `.Value`.

### ZA0210 — `UseUtf8StringLiteral`

**Problem:** `Encoding.UTF8.GetBytes("constant")` allocates and encodes on every call. A `"constant"u8` literal is static data.

**Detection:** a call to `Encoding.UTF8.GetBytes(string)` where:
1. the argument is a compile-time constant;
2. `LanguageVersion` is 11 or later;
3. the result is implicitly converted to `ReadOnlySpan<byte>`, for example when passed to a span parameter.

When the result is kept as a `byte[]` (assigned, returned, stored), the call is not reported: there a `u8` literal would need `.ToArray()` and is no improvement. This narrowing is what makes Warning safe.

**Message:** `"Use the UTF-8 literal \"{0}\"u8 instead of encoding a constant string on every call"`

**Code fix:** replace the call with the `u8` literal.

### ZA0211 — `UseSpanSplit`

**Problem:** `string.Split` allocates the result array and one string per part.

**Detection:**
1. A `foreach` directly over `string.Split(char)` or `string.Split(string)`.
2. No `StringSplitOptions` argument, or `StringSplitOptions.None`.
3. `MemoryExtensions.Split(ReadOnlySpan<char>, char)` exists in the compilation (.NET 9).

A split whose array is assigned or used other than by that `foreach` is not reported.

**Message:** `"Enumerate '{0}.AsSpan().Split(...)' to split without allocating the parts"`

**Code fix:** none. The loop variable changes from `string` to `Range`, so the body has to change.

### ZA0212 — `UseTryParse`

**Problem:** a failed `Parse` allocates an exception and captures a stack trace. A `catch` that turns the failure into a default value pays that cost on every bad input.

**Detection:**
1. A call to a static method named `Parse`.
2. The containing type also has a static `TryParse` whose parameters are the `Parse` parameters followed by `out T`. That covers the BCL primitives, `Guid`, `DateTime`, `Enum.Parse<T>` and any custom `IParsable<T>` type.
3. The call is inside a `try` with a `catch` for `FormatException`, `OverflowException`, `ArgumentException` or `Exception`, or a bare `catch`. That `catch` does not rethrow (no `throw;` and no `throw` of a new exception).

**Message:** `"Use '{0}.TryParse' instead of catching the exception from '{0}.Parse'"`

### ZA0303 — `ReturnRentedArray`

**Problem:** an array rented from an `ArrayPool<T>` and never returned leaves the pool empty, so later rents allocate again.

**Detection:**
1. A `Rent` call on any `ArrayPool<T>` instance is assigned to a local.
2. The method has no `Return` call with that local as its first argument.
3. The local does not **escape**. It escapes if it is returned, assigned to a field, property, `ref` or `out` parameter, captured by a lambda or local function, or passed as an argument other than through an implicit conversion to `Span<T>`, `ReadOnlySpan<T>`, `Memory<T>` or `ReadOnlyMemory<T>`. Passing the array itself, even to a `T[]` parameter, counts as an escape, because the callee may return it to the pool. An escape means ownership may have moved, so the rule stays silent.

A `Return` outside a `finally` is **not** reported. .NET's guidance accepts letting the GC collect the buffer on the exception path, and returning in a `finally` is wrong if an async operation may still be writing to the buffer. Only the never-returned case is reported, which is what makes Warning safe.

**Message:** `"Array rented into '{0}' is never returned to the pool"`

### ZA0304 — `UseReadOnlySpanForConstantTable`

**Problem:** `static readonly int[] Table = { … }` allocates the array and copies the data into it at type initialization. A `static ReadOnlySpan<int> Table => [ … ];` property reads the constants straight from the assembly's data section, with no allocation. NativeAOT can also pre-initialize it at compile time.

**Detection:**
1. The field is a `private` or `internal` `static readonly T[]` initialized by an array creation or collection expression whose elements are all constants.
2. `T` is `byte`, `sbyte` or `bool`. Or `T` is `char`, `short`, `ushort`, `int`, `uint`, `long`, `ulong`, `float` or `double`, and `RuntimeHelpers.CreateSpan` exists (.NET 7). These rules are the ones the #70 IL probe observed.
3. Every reference to the field is read-only: element read, `Length`, `foreach`, or an implicit conversion to `ReadOnlySpan<T>`. Any other use (LINQ, passing as an array or object, `ref` element access) silences the rule.

**Message:** `"Lookup table '{0}' can be a 'static ReadOnlySpan<{1}>' property that reads constant data without allocating"`

**Code fix:** replace the field with `private static ReadOnlySpan<T> Name => [ … ];` and keep the original accessibility. Call sites compile unchanged.

### ZA1402 — `UseStatePassingOverload`

**Problem:** a lambda that captures variables allocates a closure and a delegate on every call. These APIs have overloads that pass the state as an argument, so a static lambda can be cached.

**Detection:**
1. An argument is a lambda that captures, per `DataFlowAnalysis.Captured`.
2. The call targets one of these methods:
   - `ConcurrentDictionary<,>.GetOrAdd` and `AddOrUpdate`
   - `CancellationToken.Register` and `UnsafeRegister`
   - `ThreadPool.QueueUserWorkItem` and `UnsafeQueueUserWorkItem`
   - `TaskFactory.StartNew`
   - `string.Create`, where the call already has a state parameter that the lambda bypasses
3. The state-passing overload exists in the compilation.

The API list lives in one table in the analyzer, so adding an API is a one-line change.

**Message:** `"Lambda passed to '{0}' captures {1}; use the overload that passes state so the lambda can be static"`

**Code fix:** only when exactly one local or parameter is captured and `this` is not. The fix switches to the state overload, passes the captured value as the state argument, and makes the lambda `static` with that state parameter.

### ZA1503 — `ImplementEquatableOnStructKey`

**Problem:** a struct key that does not implement `IEquatable<T>` makes `EqualityComparer<T>.Default` fall back to `Equals(object)`, which boxes the key on every lookup. ZA1501 covers the `GetHashCode` half of this.

**Detection:**
1. One of these is created without a comparer argument:
   - `Dictionary<,>`, `HashSet<>`, `ConcurrentDictionary<,>`
   - `FrozenDictionary`/`FrozenSet` (via `ToFrozenDictionary`/`ToFrozenSet`)
   - `ToDictionary`, `ToHashSet`
2. Its key type is a struct declared in the current compilation, so the user can change it.
3. The struct does not implement `IEquatable<T>`.

Record structs and enums are not reported.

**Location:** the creation or call site. The message names the struct.

**Message:** `"Struct '{0}' is used as a hash key but does not implement IEquatable<{0}>, so every lookup boxes it"`

---

## NativeAOT rules

The package's `buildTransitive/ZeroAlloc.Analyzers.props` gains two `CompilerVisibleProperty` items: `IsTestProject` and `EnableConfigurationBindingGenerator`.

### ZA1706 — `MarkLibraryAotCompatible`

**Problem:** the SDK's trim and AOT analyzers only run when `IsAotCompatible`, `PublishAot` or `EnableAotAnalyzer` is set. A library without them can ship AOT-hostile code and nothing warns until an app publishes with it.

**Detection:** a compilation-end diagnostic with no source location, reported once per compilation, when all of these hold:
1. `Compilation.Options.OutputKind` is `DynamicallyLinkedLibrary`;
2. `IsTestProject` is not `true`;
3. the target framework is net8.0 or later;
4. none of `PublishAot`, `IsAotCompatible` or `EnableAotAnalyzer` is `true`.

A multi-targeted library only gets the diagnostic in its net8+ compilations. Apps are not reported; whether an app publishes with AOT is its own decision.

**Message:** `"Library does not set IsAotCompatible, so the SDK's trim and AOT analyzers are off; set it for net8.0 and later target frameworks"`

### ZA1707 — `UseConfigurationBindingGenerator`

**Problem:** the configuration binder uses reflection. The configuration-binding source generator replaces those calls with generated code.

**Detection:**
1. A call to any of these:
   - `ConfigurationBinder.Bind`, `Get<T>`, `Get(Type)` or `GetValue`
   - `OptionsBuilderConfigurationExtensions.Bind` or `BindConfiguration`
   - `OptionsConfigurationServiceCollectionExtensions.Configure<T>(IConfiguration)`
2. `EnableConfigurationBindingGenerator` is not `true`.
3. The AOT stand-down does not apply. `PublishAot` turns the generator on by itself, and `IsAotCompatible` turns on IL2026.

**Message:** `"'{0}' binds configuration with reflection; set <EnableConfigurationBindingGenerator>true</EnableConfigurationBindingGenerator> to generate the binding code"`

### ZA1708 — `UseOptionsValidatorGenerator`

**Problem:** `ValidateDataAnnotations()` validates options with reflection.

**Detection:** a call to `OptionsBuilderDataAnnotationsExtensions.ValidateDataAnnotations`, unless the AOT stand-down applies.

**Message:** `"'ValidateDataAnnotations' validates with reflection; use an [OptionsValidator] source-generated validator instead"`

### ZA1709 — `AvoidDynamic`

**Problem:** `dynamic` dispatch goes through the C# runtime binder, which needs reflection and runtime code generation. Under NativeAOT it fails or is trimmed away.

**Detection:** a dynamic invocation, member reference, indexer access or object creation, unless the AOT stand-down applies. Only the outermost dynamic operation of an expression is reported, so `d.A.B(c)` gives one diagnostic. A declaration typed `dynamic` that is never dispatched on is not reported.

**Message:** `"'dynamic' dispatch uses the runtime binder, which is not supported under NativeAOT"`

---

## Testing

- Each rule gets a `tests/ZeroAlloc.Analyzers.Tests/ZAxxxx_*Tests.cs` file with a positive case and one test per documented skip or escape.
- Symbol-gated rules are tested against the `Net60`, `Net70`, `Net80` and `Net90` reference assemblies, as ZA0602's tests are.
- Each code fix gets a round-trip test through `CSharpCodeFixVerifier`.
- ZA1706 to ZA1709 set `PublishAot`, `IsAotCompatible`, `IsTestProject` and `EnableConfigurationBindingGenerator` through the verifier's globalconfig, and each stand-down has its own test.

## Per-rule checklist

- `DiagnosticIds` constant
- `AnalyzerReleases.Unshipped.md` entry
- Section in the matching `docs/rules/*.md` with Why, Before and After
- Rows in the README and `docs/getting-started.md` rule tables
- Its own `feat:` commit

## Delivery

One GitHub issue per rule, labelled `enhancement`, plus one `needs-decision` issue for the assembly-scanning hints. The work ships in three PRs, each merged before the next one starts so that no changelog entry is lost:

1. ZA0110, ZA0111, ZA0210, ZA0211, ZA0212, ZA0303, ZA0304
2. ZA1402, ZA1503
3. ZA1706, ZA1707, ZA1708, ZA1709

Each PR body carries a `BEGIN_COMMIT_OVERRIDE` block that lists one `feat:` line per rule, so release-please credits every rule after the squash merge.
