using EvalAppCore = global::EvalApp.Core;

namespace EvalApp.Consumer;

/// <summary>Controls how a parallel ForEach responds when one or more items fail.</summary>
public enum ForEachFailureMode
{
    CollectAndThrow,
    FailFast,
    ContinueOnError
}

internal static class ForEachFailureModeExtensions
{
    internal static EvalAppCore.ForEachFailureMode ToInternal(this ForEachFailureMode m) => m switch
    {
        ForEachFailureMode.CollectAndThrow => EvalAppCore.ForEachFailureMode.CollectAndThrow,
        ForEachFailureMode.FailFast        => EvalAppCore.ForEachFailureMode.FailFast,
        ForEachFailureMode.ContinueOnError => EvalAppCore.ForEachFailureMode.ContinueOnError,
        _                                  => throw new ArgumentOutOfRangeException(nameof(m))
    };
}
