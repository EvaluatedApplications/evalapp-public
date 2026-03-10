namespace EvalApp.Consumer;

/// <summary>
/// Concrete <see cref="GlobalContext"/> for pipelines that have no global state.
/// Use <c>ContextPureStep&lt;NullGlobalContext, TDomain, T&gt;</c> when your step
/// needs domain context but no global context.
/// </summary>
public sealed class NullGlobalContext : GlobalContext
{
    public static readonly NullGlobalContext Instance = new();
}
