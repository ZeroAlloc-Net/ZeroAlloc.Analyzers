# Design: ZLinq-Inspired Performance Warning Analyzers

**Date:** 2026-03-13
**Source inspiration:** [Cysharp/ZLinq](https://github.com/Cysharp/ZLinq) — performance pitfalls documented in README

## Context

ZLinq documents several performance pitfalls that cause unexpected heap allocations. Three of these map cleanly to new Roslyn analyzer rules for ZeroAlloc that don't overlap with any existing rule.

## Rules

### ZA0606 — `AvoidForeachOverInterfaceCollection`

**Category:** `Performance.Linq`
**Severity:** Warning

**Problem:** When a local variable is declared as `IEnumerable<T>`, `ICollection<T>`, `IList<T>`, `IReadOnlyCollection<T>`, or `IReadOnlyList<T>` and used in a `foreach`, the runtime must call `GetEnumerator()` through the interface, which boxes the enumerator onto the heap. Concrete types like `List<T>` and `T[]` have optimized `foreach` paths that avoid this allocation.

```csharp
// Bad — allocates heap enumerator
IEnumerable<int> items = new List<int> { 1, 2, 3 };
foreach (var item in items) { }

// Good — no enumerator allocation
List<int> items = new List<int> { 1, 2, 3 };
foreach (var item in items) { }
```

**Detection logic:**
1. Register `ForEachStatement` syntax nodes
2. Get the type of the foreach expression via semantic model
3. If the type is one of the above interfaces (not a concrete type):
4. Walk up the syntax tree to find the local variable declaration
5. Only flag if the variable is **declared locally** and initialized with a concrete collection (new expression, array initializer, etc.)
6. Skip method parameters, fields, and variables initialized from method calls (caller controls those)

**Message:** `"Local variable '{0}' is typed as '{1}' — foreach allocates a heap enumerator; use a concrete type (List<T>, T[]) instead"`

**Code fix:** None (changing declared type may require refactoring throughout the method)

---

### ZA0208 — `AvoidStringJoinBoxingOverload`

**Category:** `Performance.Strings`
**Severity:** Warning

**Problem:** `string.Join` has two overloads: `Join(string, IEnumerable<string>)` and `Join(string/char, params object[])`. When the argument is not `IEnumerable<string>`, the compiler selects the `params object[]` overload, boxing every element and calling `ToString()` implicitly. This is both an allocation and a correctness footgun.

```csharp
List<int> numbers = new() { 1, 2, 3 };

// Bad — resolves to params object[], boxes each int
string result = string.Join(", ", numbers);

// Good — explicit, no boxing
string result = string.Join(", ", numbers.Select(n => n.ToString()));
```

**Detection logic:**
1. Register `InvocationExpression` for method name `Join`
2. Verify via semantic model it resolves to `string.Join`
3. Check the resolved overload — flag if it is `string.Join(string, params object[])` or `string.Join(char, params object[])`

**Message:** `"string.Join resolves to the 'params object[]' overload — each element is boxed; use .Select(x => x.ToString()) or cast to IEnumerable<string>"`

**Code fix:** None (safe transformation depends on element type and intent)

---

### ZA0108 — `AvoidRedundantMaterialization`

**Category:** `Performance.Collections`
**Severity:** Warning

**Problem:** Calling `.ToList()` on an already-`List<T>` or `.ToArray()` on an already-`T[]` allocates a new collection unnecessarily. This is a common mistake when refactoring or when the source type is not checked.

```csharp
List<int> items = new() { 1, 2, 3 };

// Bad — allocates a new List<int> with the same elements
var copy = items.ToList();

// Good — just use items directly, or use AsSpan()/slice if a copy is needed
```

**Detection logic:**
1. Register `InvocationExpression` nodes for `.ToList()` and `.ToArray()`
2. Get the receiver's type via semantic model
3. Flag if: `.ToList()` receiver is `List<T>` or a subtype; `.ToArray()` receiver is `T[]`
4. Also flag: `.ToArray()` on `ImmutableArray<T>` (array-backed, copying is wasteful)

**Message:** `"'{0}' is already a {1} — '{2}' allocates a new collection unnecessarily; use the original directly"`

**Code fix:** Yes — remove the `.ToList()`/`.ToArray()` invocation, leaving the receiver expression in place

---

## ID Registry Updates

| ID | Constant | Category |
|----|----------|----------|
| ZA0108 | `AvoidRedundantMaterialization` | Performance.Collections |
| ZA0208 | `AvoidStringJoinBoxingOverload` | Performance.Strings |
| ZA0606 | `AvoidForeachOverInterfaceCollection` | Performance.Linq |

## Files to Create/Modify

- `src/ZeroAlloc.Analyzers/DiagnosticIds.cs` — add three new constants
- `src/ZeroAlloc.Analyzers/Analyzers/AvoidForeachOverInterfaceCollectionAnalyzer.cs` — new
- `src/ZeroAlloc.Analyzers/Analyzers/AvoidStringJoinBoxingOverloadAnalyzer.cs` — new
- `src/ZeroAlloc.Analyzers/Analyzers/AvoidRedundantMaterializationAnalyzer.cs` — new
- `src/ZeroAlloc.Analyzers.CodeFixes/AvoidRedundantMaterializationCodeFixProvider.cs` — new (ZA0108 only)
- `tests/ZeroAlloc.Analyzers.Tests/` — test files for each rule
