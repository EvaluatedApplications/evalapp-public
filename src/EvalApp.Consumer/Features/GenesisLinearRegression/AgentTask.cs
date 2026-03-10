namespace EvalApp.Consumer.Features.GenesisLinearRegression;

/// <summary>
/// A self-contained evaluation task for a single agent.
/// Bundles the agent with references to training data so that
/// ForEach sub-steps can evaluate agents independently and in parallel.
/// </summary>
public record AgentTask(
    Agent Agent,
    double[][] TrainingX,
    double[] TrainingY
);
