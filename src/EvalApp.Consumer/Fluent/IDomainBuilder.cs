namespace EvalApp.Consumer;

/// <summary>Domain builder. Defines tasks within a named domain.</summary>
public interface IDomainBuilder
{
    IEmptyTaskBuilder<T> DefineTask<T>(string name);
    void Build(string? licenseKey = null);
}
