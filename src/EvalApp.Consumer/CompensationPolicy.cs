using EvalAppAbstractions = global::EvalApp.Abstractions;

namespace EvalApp.Consumer;

/// <summary>Controls how compensation failures are handled during saga rollback.</summary>
public enum CompensationPolicy
{
    BestEffort,
    AbortOnFirst,
    SwallowErrors
}

internal static class CompensationPolicyExtensions
{
    internal static EvalAppAbstractions.CompensationPolicy ToInternal(this CompensationPolicy p) => p switch
    {
        CompensationPolicy.BestEffort    => EvalAppAbstractions.CompensationPolicy.BestEffort,
        CompensationPolicy.AbortOnFirst  => EvalAppAbstractions.CompensationPolicy.AbortOnFirst,
        CompensationPolicy.SwallowErrors => EvalAppAbstractions.CompensationPolicy.SwallowErrors,
        _                                => throw new ArgumentOutOfRangeException(nameof(p))
    };
}
