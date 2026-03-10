using InternalAbstractions = global::EvalApp.Abstractions;

namespace EvalApp.Consumer;

/// <summary>
/// Context-aware side-effect step. Override <see cref="ExecuteAsync"/> to access global and domain contexts.
/// Use for I/O-bound work that needs domain services.
/// </summary>
public abstract class ContextSideEffectStep<TGlobal, TDomain, T> : InternalAbstractions.IStep<T>, InternalAbstractions.IContextSettable
    where TGlobal : GlobalContext
    where TDomain : DomainContext
{
    public TGlobal Global { get; private set; } = default!;
    public TDomain Domain { get; private set; } = default!;

    void InternalAbstractions.IContextSettable.SetContextUntyped(object global, object domain)
    {
        Global = (TGlobal)global;
        Domain = (TDomain)domain;
    }

    protected abstract ValueTask<T> ExecuteAsync(
        T data, TGlobal global, TDomain domain, CancellationToken ct);

    async ValueTask<InternalAbstractions.StepResult<T>> InternalAbstractions.IStep<T>.ExecuteAsync(
        T data, InternalAbstractions.StepContext context, CancellationToken ct)
    {
        try
        {
            var result = await ExecuteAsync(data, Global, Domain, ct);
            return new InternalAbstractions.StepResult<T>.Success(result);
        }
        catch (Exception ex)
        {
            return new InternalAbstractions.StepResult<T>.Failure(data, ex);
        }
    }
}
