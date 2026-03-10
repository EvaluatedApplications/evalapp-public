namespace EvalApp.Consumer;

/// <summary>
/// Base class for pipeline steps whose primary purpose is a side effect — for example,
/// writing to a database, publishing an event, or sending a notification.
/// The step receives the current pipeline data, performs its side effect, and returns
/// the (optionally updated) data to continue the pipeline.
/// </summary>
/// <remarks>
/// Use <see cref="SideEffectStep{T}"/> when the step exists for what it <em>does</em>
/// rather than what it returns. For steps that primarily transform data, prefer
/// <see cref="AsyncStep{T}"/> or <see cref="PureStep{T}"/>.
/// </remarks>
/// <typeparam name="T">The pipeline data record type.</typeparam>
public abstract class SideEffectStep<T> : AsyncStep<T>, IStep<T>
{
    /// <summary>
    /// Declares which resource this step consumes. When set and the step is added via
    /// <c>AddStep&lt;TStep&gt;()</c>, the pipeline builder automatically wraps it in a
    /// <see cref="Gate"/> for the declared resource — preventing accidentally ungated side effects.
    /// </summary>
    /// <remarks>
    /// Returns <see langword="null"/> by default. Override to declare the resource kind:
    /// <code>
    /// public override ResourceKind? ResourceKind => EvalApp.Consumer.ResourceKind.Database;
    /// </code>
    /// </remarks>
    public virtual ResourceKind? ResourceKind => null;

    /// <summary>
    /// Performs the side effect and returns the pipeline data (optionally updated).
    /// Throw to signal failure; the pipeline will capture the exception.
    /// </summary>
    public abstract override ValueTask<T> ExecuteAsync(T data, CancellationToken ct);
}
