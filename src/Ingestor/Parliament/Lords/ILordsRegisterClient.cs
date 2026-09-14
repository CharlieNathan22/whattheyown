namespace Ingestor.Parliament.Lords;

/// <summary>
/// Fetches the full Lords register from <c>LordsInterests/Register</c> on the Members API
/// (ARCHITECTURE.md §3.2). Fetch only: no parsing beyond member IDs, no archiving.
/// </summary>
public interface ILordsRegisterClient
{
    /// <summary>
    /// Fetches every page with <c>includeDeleted=true</c>, sequentially, and asserts that the distinct
    /// member count equals <c>totalResults</c>.
    /// </summary>
    /// <exception cref="RegisterCoverageException">
    /// The pages did not cover every member. No partial set is ever returned.
    /// </exception>
    /// <exception cref="InvalidDataException">A page did not have the shape §3.2 documents.</exception>
    Task<LordsRegisterSnapshot> FetchAllAsync(CancellationToken cancellationToken);
}

/// <summary>One page of the register as received.</summary>
/// <param name="PageNumber">The 1-indexed <c>page</c> parameter this was requested with.</param>
/// <param name="RawJson">
/// The response body exactly as received, decoded as UTF-8. The archive writer stores this, never a
/// re-serialised round trip.
/// </param>
/// <param name="TotalResults">The page's own <c>totalResults</c>.</param>
/// <param name="MemberIds">Member IDs on this page, in response order.</param>
public sealed record LordsRegisterPage(
    int PageNumber,
    string RawJson,
    int TotalResults,
    IReadOnlyList<int> MemberIds);

/// <summary>A complete, coverage-checked fetch of the Lords register.</summary>
public sealed record LordsRegisterSnapshot(
    int TotalResults,
    IReadOnlyList<LordsRegisterPage> Pages,
    IReadOnlySet<int> DistinctMemberIds);
