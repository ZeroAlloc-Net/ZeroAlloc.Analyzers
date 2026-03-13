# Design: Additional Performance Warning Analyzers (Batch 2)

**Date:** 2026-03-13
**Source inspiration:** RoslynClrHeapAllocationAnalyzer (HAA rules), dotnet CA rules (CA1825, CA1851), .NET performance blog posts

## Context

Following the ZLinq-inspired batch (ZA0108, ZA0208, ZA0606), this batch adds four more performance analyzer rules covering zero-length array allocations, value-type boxing in string concatenation, multiple enumeration of lazy sequences, and Span<T> misuse in async methods.

## Rules

### ZA0109 — `AvoidZeroLengthArrayAllocation`

**Category:** `Performance.Collections`
**Severity:** Warning
**Code fix:** Yes

**Problem:** `new T[0]` and `new T[] { }` allocate a new array object on the heap on every execution. `Array.Empty<T>()` returns a cached singleton zero-length array, avoiding any allocation.

```csharp
// Bad — allocates every time
static readonly int[] Empty1 = new int[0];
static readonly int[] Empty2 = new int[] { };

// Good — singleton, no allocation
static readonly int[] Empty3 = Array.Empty<int>();
```

**Detection logic:**
1. Register `ArrayCreationExpression` and `ImplicitArrayCreationExpression` syntax nodes
2. Flag `new T[N]` where `N` resolves to the constant `0`
3. Flag `new T[] { }` — array creation with empty initializer list
4. Verify via semantic model that the element type is resolvable (to construct the `Array.Empty<T>()` fix)

**Message:** `"Allocation of zero-length '{0}' array — use 'Array.Empty<{0}>()' to return a cached singleton instead"`

**Code fix:** Replace `new T[0]` / `new T[] {}` with `Array.Empty<T>()`; add `using System;` if not present.

---

### ZA0209 — `AvoidValueTypeBoxingInStringConcat`

**Category:** `Performance.Strings`
**Severity:** Warning
**Code fix:** No

**Problem:** The `+` binary operator on `string` with a non-string operand resolves to `string.Concat(object, object)`, which boxes the value-type operand onto the heap. String interpolation (`$"..."`) uses `DefaultInterpolatedStringHandler` in .NET 6+ and avoids boxing for most primitive types.

```csharp
int count = 42;

// Bad — boxes count to object
string s = "Count: " + count;

// Good — no boxing
string s = $"Count: {count}";
// or
string s = "Count: " + count.ToString();
```

**Detection logic:**
1. Register `BinaryExpression` with `SyntaxKind.AddExpression`
2. Use semantic model to verify one operand's type is `string`
3. Verify the other operand's type is a value type (struct, enum, or primitive) — not `string`, not `char` (char concat is a special overload)
4. Confirm the resolved operator is `string.Concat` (not a user-defined `+`)

**Message:** `"Value type '{0}' is boxed in string concatenation — use string interpolation ($\"...\") or .ToString() to avoid the heap allocation"`

**Note:** Does not overlap with ZA0503 (`AvoidBoxingEverywhere`), which only examines method call argument boxing via invocation expressions, not the `+` binary operator.

---

### ZA0607 — `AvoidMultipleEnumeration`

**Category:** `Performance.Linq`
**Severity:** Warning
**Code fix:** No

**Problem:** When a local variable is typed as `IEnumerable<T>` (or a related lazy interface), each `foreach` restarts the sequence from scratch by calling `GetEnumerator()`. For deferred LINQ queries, this re-executes the entire query pipeline on each iteration. Materializing once with `.ToList()` or `.ToArray()` before iterating avoids re-execution.

```csharp
IEnumerable<int> query = items.Where(x => x > 0); // deferred

// Bad — executes Where twice
foreach (var x in query) { /* first pass */ }
foreach (var x in query) { /* second pass — re-executes Where */ }

// Good
var list = query.ToList();
foreach (var x in list) { }
foreach (var x in list) { }
```

**Detection logic (Approach A — foreach-only):**
1. Register `MethodDeclaration` (and local function, lambda with body)
2. Collect all `ForEachStatement` nodes in the method body
3. For each foreach, resolve the expression to a local symbol via semantic model
4. If the local symbol's declared type is a lazy interface (`IEnumerable<T>`, `IQueryable<T>`) — not a concrete collection
5. Group by local symbol; if any symbol appears in ≥ 2 separate foreach statements, flag the **second** (and subsequent) foreach expression

**Scope exclusions:**
- Method parameters (can't change caller)
- Locals typed as concrete collections (`List<T>`, `T[]`, etc.)
- Locals initialized from non-method-call expressions (already handled by ZA0606)

**Message:** `"'{0}' is an IEnumerable<T> that is foreach'd multiple times — each iteration restarts the sequence; call .ToList() or .ToArray() first to materialize once"`

---

### ZA1104 — `AvoidSpanInAsyncMethod`

**Category:** `Performance.Async`
**Severity:** Warning
**Code fix:** No

**Problem:** `Span<T>` and `ReadOnlySpan<T>` are stack-only `ref struct` types and cannot safely span an `await` boundary. In .NET 9+ this is a compile error; in earlier targets it silently compiles but produces undefined behavior. The fix is to use `Memory<T>` or `ReadOnlyMemory<T>` instead.

```csharp
// Bad — Span<T> in async method
async Task ProcessAsync(Span<byte> data)
{
    await Task.Delay(1);
    // data may be invalid after await
}

// Good
async Task ProcessAsync(Memory<byte> data)
{
    await Task.Delay(1);
}
```

**Detection logic:**
1. Register `MethodDeclaration` nodes that are `async` (have the `async` modifier)
2. Check if the method body contains at least one `AwaitExpression`
3. Collect parameters typed as `Span<T>` or `ReadOnlySpan<T>`
4. Collect local variable declarations typed as `Span<T>` or `ReadOnlySpan<T>` within the method body
5. Flag each one found

**Message:** `"'{0}' is a Span<T> in an async method — Span<T> cannot safely cross await boundaries; use Memory<T> or ReadOnlyMemory<T> instead"`

---

## ID Registry Updates

| ID | Constant | Category |
|----|----------|----------|
| ZA0109 | `AvoidZeroLengthArrayAllocation` | Performance.Collections |
| ZA0209 | `AvoidValueTypeBoxingInStringConcat` | Performance.Strings |
| ZA0607 | `AvoidMultipleEnumeration` | Performance.Linq |
| ZA1104 | `AvoidSpanInAsyncMethod` | Performance.Async |

## Files to Create/Modify

- `src/ZeroAlloc.Analyzers/DiagnosticIds.cs` — add four new constants
- `src/ZeroAlloc.Analyzers/Analyzers/AvoidZeroLengthArrayAllocationAnalyzer.cs` — new
- `src/ZeroAlloc.Analyzers/Analyzers/AvoidValueTypeBoxingInStringConcatAnalyzer.cs` — new
- `src/ZeroAlloc.Analyzers/Analyzers/AvoidMultipleEnumerationAnalyzer.cs` — new
- `src/ZeroAlloc.Analyzers/Analyzers/AvoidSpanInAsyncMethodAnalyzer.cs` — new
- `src/ZeroAlloc.Analyzers.CodeFixes/AvoidZeroLengthArrayAllocationCodeFixProvider.cs` — new (ZA0109 only)
- `tests/ZeroAlloc.Analyzers.Tests/ZA0109_AvoidZeroLengthArrayAllocationTests.cs` — new
- `tests/ZeroAlloc.Analyzers.Tests/ZA0209_AvoidValueTypeBoxingInStringConcatTests.cs` — new
- `tests/ZeroAlloc.Analyzers.Tests/ZA0607_AvoidMultipleEnumerationTests.cs` — new
- `tests/ZeroAlloc.Analyzers.Tests/ZA1104_AvoidSpanInAsyncMethodTests.cs` — new
