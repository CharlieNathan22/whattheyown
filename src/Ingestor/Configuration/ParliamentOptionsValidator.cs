using Microsoft.Extensions.Options;

namespace Ingestor.Configuration;

/// <summary>
/// Checks the Parliament base addresses beyond presence: both must be absolute http(s) URIs ending in
/// a slash, because typed clients build every request from a relative path against them. A missing
/// trailing slash silently truncates the last path segment of the base address.
/// </summary>
internal sealed class ParliamentOptionsValidator : IValidateOptions<ParliamentOptions>
{
    public ValidateOptionsResult Validate(string? name, ParliamentOptions options)
    {
        var failures = new List<string>();

        Check("Parliament:InterestsApiBaseUrl", options.InterestsApiBaseUrl, failures);
        Check("Parliament:MembersApiBaseUrl", options.MembersApiBaseUrl, failures);

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }

    private static void Check(string key, string value, List<string> failures)
    {
        // Presence is covered by the [Required] annotation; don't report it twice.
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            failures.Add($"{key} is not an absolute http(s) URI (value: '{value}').");
            return;
        }

        if (!value.EndsWith('/'))
        {
            failures.Add($"{key} must end with '/' so relative request paths resolve correctly (value: '{value}').");
        }
    }
}
