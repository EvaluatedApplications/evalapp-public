using EvalAppFluent = global::EvalApp.Fluent;

namespace EvalApp.Consumer.Internal;

internal sealed class SagaBuilder<T> : Consumer.ISagaBuilder<T>
{
    private EvalAppFluent.ISagaBuilder<T> _inner;
    private readonly TaskBuilder<T> _taskBuilder;
    private readonly Consumer.IStepFactory _factory;

    internal SagaBuilder(EvalAppFluent.ISagaBuilder<T> inner, TaskBuilder<T> taskBuilder)
    {
        _inner       = inner;
        _taskBuilder = taskBuilder;
        _factory     = taskBuilder.Factory;
    }

    private Action<EvalAppFluent.ISubTaskBuilder<T>> WrapSub(Action<Consumer.ISubTaskBuilder<T>> action)
        => innerSub => action(new SubTaskBuilder<T>(innerSub, _factory));

    public Consumer.ISagaBuilder<T> AddStep(string name, Func<T, T> transform)
    {
        _inner = _inner.AddStep(name, transform);
        return this;
    }

    public Consumer.ISagaBuilder<T> AddStep(string name, Func<T, CancellationToken, ValueTask<T>> transform)
    {
        _inner = _inner.AddStep(name, transform);
        return this;
    }

    public Consumer.ISagaBuilder<T> AddStep(string name, Consumer.IStep<T> instance)
    {
        _inner = _inner.AddStep(name, new StepAdapter<T>(instance));
        return this;
    }

    public Consumer.ISagaBuilder<T> AddStepWithCompensation(string name, Func<T, T> forward, Func<T, T> compensate)
    {
        _inner = _inner.AddStepWithCompensation(name, forward, compensate);
        return this;
    }

    public Consumer.ISagaBuilder<T> AddStepWithCompensation(
        string name,
        Func<T, CancellationToken, ValueTask<T>> forward,
        Func<T, CancellationToken, ValueTask<T>> compensate)
    {
        _inner = _inner.AddStepWithCompensation(name,
            new AsyncFuncAdapter<T>(forward),
            new AsyncFuncAdapter<T>(compensate));
        return this;
    }

    public Consumer.ISagaBuilder<T> AddGate(
        Consumer.ResourceKind kind, Action<T>? onWaiting,
        Action<Consumer.ISubTaskBuilder<T>> configure,
        Func<T, T>? compensate = null)
    {
        if (compensate is null)
            _inner = _inner.AddGate(kind.ToInternal(), onWaiting, WrapSub(configure));
        else
            _inner = _inner.AddGate(kind.ToInternal(), onWaiting, WrapSub(configure), new SyncFuncAdapter<T>(compensate));
        return this;
    }

    public Consumer.ITaskBuilder<T> EndSaga()
    {
        _inner.EndSaga();
        return _taskBuilder;
    }
}
