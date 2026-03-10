using System.Diagnostics.CodeAnalysis;
using EvalAppFluent = global::EvalApp.Fluent;

namespace EvalApp.Consumer.Internal;

internal sealed class SubTaskBuilder<T> : Consumer.ISubTaskBuilder<T>
{
    private EvalAppFluent.ISubTaskBuilder<T> _inner;
    private readonly Consumer.IStepFactory _factory;

    internal SubTaskBuilder(EvalAppFluent.ISubTaskBuilder<T> inner, Consumer.IStepFactory factory)
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

    public Consumer.ISubTaskBuilder<T> AddStep(string name, Func<T, T> transform)
    {
        _inner = _inner.AddStep(name, transform);
        return this;
    }

    public Consumer.ISubTaskBuilder<T> AddStep(string name, Func<T, CancellationToken, ValueTask<T>> transform)
    {
        _inner = _inner.AddStep(name, transform);
        return this;
    }

    public Consumer.ISubTaskBuilder<T> AddStep(string name, Consumer.IStep<T> instance)
    {
        _inner = _inner.AddStep(name, new StepAdapter<T>(instance));
        return this;
    }

    public Consumer.ISubTaskBuilder<T> AddStep(string name, Consumer.PureStep<T> step)
    {
        _inner = _inner.AddStep(name, new PureStepAdapter<T>(step));
        return this;
    }

    public Consumer.ISubTaskBuilder<T> AddStep(string name, Consumer.AsyncStep<T> step)
    {
        _inner = _inner.AddStep(name, new AsyncStepAdapter<T>(step));
        return this;
    }

    #pragma warning disable CS0618
    public Consumer.ISubTaskBuilder<T> AddStep(string name, Consumer.SideEffectStep<T> step)
        => throw new NotSupportedException(
            $"SideEffectStep '{name}' with ResourceKind={step.ResourceKind} must be added via " +
            $"AddStep<{step.GetType().Name}>() for auto-gating, or wrapped in .Gate().");
    #pragma warning restore CS0618

    [UnconditionalSuppressMessage("AOT", "IL2087", Justification = "TStep is resolved via IStepFactory; DI or Activator handles construction.")]
    public Consumer.ISubTaskBuilder<T> AddStep<TStep>(string name) where TStep : class
    {
        // Context-aware steps implement internal IStep<T> directly — bypass factory + adapter
        if (typeof(global::EvalApp.Abstractions.IStep<T>).IsAssignableFrom(typeof(TStep)))
        {
            var internalStep = (global::EvalApp.Abstractions.IStep<T>)Activator.CreateInstance(typeof(TStep))!;
            _inner = _inner.AddStep(name, internalStep);
            return this;
        }

        // PureStep<T> — create instance and wrap in PureStepAdapter
        if (typeof(Consumer.PureStep<T>).IsAssignableFrom(typeof(TStep)))
        {
            var pureStep = (Consumer.PureStep<T>)Activator.CreateInstance(typeof(TStep))!;
            _inner = _inner.AddStep(name, new PureStepAdapter<T>(pureStep));
            return this;
        }

        // SideEffectStep<T> — create, auto-gate if ResourceKind declared
        if (typeof(Consumer.SideEffectStep<T>).IsAssignableFrom(typeof(TStep)))
        {
            var sideStep = (Consumer.SideEffectStep<T>)Activator.CreateInstance(typeof(TStep))!;
            global::EvalApp.Abstractions.IStep<T> adapted = new AsyncStepAdapter<T>(sideStep);
            if (sideStep.ResourceKind is { } kind)
            {
                _inner = _inner.Gate(kind.ToInternal(), null, g => g.AddStep(name, adapted));
                return this;
            }
            _inner = _inner.AddStep(name, adapted);
            return this;
        }

        // AsyncStep<T> — create and wrap in AsyncStepAdapter
        if (typeof(Consumer.AsyncStep<T>).IsAssignableFrom(typeof(TStep)))
        {
            var asyncStep = (Consumer.AsyncStep<T>)Activator.CreateInstance(typeof(TStep))!;
            global::EvalApp.Abstractions.IStep<T> adapted = new AsyncStepAdapter<T>(asyncStep);
            _inner = _inner.AddStep(name, adapted);
            return this;
        }

        // IStep<T> — use factory
        var step = _factory.Create<T>(typeof(TStep));
        _inner = _inner.AddStep(name, new StepAdapter<T>(step));
        return this;
    }

    public Consumer.ISubTaskBuilder<T> AddStepWithFallback(string name, Func<T, T> primary, Func<T, T> fallback)
    {
        _inner = _inner.AddStep(name, async (T data, CancellationToken ct) =>
        {
            try { return primary(data); }
            catch { return fallback(data); }
        });
        return this;
    }

    public Consumer.ISubTaskBuilder<T> AddStepWithFallback(string name, Consumer.IStep<T> primary, Consumer.IStep<T> fallback)
    {
        _inner = _inner.AddStep(name, async (T data, CancellationToken ct) =>
        {
            try { return await primary.ExecuteAsync(data, ct); }
            catch { return await fallback.ExecuteAsync(data, ct); }
        });
        return this;
    }

    public Consumer.ISubTaskBuilder<T> If(
        Func<T, bool> predicate,
        Action<Consumer.IConditionalBranchBuilder<T>> then,
        Action<Consumer.IConditionalBranchBuilder<T>>? @else = null)
    {
        _inner = _inner.If(predicate, WrapBranch(then), WrapBranchOpt(@else));
        return this;
    }

    public Consumer.ISubTaskBuilder<T> ForEach<TItem>(
        Func<T, IEnumerable<TItem>> select, Func<T, IReadOnlyList<TItem>, T> merge,
        string collectionName, Consumer.TunableConfig parallelism,
        Action<Consumer.ISubTaskBuilder<TItem>> configure)
    {
        throw new NotSupportedException(
            "ForEach inside a SubTask item pipeline is not supported. " +
            "Use Gate or sequential steps instead.");
    }

    public Consumer.ISubTaskBuilder<T> Gate(
        Consumer.ResourceKind kind, Action<T>? onWaiting,
        Action<Consumer.ISubTaskBuilder<T>> configure)
    {
        _inner = _inner.Gate(kind.ToInternal(), onWaiting, WrapSub<T>(configure));
        return this;
    }
}
