using System.ComponentModel.DataAnnotations;

namespace Ingestor.Configuration;

/// <summary>
/// Cloudflare account identity. Bound from the <c>Cloudflare</c> configuration section.
/// </summary>
/// <remarks>
/// The D1 API token (<c>CF_API_TOKEN</c>) is deliberately absent: it is consumed by wrangler in CI,
/// never by the ingestor, so it stays out of this process.
/// </remarks>
public sealed class CloudflareOptions
{
    public const string SectionName = "Cloudflare";

    /// <summary>Cloudflare account id. Supplied via the <c>CF_ACCOUNT_ID</c> environment variable in CI.</summary>
    [Required(AllowEmptyStrings = false, ErrorMessage = "Cloudflare:AccountId is not set (environment variable CF_ACCOUNT_ID, or user secret 'Cloudflare:AccountId').")]
    public string AccountId { get; init; } = string.Empty;
}
