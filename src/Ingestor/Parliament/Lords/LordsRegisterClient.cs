using System.Text;
using System.Text.Json;
using Ingestor.Configuration;
using Ingestor.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Ingestor.Parliament.Lords;

/// <summary>
/// Typed <see cref="HttpClient"/> over <c>LordsInterests/Register</c>. The base address and the retry
/// pipeline come from the host registration; this class only walks the pages.
/// </summary>
public sealed class LordsRegisterClient : ILordsRegisterClient
{
    /// <summary>Fixed by the API and not adjustable — there is no <c>take</c> (§3.2).</summary>
    public const int PageSize = 20;

    private const string House = "lords";

    // Strict: invalid UTF-8 throws instead of silently becoming U+FFFD in the archive.
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    private readonly HttpClient _http;
    private readonly ParliamentOptions _options;
    private readonly ILogger<LordsRegisterClient> _logger;

    public LordsRegisterClient(HttpClient http, IOptions<ParliamentOptions> options, ILogger<LordsRegisterClient> logger)
    {
        _http = http;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>
    /// Pages needed to cover <paramref name="totalResults"/> members. Ceiling, not truncation:
    /// 815 → 41, where integer division would give 40 and drop the last 15 peers.
    /// </summary>
    public static int PageCountFor(int totalResults)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(totalResults);

        return (totalResults + PageSize - 1) / PageSize;
    }

    public async Task<LordsRegisterSnapshot> FetchAllAsync(CancellationToken cancellationToken)
    {
        // page is 1-indexed despite the docs saying 0; page 0 is a duplicate of page 1 (§3.2).
        var first = await FetchPageAsync(1, cancellationToken).ConfigureAwait(false);

        if (first.TotalResults <= 0)
        {
            throw new InvalidDataException(
                $"Lords register page 1 reported totalResults {first.TotalResults}; an empty register is not plausible.");
        }

        var pageCount = PageCountFor(first.TotalResults);
        var pages = new List<LordsRegisterPage>(pageCount) { first };

        _logger.LogInformation(
            "Lords register reports {TotalResults} members across {PageCount} pages.",
            first.TotalResults,
            pageCount);

        // Sequential by design: no published rate limit, so stay polite.
        for (var pageNumber = 2; pageNumber <= pageCount; pageNumber++)
        {
            await DelayBetweenPagesAsync(cancellationToken).ConfigureAwait(false);

            var page = await FetchPageAsync(pageNumber, cancellationToken).ConfigureAwait(false);

            if (page.TotalResults != first.TotalResults)
            {
                // Membership changed mid-run, so page boundaries shifted under us.
                throw new InvalidDataException(
                    $"Lords register totalResults changed mid-run: page 1 reported {first.TotalResults}, " +
                    $"page {pageNumber} reported {page.TotalResults}.");
            }

            pages.Add(page);
        }

        var distinct = pages.SelectMany(p => p.MemberIds).ToHashSet();

        if (distinct.Count != first.TotalResults)
        {
            throw new RegisterCoverageException(House, first.TotalResults, distinct.Count);
        }

        _logger.LogInformation(
            "Lords register coverage check passed: {DistinctMembers} distinct members = totalResults.",
            distinct.Count);

        return new LordsRegisterSnapshot(first.TotalResults, pages, distinct);
    }

    private async Task<LordsRegisterPage> FetchPageAsync(int pageNumber, CancellationToken cancellationToken)
    {
        var requestUri = $"LordsInterests/Register?page={pageNumber}&includeDeleted=true";

        using var response = await _http.GetAsync(requestUri, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
        var rawJson = StrictUtf8.GetString(body);

        var (totalResults, memberIds) = ReadMemberIds(body, pageNumber);

        _logger.LogDebug(
            "Fetched Lords register page {PageNumber}: {Bytes} bytes, {MemberCount} members.",
            pageNumber,
            body.Length,
            memberIds.Count);

        return new LordsRegisterPage(pageNumber, rawJson, totalResults, memberIds);
    }

    private static (int TotalResults, IReadOnlyList<int> MemberIds) ReadMemberIds(byte[] body, int pageNumber)
    {
        RegisterPageDto? dto;
        try
        {
            dto = JsonSerializer.Deserialize<RegisterPageDto>(body, JsonDefaults.Options);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"Lords register page {pageNumber} is not valid JSON.", ex);
        }

        if (dto?.TotalResults is not { } totalResults || dto.Items is null)
        {
            throw new InvalidDataException(
                $"Lords register page {pageNumber} is missing 'items' or 'totalResults'; the response shape differs from ARCHITECTURE.md §3.2.");
        }

        var memberIds = new List<int>(dto.Items.Count);
        for (var i = 0; i < dto.Items.Count; i++)
        {
            if (dto.Items[i]?.Value?.Member?.Id is not { } id)
            {
                throw new InvalidDataException(
                    $"Lords register page {pageNumber}, item {i} has no 'value.member.id'; the response shape differs from ARCHITECTURE.md §3.2.");
            }

            memberIds.Add(id);
        }

        return (totalResults, memberIds);
    }

    private Task DelayBetweenPagesAsync(CancellationToken cancellationToken) =>
        _options.RequestDelayMs > 0
            ? Task.Delay(_options.RequestDelayMs, cancellationToken)
            : Task.CompletedTask;

    // Only what this client needs. Everything else stays in RawJson for the archive.
    private sealed record RegisterPageDto(List<RegisterItemDto?>? Items, int? TotalResults);

    private sealed record RegisterItemDto(RegisterItemValueDto? Value);

    private sealed record RegisterItemValueDto(MemberDto? Member);

    private sealed record MemberDto(int? Id);
}
