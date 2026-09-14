using System.Text.Json;

namespace Ingestor.Serialization;

/// <summary>
/// The single <see cref="JsonSerializerOptions"/> instance used for every Parliament payload.
/// </summary>
/// <remarks>
/// Held as a static singleton deliberately: <see cref="JsonSerializerOptions"/> caches its
/// reflection metadata on first use, so a fresh instance per call silently costs a full
/// re-scan. System.Text.Json only — no Newtonsoft (ARCHITECTURE.md §14).
/// </remarks>
public static class JsonDefaults
{
    /// <summary>
    /// Case-insensitive because the two APIs disagree on casing across endpoints.
    /// </summary>
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
    };
}
