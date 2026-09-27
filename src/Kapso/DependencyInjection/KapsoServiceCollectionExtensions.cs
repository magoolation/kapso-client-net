using Kapso;
using Kapso.Http;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Registers <see cref="KapsoClient"/> with dependency injection.
/// </summary>
public static class KapsoServiceCollectionExtensions
{
    /// <summary>
    /// Name of the underlying <see cref="HttpClient"/> registration.
    /// </summary>
    public const string HttpClientName = "Kapso";

    /// <summary>
    /// Registers the client, binding options from the <c>Kapso</c> configuration
    /// section.
    /// </summary>
    /// <returns>
    /// The <see cref="IHttpClientBuilder"/>, so the application can extend the
    /// message pipeline — adding its own handlers, or replacing the resilience
    /// strategy when it would rather own retry policy.
    /// </returns>
    /// <example>
    /// <code>
    /// builder.Services.AddKapso(builder.Configuration);
    /// </code>
    /// </example>
    public static IHttpClientBuilder AddKapso(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        return services.AddKapso(options =>
            configuration.GetSection(KapsoClientOptions.DefaultConfigurationSection).Bind(options));
    }

    /// <summary>
    /// Registers the client with an API key and otherwise default settings.
    /// </summary>
    public static IHttpClientBuilder AddKapso(this IServiceCollection services, string apiKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);

        return services.AddKapso(options => options.ApiKey = apiKey);
    }

    /// <summary>
    /// Registers the client and configures it in code.
    /// </summary>
    public static IHttpClientBuilder AddKapso(
        this IServiceCollection services,
        Action<KapsoClientOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Misconfiguration surfaces at startup rather than on the first API call.
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<KapsoClientOptions>, KapsoClientOptionsValidator>());

        var options = services.AddOptions<KapsoClientOptions>().ValidateOnStart();

        if (configure is not null)
        {
            options.Configure(configure);
        }

        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<KapsoRateLimitTracker>();
        services.TryAddTransient<KapsoResilienceContextHandler>();
        services.TryAddTransient<KapsoRateLimitTrackingHandler>();

        var builder = services.AddHttpClient<KapsoClient>(HttpClientName)
            .ConfigureHttpClient(static (provider, client) =>
            {
                var settings = provider.GetRequiredService<IOptions<KapsoClientOptions>>().Value;

                // When the resilience pipeline is in charge of timeouts, HttpClient's
                // own 100 second default would cut across the retry budget.
                if (settings.Retry.Enabled)
                {
                    client.Timeout = Timeout.InfiniteTimeSpan;
                }
            });

        // Handlers registered after this one sit inside it.
        builder.AddResilienceHandler("kapso", static (pipeline, context) =>
        {
            var settings = context.ServiceProvider.GetRequiredService<IOptions<KapsoClientOptions>>().Value;
            KapsoResiliencePipeline.Configure(pipeline, settings.Retry);
        });

        // Inside the resilience handler: it needs the resilience context, and it
        // must run on every attempt rather than once per call.
        builder.AddHttpMessageHandler<KapsoResilienceContextHandler>();

        // Innermost, so the recorded rate limit reflects the most recent attempt.
        builder.AddHttpMessageHandler<KapsoRateLimitTrackingHandler>();

        return builder;
    }
}
