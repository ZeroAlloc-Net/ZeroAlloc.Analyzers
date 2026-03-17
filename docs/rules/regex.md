---
id: rules-regex
title: Regex Rules (ZA07xx)
slug: /docs/rules/regex
description: GeneratedRegex source generator vs runtime regex compilation rules.
sidebar_position: 9
---

# Regex (ZA07xx)

Regular expressions compiled at runtime carry a one-time JIT cost, memory overhead for the compiled state machine, and — unless `RegexOptions.Compiled` is used — repeated interpretation overhead. The `[GeneratedRegex]` source generator introduced in .NET 7 moves all of this to compile time, producing faster, NativeAOT-compatible, zero-allocation regex dispatch.

---

## ZA0701 — Use GeneratedRegex for compile-time regex {#za0701}

> **Severity**: Info | **Min TFM**: net7.0 | **Code fix**: No

### Why

`new Regex(pattern, RegexOptions.Compiled)` compiles the regex to IL at runtime during the first call. `[GeneratedRegex]` emits the state machine as C# source at build time — faster startup, no IL emission at runtime, smaller warm-up allocations, and full NativeAOT compatibility. The generated method is a simple static partial method, so it drops in with zero API change at the call site. Because the state machine is baked into the assembly, the JIT can also inline the matching logic more aggressively than with a runtime-compiled `Regex` object.

### Before

```csharp
// ❌ compiled at runtime, not NativeAOT-safe
private static readonly Regex _slugRegex =
    new Regex(@"[^a-z0-9\-]", RegexOptions.Compiled | RegexOptions.IgnoreCase);
private static readonly Regex _emailRegex =
    new Regex(@"^[\w.+-]+@[\w-]+\.[a-zA-Z]{2,}$", RegexOptions.Compiled);
```

### After

```csharp
// ✓ generated at compile time — NativeAOT-safe, allocation-free
[GeneratedRegex(@"[^a-z0-9\-]", RegexOptions.IgnoreCase)]
private static partial Regex SlugRegex();

[GeneratedRegex(@"^[\w.+-]+@[\w-]+\.[a-zA-Z]{2,}$")]
private static partial Regex EmailRegex();
```

Note that `RegexOptions.Compiled` is not needed — the source generator always produces compiled (ahead-of-time) code. Passing `RegexOptions.Compiled` alongside `[GeneratedRegex]` has no additional effect.

### Real-world example

A content management system that converts article titles into URL-safe slugs and validates author email addresses before saving a draft. Both operations sit on the write-path and are called on every publish request.

```csharp
using System;
using System.Text.RegularExpressions;

namespace Cms.Content;

/// <summary>
/// Generates URL slugs and validates email addresses for CMS content.
/// Uses [GeneratedRegex] so all regex state machines are emitted at
/// build time — no IL emission at startup, full NativeAOT compatibility.
/// </summary>
public sealed partial class SlugGenerator
{
    // ✓ State machine generated at build time by the Roslyn source generator.
    // The partial method is implemented in a generated file; the class must
    // itself be declared partial.

    /// <summary>Matches any character that is NOT a lowercase letter, digit, or hyphen.</summary>
    [GeneratedRegex(@"[^a-z0-9\-]", RegexOptions.IgnoreCase)]
    private static partial Regex InvalidSlugCharRegex();

    /// <summary>Collapses runs of hyphens into a single hyphen.</summary>
    [GeneratedRegex(@"-{2,}")]
    private static partial Regex RepeatedHyphenRegex();

    /// <summary>Matches leading or trailing hyphens.</summary>
    [GeneratedRegex(@"^-|-$")]
    private static partial Regex LeadingTrailingHyphenRegex();

    /// <summary>
    /// Basic RFC 5321-style email validator.
    /// Does not attempt full RFC compliance — use a proper library for critical paths.
    /// </summary>
    [GeneratedRegex(@"^[\w.+-]+@[\w-]+\.[a-zA-Z]{2,}$")]
    private static partial Regex EmailRegex();

    /// <summary>
    /// Converts an arbitrary article title into a lowercase, hyphenated URL slug.
    /// </summary>
    /// <example>
    ///   "Hello, World! 2025" → "hello-world-2025"
    /// </example>
    public string Generate(string title)
    {
        if (string.IsNullOrWhiteSpace(title))
            return string.Empty;

        // 1. Lower-case the title so the slug is predictable regardless of input casing.
        string lower = title.ToLowerInvariant();

        // 2. Replace spaces with hyphens before stripping invalid chars so that
        //    word boundaries are preserved as separators.
        string spaced = lower.Replace(' ', '-');

        // 3. Remove every character that is not a lowercase letter, digit, or hyphen.
        string stripped = InvalidSlugCharRegex().Replace(spaced, string.Empty);

        // 4. Collapse consecutive hyphens produced by punctuation removal.
        string collapsed = RepeatedHyphenRegex().Replace(stripped, "-");

        // 5. Trim leading/trailing hyphens that may appear if the title starts
        //    or ends with non-alphanumeric characters.
        string slug = LeadingTrailingHyphenRegex().Replace(collapsed, string.Empty);

        return slug;
    }

    /// <summary>
    /// Returns true when <paramref name="email"/> matches the expected address format.
    /// </summary>
    public static bool IsValidEmail(string email)
    {
        if (string.IsNullOrWhiteSpace(email))
            return false;

        // GeneratedRegex produces a Regex-derived type whose IsMatch override is
        // implemented entirely in C# source — no reflection, no IL emission.
        return EmailRegex().IsMatch(email.Trim());
    }
}
```

**Usage:**

```csharp
var generator = new SlugGenerator();

string slug = generator.Generate("Hello, World! C# & .NET 2025");
// → "hello-world-c-net-2025"

bool valid = SlugGenerator.IsValidEmail("author@example.com");
// → true
```

### Suppression

```csharp
#pragma warning disable ZA0701
// or in .editorconfig: dotnet_diagnostic.ZA0701.severity = none
```
