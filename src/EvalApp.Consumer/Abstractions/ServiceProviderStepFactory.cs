using System.Diagnostics.CodeAnalysis;

namespace EvalApp.Consumer;

/// <summary>
/// An <see cref="IStepFactory"/> that resolves step instances from an <see cref="IServiceProvider"/>.
/// Register this with <c>.WithStepFactory(new ServiceProviderStepFactory(serviceProvider))</c>
/// to enable automatic DI resolution for all steps added via <c>AddStep&lt;TStep&gt;()</c>.
/// </summary>
/// <example>
/// <code>
/// EvalApp.App("Orders")
///     .WithStepFactory(new ServiceProviderStepFactory(sp))
///     .WithResource(ResourceKind.Database)
///     .DefineDomain("OrderProcessing")
///         .DefineTask&lt;OrderData&gt;("ProcessOrder")
///             .AddStep&lt;ValidateOrderStep&gt;("Validate")
///             .Gate(ResourceKind.Database, null, g =&gt; g
///                 .AddStep&lt;CheckInventoryStep&gt;("CheckInventory"))
///             .Run(out pipeline)
///         .Build(licenseKey);
/// </code>
/// </example>
public sealed class ServiceProviderStepFactory : IStepFactory
{
    private readonly IServiceProvider _sp;

    /// <param name="serviceProvider">The application's DI container.</param>
    public ServiceProviderStepFactory(IServiceProvider serviceProvider)
    {
        _sp = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
    }

    /// <inheritdoc/>
    public IStep<T> Create<T>([DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)] Type stepType)
    {
        var service = _sp.GetService(stepType)
            ?? throw new InvalidOperationException(
                $"No service of type '{stepType.Name}' is registered in the DI container. " +
                $"Ensure it is registered before building the pipeline.");

        return (IStep<T>)service;
    }
}
