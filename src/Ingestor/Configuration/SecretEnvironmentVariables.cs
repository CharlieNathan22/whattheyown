using Microsoft.Extensions.Configuration;

namespace Ingestor.Configuration;

/// <summary>
/// Maps the deployment's documented secret environment variable names onto configuration keys.
/// </summary>
/// <remarks>
/// The secrets are named <c>R2_ACCESS_KEY_ID</c>, <c>CF_ACCOUNT_ID</c> and so on in the GitHub repo
/// settings, which the default <c>__</c> environment-variable convention would never bind to
/// <c>R2:AccessKeyId</c> or <c>Cloudflare:AccountId</c>. Registered after every other source so
/// environment values win, per ARCHITECTURE.md §14.
/// </remarks>
public static class SecretEnvironmentVariables
{
    /// <summary>
    /// The documented secret names and the configuration keys they bind to.
    /// </summary>
    /// <remarks>
    /// <c>CF_API_TOKEN</c> and <c>CH_API_KEY</c> are deliberately absent: the first belongs to
    /// wrangler in CI, the second to a phase that does not exist yet. Neither reaches this process.
    /// </remarks>
    public static readonly IReadOnlyList<(string EnvironmentVariable, string ConfigurationKey)> Aliases =
    [
        ("R2_ACCESS_KEY_ID", "R2:AccessKeyId"),
        ("R2_SECRET_ACCESS_KEY", "R2:SecretAccessKey"),
        ("R2_BUCKET_NAME", "R2:BucketName"),
        ("CF_ACCOUNT_ID", "Cloudflare:AccountId"),
    ];

    /// <summary>
    /// Resolves the aliases through <paramref name="readEnvironmentVariable"/>, skipping any that
    /// are unset or blank so they fall through to a lower-precedence source.
    /// </summary>
    /// <remarks>
    /// Takes the reader as a parameter rather than calling <see cref="Environment"/> directly so
    /// the mapping can be tested without mutating process-wide state.
    /// </remarks>
    public static Dictionary<string, string?> Resolve(Func<string, string?> readEnvironmentVariable)
    {
        var values = new Dictionary<string, string?>();

        foreach (var (environmentVariable, configurationKey) in Aliases)
        {
            var value = readEnvironmentVariable(environmentVariable);
            if (!string.IsNullOrWhiteSpace(value))
            {
                values[configurationKey] = value;
            }
        }

        return values;
    }

    public static IConfigurationBuilder AddSecretEnvironmentVariableAliases(this IConfigurationBuilder builder)
    {
        var values = Resolve(Environment.GetEnvironmentVariable);

        return values.Count == 0 ? builder : builder.AddInMemoryCollection(values);
    }
}
