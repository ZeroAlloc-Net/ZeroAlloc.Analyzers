---
id: rules-serialization
title: Serialization Rules (ZA10xx)
slug: /docs/rules/serialization
description: JSON source generation vs reflection-based serialization rules.
sidebar_position: 12
---

# Serialization (ZA10xx)

Reflection-based JSON serialization has significant startup cost: types must be discovered, constructors and properties must be resolved via reflection, and serialization delegates are generated dynamically at runtime. On net7.0+, `System.Text.Json` source generators produce all of this code at compile time — faster, AOT-compatible, and trimming-safe.

---

## ZA1001 — Use JSON source generation instead of reflection {#za1001}

> **Severity**: Info | **Min TFM**: net7.0 | **Code fix**: No

### Why

`JsonSerializer.Serialize<T>(value)` and `JsonSerializer.Deserialize<T>(json)` use a reflection-based metadata resolver by default. On the first call for each type, reflection walks the type's properties and constructors to generate a converter. This is slow, incompatible with NativeAOT, and prevents trimming. `[JsonSerializable]`-annotated `JsonSerializerContext` subclasses generate all converters at compile time, eliminating the reflection overhead entirely and making the code safe for use in trimmed and AOT-published applications.

### Before

```csharp
// ❌ reflection-based — slow first call, not AOT-safe
public async Task<IActionResult> GetOrder(int id)
{
    var order = await _service.GetByIdAsync(id);
    return Ok(order); // implicit JsonSerializer.Serialize(order)
}

// elsewhere:
var payload = JsonSerializer.Serialize(command);
var response = JsonSerializer.Deserialize<ApiResponse<OrderDto>>(json);
```

### After

```csharp
// ✓ source-generated — compile-time converters, AOT-safe
[JsonSerializable(typeof(OrderDto))]
[JsonSerializable(typeof(ApiResponse<OrderDto>))]
[JsonSerializable(typeof(CreateOrderCommand))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
public partial class AppJsonContext : JsonSerializerContext { }

// Usage:
var payload = JsonSerializer.Serialize(command, AppJsonContext.Default.CreateOrderCommand);
var response = JsonSerializer.Deserialize(json, AppJsonContext.Default.ApiResponseOrderDto);
```

### Real-world example

An ASP.NET Core minimal API that registers a `JsonSerializerContext` for the whole application so that all serialization in controllers and middleware uses source-generated converters. The `Program.cs` wires the context into `HttpJsonOptions` via `ConfigureHttpJsonOptions`, and the context class enumerates all application types that cross serialization boundaries.

```csharp
// AppJsonContext.cs
// ✓ Declare all types that the application serializes or deserializes.
// The source generator emits a TypeInfo<T> property for each one.
[JsonSerializable(typeof(OrderDto))]
[JsonSerializable(typeof(OrderLineDto))]
[JsonSerializable(typeof(ApiResponse<OrderDto>))]
[JsonSerializable(typeof(ApiResponse<IReadOnlyList<OrderDto>>))]
[JsonSerializable(typeof(CreateOrderCommand))]
[JsonSerializable(typeof(UpdateOrderStatusCommand))]
[JsonSerializable(typeof(ProblemDetails))]
[JsonSourceGenerationOptions(
    PropertyNamingPolicy        = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition      = JsonIgnoreCondition.WhenWritingNull,
    WriteIndented               = false)]
public partial class AppJsonContext : JsonSerializerContext { }

// Program.cs
var builder = WebApplication.CreateBuilder(args);

// ✓ Replace the default reflection-based options with the source-generated context.
// All MVC controllers, minimal API endpoints, and HttpClient responses
// will now use compile-time-generated converters.
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.TypeInfoResolverChain.Insert(0, AppJsonContext.Default);
});

// If you also use IHttpClientFactory / typed clients, apply the same options there.
builder.Services.AddHttpClient<OrderApiClient>()
    .ConfigureHttpClient(_ => { }); // base address etc.

var app = builder.Build();

// ❌ Before: implicit reflection-based serialization in a minimal API endpoint
app.MapGet("/orders/{id:int}", async (int id, IOrderService svc) =>
{
    var order = await svc.GetByIdAsync(id);
    // Results.Ok(order) calls JsonSerializer.Serialize(order) with the default, reflection-based
    // JsonSerializerOptions — type metadata is resolved at runtime on the first request.
    return order is null ? Results.NotFound() : Results.Ok(order);
});

// ✓ After: the context registered above is used automatically for all endpoints.
// No change needed at the endpoint level — the TypeInfoResolverChain makes it transparent.
app.MapGet("/orders/{id:int}", async (int id, IOrderService svc) =>
{
    var order = await svc.GetByIdAsync(id);
    return order is null ? Results.NotFound() : Results.Ok(order);
});

app.Run();

// OrderApiClient.cs — typed HTTP client
// ❌ Before: reflection-based deserialization in a typed HTTP client
public sealed class OrderApiClientBefore
{
    private readonly HttpClient _http;

    public OrderApiClientBefore(HttpClient http) => _http = http;

    public async Task<ApiResponse<OrderDto>?> GetOrderAsync(int id, CancellationToken ct)
    {
        var json = await _http.GetStringAsync($"/orders/{id}", ct);
        // JsonSerializer.Deserialize resolves type metadata via reflection on the first call.
        return JsonSerializer.Deserialize<ApiResponse<OrderDto>>(json);
    }

    public async Task CreateOrderAsync(CreateOrderCommand command, CancellationToken ct)
    {
        var payload = JsonSerializer.Serialize(command);   // reflection-based
        using var content = new StringContent(payload, Encoding.UTF8, "application/json");
        await _http.PostAsync("/orders", content, ct);
    }
}

// ✓ After: source-generated overloads — TypeInfo<T> resolved at compile time
public sealed class OrderApiClient
{
    private readonly HttpClient _http;

    public OrderApiClient(HttpClient http) => _http = http;

    public async Task<ApiResponse<OrderDto>?> GetOrderAsync(int id, CancellationToken ct)
    {
        using var response = await _http.GetAsync($"/orders/{id}", ct);
        response.EnsureSuccessStatusCode();
        using var stream = await response.Content.ReadAsStreamAsync(ct);
        // Pass the compile-time TypeInfo — no reflection, AOT-safe, trim-safe.
        return await JsonSerializer.DeserializeAsync(
            stream,
            AppJsonContext.Default.ApiResponseOrderDto,
            ct);
    }

    public async Task CreateOrderAsync(CreateOrderCommand command, CancellationToken ct)
    {
        // JsonSerializer.Serialize with TypeInfo overload — fully source-generated.
        var payload = JsonSerializer.Serialize(
            command,
            AppJsonContext.Default.CreateOrderCommand);

        using var content = new StringContent(payload, Encoding.UTF8, "application/json");
        await _http.PostAsync("/orders", content, ct);
    }
}
```

### Suppression

```csharp
#pragma warning disable ZA1001
// or in .editorconfig: dotnet_diagnostic.ZA1001.severity = none
```
