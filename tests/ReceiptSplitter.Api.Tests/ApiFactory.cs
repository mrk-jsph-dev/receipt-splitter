using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ReceiptSplitter.Api.Infrastructure.Http;
using ReceiptSplitter.Api.Infrastructure.Storage;
using ReceiptSplitter.Api.Shared;

namespace ReceiptSplitter.Api.Tests;

/// <summary>The real app with in-memory stores, a clock the test controls, and every log line captured.</summary>
public class ApiFactory : WebApplicationFactory<Program>
{
    private readonly Dictionary<string, string> _settings = new()
    {
        ["RateLimits:CreatePerHour"] = "100000",
        ["RateLimits:WritesPerMinute"] = "100000",
        ["RateLimits:ReadsPerMinute"] = "100000",
        ["RateLimits:CreatePerDay"] = "100000",
    };

    public TestClock Clock { get; } = new();
    public LogSink Logs { get; } = new();

    public IBillStore Store => Services.GetRequiredService<IBillStore>();

    public ApiFactory With(string key, string value)
    {
        _settings[key] = value;
        return this;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // "Testing" loads no appsettings.Development.json, so the app picks the in-memory stores.
        builder.UseEnvironment("Testing");
        foreach (var (key, value) in _settings)
            builder.UseSetting(key, value);
        builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<TimeProvider>(Clock);
            services.AddSingleton<ILoggerProvider>(Logs);
        });
    }

    /// <summary>A client with its own IP, so per-IP counters in one test don't spill into another.</summary>
    public HttpClient NewClient(string? ip = null)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add("X-Forwarded-For", ip ?? $"10.{Random.Shared.Next(256)}.{Random.Shared.Next(256)}.{Random.Shared.Next(1, 255)}");
        return client;
    }
}

public sealed class TestClock : TimeProvider
{
    private DateTimeOffset _now = new(2026, 9, 25, 10, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan by) => _now += by;
}

public sealed class LogSink : ILoggerProvider
{
    public ConcurrentQueue<string> Lines { get; } = new();

    public ILogger CreateLogger(string categoryName) => new Logger(this, categoryName);

    public void Dispose() { }

    private sealed class Logger(LogSink sink, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            sink.Lines.Enqueue($"{category}: {formatter(state, exception)} {exception}");
    }
}

public sealed record Created(string Id, string OrganizerKey);

internal static class Api
{
    public static async Task<Created> CreateSampleAsync(this HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/bills", new { source = "sample" });
        response.EnsureSuccessStatusCode();
        return (await response.ReadAsync<Created>())!;
    }

    public static async Task<BillResponse> GetBillAsync(this HttpClient client, string id)
    {
        var response = await client.GetAsync($"/api/bills/{id}");
        response.EnsureSuccessStatusCode();
        return (await response.ReadAsync<BillResponse>())!;
    }

    public static Task<HttpResponseMessage> PatchAsync(this HttpClient client, string id, object op) =>
        client.PatchAsJsonAsync($"/api/bills/{id}", op, ApiJson.Options);

    public static async Task<T?> ReadAsync<T>(this HttpResponseMessage response) =>
        JsonSerializer.Deserialize<T>(await response.Content.ReadAsStringAsync(), ApiJson.Options);

    /// <summary>Claims a unit, sending exactly what the bill currently shows as <c>from</c>.</summary>
    public static async Task<HttpResponseMessage> ClaimAsync(this HttpClient client, string id, string unitId, string personId, long weight = 1)
    {
        var bill = await client.GetBillAsync(id);
        return await client.PatchAsync(id, new { op = "claim", unitId, personId, weight, from = bill.Bill.Unit(unitId).Claims });
    }

    /// <summary>An organizer call: key in the X-Organizer-Key header, optional If-Match and JSON body.</summary>
    public static Task<HttpResponseMessage> OrganizerAsync(
        this HttpClient client, HttpMethod method, string path, string? key, object? body = null, string? ifMatch = null)
    {
        var request = new HttpRequestMessage(method, path);
        if (key is not null)
            request.Headers.Add("X-Organizer-Key", key);
        if (ifMatch is not null)
            request.Headers.TryAddWithoutValidation("If-Match", ifMatch);
        if (body is not null)
            request.Content = JsonContent.Create(body, options: ApiJson.Options);
        return client.SendAsync(request);
    }

    /// <summary>Who had what in the walkthrough. Fay to Lia are not in the spec; chosen so every published number holds.</summary>
    public static readonly (string Line, int Unit, string Person)[] WalkthroughClaims =
    [
        ("Butao King", 1, "Ana"), ("Butao King", 2, "Ben"), ("Butao King", 3, "Eli"), ("Butao King", 4, "Fay"),
        ("Butao King", 5, "Gio"), ("Butao King", 6, "Hana"), ("Butao King", 7, "Kai"), ("Butao King", 8, "Lia"),
        ("Classic Gyoza", 1, "Ben"), ("Classic Gyoza", 1, "Fay"), ("Classic Gyoza", 1, "Gio"),
        ("Coke Zero", 1, "Dan"), ("Kaedama (thick)", 1, "Jo"),
        ("Kaedama (thin)", 1, "Ana"), ("Kaedama (thin)", 2, "Gio"), ("Kaedama (thin)", 3, "Lia"),
        ("Gyu King", 1, "Carla"), ("Gyu King", 2, "Ivan"), ("Kakuni Belly", 1, "Hana"),
        ("Red King", 1, "Dan"), ("Red King", 2, "Jo"), ("Tamago", 1, "Kai"),
    ];

    public static async Task ClaimLikeWalkthroughAsync(this HttpClient client, string id)
    {
        var bill = (await client.GetBillAsync(id)).Bill;
        foreach (var (line, unit, person) in WalkthroughClaims)
            Assert.Equal(System.Net.HttpStatusCode.OK, (await client.ClaimAsync(id, bill.UnitId(line, unit), bill.PersonId(person))).StatusCode);
    }

    public static string PersonId(this BillView bill, string name) => bill.People.Single(p => p.Name == name).Id;

    /// <summary>The n-th unit (from 1) of the line with this name.</summary>
    public static string UnitId(this BillView bill, string lineName, int n = 1) =>
        bill.Receipts.SelectMany(r => r.Lines).Single(l => l.Name == lineName).Units[n - 1].Id;

    public static UnitDoc Unit(this BillView bill, string unitId) =>
        bill.Receipts.SelectMany(r => r.Lines).SelectMany(l => l.Units).Single(u => u.Id == unitId);

    public static long Owed(this BreakdownView? breakdown, BillView bill, string name) =>
        breakdown!.People.Single(p => p.PersonId == bill.PersonId(name)).Owed;
}
