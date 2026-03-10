using InternalLicensing = global::EvalApp.Licensing;

namespace EvalApp.Consumer;

/// <summary>
/// Per-product license validator. Create one per product and reuse as a singleton.
/// </summary>
public sealed class LicenseValidator
{
    private readonly InternalLicensing.LicenseGate _gate;

    internal LicenseValidator(InternalLicensing.LicenseGate gate) => _gate = gate;

    /// <summary>
    /// Validates a license key and returns the resulting <see cref="LicenseMode"/>.
    /// Null/empty → Unlicensed. Invalid → throws. Valid → Licensed.
    /// </summary>
    public LicenseMode Check(string? licenseKey)
        => (LicenseMode)(int)_gate.Check(licenseKey);

    /// <summary>
    /// Hot-path periodic validation. Re-runs full check only once per interval;
    /// returns cached result between intervals. Safe for 60fps game loops.
    /// </summary>
    public LicenseMode CheckPeriodic(string? licenseKey, TimeSpan? interval = null)
        => (LicenseMode)(int)_gate.CheckPeriodic(licenseKey, interval);

    /// <summary>Creates the NavPathfinder license validator.</summary>
    public static LicenseValidator ForNavPathfinder()
        => new(InternalLicensing.LicenseGateFactory.ForNavPathfinder());
}
