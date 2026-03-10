namespace EvalApp.Consumer.Features.HelloWorld;

/// <summary>
/// Factory that assembles and returns the compiled Hello World pipeline.
/// Call <see cref="Build"/> once at startup and register the result as a singleton.
/// </summary>
public static class HelloWorldPipeline
{
    /// <summary>
    /// Builds the Hello World pipeline.
    /// Steps run in order: NormalizeNameStep → FormatGreetingStep.
    /// </summary>
    /// <returns>A compiled, reusable <see cref="ICompiledPipeline{T}"/> instance.</returns>
    public static ICompiledPipeline<HelloWorldData> Build()
    {
        ICompiledPipeline<HelloWorldData> pipeline;

        EvalApp.App("HelloWorld")
            .WithTuning()
            .DefineDomain("Greetings")
                .DefineTask<HelloWorldData>("Greet")
                    .AddStep("NormalizeName",  new NormalizeNameStep())
                    .AddStep("FormatGreeting", new FormatGreetingStep())
                    .Run(out pipeline)
                .Build();

        return pipeline;
    }
}
