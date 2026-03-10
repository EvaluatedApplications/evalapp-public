namespace EvalApp.Consumer.Features.GenesisLinearRegression;

/// <summary>
/// An agent in the Genesis Learning platonic space.
/// Each agent holds a candidate linear model (weights + bias) and its measured error.
/// Agents come in complementary pairs (G4 Conservation): for every agent with
/// weights w, there exists a mirror agent with weights −w.
/// </summary>
public record Agent(
    double[] Weights,
    double Bias,
    double Error = double.MaxValue
);
