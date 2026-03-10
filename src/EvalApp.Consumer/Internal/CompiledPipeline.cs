using EvalApp.Core;

namespace EvalApp.Consumer.Internal;

/// <summary>
/// Opaque wrapper around a compiled EvalApp <see cref="Pipeline{T}"/>.
/// Consumers only see <see cref="ICompiledPipeline{T}"/> — the engine stays hidden.
/// </summary>
internal sealed class CompiledPipeline<T> : ICompiledPipeline<T>
{
    private readonly Pipeline<T> _pipeline;

    internal CompiledPipeline(Pipeline<T> pipeline)
    {
        _pipeline = pipeline;
    }

    public string Name => _pipeline.Name;

    public async ValueTask<PipelineResult<T>> RunAsync(T data, CancellationToken ct = default)
        => PipelineResult<T>.From(await _pipeline.RunAsync(data, ct));
}
