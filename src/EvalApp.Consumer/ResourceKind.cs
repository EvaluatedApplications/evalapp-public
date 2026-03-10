using EvalAppCore = global::EvalApp.Core;

namespace EvalApp.Consumer;

/// <summary>Identifies the type of shared resource a Gate contends for.</summary>
public readonly struct ResourceKind : IEquatable<ResourceKind>
{
    public static readonly ResourceKind Network  = new("network");
    public static readonly ResourceKind DiskIO   = new("diskio");
    public static readonly ResourceKind Cpu      = new("cpu");
    public static readonly ResourceKind Database = new("database");

    public string Name { get; }

    private ResourceKind(string name) => Name = name;

    public static ResourceKind Of(string name) => new(name);

    internal EvalAppCore.ResourceKind ToInternal() => EvalAppCore.ResourceKind.Of(Name);

    public bool Equals(ResourceKind other) => string.Equals(Name, other.Name, StringComparison.OrdinalIgnoreCase);
    public override bool Equals(object? obj) => obj is ResourceKind other && Equals(other);
    public override int GetHashCode() => StringComparer.OrdinalIgnoreCase.GetHashCode(Name ?? "");
    public static bool operator ==(ResourceKind a, ResourceKind b) => a.Equals(b);
    public static bool operator !=(ResourceKind a, ResourceKind b) => !a.Equals(b);
    public override string ToString() => Name;
}
