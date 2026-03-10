namespace EvalApp.Consumer;

/// <summary>Entry point for EvalApp pipelines.</summary>
public static class EvalApp
{
    /// <summary>
    /// Creates a new pipeline app. Chain .DefineDomain() → .DefineTask&lt;T&gt;() → steps → .Run(out pipeline) → .Build().
    /// </summary>
    /// <example>
    /// <code>
    /// EvalApp.App("MyApp")
    ///     .WithTuning()
    ///     .DefineDomain("Orders")
    ///         .DefineTask&lt;OrderData&gt;("ProcessOrder")
    ///             .AddStep("Validate", d => d with { IsValid = d.Id > 0 })
    ///             .Run(out var pipeline)
    ///         .Build();
    ///
    /// var result = await pipeline.RunAsync(new OrderData(123));
    /// </code>
    /// </example>
    public static IAppBuilder App(string name = "Pipeline") => new Internal.AppBuilder(name);
}
