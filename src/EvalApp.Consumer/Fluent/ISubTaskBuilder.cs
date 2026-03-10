namespace EvalApp.Consumer;

/// <summary>Sub-task builder for ForEach items or property sub-pipelines.</summary>
public interface ISubTaskBuilder<T>
{
    ISubTaskBuilder<T> AddStep(string name, Func<T, T> transform);
    ISubTaskBuilder<T> AddStep(string name, Func<T, CancellationToken, ValueTask<T>> transform);
    ISubTaskBuilder<T> AddStep(string name, IStep<T> instance);
    ISubTaskBuilder<T> AddStep(string name, PureStep<T> step);
    ISubTaskBuilder<T> AddStep(string name, AsyncStep<T> step);

    /// <summary>Compile-time blocked: SideEffectStep must declare ResourceKind and use AddStep&lt;TStep&gt;() for auto-gating, or be wrapped in .Gate().</summary>
    [Obsolete("SideEffectStep with ResourceKind must be added via AddStep<TStep>() for auto-gating, or wrapped in .Gate(kind, null, g => g.AddStep(...)). Ungated side effects are not allowed.", true)]
    ISubTaskBuilder<T> AddStep(string name, SideEffectStep<T> step);

    ISubTaskBuilder<T> AddStep<TStep>(string name) where TStep : class;
    ISubTaskBuilder<T> AddStepWithFallback(string name, Func<T, T> primary, Func<T, T> fallback);
    ISubTaskBuilder<T> AddStepWithFallback(string name, IStep<T> primary, IStep<T> fallback);
    ISubTaskBuilder<T> If(Func<T, bool> predicate, Action<IConditionalBranchBuilder<T>> then, Action<IConditionalBranchBuilder<T>>? @else = null);
    ISubTaskBuilder<T> ForEach<TItem>(Func<T, IEnumerable<TItem>> select, Func<T, IReadOnlyList<TItem>, T> merge, string collectionName, TunableConfig parallelism, Action<ISubTaskBuilder<TItem>> configure);
    ISubTaskBuilder<T> Gate(ResourceKind kind, Action<T>? onWaiting, Action<ISubTaskBuilder<T>> configure);
}
