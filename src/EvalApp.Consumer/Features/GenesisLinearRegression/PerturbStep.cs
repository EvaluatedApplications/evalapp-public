namespace EvalApp.Consumer.Features.GenesisLinearRegression;

/// <summary>
/// OBSERVE — Applies perturbation to agents, analogous to the generative
/// observation operator Ω that creates new elements in the platonic space.
///
/// The perturbation is the "input function" described in the problem statement:
/// by adding noise scaled to the current best error, we explore the neighbourhood
/// of promising solutions. The scale shrinks as the best error decreases,
/// implementing an automatic annealing schedule.
///
/// Because symmetric complements are regenerated after perturbation (SymmetryStep),
/// the population always explores both directions from any point — this is
/// the mechanism that prevents getting stuck in local minima.
/// </summary>
public class PerturbStep : PureStep<GenesisData>
{
    public override GenesisData Execute(GenesisData data)
    {
        // Use tick-based seed for reproducible but varying perturbations
        var rng = new Random(data.Seed + data.Tick * 7919);

        // Adaptive perturbation scale: shrink as we converge
        double scale = data.BestError < double.MaxValue
            ? Math.Max(0.001, data.PerturbationScale * Math.Sqrt(data.BestError))
            : data.PerturbationScale;

        int halfPop = data.Agents.Length / 2;
        var agents = new Agent[data.Agents.Length];

        // Keep the best agent unperturbed (elitism)
        if (data.BestAgent != null)
        {
            agents[0] = data.BestAgent;
        }
        else
        {
            agents[0] = data.Agents[0];
        }

        // Perturb remaining agents in the first half
        for (int i = 1; i < halfPop; i++)
        {
            var baseAgent = data.Agents[i];
            var weights = new double[data.FeatureCount];
            for (int j = 0; j < data.FeatureCount; j++)
            {
                // Perturbation: Gaussian-like noise via Box-Muller
                double u1 = 1.0 - rng.NextDouble();
                double u2 = rng.NextDouble();
                double noise = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
                weights[j] = baseAgent.Weights[j] + noise * scale;
            }

            double u1b = 1.0 - rng.NextDouble();
            double u2b = rng.NextDouble();
            double biasNoise = Math.Sqrt(-2.0 * Math.Log(u1b)) * Math.Cos(2.0 * Math.PI * u2b);
            double bias = baseAgent.Bias + biasNoise * scale;

            agents[i] = new Agent(weights, bias);
        }

        // Second half will be filled by SymmetryStep — copy placeholders
        for (int i = halfPop; i < data.Agents.Length; i++)
            agents[i] = data.Agents[i];

        return data with { Agents = agents, Tick = data.Tick + 1 };
    }
}
