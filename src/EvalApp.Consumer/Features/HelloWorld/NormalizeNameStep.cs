namespace EvalApp.Consumer.Features.HelloWorld;

/// <summary>
/// Trims whitespace from the incoming name and defaults an empty or whitespace-only
/// name to "World", then marks the record as normalised.
/// </summary>
public class NormalizeNameStep : PureStep<HelloWorldData>
{
    public override HelloWorldData Execute(HelloWorldData data)
    {
        var trimmed = data.Name?.Trim() ?? string.Empty;
        var normalized = string.IsNullOrEmpty(trimmed) ? "World" : trimmed;

        return data with { Name = normalized, IsNameNormalized = true };
    }
}
