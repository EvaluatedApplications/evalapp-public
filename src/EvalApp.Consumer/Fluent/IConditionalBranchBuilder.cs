namespace EvalApp.Consumer;

/// <summary>Branch builder used inside .If() — collects steps for a conditional branch.</summary>
public interface IConditionalBranchBuilder<T>
{
    IConditionalBranchBuilder<T> AddStep(string name, Func<T, T> transform);
    IConditionalBranchBuilder<T> AddStep(string name, Func<T, CancellationToken, ValueTask<T>> transform);
    IConditionalBranchBuilder<T> AddStep(string name, IStep<T> instance);
    IConditionalBranchBuilder<T> AddStep(string name, PureStep<T> step);
    IConditionalBranchBuilder<T> AddStep(string name, AsyncStep<T> step);

    /// <summary>Compile-time blocked: SideEffectStep must declare ResourceKind and use AddStep&lt;TStep&gt;() for auto-gating, or be wrapped in .Gate().</summary>
    [Obsolete("SideEffectStep with ResourceKind must be added via AddStep<TStep>() for auto-gating, or wrapped in .Gate(kind, null, g => g.AddStep(...)). Ungated side effects are not allowed.", true)]
    IConditionalBranchBuilder<T> AddStep(string name, SideEffectStep<T> step);
    IConditionalBranchBuilder<T> ForEach<TItem>(Func<T, IEnumerable<TItem>> select, Func<T, IReadOnlyList<TItem>, T> merge, string collectionName, TunableConfig parallelism, Action<ISubTaskBuilder<TItem>> configure);
    IConditionalBranchBuilder<T> Gate(ResourceKind kind, Action<T>? onWaiting, Action<ISubTaskBuilder<T>> configure);
    IConditionalBranchBuilder<T> If(Func<T, bool> predicate, Action<IConditionalBranchBuilder<T>> then, Action<IConditionalBranchBuilder<T>>? @else = null);
}
