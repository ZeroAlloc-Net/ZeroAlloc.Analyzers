---
id: rules-enums
title: Enums Rules (ZA08xx)
slug: /docs/rules/enums
description: HasFlag boxing on pre-.NET 7 and Enum.ToString reflection rules.
sidebar_position: 10
---

# Enums (ZA08xx)

Enum operations look cheap but hide subtle allocation traps: `HasFlag` boxes on older runtimes, `ToString()` uses reflection and allocates a string every call, and `GetName`/`GetValues` in loops repeat expensive reflection on every iteration. The ZA08xx rules catch these patterns.

---

## ZA0801 — Avoid Enum.HasFlag (boxes on pre-net7.0) {#za0801}

> **Severity**: Info | **Min TFM**: active only when TargetFramework < net7.0, disabled on net7.0+ | **Code fix**: No

### Why

On runtimes before .NET 7, `Enum.HasFlag(Enum flag)` accepts an `Enum` (base class) parameter, so both `this` and `flag` are boxed before the comparison — two hidden heap allocations per call. On .NET 7+ the JIT intrinsifies `HasFlag` and eliminates boxing entirely. This rule only fires for projects targeting frameworks older than net7.0; it is automatically suppressed when the project's `TargetFramework` is net7.0 or later. Replacing `HasFlag` with a bitwise `&` comparison is a mechanical transformation that is always correct for `[Flags]` enums and carries zero runtime overhead on any framework.

### Before

```csharp
// ❌ On pre-.NET 7: boxes both 'permissions' and 'Permission.Write' before comparing
if (permissions.HasFlag(Permission.Write))
{
    AllowWrite(resource);
}

if (permissions.HasFlag(Permission.Read) && permissions.HasFlag(Permission.Execute))
{
    RunScript(resource);
}
```

### After

```csharp
// ✓ Bitwise — no boxing on any framework, zero overhead
if ((permissions & Permission.Write) == Permission.Write)
{
    AllowWrite(resource);
}

if ((permissions & (Permission.Read | Permission.Execute)) == (Permission.Read | Permission.Execute))
{
    RunScript(resource);
}
```

### Real-world example

A permission authorization handler in an ASP.NET Core application targeting net6.0. The handler runs on every authenticated request, so the two `HasFlag` calls inside the hot path contribute measurable allocations under load.

```csharp
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;

namespace MyApp.Authorization;

[Flags]
public enum Permission : uint
{
    None    = 0,
    Read    = 1 << 0,
    Write   = 1 << 1,
    Delete  = 1 << 2,
    Execute = 1 << 3,
    Admin   = Read | Write | Delete | Execute,
}

public sealed class PermissionRequirement : IAuthorizationRequirement
{
    public Permission Required { get; }

    public PermissionRequirement(Permission required) => Required = required;
}

/// <summary>
/// Authorization handler evaluated on every request for endpoints that carry
/// a [RequirePermission] attribute. Runs inside the ASP.NET Core middleware
/// pipeline, so the hot path must stay allocation-free.
/// </summary>
public sealed class PermissionAuthorizationHandler
    : AuthorizationHandler<PermissionRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        PermissionRequirement requirement)
    {
        var permissionClaim = context.User.FindFirst("permissions");
        if (permissionClaim is null || !uint.TryParse(permissionClaim.Value, out var raw))
        {
            context.Fail();
            return Task.CompletedTask;
        }

        var granted = (Permission)raw;

        // ✓ Bitwise comparison — no boxing on net6.0.
        // Equivalent to granted.HasFlag(requirement.Required) but without
        // the two allocations per call that HasFlag incurs on pre-.NET 7.
        if ((granted & requirement.Required) == requirement.Required)
        {
            context.Succeed(requirement);
        }
        else
        {
            context.Fail();
        }

        return Task.CompletedTask;
    }
}
```

### Suppression

```csharp
#pragma warning disable ZA0801
// or in .editorconfig: dotnet_diagnostic.ZA0801.severity = none
```

---

## ZA0802 — Avoid Enum.ToString() allocations {#za0802}

> **Severity**: Info | **Min TFM**: Any | **Code fix**: No

### Why

`Enum.ToString()` uses reflection to look up the symbolic name for an enum value. Prior to .NET 9 there is no caching of the result string, so every call allocates a new `string` on the managed heap. On a hot path — a request handler, a serialisation loop, a metrics label builder — these small allocations accumulate and increase GC pressure. The fix is to cache name strings in a `static readonly Dictionary<TEnum, string>` built once at startup, or to use a `switch` expression for a fixed, well-known set of values. Both approaches reduce the per-call cost to a single dictionary lookup or branch, with zero heap allocation.

### Before

```csharp
// ❌ Enum.ToString() uses reflection and allocates a new string on every call
foreach (var order in pendingOrders)
{
    string statusName = order.Status.ToString();   // allocation per iteration
    logger.LogInformation("Processing order {Id} with status {Status}",
        order.Id, statusName);
}
```

### After (option 1 — static dictionary, recommended for large value sets)

```csharp
// ✓ Pre-built name map — computed once at startup, O(1) lookup per call
private static readonly Dictionary<OrderStatus, string> _statusNames =
    Enum.GetValues<OrderStatus>()
        .ToDictionary(s => s, s => s.ToString());   // ToString() called only at startup

foreach (var order in pendingOrders)
{
    string statusName = _statusNames[order.Status]; // no allocation
    logger.LogInformation("Processing order {Id} with status {Status}",
        order.Id, statusName);
}
```

### After (option 2 — switch expression, best for small well-known sets)

```csharp
// ✓ Switch expression — JIT can compile to a jump table, no reflection
string statusName = order.Status switch
{
    OrderStatus.Pending    => "Pending",
    OrderStatus.Processing => "Processing",
    OrderStatus.Shipped    => "Shipped",
    OrderStatus.Delivered  => "Delivered",
    OrderStatus.Cancelled  => "Cancelled",
    _                      => order.Status.ToString(), // fallback for unknown values
};
```

### Real-world example

A CSV exporter that serialises thousands of orders to a response stream. Each row writes the `OrderStatus` as a human-readable string. With `ToString()` on every row the allocations are visible in a dotnet-trace profile; switching to the dictionary cache eliminates them entirely.

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace MyApp.Exports;

public enum OrderStatus
{
    Pending,
    Processing,
    Shipped,
    Delivered,
    Cancelled,
    Refunded,
}

public sealed record Order(Guid Id, string Customer, decimal Total, OrderStatus Status);

public sealed class OrderCsvExporter
{
    // ✓ Name strings are materialised exactly once when the class is first used.
    // All subsequent calls to Export() reuse these interned string references.
    private static readonly Dictionary<OrderStatus, string> _statusNames =
        Enum.GetValues<OrderStatus>()
            .ToDictionary(s => s, s => s.ToString());

    private static ReadOnlySpan<byte> CsvHeader =>
        "Id,Customer,Total,Status\r\n"u8;

    /// <summary>
    /// Writes all <paramref name="orders"/> as UTF-8 CSV to <paramref name="stream"/>.
    /// Designed for large result sets — avoids per-row string allocations for enum names.
    /// </summary>
    public async Task ExportAsync(
        IReadOnlyList<Order> orders,
        Stream stream,
        CancellationToken cancellationToken = default)
    {
        // Write header directly as UTF-8 bytes — no string allocation.
        await stream.WriteAsync(CsvHeader.ToArray(), cancellationToken);

        // Rent a reusable StringBuilder to avoid per-row allocations for
        // the non-enum fields as well.
        var sb = new StringBuilder(256);

        foreach (var order in orders)
        {
            cancellationToken.ThrowIfCancellationRequested();

            sb.Clear();
            sb.Append(order.Id);
            sb.Append(',');
            sb.Append(order.Customer);
            sb.Append(',');
            sb.Append(order.Total.ToString("F2"));
            sb.Append(',');

            // ✓ Dictionary lookup — no reflection, no string allocation.
            sb.Append(_statusNames[order.Status]);
            sb.Append("\r\n");

            // Write the line as UTF-8 bytes.
            byte[] line = Encoding.UTF8.GetBytes(sb.ToString());
            await stream.WriteAsync(line, cancellationToken);
        }
    }
}
```

### Suppression

```csharp
#pragma warning disable ZA0802
// or in .editorconfig: dotnet_diagnostic.ZA0802.severity = none
```

---

## ZA0803 — Cache Enum.GetName / GetValues in loops {#za0803}

> **Severity**: Info | **Min TFM**: Any | **Code fix**: No

### Why

`Enum.GetValues<T>()` and `Enum.GetName<T>(value)` use reflection internally and are non-trivial operations. `GetValues<T>()` allocates a new array containing every defined enum value on every call. `GetName<T>(value)` performs a linear or binary search through the reflection metadata on every invocation. Calling either inside a loop re-runs that work on every iteration. The fix is to compute the results once and store them in `static readonly` fields; the loop then iterates over the pre-built arrays without touching the reflection subsystem at all.

### Before

```csharp
// ❌ GetValues<T>() allocates a fresh array on every call;
//    GetName<T>() does a reflection lookup on every iteration.
foreach (var status in Enum.GetValues<OrderStatus>())
{
    _metrics.AddCounter(Enum.GetName<OrderStatus>(status) ?? status.ToString());
}
```

### After

```csharp
// ✓ Cached arrays — reflection runs exactly once at class initialisation.
private static readonly OrderStatus[] _statuses =
    Enum.GetValues<OrderStatus>();

private static readonly string[] _statusNames =
    _statuses.Select(s => s.ToString()).ToArray();

foreach (var (status, name) in _statuses.Zip(_statusNames))
{
    _metrics.AddCounter(name);
}
```

### Real-world example

Application startup code that registers one OpenTelemetry counter per `OrderStatus` value. The registration runs once, but in integration tests and benchmarks the initialiser is called repeatedly — making the reflection cost visible. The `MetricsInitializer` below caches the values as `static readonly` fields so the loop body is pure array iteration.

```csharp
using System;
using System.Diagnostics.Metrics;
using System.Linq;

namespace MyApp.Telemetry;

public enum OrderStatus
{
    Pending,
    Processing,
    Shipped,
    Delivered,
    Cancelled,
    Refunded,
}

/// <summary>
/// Registers one OpenTelemetry counter per <see cref="OrderStatus"/> value
/// during application startup. Caches the enum values and names so that
/// reflection is never invoked inside the registration loop.
/// </summary>
public sealed class MetricsInitializer
{
    // ✓ Reflection runs once here, at class initialisation time.
    private static readonly OrderStatus[] _statuses =
        Enum.GetValues<OrderStatus>();

    // ✓ ToString() called once per value, result stored permanently.
    private static readonly string[] _statusNames =
        _statuses.Select(s => s.ToString()).ToArray();

    private readonly Meter _meter;

    // Keyed by status name — populated during Initialize().
    private readonly Counter<long>[] _orderCounters;

    public MetricsInitializer(Meter meter)
    {
        _meter = meter ?? throw new ArgumentNullException(nameof(meter));
        _orderCounters = new Counter<long>[_statuses.Length];
    }

    /// <summary>
    /// Creates one counter per status value and stores it for later use.
    /// Should be called once from the DI composition root / host startup.
    /// </summary>
    public void Initialize()
    {
        // ✓ Loop body is pure array access — no reflection at this point.
        for (int i = 0; i < _statuses.Length; i++)
        {
            string name = _statusNames[i];
            _orderCounters[i] = _meter.CreateCounter<long>(
                name: $"orders.{name.ToLowerInvariant()}",
                unit: "orders",
                description: $"Total number of orders with status {name}.");
        }
    }

    /// <summary>
    /// Increments the counter for the given <paramref name="status"/>.
    /// Called on every state transition — must stay allocation-free.
    /// </summary>
    public void RecordTransition(OrderStatus status)
    {
        // Array index lookup — O(1), no reflection.
        int index = Array.IndexOf(_statuses, status);
        if (index >= 0)
            _orderCounters[index].Add(1);
    }
}
```

### Suppression

```csharp
#pragma warning disable ZA0803
// or in .editorconfig: dotnet_diagnostic.ZA0803.severity = none
```
