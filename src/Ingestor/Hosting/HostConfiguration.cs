using Ingestor.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;

namespace Ingestor.Hosting;

/// <summary>
/// Every registration the ingestor host makes. Lives apart from <c>Program</c> so the test project
/// can build the same service graph against a deliberately incomplete configuration and assert on
/// what the operator would actually see at 3am.
/// </summary>
public static class HostConfiguration
{
    /// <summary>Per-attempt HTTP budget. The pipeline, not <c>HttpClient.Timeout</c>, owns it.</summary>
    public static readonly TimeSpan HttpAttemptTimeout = TimeSpan.FromSeconds(30);

    /// <summary>Retries after the first attempt, so four calls at worst.</summary>
    public const int HttpMaxRetryAttempts = 3;

    /// <summary>
    /// Adds the configuration sources the host does not supply by default. Call after the defaults
    /// so environment values take precedence (ARCHITECTURE.md §14).
    /// </summary>
    public static IConfigurationBuilder AddIngestorConfiguration(this IConfigurationBuilder configuration) =>
        configuration.AddSecretEnvironmentVariableAliases();

    /// <summary>Console provider only — output goes to the Actions log. No Serilog, no file sinks.</summary>
    public static ILoggingBuilder AddIngestorLogging(this ILoggingBuilder logging)
    {
        logging.ClearProviders();
        logging.AddSimpleConsole(options =>
        {
            options.SingleLine = true;
            options.UseUtcTimestamp = true;
            options.TimestampFormat = "HH:mm:ss ";
        });

        return logging;
    }

    /// <summary>
    /// Binds and validates every options type. Requires an <see cref="IConfiguration"/> in the
    /// container, which the generic host registers for you.
    /// </summary>
    public static IServiceCollection AddIngestorOptions(this IServiceCollection services)
    {
        services.AddOptions<R2Options>()
            .BindConfiguration(R2Options.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<CloudflareOptions>()
            .BindConfiguration(CloudflareOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<ParliamentOptions>()
            .BindConfiguration(ParliamentOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddSingleton<IValidateOptions<ParliamentOptions>, ParliamentOptionsValidator>();

        return services;
    }

    /// <summary>
    /// Defaults applied to every typed client, so no client can be added later without a retry
    /// policy attached.
    /// </summary>
    public static IServiceCollection AddIngestorHttpClients(this IServiceCollection services)
    {
        services.ConfigureHttpClientDefaults(http =>
        {
            // HttpClient's own 100s cap would otherwise fire partway through a retry sequence and
            // surface as a bare TaskCanceledException with no indication of which attempt failed.
            http.ConfigureHttpClient(client => client.Timeout = Timeout.InfiniteTimeSpan);

            http.AddResilienceHandler("parliament", pipeline =>
            {
                // Retry outermost, timeout innermost: every attempt gets its own full budget.
                pipeline.AddRetry(new HttpRetryStrategyOptions
                {
                    MaxRetryAttempts = HttpMaxRetryAttempts,
                    BackoffType = DelayBackoffType.Exponential,
                    Delay = TimeSpan.FromSeconds(1),
                    UseJitter = true,
                });

                pipeline.AddTimeout(HttpAttemptTimeout);
            });
        });

        return services;
    }

    /// <summary>
    /// Runs the <c>ValidateOnStart</c> checks and collects their messages instead of throwing.
    /// </summary>
    /// <remarks>
    /// Called directly rather than via <c>host.StartAsync()</c>: the host logs its own
    /// unhandled-exception stack trace over the messages, and with no hosted services registered
    /// there is nothing else startup would do.
    /// </remarks>
    /// <returns><c>true</c> when the configuration is valid.</returns>
    public static bool TryValidateConfiguration(IServiceProvider services, out IReadOnlyList<string> failures)
    {
        try
        {
            services.GetRequiredService<IStartupValidator>().Validate();
        }
        catch (Exception ex) when (ConfigurationValidation.TryGetFailures(ex, out var collected))
        {
            failures = collected;
            return false;
        }

        failures = [];
        return true;
    }
}
