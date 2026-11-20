using Microsoft.AspNetCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace AdaskoTheBeAsT.Interop.Execution.AspNetCore;

/// <summary>
/// <see cref="IServiceCollection"/> extensions for registering AOT-safe diagnostics
/// JSON serialization and global execution exception handling in ASP.NET Core hosts.
/// </summary>
public static class ExecutionAspNetCoreServiceCollectionExtensions
{
    /// <summary>
    /// Inserts <see cref="ExecutionAspNetCoreJsonSerializerContext.Default"/> at the front
    /// of ASP.NET Core's HTTP JSON <c>TypeInfoResolverChain</c> so diagnostics DTOs and
    /// <c>ProblemDetails</c> serialize without runtime reflection.
    /// </summary>
    /// <param name="services">The service collection to configure.</param>
    /// <returns>The same <paramref name="services"/> for chaining.</returns>
    public static IServiceCollection AddExecutionAspNetCoreJson(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.ConfigureHttpJsonOptions(static options =>
        {
            var chain = options.SerializerOptions.TypeInfoResolverChain;
            if (!chain.Contains(ExecutionAspNetCoreJsonSerializerContext.Default))
            {
                chain.Insert(0, ExecutionAspNetCoreJsonSerializerContext.Default);
            }
        });

        return services;
    }

    /// <summary>
    /// Registers <see cref="ExecutionExceptionHandler"/>, <c>ProblemDetails</c> services,
    /// and <see cref="ExecutionAspNetCoreJsonSerializerContext"/> in <paramref name="services"/>.
    /// </summary>
    /// <param name="services">The service collection to configure.</param>
    /// <param name="configure">Optional callback to customize <see cref="ExecutionAspNetCoreOptions"/>.</param>
    /// <returns>The same <paramref name="services"/> for chaining.</returns>
    public static IServiceCollection AddExecutionExceptionHandler(
        this IServiceCollection services,
        Action<ExecutionAspNetCoreOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddExecutionAspNetCoreJson();
        services.AddProblemDetails();
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<ExecutionAspNetCoreOptions>, ExecutionAspNetCoreOptionsValidator>());

        var optionsBuilder = services.AddOptions<ExecutionAspNetCoreOptions>();
        if (configure is not null)
        {
            optionsBuilder.Configure(configure);
        }

        optionsBuilder.ValidateOnStart();

        var alreadyRegistered = false;
        foreach (var descriptor in services)
        {
            if (descriptor.ServiceType == typeof(IExceptionHandler) &&
                descriptor.ImplementationType == typeof(ExecutionExceptionHandler))
            {
                alreadyRegistered = true;
                break;
            }
        }

        if (!alreadyRegistered)
        {
            services.AddExceptionHandler<ExecutionExceptionHandler>();
        }

        return services;
    }

    private sealed class ExecutionAspNetCoreOptionsValidator : IValidateOptions<ExecutionAspNetCoreOptions>
    {
        public ValidateOptionsResult Validate(string? name, ExecutionAspNetCoreOptions options)
        {
            if (options is null)
            {
                return ValidateOptionsResult.Fail("ExecutionAspNetCoreOptions instance must not be null.");
            }

            try
            {
                options.Validate();
                return ValidateOptionsResult.Success;
            }
            catch (ArgumentException exception)
            {
                return ValidateOptionsResult.Fail(exception.Message);
            }
        }
    }
}
