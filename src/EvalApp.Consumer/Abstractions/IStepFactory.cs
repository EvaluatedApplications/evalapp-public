using System.Diagnostics.CodeAnalysis;

namespace EvalApp.Consumer;

/// <summary>
/// Factory for creating pipeline step instances. Implement this interface to integrate
/// with a dependency injection container so steps are resolved automatically during pipeline execution.
/// </summary>
public interface IStepFactory
{
    /// <summary>Creates a step instance of the specified type.</summary>
    IStep<T> Create<T>([DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)] Type stepType);
}
