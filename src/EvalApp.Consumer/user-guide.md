# EvalApp User Guide

> This guide covers the consumer API, the design methodology, and usage patterns.
> Read it before writing your first pipeline.

---

## Table of Contents

1. [Overview](#1-overview)
2. [Quick Start](#2-quick-start)
3. [Core Concepts](#3-core-concepts)
   - [Pipeline Data Records](#31-pipeline-data-records)
   - [Steps](#32-steps)
   - [The Builder API](#33-the-builder-api)
   - [Adaptive Tuning](#34-adaptive-tuning)
   - [ForEach — Parallel Collections](#35-foreach--parallel-collections)
   - [Branching — If/Else](#36-branching--ifelse)
   - [Pressure Resources](#37-pressure-resources)
   - [PipelineResult\<T\>](#38-pipelineresultt)
4. [Design Methodology](#4-design-methodology)
5. [Examples](#5-examples)
   - [Example 1: File Processor](#example-1-file-processor)
   - [Example 2: HTTP Batch Processor](#example-2-http-batch-processor)
   - [Example 3: Conditional Pipeline](#example-3-conditional-pipeline)
   - [Example 4: Capstone — Nightly Billing Batch](#example-4-capstone--nightly-billing-batch)
6. [API Reference](#6-api-reference)
7. [Licensing](#7-licensing)
8. [FAQ and Common Mistakes](#8-faq-and-common-mistakes)

---

## 1. Overview

EvalApp compiles your processing logic — validation, enrichment, I/O, fan-out — into a single
pre-built execution path that runs at native speed. You describe *what* to do; EvalApp
determines *how* to do it as fast as your hardware allows.

**The three-part philosophy:**

| Principle | Meaning |
|---|---|
| **Declare** | Describe the pipeline topology once at startup as a fluent chain |
| **Compose** | Steps are lambdas or classes; they compose without coupling |
| **Tune** | The engine observes throughput and adjusts concurrency automatically |

**What EvalApp replaces:** the manual coordination ceremony — `SemaphoreSlim`, `Task.WhenAll`,
`AggregateException`, nested try/catch loops, concurrency parameters guessed at by hand.
You state the work; the engine handles the execution model.

**Two modes:**

- **Unlicensed** — all steps run correctly and sequentially. Free to use. No license required
  for development or low-volume production.
- **Licensed** — full engine. Parallel `ForEach`, adaptive concurrency tuning, pre-compiled
  hot path. Contact us for a license key.

---

## 2. Quick Start

```csharp
// 1. Define an immutable data record — it flows through every step
record OrderData(
    int     OrderId,
    bool    IsValid  = false,
    decimal Price    = 0,
    string? Receipt  = null);

// 2. Build once at startup
ICompiledPipeline<OrderData> pipeline;

EvalApp.App("Orders")
    .WithTuning()
    .DefineDomain("OrderProcessing")
        .DefineTask<OrderData>("ProcessOrder")
            .AddStep("Validate",    d => d with { IsValid = d.OrderId > 0 })
            .AddStep("FetchPrice",  async (d, ct) => d with { Price = await catalog.GetPrice(d.OrderId, ct) })
            .AddStep("SendReceipt", async (d, ct) => d with { Receipt = await email.Send(d, ct) })
            .Run(out pipeline)
        .Build(licenseKey: "YOUR-LICENSE-KEY");   // or .Build() for free tier

// 3. Run on every request — the compiled pipeline is reusable and thread-safe
var result = await pipeline.RunAsync(new OrderData(orderId));

switch (result)
{
    case PipelineResult<OrderData>.Success s:
        Console.WriteLine($"Done: {s.Data.Receipt}");
        break;
    case PipelineResult<OrderData>.Failure f:
        Console.Error.WriteLine($"Failed: {f.Exception.Message}");
        break;
}
```

That is the complete API for the simple case. The rest of this guide adds parallelism,
branching, tuning, and pressure signals.

---

## 3. Core Concepts

### 3.1 Pipeline Data Records

The data record is the single shared state for one pipeline run. Every step receives the
current record and returns a new one. No shared mutable state; no side-channel communication.

**Design the record first.** Before writing a single step, ask: *what does this process carry
from start to finish?* Write it as a C# `record`. The field names should speak the language
of your domain.

```csharp
// Every field annotated with its lifecycle role
record InvoiceBatchData(
    int           CustomerId,       // INPUT:   provided by the caller before the run
    DateOnly      BillingPeriod,    // INPUT:   provided by the caller before the run
    List<Invoice>? Invoices,        // STAGE 1: null until LoadInvoicesStep populates it
    List<Result>?  Results,         // OUTPUT:  null until ForEach completes
    int            SuccessCount = 0,// OUTPUT:  summarised after merge
    int            FailureCount = 0 // OUTPUT:  summarised after merge
);
```

**Annotations:**

| Annotation | Meaning |
|---|---|
| `INPUT` | Provided by the caller; present before the pipeline starts |
| `STAGE N` | Produced by step N; null until that step runs |
| `OUTPUT` | The value consumed by the caller after the run |
| `CONTROL` | A flag or nullable that drives a conditional branch |

Reading the record top to bottom should read like the algorithm. If you cannot describe the
pipeline by reading the record alone, the data model is not finished.

**Mutations use `with` expressions — never mutate the record directly:**

```csharp
// ✅ Correct — returns a new record
return d with { Price = calculatedPrice };

// ❌ Wrong — records are immutable; this does not compile
d.Price = calculatedPrice;
```

**Why immutability matters for parallel pipelines:** when `ForEach` runs items concurrently,
each item receives its own copy of the item record. No synchronisation code is needed because
there is no shared mutable state.

---

### 3.2 Steps

A step is a function from `T` to `T`. It receives the current data record and returns an
updated copy. Steps come in two forms:

#### Synchronous step — pure in-process work

```csharp
// Signature: Func<T, T>
.AddStep("Normalise",  d => d with { Name = d.Name.Trim().ToUpper() })
.AddStep("Calculate",  d => d with { Total = d.Subtotal * (1 + d.TaxRate) })
.AddStep("SetFlag",    d => d with { IsProcessed = true })
```

Use for transformations with no I/O: calculations, validation, field mapping.

#### Asynchronous step — any I/O

```csharp
// Signature: Func<T, CancellationToken, ValueTask<T>>
.AddStep("FetchPrice",  async (d, ct) => d with { Price = await catalog.GetPrice(d.Sku, ct) })
.AddStep("SaveResult",  async (d, ct) => { await repo.Save(d, ct); return d with { Saved = true }; })
.AddStep("SendEmail",   async (d, ct) => d with { EmailId = await mailer.Send(d.Address, ct) })
```

Always use the async overload for database, network, file, and external API calls. Blocking
I/O inside a sync step wastes a thread-pool thread for the duration of the wait.

#### Class-based steps

For steps that need injected services, define a class with a captured reference:

```csharp
public sealed class FetchPriceStep
{
    private readonly ICatalogService _catalog;
    public FetchPriceStep(ICatalogService catalog) => _catalog = catalog;

    public async ValueTask<OrderData> ExecuteAsync(OrderData d, CancellationToken ct)
        => d with { Price = await _catalog.GetPrice(d.Sku, ct) };
}

// Register the instance at build time — the step is a singleton reused across all runs
var fetchPrice = new FetchPriceStep(catalog);
ICompiledPipeline<OrderData> pipeline;

EvalApp.App("Orders")
    .DefineDomain("OrderProcessing")
        .DefineTask<OrderData>("ProcessOrder")
            .AddStep("FetchPrice", fetchPrice.ExecuteAsync)
            .Run(out pipeline)
        .Build(licenseKey);
```

> **Steps are singletons.** They are created once at build time and reused across all
> concurrent invocations. Never store per-execution state on a step instance. Per-run state
> belongs exclusively in the data record.

---

### 3.3 The Builder API

The complete builder chain:

```csharp
ICompiledPipeline<T> pipeline;

EvalApp.App(name)                                // (1) entry point → IAppBuilder
    .WithResource(ResourceKind.Database)           // (2) declare resource kinds used by gates
    .WithTuning()                                  // (3) adaptive tuner (optional, licensed only)
    .WithWindowBudget(cyclesPerSecond)              // (4) time-budget pressure (optional)
    .DefineDomain(domainName)                      // (5) → IDomainBuilder
        .DefineTask<T>(taskName)                   // (6) → IEmptyTaskBuilder<T>
            .AddStep(...)                          // (7) add steps → ITaskBuilder<T>
            .If(...)                               // (8) conditional branch
            .ForEach(...)                          // (9) parallel fan-out
            .Gate(...)                             // (10) resource-gated sub-pipeline
            .Pressure(...)                         // (11) scope steps in named pressure
            .WindowBudget(...)                     // (12) scope steps in window budget
            .BeginSaga(...)                        // (13) saga with compensation (optional)
                .AddStepWithCompensation(...)
            .EndSaga()
            .Run(out pipeline)                     // (14) capture pipeline → IDomainBuilder
        .Build(licenseKey);                        // (15) validate license and finalise
```

`.Run(out pipeline)` captures the compiled pipeline. `.Build(licenseKey)` validates the
license key and finalises the app. After this point, only `RunAsync` matters.

#### Register as a singleton

```csharp
// ASP.NET Core / generic host — register once at startup
services.AddSingleton(sp =>
{
    ICompiledPipeline<OrderData> pipeline;

    EvalApp.App("Orders")
        .WithTuning()
        .DefineDomain("OrderProcessing")
            .DefineTask<OrderData>("ProcessOrder")
                .AddStep("Validate",   d => d with { IsValid = d.OrderId > 0 })
                .AddStep("FetchPrice", async (d, ct) => ...)
                .Run(out pipeline)
            .Build(configuration["EvalApp:LicenseKey"]);

    return pipeline;
});

// Inject and call in a controller / handler
public class OrdersHandler(ICompiledPipeline<OrderData> pipeline)
{
    public async Task<IActionResult> Handle(int orderId, CancellationToken ct)
    {
        var result = await pipeline.RunAsync(new OrderData(orderId), ct);
        return result is PipelineResult<OrderData>.Success s
            ? Ok(s.Data)
            : Problem(detail: result.GetData().ToString());
    }
}
```

Rebuilding the pipeline per request discards all accumulated tuning state — the engine never
warms up. Always build once, inject everywhere.

---

### 3.4 Adaptive Tuning

Tuning is **opt-in** and only active in **licensed mode**. It controls `ForEach` parallelism.

#### `WithTuning()` — in-memory adaptive tuner

```csharp
EvalApp.App("Orders")
    .WithTuning()
    .DefineDomain("OrderProcessing")
        .DefineTask<OrderData>("ProcessOrder")
            .AddStep(...)
            .Run(out pipeline)
        .Build(licenseKey);
```

After each run the engine observes throughput and adjusts the number of concurrent `ForEach`
items automatically. The tuner moves through four phases:

| Phase | What happens |
|---|---|
| **Ramping** | Starts conservative; increases concurrency on each run |
| **Exploring** | Tests values above and below the current optimum |
| **Converging** | Narrows the range; settles on the best observed level |
| **Monitoring** | Holds the optimum; re-explores if throughput degrades |

No configuration is required. State is held in memory and reset on process restart — the
tuner re-discovers the optimum through the normal phase sequence.

#### `WithBayesianTuning()` — persistent adaptive tuner

```csharp
EvalApp.App("Orders")
    .WithBayesianTuning()
    .DefineDomain("OrderProcessing")
        .DefineTask<OrderData>("ProcessOrder")
            .AddStep(...)
            .Run(out pipeline)
        .Build(licenseKey);
```

Same adaptive behaviour, but the performance model survives process restarts. A restarted
process already knows which concurrency values performed well; it does not re-explore from
scratch.

**Use `WithBayesianTuning()` when:**
- The pipeline restarts frequently (scheduled jobs, deployment cycles, short-lived processes)
- Multiple instances share a resource — the persistent model accounts for contention pressure
  that a fresh-start tuner would re-learn each time

#### Tuning summary

| Build call | Execution | Tuner | Persists across restart |
|---|---|---|---|
| `.Build()` | Sequential | None | — |
| `.Build(key)` | Parallel, fixed concurrency | None | — |
| `.WithTuning()` + `.Build(key)` | Parallel, auto-tuned | Adaptive | No |
| `.WithBayesianTuning()` + `.Build(key)` | Parallel, auto-tuned | Bayesian | Yes |

---

### 3.5 ForEach — Parallel Collections

```csharp
ITaskBuilder<T> ForEach<TItem>(
    Func<T, IEnumerable<TItem>>          select,
    Func<T, IReadOnlyList<TItem>, T>     merge,
    string                               collectionName,
    Action<ISubTaskBuilder<TItem>>       configure)
```

Fans out over a collection embedded in the parent record. Each item runs through an
independent sub-pipeline. When all items complete, `merge` folds the processed items back.

```csharp
.ForEach(
    select:         batch => batch.Invoices,
    merge:          (batch, processed) => batch with { Invoices = processed },
    collectionName: "ProcessInvoices",
    configure:      inner => inner
        .AddStep("Validate",   async (inv, ct) => inv with { IsValid = await Validate(inv, ct) })
        .AddStep("Charge",     async (inv, ct) => inv with { ChargeRef = await Charge(inv, ct) })
        .AddStep("SendEmail",  async (inv, ct) => inv with { EmailSent = await Email(inv, ct) }))
```

**Behaviour:**

| | Unlicensed | Licensed |
|---|---|---|
| Item execution | Sequential | Concurrent, auto-tuned by engine |
| Failure isolation | A failure on one item does not stop others | Same |
| Merge order | Not guaranteed | Not guaranteed |

**Independence requirement:** items must not read each other's in-progress state. If item B
depends on a result computed by item A, they must be sequential steps before or after the
`ForEach`, not parallel items inside it.

**Nested ForEach is not supported.** A `ForEach` sub-pipeline cannot itself contain a
`ForEach`. Flatten the structure: complete the inner fan-out as a step, then run the outer.

---

### 3.6 Branching — If/Else

```csharp
ITaskBuilder<T> If(
    Func<T, bool>                          predicate,
    Action<IConditionalBranchBuilder<T>>   then,
    Action<IConditionalBranchBuilder<T>>?  @else = null)
```

Executes the `then` branch when `predicate` returns `true`, the `else` branch otherwise.
The `else` branch is optional — omitting it produces a silent pass-through when the condition
is false.

```csharp
// Two-branch: enterprise vs standard document template
.If(d => d.IsEnterprise,
    then:  b => b.AddStep("EnterpriseTemplate",  async (d, ct) => d with { Pdf = await RenderEnterprise(d, ct) }),
    @else: b => b.AddStep("StandardTemplate",    async (d, ct) => d with { Pdf = await RenderStandard(d, ct) }))

// Single-branch: optional discount
.If(d => d.HasDiscount,
    then: b => b.AddStep("ApplyDiscount", d => d with { Total = d.Total * 0.9m }))
```

**Both branches must operate on the same `T`.** The type system enforces this — both arms
receive an `IConditionalBranchBuilder<T>`. A branch that produces a different record type is
not supported at this level; model that as a field on the parent record instead.

**Branches can contain multiple steps:**

```csharp
.If(d => d.RequiresApproval,
    then: b => b
        .AddStep("NotifyApprover", async (d, ct) => ...)
        .AddStep("WaitForApproval", async (d, ct) => ...)
        .AddStep("RecordDecision",  async (d, ct) => ...))
```

**Branches can contain ForEach:**

```csharp
.If(d => d.IsBulkOrder,
    then:  b => b.ForEach(d => d.Items, (d, r) => d with { Items = r },
                          "BulkItems", inner => inner.AddStep("ProcessItem", ...)),
    @else: b => b.AddStep("SingleItem", async (d, ct) => ...))
```

---

### 3.7 Pressure Resources

Pressure resources are **soft budget signals** — a normalised `float` that steps read to
decide how aggressively to work. They never block. Use them for graceful degradation when the
pipeline must pace itself against a time window or an external rate limit.

| Value | Meaning |
|---|---|
| `0.0` | Budget is full — no pressure |
| `0.5` | Half the budget is consumed |
| `1.0` | Budget limit reached |
| `> 1.0` | Budget exceeded — maximum pressure |

Steps inside a pressure scope read the signal from `StepContext.ActivePressureResource` and
decide locally how to respond: skip optional enrichment, use a cheaper fallback, reduce output
quality. The pressure resource itself never forces a step to skip — the step decides.

#### `WithWindowBudget` — time-window budget

A `WindowBudgetPressure` tracks how much of a time window (in cycles per second) has been
consumed. Pressure rises as the window fills. Reset the resource at the start of each window.

```csharp
ICompiledPipeline<FrameData> pipeline;

EvalApp.App("GameLoop")
    .WithWindowBudget(cyclesPerSecond: 60)              // 60 Hz budget
    .DefineDomain("Simulation")
        .DefineTask<FrameData>("Tick")
            .AddStep("Physics",    async (d, ct) => ...) // always runs — outside WindowBudget scope
            .WindowBudget(inner => inner
                .AddStep("AiUpdate",   async (d, ct) => ...) // reads pressure; may degrade when budget is tight
                .AddStep("Particles",  async (d, ct) => ...) // skips itself when pressure > 0.9
                .AddStep("SoundFx",    async (d, ct) => ...))
            .AddStep("Render",     async (d, ct) => ...) // always runs — outside WindowBudget scope
            .Run(out pipeline)
        .Build(licenseKey);
```

> **Reset the resource at the start of each window.** A pressure resource accumulates until
> explicitly reset. Omitting the reset causes pressure to climb indefinitely, eventually
> locking all pressure-scoped steps into their maximum-degradation path permanently.

#### `WithPressure` — custom pressure source

For custom pressure signals — rate limiters, token buckets, external load indicators.
Register the resource by name with `WithPressure`, then wrap the affected steps with
`Pressure(name, ...)`.

```csharp
ICompiledPipeline<OrderData> pipeline;

EvalApp.App("Orders")
    .WithPressure(new TokenBucketPressure("vendor-api", capacity: 1000, refillRate: 100))
    .DefineDomain("OrderProcessing")
        .DefineTask<OrderData>("ProcessOrder")
            .AddStep("Load",   async (d, ct) => ...)
            .Pressure("vendor-api", inner => inner
                .AddStep("CallVendor", async (d, ct) => ...))
            .AddStep("Save",   async (d, ct) => ...)
            .Run(out pipeline)
        .Build(licenseKey);
```

**Constraint:** `Pressure()` and `WindowBudget()` cannot be used inside `If()` conditional
branches. They can appear at the top level of the pipeline and inside `ForEach` sub-pipelines.

**Constraint:** at least one `AddStep()` must appear before the first `Pressure()` or
`WindowBudget()` call in a pipeline.

---

### 3.8 PipelineResult\<T\>

Every `RunAsync` call returns `PipelineResult<T>` — a discriminated union. Always
pattern-match; never call `GetData()` blindly on a failure.

```csharp
var result = await pipeline.RunAsync(data, ct);

switch (result)
{
    case PipelineResult<OrderData>.Success s:
        // s.Data — the fully processed record
        await SaveFinalState(s.Data);
        break;

    case PipelineResult<OrderData>.Failure f:
        // f.Data     — last successful state before the failure
        // f.Exception — the cause
        // f.Message  — optional extra context (may be null)
        log.Error(f.Exception, "Pipeline failed: {Message}", f.Message);
        await RecordFailure(f.Data, f.Exception);
        break;

    case PipelineResult<OrderData>.Skipped sk:
        // A conditional branch did not match; data is unchanged
        // sk.Reason — which branch was not taken
        log.Debug("Skipped: {Reason}", sk.Reason);
        break;
}
```

**Convenience properties:**

```csharp
result.IsSuccess    // bool
result.IsFailure    // bool
result.IsSkipped    // bool
result.GetData()    // T — extracts data regardless of outcome; use only when outcome is known
```

**Exceptions inside steps** are caught by the engine and wrapped in a `Failure` result. You do
not need `try/catch` around `RunAsync` for normal step failures. Catch at the `RunAsync` call
site only for infrastructure exceptions (e.g., `OperationCanceledException`).

---

## 4. Design Methodology

Before writing any step, answer these seven questions in order. The order matters — each
answer constrains the next.

| # | Question |
|---|---|
| 1 | What is the unit of work? |
| 2 | What does it start with, carry, and produce? |
| 3 | What resources does each operation touch? |
| 4 | What needs to run per-item in parallel? |
| 5 | What conditional branches exist? |
| 6 | What needs to be rolled back if something fails? |
| 7 | Wire it together |

### Question 1 — Unit of Work

One sentence. What is one run of this pipeline? Is it one invoice, one batch, one request,
one processing window? Getting this wrong causes every subsequent decision to be made at the
wrong granularity.

### Question 2 — Data Model

Design the data record(s) before writing any code. Annotate every field as INPUT, STAGE N,
OUTPUT, or CONTROL. Reading the record definition top to bottom should read like the algorithm.
If you cannot describe the full lifecycle from the record alone, keep adding fields.

### Question 3 — Resource Classification

For each operation, identify what it touches: Database, Network, CPU, DiskIO, or nothing
(pure calculation). This tells you which steps can run in parallel and which are bottlenecks.

### Question 4 — Parallel Items

Identify every collection in the data where items are independent of each other. These are
`ForEach` boundaries. For each, confirm the independence guarantee — items must not read
each other's in-progress state.

### Question 5 — Conditional Branches

List every `If/Else` decision: the condition, what the `then` branch does, what the `else`
branch does. Both branches must write to the same output fields.

### Question 6 — Compensation

List every external effect that must be reversed if a downstream step fails: charges, emails,
file writes, database rows. For each: what is the effect, and what is the rollback action?
If no effects need rollback, state that explicitly.

> **Note:** Saga compensation (automatic rollback on failure) is available via
> `.BeginSaga()` / `.EndSaga()`. See the
> [dotnet-integration-guide](dotnet-integration-guide.md) for saga patterns.

### Question 7 — Wire It Together

Only at this step write the pipeline declaration. It should read like a plain-language
description of the process. If it does not, revisit the data model.

### Design Checklist

- [ ] **What is one run?** One item or one batch?
- [ ] **Every field annotated** as INPUT / STAGE / OUTPUT / CONTROL
- [ ] **Full lifecycle readable from the record alone**
- [ ] **Each operation's resource identified**
- [ ] **ForEach boundaries and their independence guarantee stated**
- [ ] **Every If/Else condition enumerated**
- [ ] **Compensable effects listed** (or explicitly "none")
- [ ] **Declaration reads like the problem** — if yes, it is correct

---

## 5. Examples

### Example 1: File Processor

**Unit of work:** one batch of files to validate, transform, and write.

**Data model:**

```csharp
record FileProcessorData(
    string[]       InputPaths,     // INPUT:   file paths to process
    FileEntry[]?   Loaded,         // STAGE 1: file contents after reading
    FileEntry[]?   Validated,      // STAGE 2: entries that passed validation
    FileEntry[]?   Transformed,    // STAGE 3: entries after transformation
    int            WrittenCount = 0 // OUTPUT:  how many files were written
);

record FileEntry(
    string Path,
    string? Content    = null,
    bool    IsValid    = false,
    string? OutputPath = null,
    bool    Written    = false
);
```

**Pipeline:**

```csharp
ICompiledPipeline<FileProcessorData> pipeline;

EvalApp.App("FileProcessor")
    .WithTuning()
    .DefineDomain("FileOps")
        .DefineTask<FileProcessorData>("ProcessFiles")
            // Step 1: Load all files from disk
            .AddStep("LoadFiles", async (d, ct) =>
            {
                var entries = await Task.WhenAll(
                    d.InputPaths.Select(async path =>
                        new FileEntry(path, Content: await File.ReadAllTextAsync(path, ct))));
                return d with { Loaded = entries };
            })
            // Step 2: Validate — filter out entries that are empty or too large
            .AddStep("ValidateFiles", d =>
            {
                var valid = d.Loaded!
                    .Where(e => e.Content is { Length: > 0 and < 1_000_000 })
                    .Select(e => e with { IsValid = true })
                    .ToArray();
                return d with { Validated = valid };
            })
            // Step 3: Transform each file in parallel (licensed mode)
            .ForEach(
                select:         d => d.Validated!,
                merge:          (d, processed) => d with { Transformed = processed.ToArray() },
                collectionName: "TransformFiles",
                configure:      inner => inner
                    .AddStep("Normalise",  e => e with { Content = e.Content!.Replace("\r\n", "\n") })
                    .AddStep("SetOutput",  e => e with { OutputPath = Path.ChangeExtension(e.Path, ".out") })
                    .AddStep("Write",      async (e, ct) =>
                    {
                        await File.WriteAllTextAsync(e.OutputPath!, e.Content, ct);
                        return e with { Written = true };
                    }))
            // Step 4: Summarise
            .AddStep("Summarise", d => d with { WrittenCount = d.Transformed!.Count(e => e.Written) })
            .Run(out pipeline)
        .Build(licenseKey);

var result = await pipeline.RunAsync(new FileProcessorData(inputPaths));

if (result is PipelineResult<FileProcessorData>.Success s)
    Console.WriteLine($"Written {s.Data.WrittenCount} of {s.Data.InputPaths.Length} files.");
```

**What you did not have to write:** no `SemaphoreSlim` for file I/O, no `AggregateException`
handling for failures in individual files, no concurrency parameter for the ForEach degree.

---

### Example 2: HTTP Batch Processor

**Unit of work:** one batch of URLs to fetch and store.

**Data model:**

```csharp
record HttpBatchData(
    string[]       Urls,             // INPUT:   URLs to fetch
    UrlResult[]?   Results,          // OUTPUT:  per-URL outcome after ForEach
    int            SuccessCount = 0, // OUTPUT:  summary
    int            FailureCount = 0  // OUTPUT:  summary
);

record UrlResult(
    string  Url,
    int     StatusCode = 0,
    string? Body       = null,
    bool    Stored     = false,
    string? Error      = null
);
```

**Pipeline:**

```csharp
ICompiledPipeline<HttpBatchData> pipeline;

EvalApp.App("HttpBatch")
    .WithResource(ResourceKind.Network)
    .WithTuning()
    .DefineDomain("Fetching")
        .DefineTask<HttpBatchData>("FetchBatch")
            // Fan out: fetch and store each URL in parallel (licensed), sequential (unlicensed)
            .ForEach(
                select:         d => d.Urls.Select(url => new UrlResult(url)),
                merge:          (d, results) => d with { Results = results.ToArray() },
                collectionName: "FetchAndStore",
                configure:      inner => inner
                    .AddStep("Fetch", async (r, ct) =>
                    {
                        try
                        {
                            using var response = await http.GetAsync(r.Url, ct);
                            var body = await response.Content.ReadAsStringAsync(ct);
                            return r with { StatusCode = (int)response.StatusCode, Body = body };
                        }
                        catch (HttpRequestException ex)
                        {
                            return r with { Error = ex.Message };
                        }
                    })
                    .AddStep("Store", async (r, ct) =>
                    {
                        if (r.Body is null) return r; // failed fetch — skip store
                        await storage.SaveAsync(r.Url, r.Body, ct);
                        return r with { Stored = true };
                    }))
            // Summarise after fan-out
            .AddStep("Summarise", d => d with
            {
                SuccessCount = d.Results!.Count(r => r.Stored),
                FailureCount = d.Results!.Count(r => r.Error is not null),
            })
            .Run(out pipeline)
        .Build(licenseKey);

var urls = new[] { "https://api.example.com/1", "https://api.example.com/2", /* ... */ };
var result = await pipeline.RunAsync(new HttpBatchData(urls));

if (result is PipelineResult<HttpBatchData>.Success s)
    Console.WriteLine($"Fetched {s.Data.SuccessCount} OK, {s.Data.FailureCount} failed.");
```

**Note:** individual item failures (caught inside the `Fetch` step) do not stop the pipeline
or fail other items. Each item's exception is captured in the `Error` field and the pipeline
continues. Only uncaught exceptions escalate to a `PipelineResult.Failure`.

---

### Example 3: Conditional Pipeline

**Unit of work:** one document to process — routing depends on a field on the record.

```csharp
record DocumentData(
    string  DocumentId,
    string  Type,           // "legal" | "marketing" | "technical"
    string? Content = null,
    string? ReviewedBy = null,
    string? PublishedAt = null,
    bool    RequiresLegalReview = false
);

ICompiledPipeline<DocumentData> pipeline;

EvalApp.App("Documents")
    .DefineDomain("Publishing")
        .DefineTask<DocumentData>("ProcessDocument")
            // Step 1: Load
            .AddStep("Load", async (d, ct) => d with { Content = await docs.GetContent(d.DocumentId, ct) })
            // Step 2: Flag documents that need legal review
            .AddStep("Classify", d => d with { RequiresLegalReview = d.Type == "legal" })
            // Step 3: Branch on legal vs standard workflow
            .If(d => d.RequiresLegalReview,
                then: b => b
                    .AddStep("LegalReview",   async (d, ct) => d with { ReviewedBy = await legal.ReviewAsync(d, ct) })
                    .AddStep("LegalApproval", async (d, ct) => d with { Content = await legal.ApproveAsync(d, ct) }),
                @else: b => b
                    .AddStep("AutoApprove", d => d with { ReviewedBy = "system" }))
            // Step 4: Publish — runs for both branches
            .AddStep("Publish", async (d, ct) =>
                d with { PublishedAt = await publisher.PublishAsync(d, ct) })
            .Run(out pipeline)
        .Build(licenseKey);

var result = await pipeline.RunAsync(new DocumentData("DOC-001", Type: "legal"));
```

The `If` branch is type-safe: both arms receive and return `DocumentData`. Fields written in
the `then` branch and not written in the `else` branch remain at their default value — which
is why `ReviewedBy` is nullable and `RequiresLegalReview` is a `CONTROL` field rather than an
assumption baked into the logic.

---

### Example 4: Capstone — Nightly Billing Batch

InvoiceMate runs a nightly billing batch. One pipeline run processes all invoices for one
customer.

**Data model:**

```csharp
record InvoiceBatchData(
    int            CustomerId,          // INPUT
    DateOnly       BillingPeriod,       // INPUT
    List<Invoice>? Invoices,            // STAGE 1: loaded from database
    List<Invoice>? Results,             // OUTPUT:  after ForEach
    int            SuccessCount = 0,    // OUTPUT
    int            FailureCount = 0     // OUTPUT
);

record Invoice(
    int          InvoiceId,
    int          CustomerId,
    List<Item>   LineItems,
    bool         IsEligible    = false, // STAGE 1
    decimal      DiscountPct   = 0,     // STAGE 2
    decimal      TotalAmount   = 0,     // STAGE 2
    string?      ChargeRef     = null,  // STAGE 3: payment gateway reference
    bool         IsEnterprise  = false, // CONTROL: drives template branch
    byte[]?      PdfBytes      = null,  // STAGE 4: rendered PDF
    bool         EmailSent     = false  // OUTPUT
);
```

**Pipeline declaration:**

```csharp
ICompiledPipeline<InvoiceBatchData> pipeline;

EvalApp.App("NightlyBilling")
    .WithResource(ResourceKind.Database)
    .WithResource(ResourceKind.Network)
    .WithTuning()
    .DefineDomain("Billing")
        .DefineTask<InvoiceBatchData>("ProcessBatch")
            // Step 1: Load all invoices for this customer from the database
            .AddStep("LoadInvoices", async (d, ct) =>
                d with { Invoices = await db.GetInvoicesAsync(d.CustomerId, d.BillingPeriod, ct) })
            // Step 2: Process each invoice in parallel
            .ForEach(
                select:         d => d.Invoices!,
                merge:          (d, processed) => d with { Results = processed.ToList() },
                collectionName: "ProcessInvoices",
                configure:      inner => inner
                    // 2a: Check eligibility
                    .AddStep("CheckEligibility",  async (inv, ct) =>
                        inv with { IsEligible = await billing.CheckEligibilityAsync(inv, ct) })
                    // 2b: Skip ineligible invoices cleanly
                    .If(inv => !inv.IsEligible,
                        then: b => b.AddStep("Skip", inv => inv))  // pass-through; ineligible = no-op
                    // 2c: Calculate amounts (pure — no I/O)
                    .AddStep("CalcDiscount", inv => inv with { DiscountPct = billing.DiscountFor(inv) })
                    .AddStep("CalcTotal",    inv => inv with { TotalAmount  = billing.TotalFor(inv) })
                    // 2d: Charge via payment gateway
                    .AddStep("Charge", async (inv, ct) =>
                        inv with { ChargeRef = await payments.ChargeAsync(inv, ct) })
                    // 2e: Render PDF — branch on account type
                    .If(inv => inv.IsEnterprise,
                        then:  b => b.AddStep("EnterprisePdf",  async (inv, ct) =>
                                       inv with { PdfBytes = await pdf.RenderEnterpriseAsync(inv, ct) }),
                        @else: b => b.AddStep("StandardPdf",    async (inv, ct) =>
                                       inv with { PdfBytes = await pdf.RenderStandardAsync(inv, ct) }))
                    // 2f: Send confirmation
                    .AddStep("SendEmail", async (inv, ct) =>
                        inv with { EmailSent = await email.SendAsync(inv, ct) }))
            // Step 3: Summarise outcomes
            .AddStep("Summarise", d => d with
            {
                SuccessCount = d.Results!.Count(inv => inv.EmailSent),
                FailureCount = d.Results!.Count(inv => !inv.EmailSent),
            })
            .Run(out pipeline)
        .Build(licenseKey);
```

**What you did not write:**
- No `SemaphoreSlim` or `lock` for invoice concurrency
- No `try/catch` at the `ForEach` loop level
- No `AggregateException` handling
- No manual `CancellationToken` threading across nested calls
- No concurrency parameter — the tuner discovers the optimum at runtime
- No architecture diagram — the declaration *is* the diagram

---

## 6. API Reference

### `EvalApp.App`

```csharp
public static IAppBuilder EvalApp.App(string name = "Pipeline")
```

Entry point. Creates a new app builder. `name` appears in error messages and telemetry.

---

### `IAppBuilder`

#### `WithResource` — Declare a resource kind

```csharp
IAppBuilder WithResource(ResourceKind kind)
IAppBuilder WithResource(ResourceKind kind, TunableConfig tunable)
IAppBuilder WithResource(ResourceKind kind, int maxConcurrency)
```

Declares a resource kind used by gates in the pipeline. Required before any `.Gate()` call
that references this resource.

#### `WithStepFactory`

```csharp
IAppBuilder WithStepFactory(IStepFactory factory)
```

Registers a custom step factory for DI-based step resolution. Use
`ServiceProviderStepFactory` for ASP.NET Core integration.

#### `WithTuning`

```csharp
IAppBuilder WithTuning(string? storePath = null)
```

Enables the in-memory adaptive concurrency tuner. No effect in unlicensed mode.

#### `WithBayesianTuning`

```csharp
IAppBuilder WithBayesianTuning(string? storePath = null)
```

Enables the persistent adaptive concurrency tuner. No effect in unlicensed mode.

#### `WithWindowBudget`

```csharp
IAppBuilder WithWindowBudget(int cyclesPerSecond)
IAppBuilder WithWindowBudget(string name, int cyclesPerSecond)
```

Registers a time-window budget pressure resource.

#### `DefineDomain`

```csharp
IDomainBuilder DefineDomain(string name)
```

Creates a domain scope. Multiple tasks can share one domain.

#### `Build`

```csharp
void Build(string? licenseKey = null)
```

Validates the license key (if provided) and finalises the app.

---

### `IDomainBuilder`

#### `DefineTask<T>`

```csharp
IEmptyTaskBuilder<T> DefineTask<T>(string name)
```

Creates a new task builder for data type `T`. Returns an `IEmptyTaskBuilder<T>` — `.Run()`
is not yet available until at least one step is added.

---

### `ITaskBuilder<T>` / `IEmptyTaskBuilder<T>`

After the first `.AddStep()`, the builder transitions from `IEmptyTaskBuilder<T>` to
`ITaskBuilder<T>`, unlocking `.Run()`, `.ForEach()`, `.Gate()`, `.BeginSaga()`, and other
methods.

#### `AddStep` — Synchronous

```csharp
ITaskBuilder<T> AddStep(string name, Func<T, T> transform)
```

Pure in-process transformation. No I/O allowed.

#### `AddStep` — Asynchronous

```csharp
ITaskBuilder<T> AddStep(string name, Func<T, CancellationToken, ValueTask<T>> transform)
```

Async transformation. Use for all I/O. Always propagate the `CancellationToken`.

#### `AddStep` — Class-based

```csharp
ITaskBuilder<T> AddStep(string name, IStep<T> instance)
ITaskBuilder<T> AddStep(string name, PureStep<T> step)
ITaskBuilder<T> AddStep(string name, AsyncStep<T> step)
ITaskBuilder<T> AddStep<TStep>(string name) where TStep : class
```

Class-based steps for injected services or complex logic. `AddStep<TStep>` resolves via
the registered `IStepFactory`.

#### `If` — Conditional Branch

```csharp
ITaskBuilder<T> If(
    Func<T, bool>                          predicate,
    Action<IConditionalBranchBuilder<T>>   then,
    Action<IConditionalBranchBuilder<T>>?  @else = null)
```

`else` is optional. Both branches can contain multiple steps, `ForEach`, and nested `If`.

#### `ForEach` — Parallel Fan-Out

```csharp
ITaskBuilder<T> ForEach<TItem>(
    Func<T, IEnumerable<TItem>>       select,
    Func<T, IReadOnlyList<TItem>, T>  merge,
    string                            collectionName,
    Action<ISubTaskBuilder<TItem>>    configure)
```

`select` extracts the collection. `configure` builds the per-item sub-pipeline. `merge`
folds processed items back. Merge order is not guaranteed. Additional overloads accept
`TunableConfig`, `int maxParallelism`, or `ForEachFailureMode`.

#### `Gate` — Resource-Gated Sub-Pipeline

```csharp
ITaskBuilder<T> Gate(ResourceKind kind, Action<T>? onWaiting, Action<ISubTaskBuilder<T>> configure)
ITaskBuilder<T> Gate(string name, TunableConfig tunable, Action<T>? onWaiting, Action<ISubTaskBuilder<T>> configure)
```

Runs the inner steps under a resource semaphore. The resource kind must be declared at the
app level via `.WithResource()`.

#### `WindowBudget`

```csharp
ITaskBuilder<T> WindowBudget(Action<ISubTaskBuilder<T>> configure)
```

Wraps steps in the `"window-budget"` pressure scope. Requires `WithWindowBudget` on the app.

#### `Pressure`

```csharp
ITaskBuilder<T> Pressure(string name, Action<ISubTaskBuilder<T>> configure)
```

Wraps steps in a named pressure scope.

#### `BeginSaga` / `EndSaga`

```csharp
ISagaBuilder<T> BeginSaga(CompensationPolicy policy = CompensationPolicy.BestEffort)
```

Opens a saga scope. Inside the saga, use `.AddStepWithCompensation()` to register
forward and rollback actions. Call `.EndSaga()` to return to `ITaskBuilder<T>`.

#### `Run`

```csharp
IDomainBuilder Run()
IDomainBuilder Run(out ICompiledPipeline<T> pipeline)
```

Captures the compiled pipeline and returns to the domain builder. The `out` overload
assigns the pipeline to a variable for use in your application.

---

### `ICompiledPipeline<T>`

```csharp
string Name { get; }
ValueTask<PipelineResult<T>> RunAsync(T data, CancellationToken ct = default)
```

`RunAsync` is thread-safe and safe to call concurrently. Register the compiled pipeline as a
singleton; never rebuild per request.

---

### `PipelineResult<T>`

```csharp
abstract record PipelineResult<T>
{
    sealed record Success(T Data)                                      : PipelineResult<T>;
    sealed record Failure(T Data, Exception Exception, string? Message): PipelineResult<T>;
    sealed record Skipped(T Data, string Reason)                       : PipelineResult<T>;

    bool IsSuccess { get; }
    bool IsFailure { get; }
    bool IsSkipped { get; }
    T    GetData();  // extracts T regardless of outcome
}
```

---

## 7. Licensing

| Build call | Parallelism | Adaptive Tuner | Cost |
|---|---|---|---|
| `.Build()` | Sequential | None | Free |
| `.Build(licenseKey)` | Parallel | Optional | Licensed |

**Unlicensed mode** runs all steps correctly and in declaration order. Suitable for
development, testing, CI, and low-volume production workloads.

**Licensed mode** enables:
- Parallel `ForEach` with automatic concurrency tuning
- Pre-compiled execution hot path (lower per-run overhead)
- `WithBayesianTuning()` persistent tuner
- Saga compensation with automatic rollback
- Resource gating with tunable concurrency

Contact us for a license key.

---

## 8. FAQ and Common Mistakes

### ❌ Mutating the data record instead of using `with`

```csharp
// ❌ Wrong — records are immutable; this won't compile
d.Price = 99.99m;

// ✅ Correct
return d with { Price = 99.99m };
```

Parallel ForEach steps would race if mutation were possible. Immutability is what makes
parallel pipelines correct without any locking code.

---

### ❌ Storing per-execution state on a step instance

```csharp
// ❌ Wrong — _count is shared across all concurrent runs
public sealed class CountingStep
{
    private int _count = 0;  // shared state — data race
    public OrderData Execute(OrderData d)
    {
        _count++;              // 🔴 race condition
        return d with { Count = _count };
    }
}

// ✅ Correct — per-run state lives in the record
.AddStep("IncrementCount", d => d with { Count = d.Count + 1 })
```

---

### ❌ Building the pipeline per request

```csharp
// ❌ Wrong — discards all tuning state on every request
app.MapPost("/orders", async (OrderRequest req) =>
{
    ICompiledPipeline<OrderData> pipeline;
    EvalApp.App("Orders")
        .DefineDomain("...").DefineTask<OrderData>("...")
            .AddStep(...)
            .Run(out pipeline)
        .Build(key);                       // 🔴 full rebuild, tuner lost
    return await pipeline.RunAsync(...);
});

// ✅ Correct — build once, inject everywhere
services.AddSingleton(sp =>
{
    ICompiledPipeline<OrderData> pipeline;
    EvalApp.App("Orders")
        .DefineDomain("...").DefineTask<OrderData>("...")
            .AddStep(...)
            .Run(out pipeline)
        .Build(key);
    return pipeline;
});
```

---

### ❌ Synchronous I/O inside an `AddStep(name, Func<T, T>)` overload

```csharp
// ❌ Wrong — blocks a thread-pool thread for the entire HTTP round-trip
.AddStep("Fetch", d =>
{
    var response = httpClient.GetAsync(url).Result;  // 🔴 blocking wait
    return d with { Body = response.Content.ReadAsStringAsync().Result };
})

// ✅ Correct — use the async overload
.AddStep("Fetch", async (d, ct) =>
{
    var response = await httpClient.GetAsync(url, ct);
    return d with { Body = await response.Content.ReadAsStringAsync(ct) };
})
```

---

### ❌ Using `Task.WhenAll` inside a step for collection parallelism

```csharp
// ❌ Anti-pattern — bypasses the engine's concurrency model entirely
.AddStep("ProcessAll", async (d, ct) =>
{
    var results = await Task.WhenAll(d.Items.Select(item => ProcessAsync(item, ct)));
    return d with { Results = results };
})

// ✅ Correct — use ForEach; the engine controls and tunes the degree
.ForEach(
    select:         d => d.Items,
    merge:          (d, results) => d with { Results = results.ToArray() },
    collectionName: "ProcessAll",
    configure:      inner => inner.AddStep("Process", async (item, ct) => await ProcessAsync(item, ct)))
```

---

### ❌ Placing items with dependencies inside a ForEach

```csharp
// ❌ Wrong — item B reads item A's result; they are not independent
.ForEach(
    select:         d => d.Items,
    merge:          (d, results) => d with { ... },
    collectionName: "Process",
    configure:      inner => inner
        .AddStep("Compute", (item, ct) => item with { Result = AggregateAcrossItems(d.Items) }))  // 🔴

// ✅ Correct — dependencies go in sequential steps outside ForEach
.AddStep("PreAggregate", d => d with { Aggregate = Aggregate(d.Items) })
.ForEach(
    select:         d => d.Items,
    merge:          (d, results) => d with { ... },
    collectionName: "Process",
    configure:      inner => inner
        .AddStep("Compute", (item, ct) => item with { ... }))
```

---

### ❌ Forgetting to reset a pressure resource at the window boundary

```csharp
// ❌ Wrong — pressure accumulates forever
var result = await pipeline.RunAsync(data, ct);  // on every tick; resource never reset

// ✅ Correct — reset at the start of each window
windowBudgetPressure.Reset();
var result = await pipeline.RunAsync(data, ct);
```

---

### ❌ Not propagating CancellationToken in async steps

```csharp
// ❌ Wrong — request cancellation is ignored
.AddStep("Fetch", async (d, ct) =>
    d with { Data = await httpClient.GetStringAsync(url) })  // missing ct

// ✅ Correct
.AddStep("Fetch", async (d, ct) =>
    d with { Data = await httpClient.GetStringAsync(url, ct) })
```

---

## Roadmap

The following features are planned for future releases:

| Feature | Description |
|---|---|
| **Visualization** | Export the pipeline topology as Mermaid diagrams, ASCII trees, or JSON |
| **Debug mode** | Per-run execution breadcrumb and step timing |
| **GlobalContext / DomainContext** | Typed service injection via `ContextPureStep` base classes |

For retry and timeout policies, use standard .NET `Polly` or
`Microsoft.Extensions.Resilience` inside individual steps.
