namespace EvalApp.Consumer;

/// <summary>Factory for TunableConfig — readable inline declarations.</summary>
public static class Tunable
{
    public static TunableConfig Between(int min, int max, int @default) => new(min, max, @default);
    public static TunableConfig FixedAt(int value) => new(value, value, value);
    public static TunableConfig ForCpu()
    {
        var cpus = Environment.ProcessorCount;
        return new TunableConfig(Min: Math.Max(1, cpus / 2), Max: cpus * 2, Default: cpus);
    }
    public static TunableConfig ForItems()
    {
        var cpus = Environment.ProcessorCount;
        var max  = Math.Clamp(cpus * 8, 32, 256);
        var warm = Math.Clamp(cpus, 4, 32);
        return new TunableConfig(Min: 4, Max: max, Default: warm);
    }
    public static TunableConfig ForItems(int @default)
    {
        var max = Math.Clamp(Environment.ProcessorCount * 8, 32, 256);
        return new TunableConfig(Min: 1, Max: max, Default: Math.Clamp(@default, 1, max));
    }
    public static TunableConfig InlineBelow(int threshold) => new(threshold, threshold, threshold);
    public static TunableConfig InlineBelowBetween(int min, int max, int @default) => new(min, max, @default);
}
