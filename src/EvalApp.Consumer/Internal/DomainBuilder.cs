using EvalAppFluent = global::EvalApp.Fluent;
using EvalApp.Licensing;

namespace EvalApp.Consumer.Internal;

internal sealed class DomainBuilder : Consumer.IDomainBuilder
{
    private static readonly LicenseGate _gate = LicenseGateFactory.ForEvalApp();
    private readonly EvalAppFluent.IDomainBuilder _inner;
    internal readonly Consumer.IStepFactory Factory;

    internal DomainBuilder(EvalAppFluent.IDomainBuilder inner, Consumer.IStepFactory factory)
    {
        _inner  = inner;
        Factory = factory;
    }

    public Consumer.IEmptyTaskBuilder<T> DefineTask<T>(string name)
        => new TaskBuilder<T>(_inner.DefineTask<T>(name), this);

    public void Build(string? licenseKey = null)
    {
        if (!string.IsNullOrWhiteSpace(licenseKey))
            _gate.Check(licenseKey);
        _inner.Build();
    }
}
