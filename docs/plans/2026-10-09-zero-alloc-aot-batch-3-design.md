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
| ZA1402 | `UseStatePassingOverload` | Performance.Delegates | Info | GetOrAdd and QueueUserWorkItem only |
| ZA1503 | `ImplementEquatableOnStructKey` | Performance.ValueTypes | Info | No |
| ZA1706 | `MarkLibraryAotCompatible` | Performance.Aot | Info | No |
| ZA1707 | `UseConfigurationBindingGenerator` | Performance.Aot | Info | No |
| ZA1708 | `UseOptionsValidatorGenerator` | Performance.Aot | Info | No |
| ZA1709 | `AvoidDynamic` | Performance.Aot | Info | No |
| ZA1710 | `AvoidAssemblyScanningRegistration` | Performance.Aot | Info | No |

ZA0207 stays unused because nothing records why it was skipped. ZA1103 stays reserved, as `DiagnosticIds.cs` documents.

---

## Allocation rules

### ZA0110 — `PreferParamsSpan`

**Problem:** every expanded call to a `params T[]` method allocates an array. Since C# 13, `params ReadOnlySpan<T>` is backed by an inline array on .NET 8+ and allocates nothing (#70).

**Detection:**
1. A method declared in source has a `params T[]` parameter.
2. The compilation's `LanguageVersion` is 13 or later and `InlineArrayAttribute` exists (.NET 8).
3. Skip overrides, explicit and implicit interface implementations, `extern` methods, and methods whose array **escapes**. The array escapes if it is assigned, stored, returned, captured by a lambda or local function, passed to a parameter typed `T[]`, `object` or an interface, or used through an array-only member (anything except the indexer, `Length` and `foreach`).

Reported from a compilation-end action, because the check needs every use of the symbol in the compilation. Methods used as a method group anywhere in the compilation are not reported, nor are virtual, abstract or partial methods, async methods and iterators. The rule requires InlineArrayAttribute (.NET 8). It skips primary-constructor parameters, methods declared in generated files, and methods implementing an interface member through any type in the compilation. It skips methods with a caller in this compilation that passes the params argument in normal form with a type other than exactly T[], including null, or that is inside an expression-tree lambda. Internal members count as exposed when the assembly has InternalsVisibleTo.

A params span is implicitly scoped, so the rule also skips methods where the fixed span could escape: methods that return a ref struct or by reference, take ref or out parameters of a ref struct type, or are instance members of a ref struct; and methods where a conversion of the array to `ReadOnlySpan<T>` is assigned to a local or field, returned, passed by reference, or passed to a call that returns a ref struct or takes another ref struct by ref or out. It skips methods that share their name with another method in the containing type or its base types, and members of types deriving from `System.Attribute`. A call on an element of a type parameter without a reference type constraint keeps the array, like one on a mutable struct.

**Message:** `"params parameter '{0}' of '{1}' can be 'params ReadOnlySpan<{2}>' so callers do not allocate{3}"`. `{1}` is the method name, or the type name for a constructor. `{3}` is empty for methods not visible outside the assembly, and otherwise `"; it is visible outside the assembly, so add an overload instead of changing it"`, because changing the signature there is a binary breaking change.

**Code fix:** only for methods not visible outside the assembly. It changes the parameter type in place. Exposed methods get no fix, because an overload's body is the author's call.

### ZA0111 — `AvoidConcurrentDictionarySnapshot`

**Problem:** every read of `ConcurrentDictionary<TKey, TValue>.Keys` or `.Values` takes every lock and copies the contents into a new collection. Enumerating the dictionary itself takes no locks and makes no copy.

**Detection:** `.Keys` or `.Values` of a `ConcurrentDictionary<,>` is the collection of a `foreach`, or the source of a LINQ call. A snapshot assigned to a local or field and read later is not reported, because taking a consistent snapshot may be the intent. Predicate-less `Any`, `Count` and `LongCount` are not reported, because they do not enumerate the snapshot.

**Message:** `"'{0}' copies the dictionary under all locks; enumerate the dictionary and use '{1}' instead"`

**Code fix:** For foreach, deconstruct the pair: `foreach (var key in map.Keys)` becomes `foreach (var (key, _) in map)`, leaving the loop body unchanged. Offered only when `KeyValuePair<TKey, TValue>.Deconstruct` exists and the loop variable is `var` or the element type. The fix switches from a point-in-time snapshot to live enumeration, so it is withheld when the loop body writes to the same dictionary through the indexer setter, `TryAdd`, `GetOrAdd`, `AddOrUpdate`, `TryUpdate`, `TryRemove` or `Clear`.

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

A split whose array is assigned or used other than by that `foreach` is not reported. The span enumerator is a ref struct, so the rule also skips loops whose body contains an `await` or a `yield`, and loops in async functions or iterators below C# 13.

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
3. The local does not **escape**. It escapes if it is returned, assigned to a field, property, `ref` or `out` parameter, captured by a lambda or local function, or passed as an argument other than through an implicit conversion to `Span<T>` or `ReadOnlySpan<T>`. Conversions to `Memory<T>` and `ReadOnlyMemory<T>` are escapes. Passing the array itself, even to a `T[]` parameter, counts as an escape, because the callee may return it to the pool. An escape means ownership may have moved, so the rule stays silent.

Passing the array to a method of System.MemoryExtensions, System.Array, System.Buffer or System.IO.Stream, including Stream subclasses, is not an escape: those APIs read or write the array without taking ownership. Memory<T> and ReadOnlyMemory<T> conversions and constructions are escapes. A trusted call is non-escaping when it returns void, a primitive, a Span or a ReadOnlySpan.

A span cannot carry ownership back to the pool, so span conversions, span constructions and trusted span-returning calls are never escapes, even when the span is returned directly, through a span local, or from a span-returning function. A Rent whose array leaves the method only as a span is a leak and is reported.

A `Return` outside a `finally` is **not** reported. .NET's guidance accepts letting the GC collect the buffer on the exception path, and returning in a `finally` is wrong if an async operation may still be writing to the buffer. Only the never-returned case is reported, which is what makes Warning safe.

**Message:** `"Array rented into '{0}' is never returned to the pool"`

### ZA0304 — `UseReadOnlySpanForConstantTable`

**Problem:** `static readonly int[] Table = { … }` allocates the array and copies the data into it at type initialization. A `static ReadOnlySpan<int> Table => [ … ];` property reads the constants straight from the assembly's data section, with no allocation. NativeAOT can also pre-initialize it at compile time.

**Detection:**
1. The field is a `private` or `internal` `static readonly T[]` initialized by an array creation or collection expression whose elements are all constants.
2. `T` is `byte`, `sbyte` or `bool`. Or `T` is `char`, `short`, `ushort`, `int`, `uint`, `long`, `ulong`, `float` or `double`, and `RuntimeHelpers.CreateSpan` exists (.NET 7). These rules are the ones the #70 IL probe observed.
3. Every reference to the field is read-only: element read, `Length`, `foreach`, or an implicit conversion to `ReadOnlySpan<T>`. Any other use, such as LINQ, passing it as an array or object, or `ref` element access, silences the rule.

Reported from a compilation-end action, because the check needs every use of the symbol in the compilation. Generated code is analyzed but not reported, and tables declared in generated files are skipped. Internal tables are skipped when the assembly has InternalsVisibleTo. Uses inside async functions, iterators and expression-tree lambdas disqualify the table.

**Message:** `"Lookup table '{0}' can be a 'static ReadOnlySpan<{1}>' property that reads constant data without allocating"`

**Code fix:** replace the field with `private static ReadOnlySpan<T> Name => [ … ];` and keep the original accessibility. Call sites compile unchanged. The fix emits a collection expression on C# 12 and later, otherwise `new T[] { … }`, and keeps the original element text and comments.

### ZA1402 — `UseStatePassingOverload`

**Problem:** a lambda that captures variables allocates a closure and a delegate on every call. These APIs have overloads that pass the state as an argument, so a static lambda can be cached.

**Detection:**
1. An argument is a lambda that captures, per `DataFlowAnalysis.Captured`.
2. The call targets one of these methods:
   - `ConcurrentDictionary<,>.GetOrAdd` and `AddOrUpdate`
   - `CancellationToken.Register` and `UnsafeRegister`
   - `ThreadPool.QueueUserWorkItem` and `UnsafeQueueUserWorkItem`
   - `TaskFactory.StartNew` and `TaskFactory<TResult>.StartNew`
   - `string.Create`, where the call already has a state parameter that the lambda bypasses
3. The state-passing overload exists in the compilation.

Inside a loop the same lambda is also reported by ZA0502; ZA1402 gives the API-specific fix.

Only lambdas passed to the API's callback parameter are reported (valueFactory; addValueFactory and updateValueFactory; callback; callBack; action and function; action). A lambda stored as a dictionary value is not. `const` locals are not captures. A primary-constructor parameter used outside the constructor counts as capturing `this`. A call to a non-static local function, or a method group of one, counts what that local function captures; its body is walked too, with a visited set against recursion.

The API list lives in one table in the analyzer, so adding an API is a one-line change.

**Message:** `"Lambda passed to '{0}' captures {1}; use the overload that passes state so the lambda can be static"`

**Code fix:** Offered for ConcurrentDictionary.GetOrAdd and ThreadPool.QueueUserWorkItem, whose state is typed. It requires exactly one captured local or parameter, with no this, no ref local and no ref or out parameter; the captured variable is never written in its declaring body; the lambda is implicitly typed; and for QueueUserWorkItem, the lambda does not use its own parameter. QueueUserWorkItem is rewritten to the generic overload with preferLocal: false. Object-state APIs, AddOrUpdate and string.Create get the diagnostic only.

The fix also needs C# 9 or later. It is withheld when the lambda calls a non-static local function, when the captured variable is a mutable struct (not readonly, not enum, not a primitive), when its type is `dynamic` or a pointer, or when the variable is taken by reference anywhere in its body. It keeps `async`, attributes and trivia, and names the new arguments when the call uses named arguments, and escapes a keyword-named variable as `@name`. UnsafeQueueUserWorkItem is diagnostic-only: a generic `UnsafeQueueUserWorkItem<TState>` overload does exist, but its WaitCallback form already takes a state argument, which a fix would have to drop or merge.

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

A call counts as passing a comparer only when an argument supplies a non-null IEqualityComparer<T>; an optional comparer parameter left at its default does not count. Collection expressions are not covered.

`Nullable<T>` keys are unwrapped, so `Dictionary<Point?, int>` reports `Point`. Passing `EqualityComparer<T>.Default` counts as passing no comparer, because it is the comparer that boxes. Not covered: ImmutableDictionary and ImmutableHashSet, and the LINQ hashing operators (Distinct, GroupBy, ToLookup, Union, Intersect, Except). SortedSet and SortedDictionary are not hashed collections.

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

An explicit IsAotCompatible=false also stands the rule down, because it is a decision; only an unset property is reported. The conditions are checked at compilation start, so apps, test projects and older target frameworks add no per-file work. The rule uses one whole-compilation action rather than a start action that only registers an end action, because RS1013 rejects the latter; it therefore does not literally register zero callbacks.

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

Calls whose bound type is only known at run time are not reported, because the generator cannot replace them: a `System.Type` argument that is not a typeof expression, or `Bind` with an `object`-typed instance. Calls the generator refuses with warning SYSLIB1104 are not reported either. The bound type is the method's type argument, the typeof operand, or the static type of the instance `Bind` fills; the call is skipped when that type, or any type argument within it at any depth, is a type parameter, or when it, a containing type or a type argument within it is private, protected or private protected. The rule reports only when Microsoft.Extensions.Configuration.Binder 8.0 or later is referenced, and only from C# 12, because the generator needs interceptors and emits error SYSLIB1102 below it.

**Message:** `"'{0}' binds configuration with reflection; set <EnableConfigurationBindingGenerator>true</EnableConfigurationBindingGenerator> to generate the binding code"`

### ZA1708 — `UseOptionsValidatorGenerator`

**Problem:** `ValidateDataAnnotations()` validates options with reflection.

**Detection:** a call to `OptionsBuilderDataAnnotationsExtensions.ValidateDataAnnotations`, unless the AOT stand-down applies. The rule reports only where `[OptionsValidator]` exists, that is Microsoft.Extensions.Options 8.0 or later, and only from C# 8, because the generator emits error SYSLIB1216 below it.

**Message:** `"'ValidateDataAnnotations' validates with reflection; use an [OptionsValidator] source-generated validator instead"`

### ZA1709 — `AvoidDynamic`

**Problem:** `dynamic` dispatch goes through the C# runtime binder, which needs reflection and runtime code generation. Under NativeAOT it fails or is trimmed away.

**Detection:** a dynamic invocation, member reference, indexer access or object creation, unless the AOT stand-down applies. Only the outermost dynamic operation of an expression is reported, so `d.A.B(c)` gives one diagnostic. A declaration typed `dynamic` that is never dispatched on is not reported.

Besides the four dynamic operation kinds, binary and unary operators, compound assignments, increments and decrements on dynamic operands, and conversions from dynamic to a type other than object or dynamic, and `await` on a dynamic operand, are reported too, because each goes through the runtime binder. An `as` conversion is a type test and is not reported. The outermost-only rule applies across all of them.

**Message:** `"'dynamic' dispatch uses the runtime binder, which is not supported under Native AOT"`

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

## Addendum: ZA1710 (#86)

Decision on the assembly-scanning hints: report the known scanning APIs and give per-package advice on the ZeroAlloc replacement, rather than leave the idea out of scope.

- **Scope.** Scrutor `Scan`, MediatR `RegisterServicesFromAssembly*` and `AddMediatR` with an assembly or type, FluentValidation `AddValidatorsFromAssembly*`, and AutoMapper `AddAutoMapper` with an assembly or type. The message ends with a per-package hint held in the table, because the replacements differ: Inject registers services with a source generator; Mediator dispatches without reflection but registers only `IMediator`, so its handlers go through Inject; Validation registers validators at compile time; Mapping generates static mappers and needs no registration.
- **Matching.** By the declaring assembly's name and the method name, held in one table. Type names are not matched, because these libraries have moved their extensions between types and packages.
- **Stand-down.** Decided per call. The rule skips a call only when the method or its containing type carries `RequiresUnreferencedCode` and the SDK trim analyzer is on, or carries `RequiresDynamicCode` and the SDK AOT analyzer is on. `EnableAotAnalyzer` alone does not count for the trim analyzer. An unannotated API still reports, because the SDK says nothing about it, so unlike ZA1707 to ZA1709 the rule does not stand down for the whole project.
- **Code fix.** None. The replacement needs a package and attributes on the user's types.
