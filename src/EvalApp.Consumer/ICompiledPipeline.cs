namespace EvalApp.Consumer;

/// <summary>
/// A compiled, opaque pipeline ready to execute.
/// The internal execution engine is hidden — consumers only interact through this interface.
/// </summary>
public interface ICompiledPipeline<T>
{
    /// <summary>The name given to this pipeline at build time.</summary>
    string Name { get; }

    /// <summary>
    /// Executes the pipeline with the given input data.
    /// Returns a <see cref="PipelineResult{T}"/> — Success, Failure, or Skipped.
    /// </summary>
    ValueTask<PipelineResult<T>> RunAsync(T data, CancellationToken ct = default);
}
