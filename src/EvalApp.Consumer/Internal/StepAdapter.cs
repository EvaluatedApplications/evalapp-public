using Abstractions = global::EvalApp.Abstractions;

namespace EvalApp.Consumer.Internal;

/// <summary>Adapts consumer IStep&lt;T&gt; to internal IStep&lt;T&gt;.</summary>
internal sealed class StepAdapter<T>(Consumer.IStep<T> step) : Abstractions.IStep<T>
{
    public async ValueTask<Abstractions.StepResult<T>> ExecuteAsync(T data, Abstractions.StepContext ctx, CancellationToken ct)
    {
        var result = await step.ExecuteAsync(data, ct);
        return new Abstractions.StepResult<T>.Success(result);
    }
}

/// <summary>Adapts PureStep&lt;T&gt; to internal IStep&lt;T&gt;.</summary>
internal sealed class PureStepAdapter<T>(Consumer.PureStep<T> step) : Abstractions.IStep<T>
{
    public ValueTask<Abstractions.StepResult<T>> ExecuteAsync(T data, Abstractions.StepContext ctx, CancellationToken ct)
        => new(new Abstractions.StepResult<T>.Success(step.Execute(data)));
}

/// <summary>Adapts AsyncStep&lt;T&gt; to internal IStep&lt;T&gt;.</summary>
internal sealed class AsyncStepAdapter<T>(Consumer.AsyncStep<T> step) : Abstractions.IStep<T>
{
    public async ValueTask<Abstractions.StepResult<T>> ExecuteAsync(T data, Abstractions.StepContext ctx, CancellationToken ct)
        => new Abstractions.StepResult<T>.Success(await step.ExecuteAsync(data, ct));
}

/// <summary>Adapts Func&lt;T,T&gt; to internal IStep&lt;T&gt; (for compensation steps).</summary>
internal sealed class SyncFuncAdapter<T>(Func<T, T> func) : Abstractions.IStep<T>
{
    public ValueTask<Abstractions.StepResult<T>> ExecuteAsync(T data, Abstractions.StepContext ctx, CancellationToken ct)
        => new(new Abstractions.StepResult<T>.Success(func(data)));
}

/// <summary>Adapts Func&lt;T,CancellationToken,ValueTask&lt;T&gt;&gt; to internal IStep&lt;T&gt;.</summary>
internal sealed class AsyncFuncAdapter<T>(Func<T, CancellationToken, ValueTask<T>> func) : Abstractions.IStep<T>
{
    public async ValueTask<Abstractions.StepResult<T>> ExecuteAsync(T data, Abstractions.StepContext ctx, CancellationToken ct)
        => new Abstractions.StepResult<T>.Success(await func(data, ct));
}
