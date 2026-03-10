using EvalAppTuning = global::EvalApp.Tuning;

namespace EvalApp.Consumer;

/// <summary>Immutable declaration of how a pipeline variable can be tuned.</summary>
public sealed record TunableConfig(int Min, int Max, int Default)
{
    internal EvalAppTuning.TunableConfig ToInternal() => new(Min, Max, Default);
}
