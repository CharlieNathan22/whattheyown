using System.ComponentModel.DataAnnotations;

namespace Ingestor.Configuration;

/// <summary>
/// Cloudflare R2 credentials for the raw-response archive.
/// Bound from the <c>R2</c> configuration section.
/// </summary>
public sealed class R2Options
{
    public const string SectionName = "R2";

    /// <summary>R2 access key id. Supplied via the <c>R2_ACCESS_KEY_ID</c> environment variable in CI.</summary>
    [Required(AllowEmptyStrings = false, ErrorMessage = "R2:AccessKeyId is not set (environment variable R2_ACCESS_KEY_ID, or user secret 'R2:AccessKeyId').")]
    public string AccessKeyId { get; init; } = string.Empty;

    /// <summary>R2 secret access key. Supplied via the <c>R2_SECRET_ACCESS_KEY</c> environment variable in CI.</summary>
    [Required(AllowEmptyStrings = false, ErrorMessage = "R2:SecretAccessKey is not set (environment variable R2_SECRET_ACCESS_KEY, or user secret 'R2:SecretAccessKey').")]
    public string SecretAccessKey { get; init; } = string.Empty;

    /// <summary>Bucket holding the verbatim archive. Defaults from appsettings.json.</summary>
    [Required(AllowEmptyStrings = false, ErrorMessage = "R2:BucketName is not set (appsettings.json, or environment variable R2_BUCKET_NAME).")]
    public string BucketName { get; init; } = string.Empty;
}
