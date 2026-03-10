namespace EvalApp.Consumer;

/// <summary>
/// Base class for global context shared across all domains and tasks.
/// Subclass this to inject shared services (HTTP clients, configuration, etc.).
/// </summary>
public abstract class GlobalContext : global::EvalApp.Context.GlobalContext
{
}
