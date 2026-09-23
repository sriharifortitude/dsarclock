using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DsarClock.Api;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Testcontainers.PostgreSql;

namespace DsarClock.Api.Tests;

/// <summary>
/// A clock the tests set to any date, in either direction. FakeTimeProvider
/// refuses to go backwards, which is right for timer tests and wrong for
/// tests that each live on their own calendar day.
/// </summary>
public sealed class SettableClock : TimeProvider
{
    public DateTimeOffset Now { get; set; } = DateTimeOffset.UnixEpoch;

    public override DateTimeOffset GetUtcNow() => Now;
}

/// <summary>One Postgres container and one app for the whole class; each test uses fresh request ids.</summary>
public sealed class StackFixture : IAsyncLifetime
{
    public const string Token = "test-token-0123456789abcdef0123456789";
    private readonly PostgreSqlContainer _pg = new PostgreSqlBuilder("postgres:17-alpine").Build();
    public SettableClock Clock { get; } = new();
    public WebApplicationFactory<Program> Factory { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await _pg.StartAsync();
        // Top-level Program reads these before the host is built, so they go in the environment.
        Environment.SetEnvironmentVariable("ConnectionStrings__Default", _pg.GetConnectionString());
        Environment.SetEnvironmentVariable("DsarClock__ApiToken", Token);
        Environment.SetEnvironmentVariable("DsarClock__TimeZone", "Europe/Berlin");
        Factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
            b.ConfigureTestServices(s => s.AddSingleton<TimeProvider>(Clock)));
    }

    public async Task DisposeAsync()
    {
        await Factory.DisposeAsync();
        await _pg.DisposeAsync();
    }

    public string ConnectionString => _pg.GetConnectionString();

    /// <summary>Midday in Berlin on the given date, so "today" is unambiguous.</summary>
    public void SetToday(string date) =>
        Clock.Now = (new DateTimeOffset(DateTime.Parse(date, CultureInfo.InvariantCulture).AddHours(10), TimeSpan.Zero));
}

public class ApiTests(StackFixture stack) : IClassFixture<StackFixture>
{
    private HttpClient Client(bool auth = true)
    {
        var c = stack.Factory.CreateClient();
        if (auth) c.DefaultRequestHeaders.Authorization = new("Bearer", StackFixture.Token);
        c.DefaultRequestHeaders.Add("X-Actor", "dpo@example.org");
        return c;
    }

    private static async Task<JsonElement> Json(HttpResponseMessage r) => (await r.Content.ReadFromJsonAsync<JsonElement>());

    private static async Task<(string Id, JsonElement Body)> Create(HttpClient c, string received, string subject = "CRM-1")
    {
        var r = await c.PostAsJsonAsync("/api/requests", new { type = "Access", subjectReference = subject, receivedOn = received, jurisdiction = "DE", channel = "email" });
        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        var body = await Json(r);
        return (body.GetProperty("id").GetString()!, body);
    }

    [Fact]
    public async Task RefusesWithoutTheTokenButLeavesHealthOpen()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await Client(auth: false).GetAsync("/api/requests")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Client(auth: false).GetAsync("/healthz")).StatusCode);
    }

    [Fact]
    public async Task ExtendThenRefuseLateAgainstTheOriginalMonth()
    {
        var c = Client();
        stack.SetToday("2026-02-10");
        var (id, created) = await Create(c, "2026-01-31");
        Assert.Equal("2026-03-02", created.GetProperty("deadline").GetString());
        // Feb 11-13, 16-20, 23-27 and Mar 2: fourteen working days.
        Assert.Equal(14, created.GetProperty("workingDaysLeft").GetInt32());

        var extended = await Json(await c.PostAsJsonAsync($"/api/requests/{id}/extend", new { text = "412 mailboxes to search" }));
        Assert.Equal("2026-04-28", extended.GetProperty("deadline").GetString());
        Assert.Equal("2026-03-02", extended.GetProperty("initialDeadline").GetString());

        stack.SetToday("2026-03-10");
        var refused = await c.PostAsJsonAsync($"/api/requests/{id}/refuse", new { ground = "ManifestlyUnfounded", explanation = "third identical request" });
        Assert.Equal(HttpStatusCode.OK, refused.StatusCode);
        var events = (await Json(refused)).GetProperty("events").EnumerateArray().Select(e => (e.GetProperty("kind").GetString(), e.GetProperty("detail").GetString(), e.GetProperty("actor").GetString())).ToList();
        Assert.Equal(["received", "extended", "refused"], events.Select(e => e.Item1));
        Assert.EndsWith("-- LATE: deadline was 2026-03-02", events[2].Item2);
        Assert.All(events, e => Assert.Equal("dpo@example.org", e.Item3));
    }

    [Fact]
    public async Task ALateExtensionIsA422NamingTheRule()
    {
        var c = Client();
        stack.SetToday("2026-03-05");
        var (id, _) = await Create(c, "2026-01-31");
        var r = await c.PostAsJsonAsync($"/api/requests/{id}/extend", new { text = "volume" });
        Assert.Equal((HttpStatusCode)422, r.StatusCode);
        Assert.Equal("Art. 12(3): the data subject must be told of an extension within one month; that ended 2026-03-02",
            (await Json(r)).GetProperty("detail").GetString());
    }

    [Fact]
    public async Task RegionalHolidaysMoveTheDeadline()
    {
        var c = Client();
        stack.SetToday("2026-12-10");
        var (_, before) = await Create(c, "2026-12-06");
        Assert.Equal("2027-01-06", before.GetProperty("deadline").GetString());
        Assert.Equal(HttpStatusCode.NoContent, (await c.PutAsJsonAsync("/api/holidays/DE/2027-01-06", new { text = "Epiphany (Bavaria)" })).StatusCode);
        var (_, after) = await Create(c, "2026-12-06", "CRM-2");
        Assert.Equal("2027-01-07", after.GetProperty("deadline").GetString());
        var list = await Json(await c.GetAsync("/api/holidays/DE/2027"));
        Assert.Contains("2027-01-06", list.EnumerateArray().Select(d => d.GetString()));
    }

    [Fact]
    public async Task TheOverdueListShowsOnlyOverdueOpenRequests()
    {
        var c = Client();
        stack.SetToday("2026-02-10");
        var (late, _) = await Create(c, "2026-01-02", "CRM-LATE"); // due Mon 2 Feb 2026
        var (fine, _) = await Create(c, "2026-02-09", "CRM-FINE");
        var overdue = (await Json(await c.GetAsync("/api/requests?overdue=true"))).EnumerateArray().Select(r => r.GetProperty("id").GetString()).ToList();
        Assert.Contains(late, overdue);
        Assert.DoesNotContain(fine, overdue);
    }

    [Fact]
    public async Task TheAuditTrailCannotBeEditedEvenDirectly()
    {
        var c = Client();
        stack.SetToday("2026-02-10");
        var (id, _) = await Create(c, "2026-02-01");
        await using var conn = new NpgsqlConnection(stack.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("update request_events set detail = 'rewritten' where request_id = @id", conn);
        cmd.Parameters.AddWithValue("id", Guid.Parse(id));
        var e = await Assert.ThrowsAsync<PostgresException>(() => cmd.ExecuteNonQueryAsync());
        Assert.Contains("append-only", e.MessageText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TwoPeopleActingAtOnceGetAConflictNotALostUpdate()
    {
        var c = Client();
        stack.SetToday("2026-02-10");
        var (id, _) = await Create(c, "2026-02-01");
        using var scopeA = stack.Factory.Services.CreateScope();
        using var scopeB = stack.Factory.Services.CreateScope();
        var a = scopeA.ServiceProvider.GetRequiredService<DsarDb>();
        var b = scopeB.ServiceProvider.GetRequiredService<DsarDb>();
        var rowA = await a.Requests.SingleAsync(r => r.Id == Guid.Parse(id));
        var rowB = await b.Requests.SingleAsync(r => r.Id == Guid.Parse(id));
        rowA.Extended = true;
        await a.SaveChangesAsync();
        rowB.Status = "Completed";
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => b.SaveChangesAsync());
    }

    [Fact]
    public async Task InvalidInputIsRefusedWithProblemDetails()
    {
        var c = Client();
        stack.SetToday("2026-02-10");
        var r = await c.PostAsJsonAsync("/api/requests", new { type = "Access", subjectReference = "", receivedOn = "2026-02-01", jurisdiction = "DE" });
        Assert.Equal((HttpStatusCode)422, r.StatusCode);
        var future = await c.PostAsJsonAsync("/api/requests", new { type = "Access", subjectReference = "X", receivedOn = "2026-03-01", jurisdiction = "DE" });
        Assert.Contains("cannot be received in the future", (await Json(future)).GetProperty("detail").GetString(), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync($"/api/requests/{Guid.NewGuid()}")).StatusCode);
    }
}
