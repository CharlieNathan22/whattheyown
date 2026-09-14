using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Web;

namespace Ingestor.Tests.Fakes;

/// <summary>
/// Serves <c>fixtures/lords-full/</c> in place of the Members API register endpoint. Records every
/// request and the peak number of requests in flight, so tests can assert on how the client paged.
/// </summary>
public sealed class FixtureLordsRegisterHandler : HttpMessageHandler
{
    public const string BaseAddress = "https://members-api.test/api/";

    private readonly Func<int, int> _servePageFor;
    private int _inFlight;
    private int _maxInFlight;

    /// <param name="servePageFor">
    /// Maps the requested <c>page</c> to the fixture page served. Identity by default; override it to
    /// simulate a pagination regression.
    /// </param>
    public FixtureLordsRegisterHandler(Func<int, int>? servePageFor = null)
    {
        _servePageFor = servePageFor ?? (page => page);
    }

    public ConcurrentQueue<Uri> Requests { get; } = new();

    public int MaxConcurrentRequests => _maxInFlight;

    public IEnumerable<int> RequestedPages =>
        Requests.Select(uri => int.Parse(HttpUtility.ParseQueryString(uri.Query)["page"]!));

    public static string FixturePath(int page) =>
        Path.Combine(FixturePaths.FixturesDirectory, "lords-full", $"page-{page:D4}.json");

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var inFlight = Interlocked.Increment(ref _inFlight);
        UpdateMax(inFlight);

        try
        {
            // Yield so overlapping requests from a parallel caller would actually overlap.
            await Task.Yield();
            cancellationToken.ThrowIfCancellationRequested();

            var uri = request.RequestUri!;
            Requests.Enqueue(uri);

            if (request.Method != HttpMethod.Get || uri.AbsolutePath != "/api/LordsInterests/Register")
            {
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            }

            var query = HttpUtility.ParseQueryString(uri.Query);
            if (!int.TryParse(query["page"], out var page) || query["includeDeleted"] != "true")
            {
                return new HttpResponseMessage(HttpStatusCode.BadRequest);
            }

            var path = FixturePath(_servePageFor(page));
            if (!File.Exists(path))
            {
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            }

            var content = new ByteArrayContent(await File.ReadAllBytesAsync(path, cancellationToken));
            content.Headers.ContentType = new MediaTypeHeaderValue("application/json") { CharSet = "utf-8" };

            return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
        }
        finally
        {
            Interlocked.Decrement(ref _inFlight);
        }
    }

    private void UpdateMax(int candidate)
    {
        int current;
        while (candidate > (current = _maxInFlight) &&
               Interlocked.CompareExchange(ref _maxInFlight, candidate, current) != current)
        {
        }
    }
}
