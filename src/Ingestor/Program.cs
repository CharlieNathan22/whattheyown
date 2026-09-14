using System.Text;
using Ingestor.Configuration;
using Ingestor.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

// Exit codes are read by the GitHub Actions workflow.
const int ExitSuccess = 0;
const int ExitFailed = 1;
const int ExitConfigurationInvalid = 2;

// Windows terminals otherwise render UTF-8 as CP1252 and make a correct '£' look broken.
// Console output is never evidence either way (ARCHITECTURE.md §14) — this is for the Actions log.
Console.OutputEncoding = Encoding.UTF8;

// ContentRootPath is pinned to the binary's directory so appsettings.json resolves the same way
// under `dotnet run --project src/Ingestor` from the repo root as it does from a published output.
var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
{
    ContentRootPath = AppContext.BaseDirectory,
});

// The host defaults give us appsettings.json, appsettings.{Environment}.json, user secrets in
// Development, then environment variables. The aliases go last, so environment values win.
builder.Configuration.AddIngestorConfiguration();

builder.Logging.AddIngestorLogging();

builder.Services
    .AddIngestorOptions()
    .AddIngestorHttpClients();

using var host = builder.Build();

var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Ingestor");

if (!HostConfiguration.TryValidateConfiguration(host.Services, out var configurationFailures))
{
    foreach (var failure in configurationFailures)
    {
        logger.LogError("Configuration error: {Failure}", failure);
    }

    return ExitConfigurationInvalid;
}

try
{
    var parliament = host.Services.GetRequiredService<IOptions<ParliamentOptions>>().Value;
    var r2 = host.Services.GetRequiredService<IOptions<R2Options>>().Value;

    // Never log R2 credentials or anything derived from them.
    logger.LogInformation(
        "Ingestor configured. Environment={Environment} Commons={InterestsApi} Lords={MembersApi} Archive={Bucket}",
        builder.Environment.EnvironmentName,
        parliament.InterestsApiBaseUrl,
        parliament.MembersApiBaseUrl,
        r2.BucketName);

    // Phase 2 onwards hangs off here: fetch, assert coverage, archive, diff, export.

    return ExitSuccess;
}
catch (Exception ex)
{
    logger.LogError(ex, "Ingestor run failed.");
    return ExitFailed;
}
