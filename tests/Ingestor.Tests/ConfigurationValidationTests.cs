using Ingestor.Configuration;
using Ingestor.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Ingestor.Tests;

/// <summary>
/// Options validation is load-bearing: it is the difference between a 3am cron failure that names
/// the missing setting and a null reference three layers deep in an HTTP call. These tests assert
/// on the message the operator actually reads.
/// </summary>
public class ConfigurationValidationTests
{
    private static readonly Dictionary<string, string?> CompleteConfiguration = new()
    {
        ["R2:AccessKeyId"] = "test-access-key-id",
        ["R2:SecretAccessKey"] = "test-secret-access-key",
        ["R2:BucketName"] = "whattheyown-raw",
        ["Cloudflare:AccountId"] = "test-account-id",
        ["Parliament:InterestsApiBaseUrl"] = "https://interests-api.parliament.uk/api/v1/",
        ["Parliament:MembersApiBaseUrl"] = "https://members-api.parliament.uk/api/",
    };

    [Fact]
    public void CompleteConfiguration_Validates()
    {
        var valid = Validate(CompleteConfiguration, out var failures);

        Assert.True(valid, string.Join(" | ", failures));
        Assert.Empty(failures);
    }

    [Theory]
    [InlineData("R2:AccessKeyId", "R2_ACCESS_KEY_ID")]
    [InlineData("R2:SecretAccessKey", "R2_SECRET_ACCESS_KEY")]
    [InlineData("R2:BucketName", "R2_BUCKET_NAME")]
    [InlineData("Cloudflare:AccountId", "CF_ACCOUNT_ID")]
    [InlineData("Parliament:InterestsApiBaseUrl", null)]
    [InlineData("Parliament:MembersApiBaseUrl", null)]
    public void MissingSetting_FailsNamingTheSettingAndWhereToSetIt(string key, string? environmentVariable)
    {
        var configuration = Without(key);

        var valid = Validate(configuration, out var failures);

        Assert.False(valid);
        var failure = Assert.Single(failures);
        Assert.Contains(key, failure, StringComparison.Ordinal);

        if (environmentVariable is not null)
        {
            Assert.Contains(environmentVariable, failure, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void EveryMissingSetting_IsReportedInOneRun_NotOnePerRun()
    {
        var configuration = new Dictionary<string, string?>(CompleteConfiguration);
        configuration.Remove("R2:AccessKeyId");
        configuration.Remove("R2:SecretAccessKey");
        configuration.Remove("Cloudflare:AccountId");

        var valid = Validate(configuration, out var failures);

        Assert.False(valid);
        Assert.Equal(3, failures.Count);
        Assert.Contains(failures, f => f.Contains("R2:AccessKeyId", StringComparison.Ordinal));
        Assert.Contains(failures, f => f.Contains("R2:SecretAccessKey", StringComparison.Ordinal));
        Assert.Contains(failures, f => f.Contains("Cloudflare:AccountId", StringComparison.Ordinal));
    }

    [Fact]
    public void BlankSetting_IsTreatedAsMissing_NotAsAValidEmptyCredential()
    {
        var configuration = new Dictionary<string, string?>(CompleteConfiguration)
        {
            ["R2:SecretAccessKey"] = "   ",
        };

        var valid = Validate(configuration, out var failures);

        Assert.False(valid);
        Assert.Contains(failures, f => f.Contains("R2:SecretAccessKey", StringComparison.Ordinal));
    }

    [Fact]
    public void BaseUrlWithoutTrailingSlash_Fails_BecauseItSilentlyTruncatesThePath()
    {
        // new Uri("https://members-api.parliament.uk/api", "LordsInterests/Register") drops "/api".
        var configuration = new Dictionary<string, string?>(CompleteConfiguration)
        {
            ["Parliament:MembersApiBaseUrl"] = "https://members-api.parliament.uk/api",
        };

        var valid = Validate(configuration, out var failures);

        Assert.False(valid);
        var failure = Assert.Single(failures);
        Assert.Contains("Parliament:MembersApiBaseUrl", failure, StringComparison.Ordinal);
        Assert.Contains("must end with '/'", failure, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("members-api.parliament.uk/api/")]
    [InlineData("ftp://members-api.parliament.uk/api/")]
    [InlineData("not a url")]
    public void BaseUrlThatIsNotAnAbsoluteHttpUri_Fails(string value)
    {
        var configuration = new Dictionary<string, string?>(CompleteConfiguration)
        {
            ["Parliament:MembersApiBaseUrl"] = value,
        };

        var valid = Validate(configuration, out var failures);

        Assert.False(valid);
        var failure = Assert.Single(failures);
        Assert.Contains("Parliament:MembersApiBaseUrl", failure, StringComparison.Ordinal);
        Assert.Contains("absolute http(s) URI", failure, StringComparison.Ordinal);
    }

    [Fact]
    public void MissingBaseUrl_IsReportedOnce_NotAsBothAbsentAndMalformed()
    {
        var valid = Validate(Without("Parliament:MembersApiBaseUrl"), out var failures);

        Assert.False(valid);
        Assert.Single(failures);
    }

    private static Dictionary<string, string?> Without(string key)
    {
        var configuration = new Dictionary<string, string?>(CompleteConfiguration);
        configuration.Remove(key);

        return configuration;
    }

    /// <summary>
    /// Builds the same service graph the host builds and runs the real ValidateOnStart pass over it.
    /// </summary>
    private static bool Validate(Dictionary<string, string?> values, out IReadOnlyList<string> failures)
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton(configuration);
        services.AddIngestorOptions();

        using var provider = services.BuildServiceProvider();

        return HostConfiguration.TryValidateConfiguration(provider, out failures);
    }
}

/// <summary>
/// The GitHub secret names do not follow the default <c>__</c> convention, so the mapping between
/// them and the configuration keys is hand-written and worth pinning down.
/// </summary>
public class SecretEnvironmentVariableTests
{
    [Theory]
    [InlineData("R2_ACCESS_KEY_ID", "R2:AccessKeyId")]
    [InlineData("R2_SECRET_ACCESS_KEY", "R2:SecretAccessKey")]
    [InlineData("R2_BUCKET_NAME", "R2:BucketName")]
    [InlineData("CF_ACCOUNT_ID", "Cloudflare:AccountId")]
    public void DocumentedSecretName_BindsToItsConfigurationKey(string environmentVariable, string configurationKey)
    {
        var resolved = SecretEnvironmentVariables.Resolve(
            name => name == environmentVariable ? "a-value" : null);

        Assert.Equal("a-value", Assert.Contains(configurationKey, resolved));
    }

    [Fact]
    public void UnsetOrBlankSecret_IsOmitted_SoALowerPrecedenceSourceStillApplies()
    {
        var resolved = SecretEnvironmentVariables.Resolve(
            name => name == "R2_BUCKET_NAME" ? "   " : null);

        Assert.Empty(resolved);
    }

    [Fact]
    public void CloudflareApiTokenAndCompaniesHouseKey_AreNotBoundIntoThisProcess()
    {
        var names = SecretEnvironmentVariables.Aliases.Select(a => a.EnvironmentVariable).ToList();

        Assert.DoesNotContain("CF_API_TOKEN", names);
        Assert.DoesNotContain("CH_API_KEY", names);
    }
}
