namespace EvalApp.Consumer;

/// <summary>Task builder AFTER at least one step — Run() is now available.</summary>
public interface ITaskBuilder<T>
{
    ITaskBuilder<T> WithMiddleware(IStepMiddleware<T> middleware);
    ITaskBuilder<T> AddStep(string name, Func<T, T> transform);
    ITaskBuilder<T> AddStep(string name, Func<T, CancellationToken, ValueTask<T>> transform);
    ITaskBuilder<T> AddStep(string name, IStep<T> instance);
    ITaskBuilder<T> AddStep(string name, PureStep<T> step);
    ITaskBuilder<T> AddStep(string name, AsyncStep<T> step);

    /// <summary>Compile-time blocked: SideEffectStep must declare ResourceKind and use AddStep&lt;TStep&gt;() for auto-gating, or be wrapped in .Gate().</summary>
    [Obsolete("SideEffectStep with ResourceKind must be added via AddStep<TStep>() for auto-gating, or wrapped in .Gate(kind, null, g => g.AddStep(...)). Ungated side effects are not allowed.", true)]
    ITaskBuilder<T> AddStep(string name, SideEffectStep<T> step);

    ITaskBuilder<T> AddStep<TStep>(string name) where TStep : class;
    ITaskBuilder<T> AddStepWithFallback(string name, Func<T, T> primary, Func<T, T> fallback);
    ITaskBuilder<T> AddStepWithFallback(string name, IStep<T> primary, IStep<T> fallback);
    ITaskBuilder<T> If(Func<T, bool> predicate, Action<IConditionalBranchBuilder<T>> then, Action<IConditionalBranchBuilder<T>>? @else = null);
    ITaskBuilder<T> AddSubTaskFor<TProp>(Func<T, TProp> select, Func<T, TProp, T> merge, string propertyName, Action<ISubTaskBuilder<TProp>> configure);
    ITaskBuilder<T> ForEach<TItem>(Func<T, IEnumerable<TItem>> select, Func<T, IReadOnlyList<TItem>, T> merge, string collectionName, Action<ISubTaskBuilder<TItem>> configure);
    ITaskBuilder<T> ForEach<TItem>(Func<T, IEnumerable<TItem>> select, Func<T, IReadOnlyList<TItem>, T> merge, string collectionName, int maxParallelism, Action<ISubTaskBuilder<TItem>> configure);
    ITaskBuilder<T> ForEach<TItem>(Func<T, IEnumerable<TItem>> select, Func<T, IReadOnlyList<TItem>, T> merge, string collectionName, Func<int> getMaxParallelism, Action<ISubTaskBuilder<TItem>> configure);
    ITaskBuilder<T> ForEach<TItem>(Func<T, IEnumerable<TItem>> select, Func<T, IReadOnlyList<TItem>, T> merge, string collectionName, TunableConfig parallelism, Action<ISubTaskBuilder<TItem>> configure);
    ITaskBuilder<T> ForEach<TItem>(Func<T, IEnumerable<TItem>> select, Func<T, IReadOnlyList<TItem>, T> merge, string collectionName, TunableConfig parallelism, TunableConfig minItemsForParallel, Action<ISubTaskBuilder<TItem>> configure);
    ITaskBuilder<T> ForEach<TItem>(Func<T, IEnumerable<TItem>> select, Func<T, IReadOnlyList<TItem>, T> merge, string collectionName, int maxParallelism, ForEachFailureMode failureMode, Action<ISubTaskBuilder<TItem>> configure);
    ITaskBuilder<T> ForEach<TItem>(Func<T, IEnumerable<TItem>> select, Func<T, IReadOnlyList<TItem>, T> merge, string collectionName, TunableConfig parallelism, ForEachFailureMode failureMode, Action<ISubTaskBuilder<TItem>> configure);
    ITaskBuilder<T> Gate(string name, TunableConfig tunable, Action<T>? onWaiting, Action<ISubTaskBuilder<T>> configure);
    ITaskBuilder<T> Gate(ResourceKind kind, Action<T>? onWaiting, Action<ISubTaskBuilder<T>> configure);
    ITaskBuilder<T> Pressure(string name, Action<ISubTaskBuilder<T>> configure);
    ITaskBuilder<T> WindowBudget(Action<ISubTaskBuilder<T>> configure);
    ISagaBuilder<T> BeginSaga(CompensationPolicy policy = CompensationPolicy.BestEffort);
    IDomainBuilder Run();
    IDomainBuilder Run(out ICompiledPipeline<T> pipeline);
}
