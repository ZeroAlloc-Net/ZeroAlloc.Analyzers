# Logging (ZA04xx)

Structured logging sits on the critical path of nearly every production service. The ZA04xx rules ensure you use compile-time source generation rather than runtime reflection, eliminating per-call boxing and template parsing overhead.

---

## ZA0401 — Use LoggerMessage source generator {#za0401}

> **Severity**: Info | **Min TFM**: net6.0 | **Code fix**: No

### Why

Calling `logger.LogInformation("Processed {Count} items", count)` involves three costs on every call: boxing the `count` argument into an `object[]`, allocating a `FormattedLogValues` struct (which gets boxed), and parsing the message template string. The `[LoggerMessage]` source generator runs at compile time and produces a strongly-typed, non-virtual log method with no boxing, no template parsing, and no intermediate allocations.

Each call to a plain `ILogger.Log*` extension method allocates at minimum one array and one boxed struct. Under high request throughput — thousands of requests per second — this adds up to measurable GC pressure and increased pause frequency. Source-generated log methods are static, delegate directly to the underlying `ILogger.Log` overload, and carry zero per-call allocation cost.

### Before

```csharp
// ❌ boxes arguments, parses template at runtime
_logger.LogInformation("Request {Method} {Path} completed in {ElapsedMs}ms with status {Status}",
    context.Request.Method,
    context.Request.Path,
    sw.ElapsedMilliseconds,
    context.Response.StatusCode);
```

### After

```csharp
// ✓ compile-time generated — no boxing, no template parsing
public static partial class AppLog
{
    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Request {Method} {Path} completed in {ElapsedMs}ms with status {Status}")]
    public static partial void RequestCompleted(
        ILogger logger,
        string method,
        string path,
        long elapsedMs,
        int status);
}

// Usage:
AppLog.RequestCompleted(_logger, context.Request.Method, context.Request.Path,
    sw.ElapsedMilliseconds, context.Response.StatusCode);
```

### Real-world example

A complete ASP.NET Core request-logging middleware that measures every request and logs start, completion, slow-request warnings, and unhandled errors — all using `[LoggerMessage]` generated methods with zero per-call allocations.

```csharp
using System.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace MyApp.Middleware;

public sealed class RequestLoggingMiddleware
{
    private const long SlowRequestThresholdMs = 500;

    private readonly RequestDelegate _next;
    private readonly ILogger<RequestLoggingMiddleware> _logger;

    public RequestLoggingMiddleware(RequestDelegate next, ILogger<RequestLoggingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var sw = Stopwatch.StartNew();
        var method = context.Request.Method;
        var path = context.Request.Path.Value ?? "/";

        Log.RequestStarted(_logger, method, path);

        try
        {
            await _next(context);
            sw.Stop();

            var statusCode = context.Response.StatusCode;
            var elapsedMs = sw.ElapsedMilliseconds;

            if (elapsedMs >= SlowRequestThresholdMs)
            {
                Log.SlowRequest(_logger, method, path, elapsedMs, statusCode);
            }
            else
            {
                Log.RequestCompleted(_logger, method, path, elapsedMs, statusCode);
            }
        }
        catch (Exception ex)
        {
            sw.Stop();
            Log.RequestFailed(_logger, method, path, sw.ElapsedMilliseconds, ex);
            throw;
        }
    }
}

// ✓ All log methods are compile-time generated.
//   No object[] allocation, no FormattedLogValues boxing, no template parsing at runtime.
internal static partial class Log
{
    [LoggerMessage(
        EventId = 1000,
        Level = LogLevel.Debug,
        Message = "HTTP {Method} {Path} started")]
    internal static partial void RequestStarted(
        ILogger logger,
        string method,
        string path);

    [LoggerMessage(
        EventId = 1001,
        Level = LogLevel.Information,
        Message = "HTTP {Method} {Path} completed in {ElapsedMs}ms with status {StatusCode}")]
    internal static partial void RequestCompleted(
        ILogger logger,
        string method,
        string path,
        long elapsedMs,
        int statusCode);

    [LoggerMessage(
        EventId = 1002,
        Level = LogLevel.Warning,
        Message = "HTTP {Method} {Path} completed in {ElapsedMs}ms with status {StatusCode} — exceeds slow-request threshold")]
    internal static partial void SlowRequest(
        ILogger logger,
        string method,
        string path,
        long elapsedMs,
        int statusCode);

    [LoggerMessage(
        EventId = 1003,
        Level = LogLevel.Error,
        Message = "HTTP {Method} {Path} failed after {ElapsedMs}ms with unhandled exception")]
    internal static partial void RequestFailed(
        ILogger logger,
        string method,
        string path,
        long elapsedMs,
        Exception exception);
}
```

The generated code for each `[LoggerMessage]` method looks roughly like:

```csharp
// Source-generator output (illustrative — not hand-written)
internal static partial void RequestCompleted(
    ILogger logger, string method, string path, long elapsedMs, int statusCode)
{
    if (logger.IsEnabled(LogLevel.Information))
    {
        logger.Log(
            LogLevel.Information,
            new EventId(1001, nameof(RequestCompleted)),
            new RequestCompletedData(method, path, elapsedMs, statusCode),
            null,
            RequestCompletedData.Format);
    }
}
```

`RequestCompletedData` is a strongly-typed `struct` that implements `IReadOnlyList<KeyValuePair<string, object?>>` and formats the message without allocating. There is no `object[]`, no boxing of `elapsedMs` or `statusCode`, and no template parsing.

### Suppression

```csharp
#pragma warning disable ZA0401
_logger.LogInformation("Fallback path for {Reason}", reason);
#pragma warning restore ZA0401
// or in .editorconfig:
// dotnet_diagnostic.ZA0401.severity = none
```
