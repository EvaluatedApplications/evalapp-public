namespace EvalApp.Consumer.Features.GenesisLinearRegression;

/// <summary>
/// GENESIS_INIT — Initialises the platonic space from the void (∅).
/// Creates an initial population of agents by sampling random weights,
/// analogous to the first observation of the void generating {∅, 0, +1, −1}.
/// Each agent is paired with its symmetric complement (G4 Conservation).
/// </summary>
public class InitializeAgentsStep : PureStep<GenesisData>
{
    public override GenesisData Execute(GenesisData data)
    {
        var rng = new Random(data.Seed);
        int halfPop = data.PopulationSize / 2;
        var agents = new Agent[data.PopulationSize];

        for (int i = 0; i < halfPop; i++)
        {
            // Generate an agent from the void with random weights
            var weights = new double[data.FeatureCount];
            for (int j = 0; j < data.FeatureCount; j++)
                weights[j] = (rng.NextDouble() - 0.5) * 2.0 * data.PerturbationScale;

            double bias = (rng.NextDouble() - 0.5) * 2.0 * data.PerturbationScale;

            agents[i] = new Agent(weights, bias);

            // G4 Conservation: generate the symmetric complement (−w, −b)
            var complement = new double[data.FeatureCount];
            for (int j = 0; j < data.FeatureCount; j++)
                complement[j] = -weights[j];

            agents[i + halfPop] = new Agent(complement, -bias);
        }

        return data with { Agents = agents, Tick = 0 };
    }
}
