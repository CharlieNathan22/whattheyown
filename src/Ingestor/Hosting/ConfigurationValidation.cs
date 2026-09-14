using Microsoft.Extensions.Options;

namespace Ingestor.Hosting;

/// <summary>
/// Unwraps options-validation failures from whatever the validator threw them inside.
/// </summary>
public static class ConfigurationValidation
{
    /// <summary>
    /// Collects every validation message reachable from <paramref name="exception"/>.
    /// </summary>
    /// <remarks>
    /// The startup validator wraps each failing options type in its own
    /// <see cref="OptionsValidationException"/> and those in an <see cref="AggregateException"/>,
    /// so all of them are reported at once rather than one missing setting per run.
    /// </remarks>
    /// <returns><c>true</c> if at least one validation message was found.</returns>
    public static bool TryGetFailures(Exception exception, out IReadOnlyList<string> failures)
    {
        var collected = new List<string>();
        Collect(exception, collected);
        failures = collected;

        return collected.Count > 0;
    }

    private static void Collect(Exception exception, List<string> collected)
    {
        switch (exception)
        {
            case AggregateException aggregate:
                foreach (var inner in aggregate.Flatten().InnerExceptions)
                {
                    Collect(inner, collected);
                }

                break;

            case OptionsValidationException validation:
                collected.AddRange(validation.Failures);
                break;

            default:
                if (exception.InnerException is not null)
                {
                    Collect(exception.InnerException, collected);
                }

                break;
        }
    }
}
