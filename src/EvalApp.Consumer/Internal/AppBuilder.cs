using EvalAppFluent = global::EvalApp.Fluent;
using EvalApp.Licensing;

namespace EvalApp.Consumer.Internal;

internal sealed class AppBuilder : Consumer.IAppBuilder
{
    private static readonly LicenseGate _gate = LicenseGateFactory.ForEvalApp();
    private EvalAppFluent.IAppBuilder _inner;
    private Consumer.IStepFactory _stepFactory = Consumer.DefaultStepFactory.Instance;

    internal AppBuilder(string name)
    {
        _inner = EvalAppFluent.Eval.App(name);
    }

    public Consumer.IAppBuilder WithStepFactory(Consumer.IStepFactory factory)
    {
        _stepFactory = factory ?? throw new ArgumentNullException(nameof(factory));
        return this;
    }

    public Consumer.IAppBuilder WithResource(Consumer.ResourceKind kind)
    {
        _inner = _inner.WithResource(kind.ToInternal());
        return this;
    }

    public Consumer.IAppBuilder WithResource(Consumer.ResourceKind kind, Consumer.TunableConfig tunable)
    {
        _inner = _inner.WithResource(kind.ToInternal(), tunable.ToInternal());
        return this;
    }

    public Consumer.IAppBuilder WithResource(Consumer.ResourceKind kind, int maxConcurrency)
    {
        _inner = _inner.WithResource(kind.ToInternal(), maxConcurrency);
        return this;
    }

    public Consumer.IAppBuilder WithTuning(string? storePath = null)
    {
        _inner = _inner.WithTuning(storePath);
        return this;
    }

    public Consumer.IAppBuilder WithBayesianTuning(string? storePath = null)
    {
        _inner = _inner.WithBayesianTuning(storePath);
        return this;
    }

    public Consumer.IAppBuilder WithWindowBudget(int cyclesPerSecond)
    {
        _inner = _inner.WithWindowBudget(cyclesPerSecond);
        return this;
    }

    public Consumer.IAppBuilder WithWindowBudget(string name, int cyclesPerSecond)
    {
        _inner = _inner.WithWindowBudget(name, cyclesPerSecond);
        return this;
    }

    public Consumer.IAppBuilder WithContext(object? globalContext)
    {
        _inner = _inner.WithContext(globalContext);
        return this;
    }

    public Consumer.IDomainBuilder DefineDomain(string name)
        => new DomainBuilder(_inner.DefineDomain(name), _stepFactory);

    public Consumer.IDomainBuilder DefineDomain(string name, object? domainContext)
        => new DomainBuilder(_inner.DefineDomain(name, domainContext), _stepFactory);

    /// <summary>
    /// Validates the license key if provided. Pipelines are wired by IDomainBuilder.Build().
    /// </summary>
    public void Build(string? licenseKey = null)
    {
        if (!string.IsNullOrWhiteSpace(licenseKey))
            _gate.Check(licenseKey);
    }
}
