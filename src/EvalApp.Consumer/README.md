# EvalApp.Consumer

Consumer-grade async pipeline library for .NET 8 with adaptive resource gating,
automatic concurrency tuning, sagas with compensation, and parallel ForEach processing.

---

## Documentation

| Guide | Description |
|---|---|
| [user-guide.md](user-guide.md) | Core concepts, quick start, API reference, design methodology |
| [dotnet-integration-guide.md](dotnet-integration-guide.md) | ASP.NET Core / Generic Host DI integration, step patterns, gate configuration, testing |

---

## Quick Start

```csharp
// 1. Define an immutable data record
record OrderData(string Sku, int Quantity, bool IsValidated = false, decimal Total = 0);

// 2. Build once at startup
ICompiledPipeline<OrderData> pipeline;

EvalApp.App("Orders")
    .WithResource(ResourceKind.Network)
    .WithTuning()
    .DefineDomain("OrderProcessing")
        .DefineTask<OrderData>("ProcessOrder")
            .AddStep("Validate", d => d with { IsValidated = d.Quantity > 0 })
            .Gate(ResourceKind.Network, null, g => g
                .AddStep("FetchPrice", async (d, ct) =>
                    d with { Total = await catalog.GetPrice(d.Sku, ct) * d.Quantity }))
            .Run(out pipeline)
        .Build(licenseKey);   // omit licenseKey for free sequential mode

// 3. Execute — thread-safe, reuse across requests
var result = await pipeline.RunAsync(new OrderData("SKU-001", Quantity: 2), ct);

switch (result)
{
    case PipelineResult<OrderData>.Success s:
        Console.WriteLine($"Total: {s.Data.Total:C}");
        break;
    case PipelineResult<OrderData>.Failure f:
        Console.Error.WriteLine($"Failed: {f.Exception.Message}");
        break;
}
```

See [dotnet-integration-guide.md](dotnet-integration-guide.md) for a complete ASP.NET Core
example with DI registration, gate configuration, ForEach, sagas, and testing patterns.

---

## License

Unlicense — see package metadata. A commercial license key is required to enable parallel
`ForEach` and adaptive concurrency tuning.
