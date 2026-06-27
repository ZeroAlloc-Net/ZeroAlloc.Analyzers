---
id: rules-data-layout
title: Data Layout Rules (ZA16xx)
slug: /docs/rules/data-layout
description: Cache-line aware struct packing and false-sharing rules.
sidebar_position: 16
---

# Data Layout (ZA16xx)

CPUs never read individual bytes from memory — they pull in 64-byte **cache lines**. How your fields are laid out inside that invisible 64-byte grid decides how many lines a value spans, how many cache misses a loop takes, and whether two threads silently stall each other. The ZA16xx rules surface two layout problems the compiler will not warn you about: structs that waste space to alignment padding, and hot fields that share a cache line across threads (false sharing).

---

## ZA1601 — Reorder struct fields to reduce padding {#za1601}

> **Severity**: Info | **Min TFM**: Any | **Code fix**: No

### Why

Each field in a struct is aligned to its natural boundary — an `int` to 4 bytes, a `long`/`double` to 8. When a small field precedes a larger one, the compiler inserts invisible **padding** bytes to satisfy that alignment, and the struct is finally rounded up to the size of its largest member. Declaring fields from largest alignment to smallest packs them tightly and shrinks the struct. A smaller struct touches fewer cache lines when stored in an array or collection, so hot loops over it take fewer cache misses.

This rule only fires on structs with the **compiler-default layout** (no explicit `[StructLayout]` attribute) made up entirely of fixed-size primitive or enum fields, so the suggested reorder is always safe and the byte counts are deterministic across platforms. Structs with an explicit `[StructLayout]` are left alone — that attribute usually signals intentional ordering for interop or serialization.

### Before

```csharp
// ❌ 24 bytes: 7 bytes of padding after A, 7 more after C
struct Particle
{
    public byte Flags;   // offset 0
    public long Id;      // offset 8 (7 padding bytes inserted before it)
    public byte Team;    // offset 16 (struct rounded up to 24)
}
```

### After

```csharp
// ✓ 16 bytes: largest alignment first, no wasted padding
struct Particle
{
    public long Id;      // offset 0
    public byte Flags;   // offset 8
    public byte Team;    // offset 9 (struct rounded up to 16)
}
```

### Real-world example

A particle system stores a million particles in a `Particle[]`. At 24 bytes each the array is ~24 MB and each 64-byte cache line holds 2 particles with 16 wasted bytes. Reordered to 16 bytes the array is ~16 MB and a cache line holds 4 particles — the per-frame update loop streams through a third less memory and takes far fewer cache misses, with no change to the code that uses the struct.

### Suppression

```csharp
#pragma warning disable ZA1601
struct Layout { public byte A; public long B; public byte C; }
#pragma warning restore ZA1601
// or in .editorconfig:
// dotnet_diagnostic.ZA1601.severity = none
```

---

## ZA1602 — Isolate Interlocked-updated fields to avoid false sharing {#za1602}

> **Severity**: Info | **Min TFM**: Any | **Code fix**: No | **Enabled by default**: No (opt-in)

### Why

When two CPU cores write to *different* fields that happen to sit on the **same 64-byte cache line**, each write invalidates the other core's cached copy of the whole line. The cores ping-pong the line back and forth over the cache-coherency protocol, stalling both threads — even though they never touched the same variable. This is **false sharing**, and it is invisible: there is no lock, no syscall, and it never shows up in a profiler as a single hot line.

The classic trigger is a type with several counters updated with `Interlocked` from different threads, all packed together. This rule flags an instance field that is the target of an `Interlocked` operation when it shares its containing type — and therefore likely a cache line — with other instance fields, and the type does not already use explicit layout to separate them.

Whether false sharing actually occurs depends on runtime threading, which cannot be proven statically, so **this rule is disabled by default**. Enable it on hot, highly-concurrent types:

```ini
# .editorconfig
dotnet_diagnostic.ZA1602.severity = info
```

### Before

```csharp
// ❌ _reads and _writes live on the same cache line; two threads updating
//    them independently invalidate each other's line on every write
class Stats
{
    private long _reads;
    private long _writes;

    public void RecordRead()  => Interlocked.Increment(ref _reads);
    public void RecordWrite() => Interlocked.Increment(ref _writes);
}
```

### After

```csharp
// ✓ each counter sits on its own 64-byte cache line — no false sharing
using System.Runtime.InteropServices;

[StructLayout(LayoutKind.Explicit)]
class Stats
{
    [FieldOffset(0)]  private long _reads;
    [FieldOffset(64)] private long _writes;

    public void RecordRead()  => Interlocked.Increment(ref _reads);
    public void RecordWrite() => Interlocked.Increment(ref _writes);
}
```

> **Note:** padding only works reliably on structs and explicitly-laid-out types. The JIT is free to reorder the fields of an ordinary reference type, so adding "spacer" fields to a plain `class` does not guarantee separation — use `[StructLayout(LayoutKind.Explicit)]` with `FieldOffset`, or per-thread/striped counters, when isolation matters.

### Real-world example

A high-throughput request pipeline keeps `_successCount` and `_failureCount` as adjacent `long` fields, each bumped with `Interlocked.Increment` from worker threads. Under load the two counters land on one cache line and the workers spend their time ping-ponging that line between cores. Separating the counters onto distinct cache lines removes the contention and the per-core throughput recovers — with no change to the counting logic.

### Suppression

```csharp
#pragma warning disable ZA1602
// or in .editorconfig:
// dotnet_diagnostic.ZA1602.severity = none   (already disabled by default)
```
