namespace EvalApp.Consumer;

/// <summary>Task builder BEFORE any steps are added. Run() is not available yet.</summary>
public interface IEmptyTaskBuilder<T>
{
    IEmptyTaskBuilder<T> WithMiddleware(IStepMiddleware<T> middleware);
    ITaskBuilder<T> AddStep(string name, Func<T, T> transform);
    ITaskBuilder<T> AddStep(string name, Func<T, CancellationToken, ValueTask<T>> transform);
    ITaskBuilder<T> AddStep(string name, IStep<T> instance);
    ITaskBuilder<T> AddStep(string name, PureStep<T> step);
    ITaskBuilder<T> AddStep(string name, AsyncStep<T> step);

    /// <summary>Compile-time blocked: SideEffectStep must declare ResourceKind and use AddStep&lt;TStep&gt;() for auto-gating, or be wrapped in .Gate().</summary>
    [Obsolete("SideEffectStep with ResourceKind must be added via AddStep<TStep>() for auto-gating, or wrapped in .Gate(kind, null, g => g.AddStep(...)). Ungated side effects are not allowed.", true)]
    ITaskBuilder<T> AddStep(string name, SideEffectStep<T> step);

    ITaskBuilder<T> AddStep<TStep>(string name) where TStep : class;
    ITaskBuilder<T> If(Func<T, bool> predicate, Action<IConditionalBranchBuilder<T>> then, Action<IConditionalBranchBuilder<T>>? @else = null);
}
