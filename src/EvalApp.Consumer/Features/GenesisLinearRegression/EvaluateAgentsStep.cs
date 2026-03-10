namespace EvalApp.Consumer.Features.GenesisLinearRegression;

/// <summary>
/// PATTERN DETECTION — Evaluates all agents against the training data,
/// measuring each agent's prediction error (MSE). This is the "measure
/// arbitrary outputs" step described in the problem statement.
///
/// For a linear model: ŷ = Σ(wⱼ · xⱼ) + b
/// Error = (1/N) Σ(ŷᵢ − yᵢ)²
///
/// Evaluation happens for ALL agents — the EvalApp pipeline's ForEach
/// processes them in parallel (the "action phase happens in parallel").
/// </summary>
public class EvaluateAgentsStep : PureStep<GenesisData>
{
    public override GenesisData Execute(GenesisData data)
    {
        var evaluated = new Agent[data.Agents.Length];

        for (int a = 0; a < data.Agents.Length; a++)
        {
            var agent = data.Agents[a];
            double totalError = 0;
            int n = data.TrainingY.Length;

            for (int i = 0; i < n; i++)
            {
                double prediction = agent.Bias;
                for (int j = 0; j < agent.Weights.Length; j++)
                    prediction += agent.Weights[j] * data.TrainingX[i][j];

                double diff = prediction - data.TrainingY[i];
                totalError += diff * diff;
            }

            double mse = totalError / n;
            evaluated[a] = agent with { Error = mse };
        }

        return data with { Agents = evaluated };
    }
}
