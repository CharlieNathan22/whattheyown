namespace Ingestor.Tests.Fakes;

public static class FixturePaths
{
    public static string FixturesDirectory { get; } = Find();

    private static string Find()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "fixtures");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException(
            $"Could not locate a 'fixtures' directory above {AppContext.BaseDirectory}.");
    }
}
