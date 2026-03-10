namespace EvalApp.Consumer;

/// <summary>
/// Consumer step interface. Returns the transformed data directly.
/// Use PureStep&lt;T&gt; or AsyncStep&lt;T&gt; as base classes for convenience.
/// </summary>
public interface IStep<T>
{
    ValueTask<T> ExecuteAsync(T data, CancellationToken ct = default);
}
