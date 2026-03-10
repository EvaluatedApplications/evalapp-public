namespace EvalApp.Consumer;

/// <summary>Top-level pipeline app builder.</summary>
public interface IAppBuilder
{
    IAppBuilder WithStepFactory(IStepFactory factory);
    IAppBuilder WithResource(ResourceKind kind);
    IAppBuilder WithResource(ResourceKind kind, TunableConfig tunable);
    IAppBuilder WithResource(ResourceKind kind, int maxConcurrency);
    IAppBuilder WithTuning(string? storePath = null);
    IAppBuilder WithBayesianTuning(string? storePath = null);
    IAppBuilder WithWindowBudget(int cyclesPerSecond);
    IAppBuilder WithWindowBudget(string name, int cyclesPerSecond);
    IAppBuilder WithContext(object? globalContext);
    IDomainBuilder DefineDomain(string name);
    IDomainBuilder DefineDomain(string name, object? domainContext);
    void Build(string? licenseKey = null);
}
