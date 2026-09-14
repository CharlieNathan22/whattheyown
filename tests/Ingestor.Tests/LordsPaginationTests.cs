using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ingestor.Tests;

public class LordsPaginationTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private sealed record RegisterPage(
        [property: JsonPropertyName("items")] List<RegisterItem> Items,
        [property: JsonPropertyName("totalResults")] int TotalResults);

    private sealed record RegisterItem(
        [property: JsonPropertyName("value")] RegisterItemValue Value);

    private sealed record RegisterItemValue(
        [property: JsonPropertyName("member")] Member Member);

    private sealed record Member(
        [property: JsonPropertyName("id")] int Id);

    [Fact]
    public void LordsRegisterPage1And2_MustHaveDisjointMemberIds_BecausePageIs1IndexedNot0Indexed()
    {
        var page1 = LoadMemberIds("page-0001.json");
        var page2 = LoadMemberIds("page-0002.json");

        Assert.NotEmpty(page1);
        Assert.NotEmpty(page2);
        Assert.Empty(page1.Intersect(page2));
    }

    [Fact]
    public void AllLordsPages_ContainExactly815DistinctMembers()
    {
        var allIds = new HashSet<int>();
        int? totalResults = null;
        var fixturesDirectory = FindFixturesDirectory();

        for (var page = 1; page <= 41; page++)
        {
            var fixtureFileName = $"page-{page:D4}.json";
            var path = Path.Combine(fixturesDirectory, "lords-full", fixtureFileName);
            var json = File.ReadAllText(path, Encoding.UTF8);
            var parsed = JsonSerializer.Deserialize<RegisterPage>(json, JsonOptions)!;

            Assert.NotEmpty(parsed.Items);
            totalResults ??= parsed.TotalResults;

            foreach (var item in parsed.Items)
            {
                allIds.Add(item.Value.Member.Id);
            }
        }

        Assert.Equal(815, totalResults);
        Assert.Equal(totalResults, allIds.Count);
    }

    private static HashSet<int> LoadMemberIds(string fixtureFileName)
    {
        var path = Path.Combine(FindFixturesDirectory(), "lords-full", fixtureFileName);
        var json = File.ReadAllText(path, Encoding.UTF8);
        var page = JsonSerializer.Deserialize<RegisterPage>(json, JsonOptions);

        return page!.Items.Select(i => i.Value.Member.Id).ToHashSet();
    }

    private static string FindFixturesDirectory() => Fakes.FixturePaths.FixturesDirectory;
}