using EvalAppFluent = global::EvalApp.Fluent;

namespace EvalApp.Consumer.Internal;

internal sealed class ConditionalBranchBuilder<T> : Consumer.IConditionalBranchBuilder<T>
{
    private EvalAppFluent.IConditionalBranchBuilder<T> _inner;
    private readonly Consumer.IStepFactory _factory;

    internal ConditionalBranchBuilder(EvalAppFluent.IConditionalBranchBuilder<T> inner, Consumer.IStepFactory factory)
    {
        _inner   = inner;
        _factory = factory;
    }

    private Action<EvalAppFluent.ISubTaskBuilder<TItem>> WrapSub<TItem>(Action<Consumer.ISubTaskBuilder<TItem>> action)
        => innerSub => action(new SubTaskBuilder<TItem>(innerSub, _factory));

    private Action<EvalAppFluent.IConditionalBranchBuilder<T>> WrapBranch(Action<Consumer.IConditionalBranchBuilder<T>> action)
        => innerBranch => action(new ConditionalBranchBuilder<T>(innerBranch, _factory));

    private Action<EvalAppFluent.IConditionalBranchBuilder<T>>? WrapBranchOpt(Action<Consumer.IConditionalBranchBuilder<T>>? action)
        => action is null ? null : innerBranch => action(new ConditionalBranchBuilder<T>(innerBranch, _factory));

    public Consumer.IConditionalBranchBuilder<T> AddStep(string name, Func<T, T> transform)
    {
        _inner = _inner.AddStep(name, transform);
        return this;
    }

    public Consumer.IConditionalBranchBuilder<T> AddStep(string name, Func<T, CancellationToken, ValueTask<T>> transform)
    {
        _inner = _inner.AddStep(name, transform);
        return this;
    }

    public Consumer.IConditionalBranchBuilder<T> AddStep(string name, Consumer.IStep<T> instance)
    {
        _inner = _inner.AddStep(name, new StepAdapter<T>(instance));
        return this;
    }

    public Consumer.IConditionalBranchBuilder<T> AddStep(string name, Consumer.PureStep<T> step)
    {
        _inner = _inner.AddStep(name, new PureStepAdapter<T>(step));
        return this;
    }

    public Consumer.IConditionalBranchBuilder<T> AddStep(string name, Consumer.AsyncStep<T> step)
    {
        _inner = _inner.AddStep(name, new AsyncStepAdapter<T>(step));
        return this;
    }

    #pragma warning disable CS0618
    public Consumer.IConditionalBranchBuilder<T> AddStep(string name, Consumer.SideEffectStep<T> step)
        => throw new NotSupportedException(
            $"SideEffectStep '{name}' with ResourceKind={step.ResourceKind} must be added via " +
            $"AddStep<TStep>() for auto-gating, or wrapped in .Gate().");
    #pragma warning restore CS0618

    public Consumer.IConditionalBranchBuilder<T> ForEach<TItem>(
        Func<T, IEnumerable<TItem>> select, Func<T, IReadOnlyList<TItem>, T> merge,
        string collectionName, Consumer.TunableConfig parallelism,
        Action<Consumer.ISubTaskBuilder<TItem>> configure)
    {
        _inner = _inner.ForEach(select, merge, collectionName, parallelism.ToInternal(), WrapSub<TItem>(configure));
        return this;
    }

    public Consumer.IConditionalBranchBuilder<T> Gate(
        Consumer.ResourceKind kind, Action<T>? onWaiting,
        Action<Consumer.ISubTaskBuilder<T>> configure)
    {
        _inner = _inner.Gate(kind.ToInternal(), onWaiting, WrapSub<T>(configure));
        return this;
    }

    public Consumer.IConditionalBranchBuilder<T> If(
        Func<T, bool> predicate,
        Action<Consumer.IConditionalBranchBuilder<T>> then,
        Action<Consumer.IConditionalBranchBuilder<T>>? @else = null)
    {
        _inner = _inner.If(predicate, WrapBranch(then), WrapBranchOpt(@else));
        return this;
    }
}
