namespace EvalApp.Consumer;

/// <summary>
/// Base class for pipeline steps that read from or write to state shared with another pipeline or domain.
/// </summary>
/// <remarks>
/// <para>
/// Use <see cref="CrossDomainStep{T,TShared}"/> when a step needs to communicate across pipeline boundaries —
/// for example, writing a result to a shared store that a sibling pipeline reads, or publishing
/// to a shared event channel.
/// </para>
/// <para>
/// The shared state object is injected via the constructor and exposed as <see cref="Shared"/>.
/// The type of <typeparamref name="TShared"/> is unrestricted — use whatever abstraction fits
/// your cross-domain contract (a service interface, a concurrent collection, an event bus, etc.).
/// </para>
/// <example>
/// <code>
/// public class PublishOrderEventStep : CrossDomainStep&lt;OrderData, IOrderEventBus&gt;
/// {
///     public PublishOrderEventStep(IOrderEventBus bus) : base(bus) { }
///
///     public override async ValueTask&lt;OrderData&gt; ExecuteAsync(OrderData data, CancellationToken ct)
///     {
///         await Shared.PublishAsync(new OrderPlaced(data.OrderId), ct);
///         return data with { EventPublished = true };
///     }
/// }
/// </code>
/// </example>
/// </remarks>
/// <typeparam name="T">The pipeline data record type.</typeparam>
/// <typeparam name="TShared">The type of the shared state or service bridging the two domains.</typeparam>
public abstract class CrossDomainStep<T, TShared> : SideEffectStep<T>
{
    /// <summary>The shared state or service injected at construction time.</summary>
    protected TShared Shared { get; }

    /// <summary>
    /// Initialises the step with the shared state object that bridges this pipeline to another domain.
    /// </summary>
    /// <param name="shared">The shared state or service. Must not be null.</param>
    protected CrossDomainStep(TShared shared)
    {
        Shared = shared ?? throw new ArgumentNullException(nameof(shared));
    }

    /// <summary>
    /// Performs the cross-domain interaction and returns the (optionally updated) pipeline data.
    /// Throw to signal failure; the pipeline will capture the exception.
    /// </summary>
    public abstract override ValueTask<T> ExecuteAsync(T data, CancellationToken ct);
}
