namespace EvalApp.Consumer;

/// <summary>
/// Consumer middleware interface. Wraps step execution for cross-cutting concerns
/// (audit, retry, timing, etc.).
/// </summary>
public interface IStepMiddleware<T>
{
    ValueTask<PipelineResult<T>> ExecuteAsync(
        T data,
        Func<T, CancellationToken, ValueTask<PipelineResult<T>>> next,
        CancellationToken ct = default);
}
