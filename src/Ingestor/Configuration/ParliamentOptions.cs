using System.ComponentModel.DataAnnotations;

namespace Ingestor.Configuration;

/// <summary>
/// Base addresses of the two Parliament APIs. Bound from the <c>Parliament</c> configuration section.
/// </summary>
/// <remarks>
/// Two APIs, not one client with a parameter: the Commons Interests API and the Lords register
/// endpoint on the Members API have different shapes (ARCHITECTURE.md §3).
/// </remarks>
public sealed class ParliamentOptions
{
    public const string SectionName = "Parliament";

    /// <summary>Commons Interests API, e.g. <c>https://interests-api.parliament.uk/api/v1/</c>.</summary>
    [Required(AllowEmptyStrings = false, ErrorMessage = "Parliament:InterestsApiBaseUrl is not set (appsettings.json).")]
    public string InterestsApiBaseUrl { get; init; } = string.Empty;

    /// <summary>Members API, e.g. <c>https://members-api.parliament.uk/api/</c>.</summary>
    [Required(AllowEmptyStrings = false, ErrorMessage = "Parliament:MembersApiBaseUrl is not set (appsettings.json).")]
    public string MembersApiBaseUrl { get; init; } = string.Empty;

    /// <summary>
    /// Pause between sequential page requests. Neither API publishes a rate limit, so a 41-page Lords
    /// run stays polite. Tests set it to 0.
    /// </summary>
    [Range(0, 60_000, ErrorMessage = "Parliament:RequestDelayMs must be between 0 and 60000.")]
    public int RequestDelayMs { get; init; } = 500;
}
