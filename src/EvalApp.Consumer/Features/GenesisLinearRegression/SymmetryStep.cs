namespace EvalApp.Consumer.Features.GenesisLinearRegression;

/// <summary>
/// CONSERVE — Enforces G4 Conservation by ensuring every agent has its
/// symmetric complement in the population. For every agent with weights w
/// and bias b, the complement has weights −w and bias −b, so that
/// agent ⊕ complement = 0̂ (the void identity).
///
/// This symmetry property is key to avoiding local minima: the population
/// always spans both sides of the origin, preventing the search from
/// collapsing into a single basin of attraction.
/// </summary>
public class SymmetryStep : PureStep<GenesisData>
{
    public override GenesisData Execute(GenesisData data)
    {
        int halfPop = data.Agents.Length / 2;
        var agents = new Agent[data.Agents.Length];

        // Copy the first half (the "positive" agents)
        for (int i = 0; i < halfPop; i++)
            agents[i] = data.Agents[i];

        // Generate symmetric complements for the second half
        for (int i = 0; i < halfPop; i++)
        {
            var w = agents[i].Weights;
            var complement = new double[w.Length];
            for (int j = 0; j < w.Length; j++)
                complement[j] = -w[j];

            agents[i + halfPop] = new Agent(complement, -agents[i].Bias);
        }

        return data with { Agents = agents };
    }
}
