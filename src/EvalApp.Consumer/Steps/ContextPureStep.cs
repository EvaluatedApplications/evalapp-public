using InternalAbstractions = global::EvalApp.Abstractions;

namespace EvalApp.Consumer;

/// <summary>
/// Context-aware pure step. Override <see cref="TransformAsync"/> to access global and domain contexts.
/// </summary>
public abstract class ContextPureStep<TGlobal, TDomain, T> : InternalAbstractions.IStep<T>, InternalAbstractions.IContextSettable
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

    protected abstract ValueTask<T> TransformAsync(
        T data, TGlobal global, TDomain domain, CancellationToken ct);

    public async ValueTask<InternalAbstractions.StepResult<T>> ExecuteAsync(
        T data, InternalAbstractions.StepContext context, CancellationToken ct = default)
    {
        try
        {
            var result = await TransformAsync(data, Global, Domain, ct);
            return new InternalAbstractions.StepResult<T>.Success(result);
        }
        catch (Exception ex)
        {
            return new InternalAbstractions.StepResult<T>.Failure(data, ex);
        }
    }
}
