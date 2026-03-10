namespace EvalApp.Consumer.Features.GenesisLinearRegression;

/// <summary>
/// SELECT — Selects the best agents from the population, analogous to
/// the selection policy σ that determines what to observe next.
///
/// Keeps the top-performing agents and records the best overall solution.
/// The combination of selection + symmetry + perturbation forms the
/// Genesis Learning cycle that converges on the optimal linear regression
/// solution without getting stuck in local minima.
/// </summary>
public class SelectStep : PureStep<GenesisData>
{
    public override GenesisData Execute(GenesisData data)
    {
        // Sort agents by error (ascending — lower is better)
        var sorted = data.Agents
            .OrderBy(a => a.Error)
            .ToArray();

        // Track the best agent across all ticks
        var currentBest = sorted[0];
        var bestAgent = data.BestAgent;
        double bestError = data.BestError;

        if (currentBest.Error < bestError)
        {
            bestAgent = currentBest;
            bestError = currentBest.Error;
        }

        // Keep top half as "survivors" — the other half will be
        // regenerated as symmetric complements by SymmetryStep
        int halfPop = data.Agents.Length / 2;
        var survivors = new Agent[data.Agents.Length];
        for (int i = 0; i < halfPop; i++)
            survivors[i] = sorted[i];
        for (int i = halfPop; i < data.Agents.Length; i++)
            survivors[i] = sorted[i - halfPop];

        return data with
        {
            Agents = survivors,
            BestAgent = bestAgent,
            BestError = bestError
        };
    }
}
