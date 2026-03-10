namespace EvalApp.Consumer.Features.GenesisLinearRegression;

/// <summary>
/// Factory that assembles the Genesis Linear Regression pipeline.
///
/// The algorithm implements a novel learning approach inspired by Genesis Learning:
///
///   1. INIT     — Create agents from the void, each with a symmetric complement (G4)
///   2. PERTURB  — Apply perturbation (the "input function") to explore the space
///   3. SYMMETRY — Regenerate symmetric complements to maintain conservation (G4)
///   4. EVALUATE — Measure all agents against training data (parallel via ForEach)
///   5. SELECT   — Keep the best agents for the next tick
///
/// The symmetry/perturbation cycle is the key insight: by always maintaining
/// complementary pairs, the algorithm explores both sides of any solution and
/// cannot collapse into a local minimum. The perturbation scale adapts based
/// on the current best error, implementing automatic convergence.
///
/// When a license key is provided, agent evaluation runs in parallel via
/// ForEach with CPU-count parallelism (Environment.ProcessorCount), giving
/// significant speed-ups on multi-core machines.
///
/// One pipeline run = one Genesis tick. Call RunAsync in a loop for multiple ticks.
/// </summary>
public static class GenesisLinearRegressionPipeline
{
    /// <summary>
    /// Builds a single-tick pipeline. Run it in a loop for iterative convergence.
    /// Pass a valid license key to enable parallel agent evaluation via ForEach.
    /// </summary>
    public static ICompiledPipeline<GenesisData> BuildTick(string? licenseKey = null)
    {
        ICompiledPipeline<GenesisData> pipeline;

        EvalApp.App("GenesisLinearRegression")
            .DefineDomain("Learning")
                .DefineTask<GenesisData>("GenesisTick")
                    .AddStep("Perturb",  new PerturbStep())
                    .AddStep("Symmetry", new SymmetryStep())
                    .ForEach<AgentTask>(
                        select: data => data.Agents.Select(a =>
                            new AgentTask(a, data.TrainingX, data.TrainingY)),
                        merge: (data, results) => data with
                        {
                            Agents = results.Select(t => t.Agent).ToArray()
                        },
                        collectionName: "Agents",
                        maxParallelism: Environment.ProcessorCount,
                        configure: b => b.AddStep("EvaluateAgent", task =>
                        {
                            double totalError = 0;
                            int n = task.TrainingY.Length;
                            for (int i = 0; i < n; i++)
                            {
                                double prediction = task.Agent.Bias;
                                for (int j = 0; j < task.Agent.Weights.Length; j++)
                                    prediction += task.Agent.Weights[j] * task.TrainingX[i][j];
                                double diff = prediction - task.TrainingY[i];
                                totalError += diff * diff;
                            }
                            double mse = totalError / n;
                            return task with { Agent = task.Agent with { Error = mse } };
                        }))
                    .AddStep("Select", new SelectStep())
                    .Run(out pipeline)
                .Build(licenseKey);

        return pipeline;
    }

    /// <summary>
    /// Builds the initialisation pipeline (run once before the tick loop).
    /// Pass a valid license key to enable parallel agent evaluation via ForEach.
    /// </summary>
    public static ICompiledPipeline<GenesisData> BuildInit(string? licenseKey = null)
    {
        ICompiledPipeline<GenesisData> pipeline;

        EvalApp.App("GenesisInit")
            .DefineDomain("Learning")
                .DefineTask<GenesisData>("Init")
                    .AddStep("InitAgents", new InitializeAgentsStep())
                    .ForEach<AgentTask>(
                        select: data => data.Agents.Select(a =>
                            new AgentTask(a, data.TrainingX, data.TrainingY)),
                        merge: (data, results) => data with
                        {
                            Agents = results.Select(t => t.Agent).ToArray()
                        },
                        collectionName: "Agents",
                        maxParallelism: Environment.ProcessorCount,
                        configure: b => b.AddStep("EvaluateAgent", task =>
                        {
                            double totalError = 0;
                            int n = task.TrainingY.Length;
                            for (int i = 0; i < n; i++)
                            {
                                double prediction = task.Agent.Bias;
                                for (int j = 0; j < task.Agent.Weights.Length; j++)
                                    prediction += task.Agent.Weights[j] * task.TrainingX[i][j];
                                double diff = prediction - task.TrainingY[i];
                                totalError += diff * diff;
                            }
                            double mse = totalError / n;
                            return task with { Agent = task.Agent with { Error = mse } };
                        }))
                    .AddStep("Select", new SelectStep())
                    .Run(out pipeline)
                .Build(licenseKey);

        return pipeline;
    }

    /// <summary>
    /// Convenience method: runs the full Genesis Learning loop for linear regression.
    /// </summary>
    /// <param name="trainingX">Training inputs — each row is a sample, each column a feature.</param>
    /// <param name="trainingY">Training targets — one value per sample.</param>
    /// <param name="maxTicks">Maximum number of Genesis ticks (iterations).</param>
    /// <param name="convergenceThreshold">Stop when MSE drops below this value.</param>
    /// <param name="populationSize">Number of agents in the platonic space.</param>
    /// <param name="seed">Random seed for reproducibility.</param>
    /// <param name="licenseKey">EvalApp license key — enables parallel agent evaluation.</param>
    /// <returns>The converged GenesisData with the best agent containing learned weights.</returns>
    public static async Task<GenesisData> SolveAsync(
        double[][] trainingX,
        double[] trainingY,
        int maxTicks = 200,
        double convergenceThreshold = 1e-6,
        int populationSize = 32,
        int seed = 42,
        string? licenseKey = null,
        CancellationToken ct = default)
    {
        int featureCount = trainingX[0].Length;

        var data = new GenesisData(
            TrainingX: trainingX,
            TrainingY: trainingY,
            Agents: Array.Empty<Agent>(),
            FeatureCount: featureCount,
            PopulationSize: populationSize,
            PerturbationScale: 1.0,
            Seed: seed
        );

        // Phase 1: Initialise from the void
        var initPipeline = BuildInit(licenseKey);
        var initResult = await initPipeline.RunAsync(data, ct);
        data = initResult.GetData();

        // Phase 2: Genesis tick loop
        var tickPipeline = BuildTick(licenseKey);

        for (int τ = 0; τ < maxTicks; τ++)
        {
            var result = await tickPipeline.RunAsync(data, ct);
            data = result.GetData();

            // Convergence check: has the platonic space stabilised?
            if (data.BestError <= convergenceThreshold)
                break;
        }

        return data;
    }
}
