namespace EvalApp.Consumer;

/// <summary>Saga builder — collects forward steps with optional compensation.</summary>
public interface ISagaBuilder<T>
{
    ISagaBuilder<T> AddStep(string name, Func<T, T> transform);
    ISagaBuilder<T> AddStep(string name, Func<T, CancellationToken, ValueTask<T>> transform);
    ISagaBuilder<T> AddStep(string name, IStep<T> instance);
    ISagaBuilder<T> AddStepWithCompensation(string name, Func<T, T> forward, Func<T, T> compensate);
    ISagaBuilder<T> AddStepWithCompensation(string name, Func<T, CancellationToken, ValueTask<T>> forward, Func<T, CancellationToken, ValueTask<T>> compensate);
    ISagaBuilder<T> AddGate(ResourceKind kind, Action<T>? onWaiting, Action<ISubTaskBuilder<T>> configure, Func<T, T>? compensate = null);
    ITaskBuilder<T> EndSaga();
}
