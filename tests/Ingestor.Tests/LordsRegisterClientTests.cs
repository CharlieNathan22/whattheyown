using System.Text;
using Ingestor.Configuration;
using Ingestor.Hosting;
using Ingestor.Parliament;
using Ingestor.Parliament.Lords;
using Ingestor.Tests.Fakes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Ingestor.Tests;

public class LordsRegisterClientTests
{
    private const int FixtureTotalResults = 815;
    private const int FixturePageCount = 41;

    [Fact]
    public async Task FetchAll_FetchesEveryPage_And815DistinctMembersEqualTotalResults()
    {
        var handler = new FixtureLordsRegisterHandler();

        var snapshot = await CreateClient(handler).FetchAllAsync(CancellationToken.None);

        Assert.Equal(FixtureTotalResults, snapshot.TotalResults);
        Assert.Equal(FixtureTotalResults, snapshot.DistinctMemberIds.Count);
        Assert.Equal(snapshot.TotalResults, snapshot.DistinctMemberIds.Count);
        Assert.Equal(Enumerable.Range(1, FixturePageCount), snapshot.Pages.Select(p => p.PageNumber));
        Assert.Equal(15, snapshot.Pages[^1].MemberIds.Count);
    }

    [Fact]
    public async Task FetchAll_RequestsPages1To41InOrder_NeverPage0_WithIncludeDeleted()
    {
        var handler = new FixtureLordsRegisterHandler();

        await CreateClient(handler).FetchAllAsync(CancellationToken.None);

        Assert.Equal(Enumerable.Range(1, FixturePageCount), handler.RequestedPages);
        Assert.All(handler.Requests, uri => Assert.Contains("includeDeleted=true", uri.Query, StringComparison.Ordinal));
    }

    [Fact]
    public async Task FetchAll_NeverRequestsInParallel()
    {
        var handler = new FixtureLordsRegisterHandler();

        await CreateClient(handler).FetchAllAsync(CancellationToken.None);

        Assert.Equal(1, handler.MaxConcurrentRequests);
    }

    [Fact]
    public async Task FetchAll_HandlerServingPage1Twice_Throws_RatherThanReturningAPartialSet()
    {
        // Page 2 comes back as page 1: 20 members short.
        var handler = new FixtureLordsRegisterHandler(page => page == 2 ? 1 : page);

        var ex = await Assert.ThrowsAsync<RegisterCoverageException>(
            () => CreateClient(handler).FetchAllAsync(CancellationToken.None));

        Assert.Equal("lords", ex.House);
        Assert.Equal(FixtureTotalResults, ex.ExpectedMembers);
        Assert.Equal(FixtureTotalResults - 20, ex.DistinctMembers);
    }

    [Fact]
    public async Task FetchAll_HandlerServingPage1Forever_Throws()
    {
        // The regression §3.2 warns about: a plausible-looking site built on 2% of the data.
        var handler = new FixtureLordsRegisterHandler(_ => 1);

        var ex = await Assert.ThrowsAsync<RegisterCoverageException>(
            () => CreateClient(handler).FetchAllAsync(CancellationToken.None));

        Assert.Equal(20, ex.DistinctMembers);
    }

    [Theory]
    [InlineData(815, 41)]
    [InlineData(800, 40)]
    [InlineData(801, 41)]
    [InlineData(820, 41)]
    [InlineData(821, 42)]
    [InlineData(20, 1)]
    [InlineData(1, 1)]
    [InlineData(0, 0)]
    public void PageCountFor_Ceilings(int totalResults, int expectedPages)
    {
        Assert.Equal(expectedPages, LordsRegisterClient.PageCountFor(totalResults));
    }

    [Fact]
    public async Task FetchAll_RawJsonIsTheUntouchedResponseBody_AndPoundSignSurvives()
    {
        var handler = new FixtureLordsRegisterHandler();

        var snapshot = await CreateClient(handler).FetchAllAsync(CancellationToken.None);

        foreach (var page in snapshot.Pages)
        {
            var fixtureBytes = await File.ReadAllBytesAsync(FixtureLordsRegisterHandler.FixturePath(page.PageNumber));
            Assert.True(
                fixtureBytes.AsSpan().SequenceEqual(Encoding.UTF8.GetBytes(page.RawJson)),
                $"Page {page.PageNumber} RawJson differs from the bytes served.");
        }

        Assert.Contains("£15,000", snapshot.Pages[0].RawJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FetchAll_HonoursCancellation()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => CreateClient(new FixtureLordsRegisterHandler()).FetchAllAsync(cts.Token));
    }

    [Fact]
    public void HostRegistration_ResolvesTypedClient()
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Parliament:InterestsApiBaseUrl"] = "https://interests-api.parliament.uk/api/v1/",
                ["Parliament:MembersApiBaseUrl"] = "https://members-api.parliament.uk/api/",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton(configuration);
        services.AddLogging();
        services.AddIngestorOptions().AddIngestorHttpClients();

        using var provider = services.BuildServiceProvider();

        Assert.IsType<LordsRegisterClient>(provider.GetRequiredService<ILordsRegisterClient>());
        Assert.Equal(500, provider.GetRequiredService<IOptions<ParliamentOptions>>().Value.RequestDelayMs);
    }

    private static LordsRegisterClient CreateClient(HttpMessageHandler handler)
    {
        var http = new HttpClient(handler) { BaseAddress = new Uri(FixtureLordsRegisterHandler.BaseAddress) };
        var options = Options.Create(new ParliamentOptions
        {
            InterestsApiBaseUrl = "https://interests-api.test/api/v1/",
            MembersApiBaseUrl = FixtureLordsRegisterHandler.BaseAddress,
            RequestDelayMs = 0,
        });

        return new LordsRegisterClient(http, options, NullLogger<LordsRegisterClient>.Instance);
    }
}
