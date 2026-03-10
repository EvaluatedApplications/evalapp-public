namespace EvalApp.Consumer;

/// <summary>
/// Base class for an asynchronous pipeline step.
/// Override <see cref="ExecuteAsync"/> to transform the data record.
/// Use for I/O-bound work: network calls, database reads/writes, file access.
/// </summary>
/// <example>
/// <code>
/// public class FetchPriceStep : AsyncStep&lt;OrderData&gt;
/// {
///     private readonly ICatalog _catalog;
///     public FetchPriceStep(ICatalog catalog) => _catalog = catalog;
///
///     public override async ValueTask&lt;OrderData&gt; ExecuteAsync(OrderData data, CancellationToken ct)
///     {
///         var price = await _catalog.GetPrice(data.Sku, ct);
///         return data with { Price = price };
///     }
/// }
///
/// EvalApp.App("Orders")
///     .DefineDomain("Processing")
///         .DefineTask&lt;OrderData&gt;("ProcessOrder")
///             .AddStep("FetchPrice", new FetchPriceStep(catalog))
///             .Run(out var pipeline)
///         .Build();
/// </code>
/// </example>
public abstract class AsyncStep<T>
{
    /// <summary>
    /// Asynchronously transform <paramref name="data"/> and return the updated record.
    /// Propagate <paramref name="ct"/> to every awaited call.
    /// </summary>
    public abstract ValueTask<T> ExecuteAsync(T data, CancellationToken ct);
}
