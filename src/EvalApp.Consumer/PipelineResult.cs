using EvalApp.Abstractions;

namespace EvalApp.Consumer;

/// <summary>
/// The result of executing a compiled EvalApp pipeline.
/// Consumers pattern-match on Success / Failure / Skipped — no dependency on EvalApp internals.
/// </summary>
public abstract record PipelineResult<T>
{
    private PipelineResult() { }

    /// <summary>All steps completed successfully. <see cref="Data"/> holds the final output.</summary>
    public sealed record Success(T Data) : PipelineResult<T>;

    /// <summary>A step failed. <see cref="Data"/> holds the last successful state; <see cref="Exception"/> holds the cause.</summary>
    public sealed record Failure(T Data, Exception Exception, string? Message = null) : PipelineResult<T>;

    /// <summary>A step was skipped (e.g. a conditional branch that did not match). <see cref="Data"/> is unchanged.</summary>
    public sealed record Skipped(T Data, string Reason) : PipelineResult<T>;

    /// <summary>Extract data regardless of outcome.</summary>
    public T GetData() => this switch
    {
        Success s  => s.Data,
        Failure f  => f.Data,
        Skipped sk => sk.Data,
        _          => throw new InvalidOperationException("Unknown PipelineResult variant"),
    };

    public bool IsSuccess => this is Success;
    public bool IsFailure => this is Failure;
    public bool IsSkipped => this is Skipped;

    /// <summary>Maps from the internal EvalApp StepResult — only called inside the engine.</summary>
    internal static PipelineResult<T> From(StepResult<T> result) => result switch
    {
        StepResult<T>.Success s  => new Success(s.Data),
        StepResult<T>.Failure f  => new Failure(f.Data, f.Exception, f.Message),
        StepResult<T>.Skipped sk => new Skipped(sk.Data, sk.Reason),
        _                        => throw new InvalidOperationException("Unknown StepResult variant"),
    };
}
