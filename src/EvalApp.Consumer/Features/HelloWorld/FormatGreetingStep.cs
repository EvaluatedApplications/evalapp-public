namespace EvalApp.Consumer.Features.HelloWorld;

/// <summary>
/// Builds the final greeting string in the form "Hello, {Name}!" from the
/// normalised name already present on the data record.
/// </summary>
public class FormatGreetingStep : PureStep<HelloWorldData>
{
    public override HelloWorldData Execute(HelloWorldData data)
        => data with { Greeting = $"Hello, {data.Name}!" };
}
