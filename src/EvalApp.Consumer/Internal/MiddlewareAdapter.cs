using Abstractions = global::EvalApp.Abstractions;

namespace EvalApp.Consumer.Internal;

internal sealed class MiddlewareAdapter<T>(Consumer.IStepMiddleware<T> middleware) : Abstractions.IStepMiddleware<T>
{
    public async ValueTask<Abstractions.StepResult<T>> ExecuteAsync(
        T data,
        Abstractions.StepContext context,
        Abstractions.StepInfo stepInfo,
        Func<T, ValueTask<Abstractions.StepResult<T>>> next,
        CancellationToken ct)
    {
        async ValueTask<PipelineResult<T>> ConsumerNext(T d, CancellationToken token)
        {
            var r = await next(d);
            return PipelineResult<T>.From(r);
        }

        var result = await middleware.ExecuteAsync(data, ConsumerNext, ct);
        return result switch
        {
            PipelineResult<T>.Success s  => new Abstractions.StepResult<T>.Success(s.Data),
            PipelineResult<T>.Failure f  => new Abstractions.StepResult<T>.Failure(f.Data, f.Exception, f.Message),
            PipelineResult<T>.Skipped sk => new Abstractions.StepResult<T>.Skipped(sk.Data, sk.Reason),
            _                            => throw new InvalidOperationException("Unknown PipelineResult variant")
        };
    }
}
