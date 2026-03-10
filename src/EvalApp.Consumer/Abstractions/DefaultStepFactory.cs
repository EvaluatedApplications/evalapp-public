using System.Diagnostics.CodeAnalysis;

namespace EvalApp.Consumer;

/// <summary>
/// Default <see cref="IStepFactory"/> — resolves step instances via <see cref="Activator.CreateInstance"/>.
/// Steps must have a public parameterless constructor. This is the fallback used when no factory is registered.
/// </summary>
public sealed class DefaultStepFactory : IStepFactory
{
    /// <summary>Shared singleton instance.</summary>
    public static readonly DefaultStepFactory Instance = new();

    /// <inheritdoc/>
    public IStep<T> Create<T>([DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)] Type stepType)
    {
        if (!typeof(IStep<T>).IsAssignableFrom(stepType))
            throw new ArgumentException(
                $"Type '{stepType.Name}' does not implement IStep<{typeof(T).Name}>.");

        return (IStep<T>)Activator.CreateInstance(stepType)!;
    }
}
