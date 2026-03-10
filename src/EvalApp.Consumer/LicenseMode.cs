namespace EvalApp.Consumer;

/// <summary>
/// The outcome of a license validation check.
/// </summary>
public enum LicenseMode
{
    /// <summary>
    /// No valid key provided. Pipeline runs fully sequential — no parallelism, no adaptive tuner.
    /// All steps execute correctly; only throughput is limited.
    /// </summary>
    Unlicensed,

    /// <summary>
    /// Valid license key confirmed. Full engine — adaptive tuner, parallel ForEach,
    /// Gate concurrency. Zero artificial limits.
    /// </summary>
    Licensed,
}
