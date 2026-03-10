namespace EvalApp.Consumer;

/// <summary>
/// Base class for domain-scoped context. One per pipeline domain.
/// Subclass this to inject domain-specific services.
/// </summary>
public abstract class DomainContext : global::EvalApp.Context.DomainContext
{
}
