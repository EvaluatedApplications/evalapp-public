namespace EvalApp.Consumer;

/// <summary>
/// Base class for a synchronous, side-effect-free pipeline step.
/// Override <see cref="Execute"/> to return a new (mutated) data record.
/// </summary>
/// <example>
/// <code>
/// public class NormaliseStep : PureStep&lt;OrderData&gt;
/// {
///     public override OrderData Execute(OrderData data)
///         => data with { Name = data.Name.Trim().ToUpper() };
/// }
///
/// EvalApp.App("Orders")
///     .DefineDomain("Processing")
///         .DefineTask&lt;OrderData&gt;("ProcessOrder")
///             .AddStep("Normalise", new NormaliseStep())
///             .Run(out var pipeline)
///         .Build();
/// </code>
/// </example>
public abstract class PureStep<T>
{
    /// <summary>Transform <paramref name="data"/> and return the updated record.</summary>
    public abstract T Execute(T data);
}
