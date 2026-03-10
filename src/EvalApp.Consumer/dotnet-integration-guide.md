# EvalApp — .NET Integration Guide

> How to integrate `EvalApp.Consumer` into ASP.NET Core and Generic Host applications.
> This guide assumes familiarity with .NET dependency injection. Read [user-guide.md](user-guide.md)
> first for core pipeline concepts.

---

## Table of Contents

1. [Installation & Setup](#1-installation--setup)
2. [Dependency Injection](#2-dependency-injection)
3. [Step Design Patterns](#3-step-design-patterns)
   - [3b. Cross-Domain & Side-Effect Steps](#3b-cross-domain--side-effect-steps)
4. [Pipeline Lifecycle](#4-pipeline-lifecycle)
5. [Gate Configuration & Tuning](#5-gate-configuration--tuning)
6. [ForEach — Parallel Collections](#6-foreach--parallel-collections)
7. [Branching & Sagas](#7-branching--sagas)
8. [Testing Pipelines](#8-testing-pipelines)
9. [Error Handling & Observability](#9-error-handling--observability)
10. [Complete Example — Order Processing Service](#10-complete-example--order-processing-service)

---

## 1. Installation & Setup

### NuGet Reference

```xml
<PackageReference Include="EvalApp.Consumer" Version="1.0.0-beta.1" />
```

### License Key Storage

The license key is passed to `.Build(licenseKey)` when building the pipeline. Store it
outside source control.

**Option A — User Secrets (development)**

```bash
dotnet user-secrets set "EvalApp:LicenseKey" "YOUR-LICENSE-KEY"
```

```csharp
// appsettings.json (development placeholder only — real value comes from user secrets)
{
  "EvalApp": {
    "LicenseKey": ""
  }
}
```

**Option B — Environment variable (CI/production)**

```
EVALAPP_LICENSE_KEY=YOUR-LICENSE-KEY
```

```csharp
// Read at startup
var licenseKey = builder.Configuration["EvalApp:LicenseKey"]
    ?? Environment.GetEnvironmentVariable("EVALAPP_LICENSE_KEY");
```

**Option C — appsettings (non-sensitive environments only)**

```json
{
  "EvalApp": {
    "LicenseKey": "YOUR-LICENSE-KEY"
  }
}
```

> **Never commit a license key to source control.** Use user secrets locally and
> environment variables or a secrets manager (Azure Key Vault, AWS Secrets Manager) in
> production.

### Recommended Project Structure

Organize pipeline code by feature domain. Pipeline construction is centralised in a single
extension method — there are no per-domain factory classes:

```
YourApp/
├── Pipelines/
│   ├── EvalAppPipelines.cs             ← holder record for all compiled pipelines
│   ├── ServiceCollectionExtensions.cs  ← AddEvalApp extension (single App build)
│   ├── Orders/
│   │   ├── Data/
│   │   │   └── OrderData.cs            ← immutable record
│   │   └── Steps/
│   │       ├── ValidateOrderStep.cs    ← PureStep<OrderData>
│   │       ├── FetchPriceStep.cs       ← AsyncStep<OrderData>
│   │       ├── CheckInventoryStep.cs   ← AsyncStep<OrderData>
│   │       └── SaveOrderStep.cs        ← AsyncStep<OrderData>
│   └── Notifications/
│       ├── Data/
│       └── Steps/
└── Program.cs
```

---

## 2. Dependency Injection

### Core Rule: Build Once, Inject Everywhere

A compiled pipeline (`ICompiledPipeline<T>`) is **thread-safe and reusable**. Build it once
at application startup and register it as a singleton. Rebuilding per request wastes the
tuner's accumulated state and adds unnecessary overhead.

```csharp
// ✅ Correct — build at startup, reuse on every request
services.AddSingleton<ICompiledPipeline<OrderData>>(sp =>
{
    var licenseKey = sp.GetRequiredService<IConfiguration>()["EvalApp:LicenseKey"];
    ICompiledPipeline<OrderData> pipeline;

    EvalApp.App("Orders")
        .WithResource(ResourceKind.Network)
        .WithResource(ResourceKind.Database)
        .WithTuning()
        .DefineDomain("OrderProcessing")
            .DefineTask<OrderData>("ProcessOrder")
                .AddStep("Validate", new ValidateOrderStep())
                .Gate(ResourceKind.Database, null, g => g
                    .AddStep("CheckInventory", new CheckInventoryStep(
                        sp.GetRequiredService<IInventoryRepository>())))
                .Gate(ResourceKind.Network, null, g => g
                    .AddStep("FetchPrice", new FetchPriceStep(
                        sp.GetRequiredService<IHttpClientFactory>())))
                .Gate(ResourceKind.Database, null, g => g
                    .AddStep("SaveOrder", new SaveOrderStep(
                        sp.GetRequiredService<IOrderRepository>())))
                .Run(out pipeline)
            .Build(licenseKey);

    return pipeline;
});
```

```csharp
// ❌ Wrong — rebuilt on every request; tuner state is lost
app.MapPost("/orders", async (OrderRequest req, IConfiguration config) =>
{
    ICompiledPipeline<OrderData> pipeline;
    EvalApp.App("Orders")
        .DefineDomain("...")
            .DefineTask<OrderData>("...")
                .AddStep(...)
                .Run(out pipeline)
            .Build(config["EvalApp:LicenseKey"]);   // 🔴 rebuild on every request

    return await pipeline.RunAsync(...);
});
```

### AddEvalApp Extension Method Pattern

For applications with multiple pipelines, centralise registration in an extension method.
Build **all** pipelines in a single `EvalApp.App()` call so they share resources, tuner state,
and the `ServiceProviderStepFactory`. Capture each pipeline via `.Run(out ...)`, store them
in a holder record, and expose each one individually through DI:

```csharp
// Pipelines/EvalAppPipelines.cs
public record EvalAppPipelines(
    ICompiledPipeline<OrderData> Orders,
    ICompiledPipeline<NotificationData> Notifications);
```

```csharp
// Pipelines/ServiceCollectionExtensions.cs
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddEvalApp(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var licenseKey = configuration["EvalApp:LicenseKey"]
            ?? Environment.GetEnvironmentVariable("EVALAPP_LICENSE_KEY");

        // Register step classes so the factory can resolve them
        services.AddSingleton<ValidateOrderStep>();
        services.AddSingleton<CheckInventoryStep>();
        services.AddSingleton<FetchPriceStep>();
        services.AddSingleton<SaveOrderStep>();
        services.AddSingleton<SendNotificationStep>();

        // Build ALL pipelines in one App call — shared resources and tuner state
        services.AddSingleton(sp =>
        {
            ICompiledPipeline<OrderData> orderPipeline;
            ICompiledPipeline<NotificationData> notificationPipeline;

            EvalApp.App("Commerce")
                .WithStepFactory(new ServiceProviderStepFactory(sp))
                .WithResource(ResourceKind.Network)
                .WithResource(ResourceKind.Database)
                .WithTuning()
                .DefineDomain("Processing")
                    .DefineTask<OrderData>("ProcessOrder")
                        .AddStep<ValidateOrderStep>("Validate")
                        .Gate(ResourceKind.Database, null, g => g
                            .AddStep<CheckInventoryStep>("CheckInventory"))
                        .Gate(ResourceKind.Network, null, g => g
                            .AddStep<FetchPriceStep>("FetchPrice"))
                        .Gate(ResourceKind.Database, null, g => g
                            .AddStep<SaveOrderStep>("SaveOrder"))
                        .Run(out orderPipeline)
                    .DefineTask<NotificationData>("SendNotification")
                        .AddStep<SendNotificationStep>("Send")
                        .Run(out notificationPipeline)
                    .Build(licenseKey);

            return new EvalAppPipelines(orderPipeline, notificationPipeline);
        });

        // Expose individual pipelines from the holder
        services.AddSingleton(sp =>
            sp.GetRequiredService<EvalAppPipelines>().Orders);
        services.AddSingleton(sp =>
            sp.GetRequiredService<EvalAppPipelines>().Notifications);

        return services;
    }
}
```

```csharp
// Program.cs
builder.Services.AddEvalApp(builder.Configuration);
```

> **Why one App call?** Each `EvalApp.App(...)` allocates its own resource semaphores and
> tuner. Building pipelines in separate `App` calls means they cannot share gate budgets or
> benefit from a single tuner observing all workloads. Always chain multiple
> `.DefineTask<T>().Run(out ...)` calls within the same domain.

### Keyed Singleton (Multiple Pipelines of Same Type)

When you have multiple `ICompiledPipeline<OrderData>` variants (e.g. retail vs wholesale),
build them in the same `App` call and expose via keyed registrations:

```csharp
// Holder record for the two order variants
public record OrderPipelines(
    ICompiledPipeline<OrderData> Retail,
    ICompiledPipeline<OrderData> Wholesale);
```

```csharp
// Build both in one App call
services.AddSingleton(sp =>
{
    ICompiledPipeline<OrderData> retailPipeline;
    ICompiledPipeline<OrderData> wholesalePipeline;

    EvalApp.App("Commerce")
        .WithStepFactory(new ServiceProviderStepFactory(sp))
        .WithResource(ResourceKind.Database)
        .WithTuning()
        .DefineDomain("Orders")
            .DefineTask<OrderData>("RetailOrder")
                .AddStep<ValidateOrderStep>("Validate")
                // ... retail-specific steps
                .Run(out retailPipeline)
            .DefineTask<OrderData>("WholesaleOrder")
                .AddStep<ValidateOrderStep>("Validate")
                // ... wholesale-specific steps
                .Run(out wholesalePipeline)
            .Build(licenseKey);

    return new OrderPipelines(retailPipeline, wholesalePipeline);
});

// Expose via keyed registrations
services.AddKeyedSingleton<ICompiledPipeline<OrderData>>("retail",
    (sp, _) => sp.GetRequiredService<OrderPipelines>().Retail);
services.AddKeyedSingleton<ICompiledPipeline<OrderData>>("wholesale",
    (sp, _) => sp.GetRequiredService<OrderPipelines>().Wholesale);
```

```csharp
// Inject with [FromKeyedServices]
app.MapPost("/retail/orders", async (
    [FromKeyedServices("retail")] ICompiledPipeline<OrderData> pipeline,
    OrderRequest req) => { /* ... */ });
```

### Automatic DI Resolution with `ServiceProviderStepFactory`

Manually passing `new FetchPriceStep(sp.GetRequiredService<...>())` works, but becomes
verbose as the number of steps grows. Register a `ServiceProviderStepFactory` once and
switch to the type-driven `AddStep<TStep>()` overload — the factory resolves each step
from the container automatically at pipeline build time.

**Before — manual DI wiring:**

```csharp
// OrderPipeline.cs
EvalApp.App("Orders")
    .WithResource(ResourceKind.Network)
    .WithResource(ResourceKind.Database)
    .WithTuning()
    .DefineDomain("OrderProcessing")
        .DefineTask<OrderData>("ProcessOrder")
            .AddStep("Validate",       new ValidateOrderStep())
            .Gate(ResourceKind.Database, null, g => g
                .AddStep("CheckInventory", new CheckInventoryStep(
                    sp.GetRequiredService<IInventoryRepository>())))
            .Gate(ResourceKind.Network, null, g => g
                .AddStep("FetchPrice",  new FetchPriceStep(
                    sp.GetRequiredService<IHttpClientFactory>())))
            .Gate(ResourceKind.Database, null, g => g
                .AddStep("SaveOrder",   new SaveOrderStep(
                    sp.GetRequiredService<IOrderRepository>())))
            .Run(out pipeline)
        .Build(licenseKey);
```

**After — `ServiceProviderStepFactory` + type-driven overload:**

```csharp
// Register steps in DI (once, in Program.cs / AddEvalApp)
services.AddSingleton<ValidateOrderStep>();
services.AddSingleton<CheckInventoryStep>();
services.AddSingleton<FetchPriceStep>();
services.AddSingleton<SaveOrderStep>();

// Build pipeline — no constructor arguments needed
EvalApp.App("Orders")
    .WithStepFactory(new ServiceProviderStepFactory(sp))
    .WithResource(ResourceKind.Network)
    .WithResource(ResourceKind.Database)
    .WithTuning()
    .DefineDomain("OrderProcessing")
        .DefineTask<OrderData>("ProcessOrder")
            .AddStep<ValidateOrderStep>("Validate")
            .Gate(ResourceKind.Database, null, g => g
                .AddStep<CheckInventoryStep>("CheckInventory"))
            .Gate(ResourceKind.Network, null, g => g
                .AddStep<FetchPriceStep>("FetchPrice"))
            .Gate(ResourceKind.Database, null, g => g
                .AddStep<SaveOrderStep>("SaveOrder"))
            .Run(out pipeline)
        .Build(licenseKey);
```

> **Lifetime guidance:** Register steps as `Singleton` when they hold only stateless,
> thread-safe injected services. Use `Scoped` only if the step needs a scoped service
> (e.g. `DbContext`) — but note that the pipeline itself is a singleton, so you must
> resolve scoped services inside `ExecuteAsync` via `IServiceScopeFactory`, not via
> constructor injection.

---

## 3. Step Design Patterns

### PureStep vs AsyncStep

| | `PureStep<T>` | `AsyncStep<T>` |
|---|---|---|
| **Base method** | `T Execute(T data)` | `ValueTask<T> ExecuteAsync(T data, CancellationToken ct)` |
| **Use for** | Validation, mapping, calculations, field normalisation | Network calls, database reads/writes, file I/O |
| **Gate needed?** | No — no external resource | Yes — wrap in `.Gate(ResourceKind.X, ...)` |
| **Returns** | New record via `with` | New record via `with` (awaited) |

```csharp
// ✅ PureStep — in-process only, no I/O
public class ValidateOrderStep : PureStep<OrderData>
{
    public override OrderData Execute(OrderData data)
    {
        if (string.IsNullOrWhiteSpace(data.Sku))
            throw new ArgumentException("SKU is required");

        if (data.Quantity <= 0)
            throw new ArgumentException("Quantity must be positive");

        return data with { IsValidated = true };
    }
}
```

```csharp
// ✅ AsyncStep — injected HttpClient via IHttpClientFactory
public class FetchPriceStep : AsyncStep<OrderData>
{
    private readonly IHttpClientFactory _httpClientFactory;

    public FetchPriceStep(IHttpClientFactory httpClientFactory)
        => _httpClientFactory = httpClientFactory;

    public override async ValueTask<OrderData> ExecuteAsync(OrderData data, CancellationToken ct)
    {
        var client = _httpClientFactory.CreateClient("CatalogApi");
        var price  = await client.GetFromJsonAsync<decimal>(
            $"/prices/{data.Sku}", ct);

        return data with { UnitPrice = price, Total = price * data.Quantity };
    }
}
```

```csharp
// ✅ AsyncStep — injected repository (EF DbContext or interface)
public class CheckInventoryStep : AsyncStep<OrderData>
{
    private readonly IInventoryRepository _inventory;

    public CheckInventoryStep(IInventoryRepository inventory)
        => _inventory = inventory;

    public override async ValueTask<OrderData> ExecuteAsync(OrderData data, CancellationToken ct)
    {
        var available = await _inventory.GetAvailableAsync(data.Sku, ct);

        if (available < data.Quantity)
            throw new InvalidOperationException(
                $"Insufficient stock: requested {data.Quantity}, available {available}");

        return data with { IsStockReserved = true };
    }
}
```

### Immutable Record + `with` Pattern

Every step receives a record and returns a **new record**. Never mutate fields in place.

```csharp
// ✅ Correct — returns new record
return data with { IsValidated = true, NormalisedSku = data.Sku.ToUpperInvariant() };

// ❌ Wrong — records are immutable; does not compile
data.IsValidated = true;
```

### One Resource Boundary Per Step

Each `AsyncStep` should cross exactly one resource boundary. Split steps that mix concerns:

```csharp
// ✅ Correct — separate steps, each gated independently
.Gate(ResourceKind.Network, null, g => g
    .AddStep("FetchPrice", new FetchPriceStep(httpClientFactory)))
.Gate(ResourceKind.Database, null, g => g
    .AddStep("SaveOrder",  new SaveOrderStep(orderRepo)))

// ❌ Wrong — two resource kinds in one step; tuner cannot optimise either
public class FetchAndSaveStep : AsyncStep<OrderData>
{
    public override async ValueTask<OrderData> ExecuteAsync(OrderData data, CancellationToken ct)
    {
        var price = await _httpClient.GetFromJsonAsync<decimal>(..., ct);  // network
        await _repo.SaveAsync(data with { UnitPrice = price }, ct);        // database — 🔴 mixed
        return data with { UnitPrice = price };
    }
}
```

### Steps Are Singletons

Step instances are created once and shared across all concurrent pipeline executions. Never
store per-run state on the instance — put it in the data record.

```csharp
// ✅ Correct — injected service is stateless; per-run state lives in the record
public class EnrichAddressStep : AsyncStep<OrderData>
{
    private readonly IGeoService _geo;
    public EnrichAddressStep(IGeoService geo) => _geo = geo;

    public override async ValueTask<OrderData> ExecuteAsync(OrderData data, CancellationToken ct)
    {
        var coords = await _geo.LookupAsync(data.PostalCode, ct);
        return data with { Latitude = coords.Lat, Longitude = coords.Lon };
    }
}

// ❌ Wrong — _requestId is shared across concurrent runs — data race
public class BadStep : AsyncStep<OrderData>
{
    private string _requestId = "";   // 🔴 mutable instance field

    public override async ValueTask<OrderData> ExecuteAsync(OrderData data, CancellationToken ct)
    {
        _requestId = Guid.NewGuid().ToString();  // 🔴 race condition
        return data with { RequestId = _requestId };
    }
}
```

### Step Registration: `AddStep<TStep>()` vs `AddStep(name, instance)`

When using `ServiceProviderStepFactory`, prefer the type-driven overload for steps
with injected dependencies. Keep the instance overload for steps that have no dependencies
or are configured inline.

```csharp
// ✅ Prefer: type-driven when steps are registered in DI
.WithStepFactory(new ServiceProviderStepFactory(sp))
...
.AddStep<ValidateOrderStep>("Validate")        // no-dep pure step
.Gate(ResourceKind.Database, null, g => g
    .AddStep<CheckInventoryStep>("CheckInventory"))  // injected dep resolved from DI

// ✅ Also fine: instance overload for steps with no DI dependencies
.AddStep("Validate", new ValidateOrderStep())

// ✅ Also fine: instance overload when not using ServiceProviderStepFactory
.Gate(ResourceKind.Database, null, g => g
    .AddStep("CheckInventory", new CheckInventoryStep(
        sp.GetRequiredService<IInventoryRepository>())))
```

The `AddStep<TStep>()` overload is available on `IEmptyTaskBuilder<T>`, `ITaskBuilder<T>`,
and `ISubTaskBuilder<T>` (inside gates and `ForEach` item pipelines).

---

## 3b. Cross-Domain & Side-Effect Steps

### `SideEffectStep<T>` — Naming communicates intent

`SideEffectStep<T>` is identical to `AsyncStep<T>` at runtime — it inherits from it directly.
The distinction is semantic: use it when the step **exists for what it does**, not for what
it returns. The data transformation (if any) is incidental.

| | `AsyncStep<T>` | `SideEffectStep<T>` |
|---|---|---|
| **Inherits from** | — | `AsyncStep<T>` |
| **Primary purpose** | Transform the data record | Produce a side effect |
| **Returns** | Updated record | Same record (or minor status flag) |
| **Typical examples** | Fetch price, enrich address | Audit log, cache bust, event publish |
| **Gate needed?** | Yes, for I/O | Yes — same rule applies |

```csharp
// ✅ SideEffectStep — writes an audit entry; data flows through unchanged
public class AuditOrderStep : SideEffectStep<OrderData>
{
    private readonly IAuditLog _audit;

    public AuditOrderStep(IAuditLog audit) => _audit = audit;

    public override async ValueTask<OrderData> ExecuteAsync(OrderData data, CancellationToken ct)
    {
        await _audit.RecordAsync(new AuditEntry(
            EntityId:   data.OrderId,
            Action:     "OrderPlaced",
            OccurredAt: DateTimeOffset.UtcNow), ct);

        return data;   // data flows through — side effect is the point
    }
}
```

```csharp
// ✅ SideEffectStep — invalidates a cache entry; sets a flag on the record
public class InvalidatePriceCacheStep : SideEffectStep<OrderData>
{
    private readonly IPriceCache _cache;

    public InvalidatePriceCacheStep(IPriceCache cache) => _cache = cache;

    public override async ValueTask<OrderData> ExecuteAsync(OrderData data, CancellationToken ct)
    {
        await _cache.InvalidateAsync(data.Sku, ct);
        return data with { PriceCacheInvalidated = true };
    }
}
```

Register and gate exactly as you would an `AsyncStep`:

```csharp
.Gate(ResourceKind.Database, null, g => g
    .AddStep("Audit", new AuditOrderStep(auditLog)))
```

#### Auto-Gating with ResourceKind

Override `ResourceKind` on your step and `AddStep<TStep>()` gates it automatically:

```csharp
public class AuditOrderStep : SideEffectStep<OrderData>
{
    private readonly IAuditLog _audit;
    public AuditOrderStep(IAuditLog audit) => _audit = audit;

    // Declare the resource — AddStep<AuditOrderStep>() auto-wraps in Gate(Database, ...)
    public override ResourceKind? ResourceKind => ResourceKind.Database;

    public override async ValueTask<OrderData> ExecuteAsync(OrderData data, CancellationToken ct)
    {
        await _audit.RecordAsync(new AuditEntry(data.OrderId, "OrderPlaced"), ct);
        return data;
    }
}
```

```csharp
// ✅ Auto-gated — no manual .Gate() needed
.AddStep<AuditOrderStep>("Audit")

// Equivalent manual form:
.Gate(ResourceKind.Database, null, g => g
    .AddStep<AuditOrderStep>("Audit"))
```

If you add a step with a declared `ResourceKind` via the *instance* overload without a gate,
the builder emits a `Debug.WriteLine` warning at build time:

```csharp
// ⚠️ Emits: [EvalApp] Warning: 'Audit' is a SideEffectStep with ResourceKind=database
//         but was added without a Gate. Use AddStep<AuditOrderStep>() for automatic gating.
.AddStep("Audit", new AuditOrderStep(auditLog))
```

This does **not** throw — it warns. Tests and trivial steps that intentionally skip gating can ignore it.

---

### `CrossDomainStep<T, TShared>` — Shared state across pipelines

Use `CrossDomainStep<T, TShared>` when a step must communicate **across pipeline boundaries** —
for example, a step in an Orders pipeline that publishes to a shared event bus that a
Notifications pipeline also subscribes to, or a step that writes into a shared price cache
used by two parallel pipelines.

The shared object is injected via the constructor and exposed as the protected `Shared`
property. No internal EvalApp types are involved — `TShared` is whatever abstraction best
fits your cross-domain contract.

#### Example A — Shared event bus (Orders → Notifications)

```csharp
// Shared abstraction — owned by neither pipeline
public interface IOrderEventBus
{
    ValueTask PublishAsync(OrderPlaced evt, CancellationToken ct);
}

// Step in the Orders pipeline
public class PublishOrderEventStep : CrossDomainStep<OrderData, IOrderEventBus>
{
    public PublishOrderEventStep(IOrderEventBus bus) : base(bus) { }

    public override async ValueTask<OrderData> ExecuteAsync(OrderData data, CancellationToken ct)
    {
        await Shared.PublishAsync(new OrderPlaced(data.OrderId, data.Total), ct);
        return data with { EventPublished = true };
    }
}
```

The Notifications pipeline receives `IOrderEventBus` from the same DI container —
both pipelines share the same singleton instance.

#### Example B — Shared price cache (two pipelines reading/writing the same dictionary)

```csharp
// Step in the Pricing pipeline — writes to the shared cache
public class CachePriceStep : CrossDomainStep<PricingData, ConcurrentDictionary<string, decimal>>
{
    public CachePriceStep(ConcurrentDictionary<string, decimal> cache) : base(cache) { }

    public override ValueTask<PricingData> ExecuteAsync(PricingData data, CancellationToken ct)
    {
        Shared[data.Sku] = data.UnitPrice;
        return ValueTask.FromResult(data with { IsCached = true });
    }
}

// Step in the Orders pipeline — reads from the same cache
public class ReadCachedPriceStep : CrossDomainStep<OrderData, ConcurrentDictionary<string, decimal>>
{
    public ReadCachedPriceStep(ConcurrentDictionary<string, decimal> cache) : base(cache) { }

    public override ValueTask<OrderData> ExecuteAsync(OrderData data, CancellationToken ct)
    {
        var price = Shared.TryGetValue(data.Sku, out var p) ? p : 0m;
        return ValueTask.FromResult(data with { UnitPrice = price });
    }
}
```

#### DI Registration — shared singleton wired into both steps

```csharp
// Program.cs
var builder = WebApplication.CreateBuilder(args);

// 1. Register the shared object as a singleton — one instance for the lifetime of the app
builder.Services.AddSingleton<IOrderEventBus, InMemoryOrderEventBus>();

// For the ConcurrentDictionary example:
// builder.Services.AddSingleton(new ConcurrentDictionary<string, decimal>());

// 2. Register steps that consume the shared object
builder.Services.AddTransient<PublishOrderEventStep>();   // resolved per-run; Shared is the same instance

// 3. Build pipelines using ServiceProviderStepFactory so steps are resolved from DI
builder.Services.AddSingleton(sp =>
{
    var licenseKey = builder.Configuration["EvalApp:LicenseKey"]!;
    ICompiledPipeline<OrderData> pipeline;

    EvalApp.App("Orders")
        .WithStepFactory(new ServiceProviderStepFactory(sp))
        .WithResource(ResourceKind.Database)
        .WithResource(ResourceKind.Network)
        .DefineDomain("OrderProcessing")
            .DefineTask<OrderData>("ProcessOrder")
                .AddStep<ValidateOrderStep>("Validate")
                .Gate(ResourceKind.Database, null, g => g
                    .AddStep<CheckInventoryStep>("CheckInventory"))
                .Gate(ResourceKind.Network, null, g => g
                    .AddStep<PublishOrderEventStep>("PublishEvent"))  // CrossDomainStep — resolved from DI
                .Run(out pipeline)
            .Build(licenseKey);

    return pipeline;
});

var app = builder.Build();
```

> **Concurrency note:** `CrossDomainStep` instances are singletons (like all steps). The
> `Shared` reference is set once at construction. If `TShared` has mutable state (e.g. a
> `ConcurrentDictionary`), ensure the shared type itself is thread-safe.

---

## 4. Pipeline Lifecycle

### Build Pattern

The builder chain follows this structure every time:

```csharp
ICompiledPipeline<T> pipeline;

EvalApp.App("AppName")           // → IAppBuilder
    .WithResource(kind)          // declare each ResourceKind used by gates
    .WithTuning()                // optional: enable adaptive concurrency
    .DefineDomain("DomainName")  // → IDomainBuilder
        .DefineTask<T>("Name")   // → IEmptyTaskBuilder<T>
            .AddStep(...)        // → ITaskBuilder<T>
            .Run(out pipeline)   // capture the pipeline; → IDomainBuilder
        .Build(licenseKey);      // compile and activate (void)
```

Multiple tasks can share one `App`:

```csharp
EvalApp.App("Commerce")
    .WithResource(ResourceKind.Network)
    .WithResource(ResourceKind.Database)
    .WithTuning()
    .DefineDomain("Orders")
        .DefineTask<OrderData>("ProcessOrder")
            .AddStep(...)
            .Run(out orderPipeline)
        .DefineTask<RefundData>("ProcessRefund")
            .AddStep(...)
            .Run(out refundPipeline)
        .Build(licenseKey);
```

### CancellationToken Propagation

Pass the request's `CancellationToken` to `RunAsync`. It flows into every async step.

```csharp
// ASP.NET Core — HttpContext.RequestAborted is the right token
app.MapPost("/orders", async (OrderRequest req, HttpContext ctx,
    ICompiledPipeline<OrderData> pipeline) =>
{
    var result = await pipeline.RunAsync(
        new OrderData(req.Sku, req.Quantity, req.CustomerEmail),
        ctx.RequestAborted);   // ← propagate request cancellation

    return result switch
    {
        PipelineResult<OrderData>.Success s => Results.Ok(s.Data.ConfirmationNumber),
        PipelineResult<OrderData>.Failure f => Results.Problem(f.Message ?? f.Exception.Message),
        _                                   => Results.StatusCode(500)
    };
});
```

Always pass `ct` to every awaited call inside `AsyncStep.ExecuteAsync`:

```csharp
// ✅ Correct
public override async ValueTask<OrderData> ExecuteAsync(OrderData data, CancellationToken ct)
{
    var result = await _client.GetFromJsonAsync<PriceResult>($"/prices/{data.Sku}", ct);
    return data with { UnitPrice = result!.Price };
}

// ❌ Wrong — ct is ignored; request cancellation has no effect
public override async ValueTask<OrderData> ExecuteAsync(OrderData data, CancellationToken ct)
{
    var result = await _client.GetFromJsonAsync<PriceResult>($"/prices/{data.Sku}");  // no ct
    return data with { UnitPrice = result!.Price };
}
```

### Wiring from a Controller

```csharp
[ApiController]
[Route("api/orders")]
public class OrdersController : ControllerBase
{
    private readonly ICompiledPipeline<OrderData> _pipeline;

    public OrdersController(ICompiledPipeline<OrderData> pipeline)
        => _pipeline = pipeline;

    [HttpPost]
    public async Task<IActionResult> Create(
        [FromBody] CreateOrderRequest req,
        CancellationToken ct)
    {
        var data   = new OrderData(req.Sku, req.Quantity, req.CustomerEmail);
        var result = await _pipeline.RunAsync(data, ct);

        return result switch
        {
            PipelineResult<OrderData>.Success s =>
                Ok(new { s.Data.ConfirmationNumber }),
            PipelineResult<OrderData>.Failure f =>
                Problem(f.Message ?? f.Exception.Message, statusCode: 500),
            _ => StatusCode(500)
        };
    }
}
```

### Handling PipelineResult\<T\>

```csharp
var result = await pipeline.RunAsync(data, ct);

// Pattern match — exhaustive
switch (result)
{
    case PipelineResult<OrderData>.Success s:
        // s.Data is the final transformed record
        logger.LogInformation("Order {Id} processed", s.Data.ConfirmationNumber);
        break;

    case PipelineResult<OrderData>.Failure f:
        // f.Data is the last successful state before the failure
        // f.Exception is the exception thrown by the failing step
        // f.Message is an optional human-readable message
        logger.LogError(f.Exception, "Order processing failed: {Msg}", f.Message);
        break;

    case PipelineResult<OrderData>.Skipped sk:
        // sk.Data is unchanged; sk.Reason explains why the pipeline was skipped
        logger.LogWarning("Pipeline skipped: {Reason}", sk.Reason);
        break;
}

// Convenience properties
if (result.IsSuccess)   { /* ... */ }
if (result.IsFailure)   { /* ... */ }
if (result.IsSkipped)   { /* ... */ }

// Extract data regardless of outcome
var finalData = result.GetData();
```

---

## 5. Gate Configuration & Tuning

### What Gates Do

A `Gate` throttles concurrent access to a shared resource (network, database, CPU, disk).
Without gates, `WithTuning()` has no signal to act on and cannot adapt concurrency.

**Gates must be declared at two levels:**

1. `WithResource(kind)` on the `IAppBuilder` — registers the semaphore
2. `.Gate(kind, ...)` in the task — wraps the steps that touch that resource

```csharp
EvalApp.App("Orders")
    .WithResource(ResourceKind.Network)     // ← declare once per ResourceKind
    .WithResource(ResourceKind.Database)    // ← declare once per ResourceKind
    .WithTuning()
    .DefineDomain("OrderProcessing")
        .DefineTask<OrderData>("ProcessOrder")
            .AddStep("Validate", new ValidateOrderStep())          // no gate — pure step
            .Gate(ResourceKind.Database, null, g => g              // ← gate wraps DB step
                .AddStep("CheckInventory", new CheckInventoryStep(inventoryRepo)))
            .Gate(ResourceKind.Network, null, g => g               // ← gate wraps HTTP step
                .AddStep("FetchPrice", new FetchPriceStep(httpFactory)))
            .Gate(ResourceKind.Database, null, g => g              // ← gate wraps DB step
                .AddStep("SaveOrder", new SaveOrderStep(orderRepo)))
            .Run(out pipeline)
        .Build(licenseKey);
```

### ResourceKind Values

| Value | Use for |
|---|---|
| `ResourceKind.Network` | HTTP calls, REST APIs, gRPC, git clone |
| `ResourceKind.Database` | SQL queries, EF Core, migrations, NoSQL |
| `ResourceKind.DiskIO` | File read/write, disk copy, git checkout |
| `ResourceKind.Cpu` | ZLib, compression, image processing, heavy computation |

For mixed CPU + I/O steps, gate on the **bottleneck** resource. For custom resource types:

```csharp
var custom = ResourceKind.Of("rate-limiter");
EvalApp.App("MyApp").WithResource(custom);
```

### WithTuning — Adaptive Mode

```csharp
EvalApp.App("MyApp")
    .WithResource(ResourceKind.Network)
    .WithTuning()       // ← in-memory adaptive tuner (resets on restart)
    // OR
    .WithBayesianTuning("path/to/tuner.json")  // ← persistent tuner (survives restarts)
```

The tuner observes gate wait times across pipeline runs and raises or lowers concurrency
limits to maximise throughput. It needs at least a few hundred runs to converge. Enable it
in licensed mode; it has no effect in unlicensed mode.

### Controlling Concurrency Manually

Use `WithResource(kind, TunableConfig)` to set bounds:

```csharp
.WithResource(ResourceKind.Network, Tunable.Between(min: 4, max: 32, @default: 8))
.WithResource(ResourceKind.Database, Tunable.FixedAt(4))    // fixed — no tuning
.WithResource(ResourceKind.Cpu, Tunable.ForCpu())           // scales with processor count
```

Or use the `int maxConcurrency` overload for a hard cap:

```csharp
.WithResource(ResourceKind.Network, maxConcurrency: 16)
```

---

## 6. ForEach — Parallel Collections

### Basic ForEach

```csharp
// InvoiceBatchData has a collection of LineItem records
// Each item is processed independently and in parallel (licensed mode)
.ForEach<LineItem>(
    select:         d => d.Items,                                 // extract items from parent
    merge:          (d, processed) => d with { Results = processed.ToArray() }, // fold back
    collectionName: "LineItems",
    configure:      item => item
        .AddStep("Validate", new ValidateLineItemStep())
        .Gate(ResourceKind.Database, null, g => g
            .AddStep("LookupProduct", new LookupProductStep(productRepo))))
```

> **Parameter order:** `select`, `merge`, `collectionName`, then `configure` (or `parallelism`
> then `configure`). Note that `merge` comes before `collectionName`.

### Controlling Parallelism

```csharp
// Tunable.ForItems() — greedy; let the tuner find the ceiling (recommended for licensed mode)
.ForEach<LineItem>(
    select:         d => d.Items,
    merge:          (d, items) => d with { Results = items.ToArray() },
    collectionName: "LineItems",
    parallelism:    Tunable.ForItems(),     // ← tuner-managed, scales with CPU
    configure:      item => item.AddStep(...))

// Tunable.ForItems(default) — start at a known concurrency, let tuner adjust
.ForEach<LineItem>(..., parallelism: Tunable.ForItems(@default: 16), configure: ...)

// Tunable.Between — explicit bounds
.ForEach<LineItem>(..., parallelism: Tunable.Between(min: 2, max: 20, @default: 8), ...)

// Tunable.FixedAt — hard cap, no tuning
.ForEach<LineItem>(..., parallelism: Tunable.FixedAt(4), configure: ...)

// int overload — fixed cap shorthand
.ForEach<LineItem>(select, merge, "LineItems", maxParallelism: 8, configure: ...)
```

### InlineBelow — Skip Thread Dispatch for Small Collections

When a collection is typically small (< N items), avoid the overhead of parallel dispatch:

```csharp
// If the collection has fewer than 4 items, run inline (sequential).
// Above 4, dispatch to thread pool.
.ForEach<LineItem>(..., parallelism: Tunable.InlineBelow(4), configure: ...)
```

### Failure Modes

```csharp
// CollectAndThrow (default) — all items run; exceptions are collected and re-thrown together
.ForEach<LineItem>(select, merge, "LineItems", maxParallelism: 8,
    failureMode: ForEachFailureMode.CollectAndThrow, configure: ...)

// FailFast — cancel remaining items on first failure
.ForEach<LineItem>(select, merge, "LineItems", maxParallelism: 8,
    failureMode: ForEachFailureMode.FailFast, configure: ...)

// ContinueOnError — process all items regardless of failures
.ForEach<LineItem>(select, merge, "LineItems", maxParallelism: 8,
    failureMode: ForEachFailureMode.ContinueOnError, configure: ...)
```

### ForEach Inside a Gate (Sub-task Parallelism)

```csharp
.Gate(ResourceKind.Network, null, g => g
    .ForEach<InvoiceItem>(
        select:         d => d.Invoices,
        merge:          (d, processed) => d with { Invoices = processed.ToArray() },
        collectionName: "Invoices",
        parallelism:    Tunable.ForItems(),
        configure:      item => item
            .AddStep("FetchRate", async (inv, ct) =>
                inv with { Rate = await rateService.GetRateAsync(inv.Code, ct) })))
```

---

## 7. Branching & Sagas

### If / Else Branching

```csharp
.If(
    predicate: d => d.IsInternational,
    then: branch => branch
        .Gate(ResourceKind.Network, null, g => g
            .AddStep("CustomsCheck", new CustomsCheckStep(customsApi)))
        .AddStep("AddDuties", new AddDutiesStep()),
    @else: branch => branch
        .AddStep("ApplyDomesticRate", new ApplyDomesticRateStep()))
```

Branches can contain `Gate`, nested `If`, and `ForEach`. Both branches receive and must
return the same `T`. The `@else` branch is optional.

```csharp
// Nested branching
.If(d => d.CustomerTier == CustomerTier.Premium,
    then: branch => branch
        .AddStep("ApplyDiscount", new ApplyDiscountStep())
        .If(d => d.OrderTotal > 1000,
            then: inner => inner.AddStep("ApplyLoyaltyBonus", new LoyaltyBonusStep())))
```

### Sagas — Compensating Transactions

Use sagas when a sequence of external operations must roll back on failure. Steps added
with `AddStepWithCompensation` will have their compensation run in reverse order if a
later step throws.

```csharp
.BeginSaga(CompensationPolicy.BestEffort)    // → ISagaBuilder<T>
    .AddStepWithCompensation(
        "ReserveInventory",
        forward:    async (d, ct) => d with { IsStockReserved = true,
                                               ReservationId = await _inv.ReserveAsync(d.Sku, d.Quantity, ct) },
        compensate: async (d, ct) => { await _inv.ReleaseAsync(d.ReservationId!, ct); return d; })
    .AddStepWithCompensation(
        "ChargePayment",
        forward:    async (d, ct) => d with { PaymentRef = await _pay.ChargeAsync(d.Total, d.Card, ct) },
        compensate: async (d, ct) => { await _pay.RefundAsync(d.PaymentRef!, ct); return d; })
    .AddStep("SendConfirmation",
        async (d, ct) => d with { ConfirmationNumber = await _mailer.SendAsync(d, ct) })
.EndSaga()    // → ITaskBuilder<T>; continue adding steps after the saga
```

For steps that touch a gated resource inside a saga, use `AddGate`:

```csharp
.BeginSaga()
    .AddGate(
        kind:       ResourceKind.Database,
        onWaiting:  null,
        configure:  g => g.AddStep("SaveOrder", async (d, ct) =>
                        d with { OrderId = await _repo.InsertAsync(d, ct) }),
        compensate: async (d, ct) => { await _repo.DeleteAsync(d.OrderId, ct); return d; })
    .AddStepWithCompensation("ChargePayment", forward: ..., compensate: ...)
.EndSaga()
```

### Compensation Policies

| Policy | Behaviour |
|---|---|
| `BestEffort` (default) | Run all compensations; collect and surface errors |
| `AbortOnFirst` | Stop compensation chain on first compensation failure |
| `SwallowErrors` | Run all compensations; silently ignore compensation failures |

---

## 8. Testing Pipelines

### Unit Testing a Step in Isolation

Steps are plain classes — test them directly without building a pipeline.

```csharp
// ValidateOrderStepTests.cs (XUnit)
public class ValidateOrderStepTests
{
    private readonly ValidateOrderStep _sut = new();

    [Fact]
    public void WhenSkuIsValid_Then_SetsIsValidatedTrue()
    {
        var data   = new OrderData("SKU-001", Quantity: 2, CustomerEmail: "a@b.com");
        var result = _sut.Execute(data);

        Assert.True(result.IsValidated);
    }

    [Fact]
    public void WhenSkuIsEmpty_Then_Throws()
    {
        var data = new OrderData("", Quantity: 2, CustomerEmail: "a@b.com");
        Assert.Throws<ArgumentException>(() => _sut.Execute(data));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void WhenQuantityNotPositive_Then_Throws(int qty)
    {
        var data = new OrderData("SKU-001", Quantity: qty, CustomerEmail: "a@b.com");
        Assert.Throws<ArgumentException>(() => _sut.Execute(data));
    }
}
```

### Unit Testing an AsyncStep

```csharp
// FetchPriceStepTests.cs
public class FetchPriceStepTests
{
    [Fact]
    public async Task WhenPriceReturned_Then_SetsUnitPriceAndTotal()
    {
        // Arrange — build step with a fake service
        var fakeCatalog = new FakeCatalogService(unitPrice: 19.99m);
        var step = new FetchPriceStep(fakeCatalog);
        var data = new OrderData("SKU-001", Quantity: 3, CustomerEmail: "a@b.com");

        // Act
        var result = await step.ExecuteAsync(data, CancellationToken.None);

        // Assert
        Assert.Equal(19.99m, result.UnitPrice);
        Assert.Equal(59.97m, result.Total);
    }

    // Fake service — no mocking framework required
    private sealed class FakeCatalogService(decimal unitPrice) : ICatalogService
    {
        public Task<decimal> GetPriceAsync(string sku, CancellationToken ct)
            => Task.FromResult(unitPrice);
    }
}
```

### Integration Testing the Full Pipeline

Build a minimal pipeline with test doubles to verify the end-to-end flow:

```csharp
public class OrderPipelineIntegrationTests
{
    [Fact]
    public async Task WhenValidOrder_Then_PipelineSucceeds()
    {
        // Arrange
        var fakeInventory = new FakeInventoryRepository(available: 10);
        var fakeCatalog   = new FakeCatalogService(unitPrice: 25.00m);
        var fakeOrderRepo = new FakeOrderRepository();

        ICompiledPipeline<OrderData> pipeline;

        EvalApp.App("Test")
            .DefineDomain("Orders")
                .DefineTask<OrderData>("ProcessOrder")
                    .AddStep("Validate",        new ValidateOrderStep())
                    .AddStep("CheckInventory",  new CheckInventoryStep(fakeInventory))
                    .AddStep("FetchPrice",      new FetchPriceStep(fakeCatalog))
                    .AddStep("SaveOrder",       new SaveOrderStep(fakeOrderRepo))
                    .Run(out pipeline)
                .Build();   // no license key — unlicensed/sequential mode is fine for tests

        var input = new OrderData("SKU-001", Quantity: 2, CustomerEmail: "test@example.com");

        // Act
        var result = await pipeline.RunAsync(input, CancellationToken.None);

        // Assert
        Assert.IsType<PipelineResult<OrderData>.Success>(result);
        var success = (PipelineResult<OrderData>.Success)result;
        Assert.NotNull(success.Data.ConfirmationNumber);
        Assert.Equal(50.00m, success.Data.Total);
    }

    [Fact]
    public async Task WhenInsufficientStock_Then_PipelineFails()
    {
        var fakeInventory = new FakeInventoryRepository(available: 0);

        ICompiledPipeline<OrderData> pipeline;

        EvalApp.App("Test")
            .DefineDomain("Orders")
                .DefineTask<OrderData>("ProcessOrder")
                    .AddStep("Validate",       new ValidateOrderStep())
                    .AddStep("CheckInventory", new CheckInventoryStep(fakeInventory))
                    .Run(out pipeline)
                .Build();

        var input = new OrderData("SKU-001", Quantity: 5, CustomerEmail: "test@example.com");

        var result = await pipeline.RunAsync(input, CancellationToken.None);

        Assert.IsType<PipelineResult<OrderData>.Failure>(result);
    }
}
```

### Testing Tips

- **No license key needed for tests** — unlicensed mode runs all steps sequentially and
  correctly. Tests do not need a license key; omit it from `.Build()`.
- **Test steps individually first** — a failing step test is faster to diagnose than a
  failing pipeline test.
- **Use fake implementations, not mocks** — small fake classes (like `FakeCatalogService`
  above) are more readable and less fragile than mock framework setups.
- **Assert on the final data record** — the record is the contract; assert its fields.
- **Gates are transparent in unlicensed mode** — you can add or omit them in test builds;
  behaviour is identical.

---

## 9. Error Handling & Observability

### How Failures Surface

When a step throws an unhandled exception, the pipeline captures it in
`PipelineResult<T>.Failure`. The `.Data` field holds the last successfully transformed
record (the state before the failing step ran).

```csharp
case PipelineResult<OrderData>.Failure f:
    // f.Data       — last good state (useful for debugging/partial saves)
    // f.Exception  — the original exception from the step
    // f.Message    — optional string set by the pipeline infrastructure
    logger.LogError(f.Exception,
        "Order pipeline failed at last-good-state OrderId={Id}", f.Data.OrderId);
    break;
```

To signal a business-logic failure (not an infrastructure error), throw a domain exception
from your step and catch the specific type in the failure handler:

```csharp
// In your step
if (available < data.Quantity)
    throw new InsufficientStockException(data.Sku, available, data.Quantity);

// In your endpoint / handler
case PipelineResult<OrderData>.Failure f when f.Exception is InsufficientStockException ex:
    return Results.Conflict($"Only {ex.Available} units of {ex.Sku} available");
```

### Middleware — Cross-Cutting Concerns

`IStepMiddleware<T>` wraps every step in a task, allowing retry, timing, and audit logic
to be applied uniformly.

```csharp
public class RetryMiddleware<T> : IStepMiddleware<T>
{
    private readonly int _maxAttempts;

    public RetryMiddleware(int maxAttempts = 3) => _maxAttempts = maxAttempts;

    public async ValueTask<PipelineResult<T>> ExecuteAsync(
        T data,
        Func<T, CancellationToken, ValueTask<PipelineResult<T>>> next,
        CancellationToken ct = default)
    {
        for (var attempt = 1; attempt <= _maxAttempts; attempt++)
        {
            var result = await next(data, ct);
            if (result is not PipelineResult<T>.Failure f) return result;
            if (attempt == _maxAttempts || ct.IsCancellationRequested) return result;

            await Task.Delay(TimeSpan.FromMilliseconds(200 * attempt), ct);
        }

        return await next(data, ct);
    }
}
```

Register middleware on the task builder (before or after steps — it wraps all steps in scope):

```csharp
.DefineTask<OrderData>("ProcessOrder")
    .WithMiddleware(new RetryMiddleware<OrderData>(maxAttempts: 3))
    .AddStep("Validate", new ValidateOrderStep())
    .Gate(ResourceKind.Network, null, g => g
        .AddStep("FetchPrice", new FetchPriceStep(httpFactory)))
    .Run(out pipeline)
```

### No ILogger in Steps

Steps must not take `ILogger` as a constructor dependency. Pipeline infrastructure handles
observability. Steps communicate outcomes by:

1. **Returning an updated record** — success path; the record carries the result
2. **Throwing an exception** — failure path; captured by the pipeline as `Failure`

```csharp
// ✅ Correct — return the updated record; logging happens at the endpoint
public override async ValueTask<OrderData> ExecuteAsync(OrderData data, CancellationToken ct)
{
    var price = await _catalog.GetPriceAsync(data.Sku, ct);
    return data with { UnitPrice = price };
}

// ❌ Wrong — ILogger injected into step
public class FetchPriceStep : AsyncStep<OrderData>
{
    private readonly ICatalogService _catalog;
    private readonly ILogger<FetchPriceStep> _logger;    // 🔴 do not inject

    public override async ValueTask<OrderData> ExecuteAsync(OrderData data, CancellationToken ct)
    {
        _logger.LogInformation("Fetching price for {Sku}", data.Sku);  // 🔴
        // ...
    }
}
```

### CancellationToken & Timeout Behaviour

When the `CancellationToken` passed to `RunAsync` is cancelled mid-flight:

- The pipeline propagates cancellation to the currently-executing step's `ExecuteAsync`.
- Steps that respect `ct` (i.e. forward it to all awaited calls) will throw
  `OperationCanceledException`, which surfaces as `PipelineResult<T>.Failure`.
- Steps that ignore `ct` will complete; the pipeline will honour the cancellation at the
  next gate or async boundary.

Always wrap `RunAsync` calls in request-scoped timeout logic at the host level
(e.g. `app.UseRequestTimeout()` in ASP.NET Core 7+), not inside steps.

---

## 10. Complete Example — Order Processing Service

A realistic ASP.NET Core minimal-API application processing orders through a 4-step pipeline.

### Data Record

```csharp
// Pipelines/Orders/Data/OrderData.cs
namespace MyApp.Pipelines.Orders.Data;

public record OrderData(
    // INPUT — provided by the caller
    string Sku,
    int    Quantity,
    string CustomerEmail,

    // STAGE 1 — set by ValidateOrderStep
    bool   IsValidated = false,

    // STAGE 2 — set by CheckInventoryStep
    bool   IsStockReserved = false,
    string? ReservationId  = null,

    // STAGE 3 — set by FetchPriceStep
    decimal UnitPrice = 0,
    decimal Total     = 0,

    // OUTPUT — set by SaveOrderStep
    string? ConfirmationNumber = null
);
```

### Steps

```csharp
// Pipelines/Orders/Steps/ValidateOrderStep.cs
public class ValidateOrderStep : PureStep<OrderData>
{
    public override OrderData Execute(OrderData data)
    {
        if (string.IsNullOrWhiteSpace(data.Sku))
            throw new ArgumentException("SKU is required");
        if (data.Quantity <= 0)
            throw new ArgumentException("Quantity must be positive");
        if (string.IsNullOrWhiteSpace(data.CustomerEmail))
            throw new ArgumentException("Customer email is required");

        return data with { IsValidated = true };
    }
}
```

```csharp
// Pipelines/Orders/Steps/CheckInventoryStep.cs
public class CheckInventoryStep : AsyncStep<OrderData>
{
    private readonly IInventoryRepository _inventory;

    public CheckInventoryStep(IInventoryRepository inventory)
        => _inventory = inventory;

    public override async ValueTask<OrderData> ExecuteAsync(OrderData data, CancellationToken ct)
    {
        var reservationId = await _inventory.ReserveAsync(data.Sku, data.Quantity, ct);
        return data with { IsStockReserved = true, ReservationId = reservationId };
    }
}
```

```csharp
// Pipelines/Orders/Steps/FetchPriceStep.cs
public class FetchPriceStep : AsyncStep<OrderData>
{
    private readonly IHttpClientFactory _httpClientFactory;

    public FetchPriceStep(IHttpClientFactory httpClientFactory)
        => _httpClientFactory = httpClientFactory;

    public override async ValueTask<OrderData> ExecuteAsync(OrderData data, CancellationToken ct)
    {
        var client = _httpClientFactory.CreateClient("CatalogApi");
        var result = await client.GetFromJsonAsync<PriceResult>(
            $"/api/prices/{Uri.EscapeDataString(data.Sku)}", ct)
            ?? throw new InvalidOperationException($"No price found for SKU {data.Sku}");

        var unitPrice = result.Price;
        return data with { UnitPrice = unitPrice, Total = unitPrice * data.Quantity };
    }

    private sealed record PriceResult(string Sku, decimal Price);
}
```

```csharp
// Pipelines/Orders/Steps/SaveOrderStep.cs
public class SaveOrderStep : AsyncStep<OrderData>
{
    private readonly IOrderRepository _orders;

    public SaveOrderStep(IOrderRepository orders) => _orders = orders;

    public override async ValueTask<OrderData> ExecuteAsync(OrderData data, CancellationToken ct)
    {
        var confirmationNumber = await _orders.SaveAsync(data, ct);
        return data with { ConfirmationNumber = confirmationNumber };
    }
}
```

### Program.cs — DI Registration

```csharp
// Program.cs
var builder = WebApplication.CreateBuilder(args);

// Application services
builder.Services.AddScoped<IInventoryRepository, InventoryRepository>();
builder.Services.AddScoped<IOrderRepository, OrderRepository>();

// Named HttpClient for the Catalog API
builder.Services.AddHttpClient("CatalogApi", client =>
{
    client.BaseAddress = new Uri(builder.Configuration["CatalogApi:BaseUrl"]
        ?? throw new InvalidOperationException("CatalogApi:BaseUrl is not configured"));
    client.Timeout = TimeSpan.FromSeconds(10);
});

// EvalApp pipeline — singleton, built once at startup
var licenseKey = builder.Configuration["EvalApp:LicenseKey"]
    ?? Environment.GetEnvironmentVariable("EVALAPP_LICENSE_KEY");

builder.Services.AddSingleton<ICompiledPipeline<OrderData>>(sp =>
{
    ICompiledPipeline<OrderData> pipeline;

    EvalApp.App("Commerce")
        .WithResource(ResourceKind.Database)
        .WithResource(ResourceKind.Network)
        .WithTuning()
        .DefineDomain("Orders")
            .DefineTask<OrderData>("ProcessOrder")
                .AddStep("Validate",
                    new ValidateOrderStep())
                .Gate(ResourceKind.Database, null, g => g
                    .AddStep("CheckInventory",
                        new CheckInventoryStep(
                            sp.GetRequiredService<IInventoryRepository>())))
                .Gate(ResourceKind.Network, null, g => g
                    .AddStep("FetchPrice",
                        new FetchPriceStep(
                            sp.GetRequiredService<IHttpClientFactory>())))
                .Gate(ResourceKind.Database, null, g => g
                    .AddStep("SaveOrder",
                        new SaveOrderStep(
                            sp.GetRequiredService<IOrderRepository>())))
                .Run(out pipeline)
            .Build(licenseKey);

    return pipeline;
});

var app = builder.Build();

// Minimal API endpoint
app.MapPost("/api/orders", async (
    CreateOrderRequest req,
    ICompiledPipeline<OrderData> pipeline,
    HttpContext ctx) =>
{
    var input = new OrderData(
        Sku:           req.Sku,
        Quantity:      req.Quantity,
        CustomerEmail: req.CustomerEmail);

    var result = await pipeline.RunAsync(input, ctx.RequestAborted);

    return result switch
    {
        PipelineResult<OrderData>.Success s =>
            Results.Ok(new { s.Data.ConfirmationNumber, s.Data.Total }),

        PipelineResult<OrderData>.Failure { Exception: ArgumentException ae } =>
            Results.BadRequest(new { error = ae.Message }),

        PipelineResult<OrderData>.Failure { Exception: InvalidOperationException ioe } =>
            Results.Conflict(new { error = ioe.Message }),

        PipelineResult<OrderData>.Failure f =>
            Results.Problem(
                title:      "Order processing failed",
                detail:     f.Message ?? f.Exception.Message,
                statusCode: 500),

        _ => Results.StatusCode(500)
    };
})
.WithName("CreateOrder")
.Produces<object>(StatusCodes.Status200OK)
.ProducesProblem(StatusCodes.Status400BadRequest)
.ProducesProblem(StatusCodes.Status500InternalServerError);

app.Run();

// Request DTO
record CreateOrderRequest(string Sku, int Quantity, string CustomerEmail);
```

### appsettings.json

```json
{
  "EvalApp": {
    "LicenseKey": ""
  },
  "CatalogApi": {
    "BaseUrl": "https://catalog.internal"
  }
}
```

---

## Quick Reference

### Builder Chain

```
EvalApp.App(name)
    .WithResource(ResourceKind.X)        ← one per ResourceKind used
    .WithTuning()                        ← optional; requires license
    .DefineDomain(name)
        .DefineTask<T>(name)
            .WithMiddleware(...)         ← optional; wraps all steps
            .AddStep(name, step)
            .Gate(ResourceKind.X, null, g => g
                .AddStep(name, step))
            .If(pred, then, else)
            .ForEach<TItem>(select, merge, name, parallelism, configure)
            .BeginSaga()
                .AddStepWithCompensation(name, forward, compensate)
            .EndSaga()
            .Run(out pipeline)           ← capture ICompiledPipeline<T>
        .Build(licenseKey)               ← void; compiles the app
```

### Step Selection

```
In-process logic (validation, mapping)?  → PureStep<T> (no gate)
External I/O (HTTP, DB, file)?           → AsyncStep<T> + Gate(ResourceKind.X, ...)
```

### License Modes

| `.Build()` call | Parallelism | Tuner | Cost |
|---|---|---|---|
| `.Build()` | Sequential | None | Free |
| `.Build(licenseKey)` | Parallel + ForEach | `.WithTuning()` active | Licensed |
