namespace EvalApp.Consumer.Features.GenesisLinearRegression;

/// <summary>
/// Immutable data record that flows through the Genesis Linear Regression pipeline.
/// Reading top-to-bottom describes the full algorithm lifecycle:
///   Training data is supplied → agents are initialised from the void →
///   symmetric complements are generated (G4) → perturbations are applied →
///   agents are evaluated in parallel → best agents are selected.
/// </summary>
public record GenesisData(
    // INPUT — training data for the linear regression problem
    double[][] TrainingX,
    double[] TrainingY,

    // STATE — the current population of agents (platonic space Π)
    Agent[] Agents,

    // OUTPUT — best agent found so far
    Agent? BestAgent = null,
    double BestError = double.MaxValue,

    // ALGORITHM PARAMETERS
    int FeatureCount = 1,
    int PopulationSize = 32,
    double PerturbationScale = 1.0,
    int Tick = 0,
    int Seed = 42
);
