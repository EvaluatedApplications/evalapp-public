# EvalApp™

**The fastest async pipeline engine for .NET 8. Self-tuning. Zero infrastructure.**

EvalApp takes your processing logic — validation, enrichment, I/O, fan-out — and turns it into a single pre-built execution path that runs at native speed. You describe *what* to do. EvalApp figures out *how fast* your hardware can do it.

---

## Why EvalApp?

These are the problems EvalApp is built around. Not features — problems that come up in every codebase that does real async work.

---

### 1. Dependency injection is solving a different problem

DI solves one thing well: getting a service instance to the code that needs it. That is genuinely useful. In practice it means a registration file, three lifetime options, and a constructor on every class that lists its dependencies. Every time a new dependency is needed somewhere deep in a call graph, the class, its registration, and everything that constructs it all need updating.

What DI does not solve: sharing call-scoped state. The data for *this* request, the connection for *this* job run — these don't belong in the container, but they still need to reach your code.

**How EvalApp handles it.** Your services are a plain typed object, created once by the caller, in scope for every step automatically. Your call-scoped state is the data record. No registration. No lifetime to track. No captive dependency.

Because your context is a plain class, steps code to the interfaces on it — `IEmailSender`, `IRepository`, `IExternalApi`. Swapping infrastructure is one line: pass a different context object. Same steps, same topology, different environment.

---

### 2. Refactoring: the cost of changing what something needs

Requirements change. You need to pass a new piece of data to a method three layers deep. You add the parameter to that method, then update every method that calls it, then every method that calls those. In a call graph six methods deep, one new requirement touches six signatures.

**How EvalApp handles it.** Steps are isolated. The only contract between them is the data record. Adding a new field requires zero changes to any step that doesn't use it. The refactoring cascade doesn't happen because steps never owned the data shape in the first place.

```csharp
// Before: adding a field requires touching every method signature
Task ProcessOrder(int id, decimal discount, ILogger log, IDb db) { ... }
Task Validate(int id, decimal discount, IDb db) { ... }

// After: add one property to the record. Zero other changes.
record OrderData(int Id, decimal Discount = 0, bool Confirmed = false);
```

---

### 3. The whole picture lives in one place

In conventional async code, understanding what a complex operation depends on means tracing the call graph across a dozen files. The full picture is never on one screen.

**How EvalApp handles it.** The data record is both the function signature and the shared state at once. Non-nullable fields are inputs — required to start. Nullable fields are outputs — null until the step responsible for them runs.

Designing the data record well is the most valuable design work in an EvalApp project. When it is right, the steps almost write themselves.

```csharp
// Design session: list everything the process needs and produces.
// This record is the design — written as code.
record BatchData(
    int BatchId,                         // INPUT:   what batch to run
    DateTime ScheduledAt,                // INPUT:   when it was scheduled
    List<Item>?  LoadedItems,            // STAGE 1: loaded from database
    List<Item>?  ValidatedItems,         // STAGE 2: passed validation rules
    List<string>? ProcessedIds,          // STAGE 3: processed successfully
    List<string>? NotificationsSent,     // STAGE 4: recipients notified
    string?      ConfirmationReference   // STAGE 5: external confirmation ref
);
// One type. The entire shape of the process is visible at a glance.
```

---

### 4. The fluent API is a guided design surface

Writing fine-tuned async code by hand is genuinely difficult. Correct semaphore handling, clean saga rollback, bounded parallelism with exception aggregation — each is a solved problem individually, but combining them correctly under pressure is hard to sustain across a codebase and a team.

**How EvalApp handles it.** The fluent API is a guided path. After `.BeginSaga()` the type system requires `.EndSaga()` before `.Run()` compiles. After `.Gate(...)` the builder presents only the steps valid inside a gate. The compiler guides you through building something structurally valid — whole categories of async coordination bugs simply don't exist because the API doesn't permit those structures.

```csharp
EvalApp.App("MyApp")
    .WithResource(ResourceKind.Database)
    .DefineDomain("Processing")
        .DefineTask<BatchData>("ProcessBatch")
            .BeginSaga()
                .AddGate(ResourceKind.Database, null, g => g
                    .AddStep("Load", async (d, ct) => d with { LoadedItems = await db.Load(ct) }),
                    compensate: (d, ct) => db.Rollback(d.BatchId, ct))
                .AddStep("Process", d => d with { ProcessedIds = Process(d.LoadedItems!) })
            .EndSaga()   // compiler requires this — can't call Run() without it
            .Run(out var pipeline)
        .Build();
```

---

### 5. Zero dependencies — it just works

Most parallelism and pipeline libraries in .NET require infrastructure: a message broker, a distributed store, a configuration system. Each solves part of the problem. Combining them means managing their interactions and carrying their operational overhead.

**How EvalApp handles it.** EvalApp is pure .NET 8. No NuGet dependencies. No broker. No external storage. No configuration files. Add the project reference and it runs — in-process, compile-time safe. The adaptive tuner persists to a local JSON file automatically. There is nothing to deploy and nothing to configure.

---

### 6. The parallelism that async/await doesn't give you for free

`async/await` is concurrency — the ability to yield while waiting. Real parallelism — multiple things executing simultaneously — is a separate problem requiring throttling, exception aggregation, and cancellation propagation across every branch. Getting all of that right is non-trivial, so the practical default is sequential `await` chains.

**How EvalApp handles it.** Parallelism is declared, not implemented.

```csharp
.ForEach<Item>(
    select:         d => d.LoadedItems!,
    merge:          (orig, results) => orig with { ProcessedIds = results.Select(r => r.Id).ToList() },
    collectionName: "ProcessItems",
    parallelism:    Tunable.ForItems(),
    configure:      inner => inner
        .Gate(ResourceKind.Database, null, g => g
            .AddStep("Save", async (item, ct) => await db.Save(item, ct))))
```

The tuner observes real throughput on every run, probes neighbouring concurrency values, and converges on the number that works for the hardware it's running on — from the first run, without a benchmarking exercise.

**30–40% more throughput over manually-tuned baselines, measured on real high-throughput batch pipelines.**

---

### 7. The architecture map you normally pay for

In a mature codebase, understanding the full flow of one operation often means tracing calls across many files. That knowledge lives in the heads of the people who built it. The industry response is a category of runtime tracing tools — useful, but they show what *happened* during a run, not what the code *is designed to do*.

**How EvalApp handles it.** The builder chain *is* the architecture map. Reading the pipeline declaration tells you in one screen: which resources exist and at what concurrency limits, which steps are CPU-only vs gated, which collections are parallelised and how, which branches are conditional, which steps have compensation registered. It is always correct and always up to date.

EvalApp also provides static export at zero cost:

```csharp
// ASCII tree — loggable at startup
string tree = PipelineVisualizer<BatchData>.ToTextTree(pipeline.Root);
// └── [Sequence]
//     ├── [Gate:database]
//     │   └── [Step] Load
//     ├── [ForEach:ProcessItems]
//     │   └── [Gate:database]
//     │       └── [Step] Save
//     └── [Gate:network]
//         └── [Step] Notify

// Mermaid diagram, JSON export also available
```

Log the tree at startup and you have a permanent, searchable record of what the application was designed to do when it was deployed.

---

---

## How it compares

| | EvalApp | Temporal | Polly / resilience4j |
|--|---------|----------|---------------------|
| **Infrastructure** | None — in-process DLL | Server + DB required | None |
| **Step overhead** | Sub-millisecond | ~100ms (replay model) | N/A |
| **Concurrency tuning** | Automatic, continuous | Manual | Manual |
| **Multi-instance coordination** | Automatic, no communication | Via Temporal server | None |
| **Pipeline topology** | First-class, compile-time safe | Code + YAML config | None |
| **Visualizer / audit log** | Built-in | External tooling | None |
| **Target workload** | High-throughput, sub-second | Long-running, durable | Reactive resilience |

Temporal is the right tool when a workflow might run for hours and must survive process restarts. EvalApp is the right tool when throughput and latency matter and infrastructure doesn't belong in the picture.

Polly and resilience4j are *reactive* — they detect when things break and respond. EvalApp is *proactive* — it continuously finds the operating point that prevents breaking in the first place.

---

## 60-second quickstart

```csharp
// 1. Define an immutable data record — your pipeline's entire state in one type
record OrderData(int OrderId, bool IsValid = false, decimal Price = 0, string? Receipt = null);

// 2. Build once at startup
EvalApp.App("ProcessOrder")
    .WithTuning()
    .DefineDomain("Orders")
        .DefineTask<OrderData>("Process")
            .AddStep("Validate",    d => d with { IsValid = d.OrderId > 0 })
            .AddStep("FetchPrice",  async (d, ct) => d with { Price = await catalog.GetPrice(d.OrderId, ct) })
            .AddStep("SendReceipt", async (d, ct) => d with { Receipt = await email.Send(d, ct) })
            .Run(out var pipeline)
        .Build(licenseKey: "YOUR-LICENSE-KEY");

// 3. Call on every request — fully reusable, thread-safe, self-tuning
var result = await pipeline.RunAsync(new OrderData(orderId));

switch (result)
{
    case PipelineResult<OrderData>.Success s:  await Save(s.Data); break;
    case PipelineResult<OrderData>.Failure f:  log.Error(f.Exception); break;
    case PipelineResult<OrderData>.Skipped sk: log.Debug(sk.Reason); break;
}
```

**Free tier:** call `.Build()` with no license key. Every step runs correctly and sequentially — no license required for development or low-volume production use.

---

## What you get

### `AddStep` — sync, async, or class-based

```csharp
// Inline lambda — best for simple transforms
.AddStep("Validate", d => d with { IsValid = d.OrderId > 0 })

// Async lambda — best for I/O
.AddStep("Fetch", async (d, ct) => d with { Price = await catalog.GetPrice(d.OrderId, ct) })

// Class-based — best for injected services or complex logic
public class FetchPriceStep : AsyncStep<OrderData>
{
    private readonly ICatalog _catalog;
    public FetchPriceStep(ICatalog catalog) => _catalog = catalog;

    public override async ValueTask<OrderData> ExecuteAsync(OrderData data, CancellationToken ct)
        => data with { Price = await _catalog.GetPrice(data.OrderId, ct) };
}
.AddStep("FetchPrice", new FetchPriceStep(catalog))
```

### `ForEach` — parallel fan-out, automatically tuned

```csharp
.ForEach(
    name:      "ProcessOrders",
    select:    d => d.Orders,
    configure: inner => inner
        .AddStep("Validate", o => o with { IsValid = o.Total > 0 })
        .AddStep("Save",     async (o, ct) => o with { Saved = await db.Save(o, ct) }),
    merge:     (orig, results) => orig with { Orders = results })
```

In licensed mode, items run in parallel. The engine observes throughput on every run and adjusts the degree of parallelism automatically. In unlicensed mode, items run sequentially — same results, no tuner.

### `If` / `Else` — conditional branches

```csharp
.If(d => d.IsInternational,
    then: p => p.AddStep("CustomsDocs", async (d, ct) => ...),
    @else: p => p.AddStep("DomesticLabel", d => ...))
```

### `WithTuning()` — adaptive concurrency

The engine measures real throughput on every run, probes candidate concurrency values, and converges on the number that works for the hardware it's running on. No benchmarking. No configuration. No manual intervention when your deployment environment changes.

### `WithBayesianTuning()` — warm-start tuning

Same adaptive engine, but the performance model survives process restarts. A freshly deployed instance already knows what concurrency worked well last time — no re-exploration from scratch. Best for workloads that restart frequently (scheduled tasks, serverless-style invocations, rolling deployments).

### `WithWindowBudget` — time-budget pressure signal

Register a cycle budget. Steps read `ctx.ActivePressureResource.Pressure` (0.0–1.0) and degrade gracefully — skip expensive enrichment, return cached data, reduce scope — when the budget is tight.

### Built-in visualizer

```csharp
// ASCII tree — loggable at startup, searchable in logs
string tree = PipelineVisualizer<OrderData>.ToTextTree(pipeline.Root);
// └── [Sequence]
//     ├── [Step] Validate
//     ├── [ForEach:ProcessOrders]
//     │   └── [Step] Save
//     └── [Step] SendReceipt

// Mermaid diagram — paste into any markdown renderer
string diagram = PipelineVisualizer<OrderData>.ToMermaid(pipeline.Root);

// JSON — machine-readable for audit logs and tooling
string json = PipelineSerializer<OrderData>.ToJson(pipeline.Root);
```

These are static exports of the declared structure. They do not require execution and are always correct — no runtime tracing needed.

---

## Installation

Contact us for a distribution package and license key. We'll get you set up.

---

## Licensing

| Mode | What you get |
|------|-------------|
| **Unlicensed** (free) | All steps run correctly and sequentially. No parallelism, no tuner. No license required — ever. Ideal for development, CI, and low-volume production. |
| **Licensed** | Full parallel engine. Adaptive concurrency tuner. Pre-compiled hot path. Multi-instance convergence. `WithWindowBudget` pressure signal. |

Contact us for a license key.

---

## Use cases

- **Batch processing** — reconciliation, data transformation, enrichment pipelines
- **Financial services** — trade enrichment, settlement batch, risk calculation pipelines
- **Game simulations** — AI decision pipelines, turn-resolution loops, simulation ticks (see [NavPathfinder](https://github.com/EvaluatedApplications/navpathfinder-public) for pathfinding)
- **ML inference** — feature extraction, scoring, post-processing pipelines at throughput

---

## Standing on the shoulders of giants

EvalApp didn't invent these ideas. It assembled them into a coherent whole for .NET.

| Concept | Origin | What EvalApp borrows |
|---------|--------|----------------------|
| **Data-Driven Design** | Game/simulation architecture | All state is in a plain typed record — no ambient globals, no property bags. Steps transform the record, never own it. |
| **Railway-Oriented Programming** | Scott Wlaschin, F# community | Every step returns either success with transformed data or a failure reason. The pipeline short-circuits cleanly on failure without exceptions for expected cases. |
| **Saga Pattern** | Hector Garcia-Molina & Kenneth Salem (1987) | Multi-step operations register compensating actions. If any step fails, prior steps are rolled back in reverse order — no distributed transaction required. |
| **Fluent Type-State Builder** | .NET builder convention + type theory | The builder's return type changes at each step. After `.BeginSaga()` you get a saga builder. After `.Run(out pipeline)` you get a finaliser. The compiler prevents invalid orderings. |
| **Adaptive Concurrency (Hill Climbing)** | Auto-Tune / reactive systems community | The tuner probes concurrency values like a gradient-free optimiser: try a neighbour, measure throughput, keep it if it helps, revert if it doesn't. No offline calibration required. |
| **Bayesian Bandit (Thompson Sampling)** | Multi-armed bandit literature | Each concurrency candidate has a Beta distribution posterior. Candidates with higher uncertainty are explored more. Arms with proven performance are exploited. Posteriors survive restarts — warm starts at first run. |
| **Domain-Driven Design** | Eric Evans, *Domain-Driven Design* (2003) | Pipeline domains map to bounded contexts. Step naming follows ubiquitous language. The data record is the domain model for that pipeline. |
| **Immutable Value Objects** | DDD / functional programming | Data records use `with` expressions — every transformation produces a new value. No shared mutable state means no race conditions and no test isolation problems. |

The term *Evaluated Application™* is original to this project. It describes a function that is fully evaluated before execution — all parameters resolved, all steps compiled into an optimised execution path, ready to run at native speed. The term, its definition, and its application to pipeline architecture are the original work of Evaluated Applications.

---

## Full API reference

[`src/EvalApp.Consumer/user-guide.md`](src/EvalApp.Consumer/user-guide.md) — complete API reference, design methodology, and examples.

---

*© 2026 Evaluated Applications. All rights reserved. EvalApp™ and Evaluated Application™ are trademarks of Evaluated Applications. The name, concept, and associated documentation are original works protected under copyright. Unauthorised reproduction or claim of authorship is prohibited.*
