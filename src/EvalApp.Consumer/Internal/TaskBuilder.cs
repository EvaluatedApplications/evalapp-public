using System.Diagnostics.CodeAnalysis;
using EvalAppFluent = global::EvalApp.Fluent;
using EvalAppCore = global::EvalApp.Core;

namespace EvalApp.Consumer.Internal;

/// <summary>
/// Consumer task builder — implements both IEmptyTaskBuilder&lt;T&gt; and ITaskBuilder&lt;T&gt;.
/// Holds the internal empty builder until the first step is added, then transitions to
/// holding the internal task builder.
/// </summary>
internal sealed class TaskBuilder<T> : Consumer.IEmptyTaskBuilder<T>, Consumer.ITaskBuilder<T>
{
    private EvalAppFluent.IEmptyTaskBuilder<T>? _emptyInner;
    private EvalAppFluent.ITaskBuilder<T>? _taskInner;
    private readonly DomainBuilder _domainBuilder;
    private readonly Consumer.IStepFactory _factory;

    internal TaskBuilder(EvalAppFluent.IEmptyTaskBuilder<T> emptyInner, DomainBuilder domainBuilder)
    {
        _emptyInner    = emptyInner;
        _domainBuilder = domainBuilder;
        _factory       = domainBuilder.Factory;
    }

    internal Consumer.IStepFactory Factory => _factory;

    // ── Transition helper ────────────────────────────────────────────────────────

    private Consumer.ITaskBuilder<T> Transition(EvalAppFluent.ITaskBuilder<T> newInner)
    {
        _taskInner  = newInner;
        _emptyInner = null;
        return this;
    }

    // ── Action wrappers ──────────────────────────────────────────────────────────

    private Action<EvalAppFluent.ISubTaskBuilder<TItem>> WrapSub<TItem>(Action<Consumer.ISubTaskBuilder<TItem>> action)
        => innerSub => action(new SubTaskBuilder<TItem>(innerSub, _factory));

    private Action<EvalAppFluent.IConditionalBranchBuilder<T>> WrapBranch(Action<Consumer.IConditionalBranchBuilder<T>> action)
        => innerBranch => action(new ConditionalBranchBuilder<T>(innerBranch, _factory));

    private Action<EvalAppFluent.IConditionalBranchBuilder<T>>? WrapBranchOpt(Action<Consumer.IConditionalBranchBuilder<T>>? action)
        => action is null ? null : innerBranch => action(new ConditionalBranchBuilder<T>(innerBranch, _factory));

    // ── IEmptyTaskBuilder<T> ─────────────────────────────────────────────────────

    Consumer.IEmptyTaskBuilder<T> Consumer.IEmptyTaskBuilder<T>.WithMiddleware(Consumer.IStepMiddleware<T> middleware)
    {
        _emptyInner = _emptyInner!.WithMiddleware(new MiddlewareAdapter<T>(middleware));
        return this;
    }

    Consumer.ITaskBuilder<T> Consumer.IEmptyTaskBuilder<T>.AddStep(string name, Func<T, T> transform)
        => Transition(_emptyInner!.AddStep(name, transform));

    Consumer.ITaskBuilder<T> Consumer.IEmptyTaskBuilder<T>.AddStep(string name, Func<T, CancellationToken, ValueTask<T>> transform)
        => Transition(_emptyInner!.AddStep(name, transform));

    Consumer.ITaskBuilder<T> Consumer.IEmptyTaskBuilder<T>.AddStep(string name, Consumer.IStep<T> instance)
        => Transition(_emptyInner!.AddStep(name, new StepAdapter<T>(instance)));

    Consumer.ITaskBuilder<T> Consumer.IEmptyTaskBuilder<T>.AddStep(string name, Consumer.PureStep<T> step)
        => Transition(_emptyInner!.AddStep(name, new PureStepAdapter<T>(step)));

    Consumer.ITaskBuilder<T> Consumer.IEmptyTaskBuilder<T>.AddStep(string name, Consumer.AsyncStep<T> step)
        => Transition(_emptyInner!.AddStep(name, new AsyncStepAdapter<T>(step)));

    #pragma warning disable CS0618 // Obsolete — required for interface implementation
    Consumer.ITaskBuilder<T> Consumer.IEmptyTaskBuilder<T>.AddStep(string name, Consumer.SideEffectStep<T> step)
        => throw new NotSupportedException(
            $"SideEffectStep '{name}' with ResourceKind={step.ResourceKind} must be added via " +
            $"AddStep<{step.GetType().Name}>() for auto-gating, or wrapped in .Gate().");
    #pragma warning restore CS0618

    [UnconditionalSuppressMessage("AOT", "IL2087", Justification = "TStep is resolved via IStepFactory; DI or Activator handles construction.")]
    Consumer.ITaskBuilder<T> Consumer.IEmptyTaskBuilder<T>.AddStep<TStep>(string name)
    {
        // Context-aware steps implement internal IStep<T> directly — bypass factory + adapter
        if (typeof(global::EvalApp.Abstractions.IStep<T>).IsAssignableFrom(typeof(TStep)))
        {
            var internalStep = (global::EvalApp.Abstractions.IStep<T>)Activator.CreateInstance(typeof(TStep))!;
            return Transition(_emptyInner!.AddStep(name, internalStep));
        }

        // PureStep<T> — create instance and wrap in PureStepAdapter
        if (typeof(Consumer.PureStep<T>).IsAssignableFrom(typeof(TStep)))
        {
            var pureStep = (Consumer.PureStep<T>)Activator.CreateInstance(typeof(TStep))!;
            return Transition(_emptyInner!.AddStep(name, new PureStepAdapter<T>(pureStep)));
        }

        // SideEffectStep<T> — create, auto-gate if ResourceKind declared, wrap in AsyncStepAdapter
        if (typeof(Consumer.SideEffectStep<T>).IsAssignableFrom(typeof(TStep)))
        {
            var sideStep = (Consumer.SideEffectStep<T>)Activator.CreateInstance(typeof(TStep))!;
            global::EvalApp.Abstractions.IStep<T> adapted = new AsyncStepAdapter<T>(sideStep);
            if (sideStep.ResourceKind is { } kind)
                return Transition(_emptyInner!.Gate(
                    kind.ToInternal(), null,
                    g => g.AddStep(name, adapted)));
            return Transition(_emptyInner!.AddStep(name, adapted));
        }

        // AsyncStep<T> — create and wrap in AsyncStepAdapter
        if (typeof(Consumer.AsyncStep<T>).IsAssignableFrom(typeof(TStep)))
        {
            var asyncStep = (Consumer.AsyncStep<T>)Activator.CreateInstance(typeof(TStep))!;
            global::EvalApp.Abstractions.IStep<T> adapted = new AsyncStepAdapter<T>(asyncStep);
            return Transition(_emptyInner!.AddStep(name, adapted));
        }

        // IStep<T> — use factory
        var step = _factory.Create<T>(typeof(TStep));
        return Transition(_emptyInner!.AddStep(name, new StepAdapter<T>(step)));
    }

    Consumer.ITaskBuilder<T> Consumer.IEmptyTaskBuilder<T>.If(
        Func<T, bool> predicate,
        Action<Consumer.IConditionalBranchBuilder<T>> then,
        Action<Consumer.IConditionalBranchBuilder<T>>? @else)
        => Transition(_emptyInner!.If(predicate, WrapBranch(then), WrapBranchOpt(@else)));

    // ── ITaskBuilder<T> ──────────────────────────────────────────────────────────

    Consumer.ITaskBuilder<T> Consumer.ITaskBuilder<T>.WithMiddleware(Consumer.IStepMiddleware<T> middleware)
    {
        _taskInner = _taskInner!.WithMiddleware(new MiddlewareAdapter<T>(middleware));
        return this;
    }

    Consumer.ITaskBuilder<T> Consumer.ITaskBuilder<T>.AddStep(string name, Func<T, T> transform)
    {
        _taskInner = _taskInner!.AddStep(name, transform);
        return this;
    }

    Consumer.ITaskBuilder<T> Consumer.ITaskBuilder<T>.AddStep(string name, Func<T, CancellationToken, ValueTask<T>> transform)
    {
        _taskInner = _taskInner!.AddStep(name, transform);
        return this;
    }

    Consumer.ITaskBuilder<T> Consumer.ITaskBuilder<T>.AddStep(string name, Consumer.IStep<T> instance)
    {
        _taskInner = _taskInner!.AddStep(name, new StepAdapter<T>(instance));
        return this;
    }

    Consumer.ITaskBuilder<T> Consumer.ITaskBuilder<T>.AddStep(string name, Consumer.PureStep<T> step)
    {
        _taskInner = _taskInner!.AddStep(name, new PureStepAdapter<T>(step));
        return this;
    }

    Consumer.ITaskBuilder<T> Consumer.ITaskBuilder<T>.AddStep(string name, Consumer.AsyncStep<T> step)
    {
        _taskInner = _taskInner!.AddStep(name, new AsyncStepAdapter<T>(step));
        return this;
    }

    #pragma warning disable CS0618
    Consumer.ITaskBuilder<T> Consumer.ITaskBuilder<T>.AddStep(string name, Consumer.SideEffectStep<T> step)
        => throw new NotSupportedException(
            $"SideEffectStep '{name}' with ResourceKind={step.ResourceKind} must be added via " +
            $"AddStep<{step.GetType().Name}>() for auto-gating, or wrapped in .Gate().");
    #pragma warning restore CS0618

    [UnconditionalSuppressMessage("AOT", "IL2087", Justification = "TStep is resolved via IStepFactory; DI or Activator handles construction.")]
    Consumer.ITaskBuilder<T> Consumer.ITaskBuilder<T>.AddStep<TStep>(string name)
    {
        // Context-aware steps implement internal IStep<T> directly — bypass factory + adapter
        if (typeof(global::EvalApp.Abstractions.IStep<T>).IsAssignableFrom(typeof(TStep)))
        {
            var internalStep = (global::EvalApp.Abstractions.IStep<T>)Activator.CreateInstance(typeof(TStep))!;
            _taskInner = _taskInner!.AddStep(name, internalStep);
            return this;
        }

        // PureStep<T> — create instance and wrap in PureStepAdapter
        if (typeof(Consumer.PureStep<T>).IsAssignableFrom(typeof(TStep)))
        {
            var pureStep = (Consumer.PureStep<T>)Activator.CreateInstance(typeof(TStep))!;
            _taskInner = _taskInner!.AddStep(name, new PureStepAdapter<T>(pureStep));
            return this;
        }

        // SideEffectStep<T> — create, auto-gate if ResourceKind declared, wrap in AsyncStepAdapter
        if (typeof(Consumer.SideEffectStep<T>).IsAssignableFrom(typeof(TStep)))
        {
            var sideStep = (Consumer.SideEffectStep<T>)Activator.CreateInstance(typeof(TStep))!;
            global::EvalApp.Abstractions.IStep<T> adapted = new AsyncStepAdapter<T>(sideStep);
            if (sideStep.ResourceKind is { } kind)
            {
                _taskInner = _taskInner!.Gate(kind.ToInternal(), null, g => g.AddStep(name, adapted));
                return this;
            }
            _taskInner = _taskInner!.AddStep(name, adapted);
            return this;
        }

        // AsyncStep<T> — create and wrap in AsyncStepAdapter
        if (typeof(Consumer.AsyncStep<T>).IsAssignableFrom(typeof(TStep)))
        {
            var asyncStep = (Consumer.AsyncStep<T>)Activator.CreateInstance(typeof(TStep))!;
            global::EvalApp.Abstractions.IStep<T> adapted = new AsyncStepAdapter<T>(asyncStep);
            _taskInner = _taskInner!.AddStep(name, adapted);
            return this;
        }

        // IStep<T> — use factory
        var step = _factory.Create<T>(typeof(TStep));
        _taskInner = _taskInner!.AddStep(name, new StepAdapter<T>(step));
        return this;
    }

    Consumer.ITaskBuilder<T> Consumer.ITaskBuilder<T>.AddStepWithFallback(string name, Func<T, T> primary, Func<T, T> fallback)
    {
        _taskInner = _taskInner!.AddStepWithFallback(name, primary, fallback);
        return this;
    }

    Consumer.ITaskBuilder<T> Consumer.ITaskBuilder<T>.AddStepWithFallback(string name, Consumer.IStep<T> primary, Consumer.IStep<T> fallback)
    {
        _taskInner = _taskInner!.AddStepWithFallback(name, new StepAdapter<T>(primary), new StepAdapter<T>(fallback));
        return this;
    }

    Consumer.ITaskBuilder<T> Consumer.ITaskBuilder<T>.If(
        Func<T, bool> predicate,
        Action<Consumer.IConditionalBranchBuilder<T>> then,
        Action<Consumer.IConditionalBranchBuilder<T>>? @else)
    {
        _taskInner = _taskInner!.If(predicate, WrapBranch(then), WrapBranchOpt(@else));
        return this;
    }

    Consumer.ITaskBuilder<T> Consumer.ITaskBuilder<T>.AddSubTaskFor<TProp>(
        Func<T, TProp> select, Func<T, TProp, T> merge, string propertyName,
        Action<Consumer.ISubTaskBuilder<TProp>> configure)
    {
        _taskInner = _taskInner!.AddSubTaskFor(select, merge, propertyName, WrapSub<TProp>(configure));
        return this;
    }

    Consumer.ITaskBuilder<T> Consumer.ITaskBuilder<T>.ForEach<TItem>(
        Func<T, IEnumerable<TItem>> select, Func<T, IReadOnlyList<TItem>, T> merge,
        string collectionName, Action<Consumer.ISubTaskBuilder<TItem>> configure)
    {
        _taskInner = _taskInner!.ForEach(select, merge, collectionName, WrapSub<TItem>(configure));
        return this;
    }

    Consumer.ITaskBuilder<T> Consumer.ITaskBuilder<T>.ForEach<TItem>(
        Func<T, IEnumerable<TItem>> select, Func<T, IReadOnlyList<TItem>, T> merge,
        string collectionName, int maxParallelism, Action<Consumer.ISubTaskBuilder<TItem>> configure)
    {
        _taskInner = _taskInner!.ForEach(select, merge, collectionName, maxParallelism, WrapSub<TItem>(configure));
        return this;
    }

    Consumer.ITaskBuilder<T> Consumer.ITaskBuilder<T>.ForEach<TItem>(
        Func<T, IEnumerable<TItem>> select, Func<T, IReadOnlyList<TItem>, T> merge,
        string collectionName, Func<int> getMaxParallelism, Action<Consumer.ISubTaskBuilder<TItem>> configure)
    {
        _taskInner = _taskInner!.ForEach(select, merge, collectionName, getMaxParallelism, WrapSub<TItem>(configure));
        return this;
    }

    Consumer.ITaskBuilder<T> Consumer.ITaskBuilder<T>.ForEach<TItem>(
        Func<T, IEnumerable<TItem>> select, Func<T, IReadOnlyList<TItem>, T> merge,
        string collectionName, Consumer.TunableConfig parallelism, Action<Consumer.ISubTaskBuilder<TItem>> configure)
    {
        _taskInner = _taskInner!.ForEach(select, merge, collectionName, parallelism.ToInternal(), WrapSub<TItem>(configure));
        return this;
    }

    Consumer.ITaskBuilder<T> Consumer.ITaskBuilder<T>.ForEach<TItem>(
        Func<T, IEnumerable<TItem>> select, Func<T, IReadOnlyList<TItem>, T> merge,
        string collectionName, Consumer.TunableConfig parallelism, Consumer.TunableConfig minItemsForParallel,
        Action<Consumer.ISubTaskBuilder<TItem>> configure)
    {
        _taskInner = _taskInner!.ForEach(select, merge, collectionName, parallelism.ToInternal(), minItemsForParallel.ToInternal(), WrapSub<TItem>(configure));
        return this;
    }

    Consumer.ITaskBuilder<T> Consumer.ITaskBuilder<T>.ForEach<TItem>(
        Func<T, IEnumerable<TItem>> select, Func<T, IReadOnlyList<TItem>, T> merge,
        string collectionName, int maxParallelism, Consumer.ForEachFailureMode failureMode,
        Action<Consumer.ISubTaskBuilder<TItem>> configure)
    {
        _taskInner = _taskInner!.ForEach(select, merge, collectionName, maxParallelism, failureMode.ToInternal(), WrapSub<TItem>(configure));
        return this;
    }

    Consumer.ITaskBuilder<T> Consumer.ITaskBuilder<T>.ForEach<TItem>(
        Func<T, IEnumerable<TItem>> select, Func<T, IReadOnlyList<TItem>, T> merge,
        string collectionName, Consumer.TunableConfig parallelism, Consumer.ForEachFailureMode failureMode,
        Action<Consumer.ISubTaskBuilder<TItem>> configure)
    {
        _taskInner = _taskInner!.ForEach(select, merge, collectionName, parallelism.ToInternal(), failureMode.ToInternal(), WrapSub<TItem>(configure));
        return this;
    }

    Consumer.ITaskBuilder<T> Consumer.ITaskBuilder<T>.Gate(
        string name, Consumer.TunableConfig tunable, Action<T>? onWaiting,
        Action<Consumer.ISubTaskBuilder<T>> configure)
    {
        _taskInner = _taskInner!.Gate(name, tunable.ToInternal(), onWaiting, WrapSub<T>(configure));
        return this;
    }

    Consumer.ITaskBuilder<T> Consumer.ITaskBuilder<T>.Gate(
        Consumer.ResourceKind kind, Action<T>? onWaiting,
        Action<Consumer.ISubTaskBuilder<T>> configure)
    {
        _taskInner = _taskInner!.Gate(kind.ToInternal(), onWaiting, WrapSub<T>(configure));
        return this;
    }

    Consumer.ITaskBuilder<T> Consumer.ITaskBuilder<T>.Pressure(string name, Action<Consumer.ISubTaskBuilder<T>> configure)
    {
        _taskInner = _taskInner!.Pressure(name, WrapSub<T>(configure));
        return this;
    }

    Consumer.ITaskBuilder<T> Consumer.ITaskBuilder<T>.WindowBudget(Action<Consumer.ISubTaskBuilder<T>> configure)
    {
        _taskInner = _taskInner!.WindowBudget(WrapSub<T>(configure));
        return this;
    }

    Consumer.ISagaBuilder<T> Consumer.ITaskBuilder<T>.BeginSaga(Consumer.CompensationPolicy policy)
    {
        var innerSaga = _taskInner!.BeginSaga(policy.ToInternal());
        return new SagaBuilder<T>(innerSaga, this);
    }

    Consumer.IDomainBuilder Consumer.ITaskBuilder<T>.Run()
    {
        _taskInner!.Run();
        return _domainBuilder;
    }

    Consumer.IDomainBuilder Consumer.ITaskBuilder<T>.Run(out Consumer.ICompiledPipeline<T> pipeline)
    {
        _taskInner!.Run(out EvalAppCore.Pipeline<T> internalPipeline);
        pipeline = new CompiledPipeline<T>(internalPipeline);
        return _domainBuilder;
    }
}
