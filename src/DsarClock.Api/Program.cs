using System.Security.Cryptography;
using System.Text;
using DsarClock.Api;
using DsarClock.Core;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

string token = builder.Configuration["DsarClock:ApiToken"] ?? "";
if (token.Length < 32) throw new InvalidOperationException("DsarClock:ApiToken (DsarClock__ApiToken) must be at least 32 characters");
string zoneId = builder.Configuration["DsarClock:TimeZone"] ?? "Europe/Berlin";
TimeZoneInfo zone = TimeZoneInfo.FindSystemTimeZoneById(zoneId);

builder.Services.AddDbContext<DsarDb>(o => o.UseNpgsql(builder.Configuration.GetConnectionString("Default")));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddProblemDetails();
builder.Services.AddHealthChecks();
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter()));

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    await DsarDb.ApplySchemaAsync(scope.ServiceProvider.GetRequiredService<DsarDb>());
}

app.UseExceptionHandler();
app.MapHealthChecks("/healthz");

byte[] expected = Encoding.UTF8.GetBytes(token);
var api = app.MapGroup("/api").AddEndpointFilter(async (ctx, next) =>
{
    string header = ctx.HttpContext.Request.Headers.Authorization.ToString();
    byte[] presented = header.StartsWith("Bearer ", StringComparison.Ordinal) ? Encoding.UTF8.GetBytes(header[7..]) : [];
    if (!CryptographicOperations.FixedTimeEquals(presented, expected)) return Results.Problem(statusCode: 401, title: "Unauthorized");
    try
    {
        return await next(ctx);
    }
    catch (RuleViolationException e)
    {
        return Results.Problem(statusCode: 422, title: "Not allowed by Art. 12 GDPR", detail: e.Message);
    }
    catch (DbUpdateConcurrencyException)
    {
        return Results.Problem(statusCode: 409, title: "Changed by someone else", detail: "reload the request and try again");
    }
});

DateOnly Today(TimeProvider clock) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.GetUtcNow(), zone).DateTime);

static string Actor(HttpContext ctx) => ctx.Request.Headers["X-Actor"].ToString() is { Length: > 0 and <= 100 } a ? a : "unknown";

async Task<HolidayCalendar> Calendar(DsarDb db, Jurisdiction j)
{
    var extra = await db.Holidays.Where(h => h.Jurisdiction == j.ToString()).Select(h => h.Day).ToListAsync();
    return new HolidayCalendar(j, extra);
}

async Task<Results<Ok<RequestView>, NotFound>> Act(Guid id, DsarDb db, TimeProvider clock, Action<DsarRequest, HolidayCalendar, DateOnly> action)
{
    var row = await db.Requests.Include(r => r.Events).FirstOrDefaultAsync(r => r.Id == id);
    if (row is null) return TypedResults.NotFound();
    var request = Mapping.ToDomain(row);
    var calendar = await Calendar(db, request.Jurisdiction);
    DateOnly today = Today(clock);
    action(request, calendar, today);
    Mapping.Apply(request, row);
    await db.SaveChangesAsync();
    return TypedResults.Ok(RequestView.Of(request, calendar, today));
}

api.MapPost("/requests", async (NewRequest body, DsarDb db, TimeProvider clock, HttpContext ctx) =>
{
    DateOnly today = Today(clock);
    var request = DsarRequest.Receive(Guid.NewGuid(), body.Type, body.SubjectReference ?? "", body.ReceivedOn ?? today, body.Jurisdiction,
        string.IsNullOrWhiteSpace(body.Channel) ? "unspecified" : body.Channel, today, Actor(ctx));
    var row = new RequestRow();
    Mapping.Apply(request, row);
    db.Requests.Add(row);
    await db.SaveChangesAsync();
    return TypedResults.Created($"/api/requests/{request.Id}", RequestView.Of(request, await Calendar(db, request.Jurisdiction), today));
});

api.MapGet("/requests/{id:guid}", async Task<Results<Ok<RequestView>, NotFound>> (Guid id, DsarDb db, TimeProvider clock) =>
{
    var row = await db.Requests.AsNoTracking().Include(r => r.Events).FirstOrDefaultAsync(r => r.Id == id);
    if (row is null) return TypedResults.NotFound();
    var request = Mapping.ToDomain(row);
    return TypedResults.Ok(RequestView.Of(request, await Calendar(db, request.Jurisdiction), Today(clock)));
});

/// Open requests, soonest deadline first -- the list a DPO works from each morning.
api.MapGet("/requests", async (DsarDb db, TimeProvider clock, bool? overdue, int? dueWithinWorkingDays) =>
{
    DateOnly today = Today(clock);
    var rows = await db.Requests.AsNoTracking().Include(r => r.Events)
        .Where(r => r.Status == "Open" || r.Status == "AwaitingIdentity").ToListAsync();
    var views = new List<RequestView>();
    foreach (var row in rows)
    {
        var request = Mapping.ToDomain(row);
        views.Add(RequestView.Of(request, await Calendar(db, request.Jurisdiction), today));
    }
    IEnumerable<RequestView> filtered = views;
    if (overdue == true) filtered = filtered.Where(v => v.Overdue);
    if (dueWithinWorkingDays is int n) filtered = filtered.Where(v => v.WorkingDaysLeft <= n);
    return filtered.OrderBy(v => v.Deadline).ThenBy(v => v.ReceivedOn).Select(v => v with { Events = [] }).ToList();
});

api.MapPost("/requests/{id:guid}/identity-requested", (Guid id, Note body, DsarDb db, TimeProvider clock, HttpContext ctx) =>
    Act(id, db, clock, (r, _, today) => r.RequestIdentity(body.Text ?? "", today, Actor(ctx))));

api.MapPost("/requests/{id:guid}/identity-confirmed", (Guid id, DsarDb db, TimeProvider clock, HttpContext ctx) =>
    Act(id, db, clock, (r, _, today) => r.IdentityConfirmed(today, Actor(ctx))));

api.MapPost("/requests/{id:guid}/extend", (Guid id, Note body, DsarDb db, TimeProvider clock, HttpContext ctx) =>
    Act(id, db, clock, (r, cal, today) => r.Extend(body.Text ?? "", today, cal, Actor(ctx))));

api.MapPost("/requests/{id:guid}/complete", (Guid id, Note body, DsarDb db, TimeProvider clock, HttpContext ctx) =>
    Act(id, db, clock, (r, cal, today) => r.Complete(body.Text ?? "", today, cal, Actor(ctx))));

api.MapPost("/requests/{id:guid}/refuse", (Guid id, Refusal body, DsarDb db, TimeProvider clock, HttpContext ctx) =>
    Act(id, db, clock, (r, cal, today) => r.Refuse(body.Ground, body.Explanation ?? "", today, cal, Actor(ctx))));

api.MapPut("/holidays/{jurisdiction}/{day}", async (Jurisdiction jurisdiction, DateOnly day, Note body, DsarDb db) =>
{
    if (await db.Holidays.FindAsync(jurisdiction.ToString(), day) is null)
    {
        db.Holidays.Add(new HolidayRow { Jurisdiction = jurisdiction.ToString(), Day = day, Note = body.Text ?? "" });
        await db.SaveChangesAsync();
    }
    return TypedResults.NoContent();
});

api.MapGet("/holidays/{jurisdiction}/{year:int}", async (Jurisdiction jurisdiction, int year, DsarDb db) =>
{
    var calendar = await Calendar(db, jurisdiction);
    var extra = await db.Holidays.Where(h => h.Jurisdiction == jurisdiction.ToString() && h.Day.Year == year).Select(h => h.Day).ToListAsync();
    return calendar.HolidaysIn(year).Concat(extra).Distinct().Order().ToList();
});

app.Run();

public partial class Program;
