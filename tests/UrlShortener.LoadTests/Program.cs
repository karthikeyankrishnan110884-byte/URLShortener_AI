using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

// Redirect load test for the URL Shortener API.
//   dotnet run -c Release --project tests/UrlShortener.LoadTests -- --base-url http://localhost:5104 --duration 20 --concurrency 64
// Measures redirect latency/throughput, then proves no click events were lost (sum of stats == successful redirects).
var options = LoadOptions.Parse(args);
var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);

using var handler = new SocketsHttpHandler
{
    AllowAutoRedirect = false,
    MaxConnectionsPerServer = options.Concurrency,
    PooledConnectionLifetime = TimeSpan.FromMinutes(5),
};
using var http = new HttpClient(handler) { BaseAddress = new Uri(options.BaseUrl), Timeout = TimeSpan.FromSeconds(10) };

Console.WriteLine($"Target {options.BaseUrl} | links={options.Links} concurrency={options.Concurrency} duration={options.DurationSeconds}s warmup={options.WarmupSeconds}s");

var codes = new List<string>();
for (var i = 0; i < options.Links; i++)
{
    var response = await http.PostAsJsonAsync("/api/v1/links", new { url = $"https://example.com/load/{i}" });
    response.EnsureSuccessStatusCode();
    var created = await response.Content.ReadFromJsonAsync<JsonElement>(json);
    codes.Add(created.GetProperty("code").GetString()!);
}

var warmup = await RunAsync(TimeSpan.FromSeconds(options.WarmupSeconds), record: false);
var result = await RunAsync(TimeSpan.FromSeconds(options.DurationSeconds), record: true);

// Event-loss check: analytics are async, so poll until the totals settle.
var expected = warmup.Successes + result.Successes;
long counted = 0;
var deadline = DateTime.UtcNow.AddSeconds(15);
do
{
    counted = 0;
    foreach (var code in codes)
    {
        var stats = await http.GetFromJsonAsync<JsonElement>($"/api/v1/links/{code}/stats", json);
        counted += stats.GetProperty("totalClicks").GetInt64();
    }

    if (counted >= expected)
    {
        break;
    }

    await Task.Delay(200);
}
while (DateTime.UtcNow < deadline);

var lat = result.LatenciesMs;
lat.Sort();
string P(double q) => lat.Count == 0 ? "n/a" : lat[Math.Min(lat.Count - 1, (int)Math.Ceiling(q * lat.Count) - 1)].ToString("0.00", CultureInfo.InvariantCulture);

Console.WriteLine();
Console.WriteLine("| label | requests | rps | p50 ms | p95 ms | p99 ms | max ms | non-302 | clicks recorded / redirects |");
Console.WriteLine("|---|---|---|---|---|---|---|---|---|");
Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
    $"| {options.Label} | {lat.Count} | {lat.Count / options.DurationSeconds:0} | {P(0.50)} | {P(0.95)} | {P(0.99)} | {P(1.0)} | {result.Errors} | {counted} / {expected} |"));

var lost = expected - counted;
if (result.Errors > 0 || lost != 0)
{
    Console.Error.WriteLine($"FAIL: errors={result.Errors} lostEvents={lost}");
    return 1;
}

if (options.P95BudgetMs is { } budget && lat.Count > 0 && double.Parse(P(0.95), CultureInfo.InvariantCulture) > budget)
{
    Console.Error.WriteLine($"FAIL: p95 above budget of {budget} ms");
    return 2;
}

return 0;

async Task<RunResult> RunAsync(TimeSpan duration, bool record)
{
    var stopAt = Stopwatch.GetTimestamp() + (long)(duration.TotalSeconds * Stopwatch.Frequency);
    var workers = Enumerable.Range(0, options.Concurrency).Select(async worker =>
    {
        var local = new List<double>(capacity: 4096);
        long successes = 0, errors = 0;
        var i = worker;
        while (Stopwatch.GetTimestamp() < stopAt)
        {
            var code = codes[i++ % codes.Count];
            var start = Stopwatch.GetTimestamp();
            try
            {
                using var response = await http.GetAsync($"/{code}", HttpCompletionOption.ResponseHeadersRead);
                var elapsed = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                if (response.StatusCode == HttpStatusCode.Found)
                {
                    successes++;
                    if (record)
                    {
                        local.Add(elapsed);
                    }
                }
                else
                {
                    errors++;
                }
            }
            catch (HttpRequestException)
            {
                errors++;
            }
            catch (TaskCanceledException)
            {
                errors++;
            }
        }

        return (local, successes, errors);
    }).ToArray();

    var done = await Task.WhenAll(workers);
    return new RunResult(done.SelectMany(d => d.local).ToList(), done.Sum(d => d.errors), done.Sum(d => d.successes));
}

internal sealed record RunResult(List<double> LatenciesMs, long Errors, long Successes);

internal sealed record LoadOptions(string BaseUrl, int DurationSeconds, int WarmupSeconds, int Concurrency, int Links, string Label, double? P95BudgetMs)
{
    public static LoadOptions Parse(string[] args)
    {
        string Get(string name, string fallback)
        {
            var i = Array.IndexOf(args, "--" + name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : fallback;
        }

        var budget = Get("p95-budget-ms", string.Empty);
        return new LoadOptions(
            Get("base-url", "http://localhost:5104"),
            int.Parse(Get("duration", "20"), CultureInfo.InvariantCulture),
            int.Parse(Get("warmup", "3"), CultureInfo.InvariantCulture),
            int.Parse(Get("concurrency", "64"), CultureInfo.InvariantCulture),
            int.Parse(Get("links", "20"), CultureInfo.InvariantCulture),
            Get("label", "run"),
            budget.Length == 0 ? null : double.Parse(budget, CultureInfo.InvariantCulture));
    }
}
