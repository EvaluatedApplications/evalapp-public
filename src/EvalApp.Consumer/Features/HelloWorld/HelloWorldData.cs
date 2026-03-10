namespace EvalApp.Consumer.Features.HelloWorld;

/// <summary>
/// Immutable data record for the Hello World pipeline.
/// Reading top-to-bottom describes the full pipeline lifecycle:
///   Name is supplied → normalised → used to build the greeting.
/// </summary>
public record HelloWorldData(
    // INPUT — supplied by the caller before the run starts
    string Name,

    // STAGE 1 — set to true by NormalizeNameStep once the name is trimmed / defaulted
    bool IsNameNormalized = false,

    // OUTPUT — the finished greeting produced by FormatGreetingStep
    string? Greeting = null
);
