using System.Reflection;
using DsarClock.Core;
using Microsoft.EntityFrameworkCore;

namespace DsarClock.Api;

public sealed class RequestRow
{
    public Guid Id { get; set; }
    public string Type { get; set; } = "";
    public string SubjectReference { get; set; } = "";
    public DateOnly ReceivedOn { get; set; }
    public string Jurisdiction { get; set; } = "";
    public string Status { get; set; } = "";
    public bool Extended { get; set; }
    public DateOnly? ClosedOn { get; set; }
    public uint Version { get; set; }
    public List<EventRow> Events { get; set; } = [];
}

public sealed class EventRow
{
    public long Id { get; set; }
    public Guid RequestId { get; set; }
    public DateOnly OnDate { get; set; }
    public string Kind { get; set; } = "";
    public string Detail { get; set; } = "";
    public string Actor { get; set; } = "";
}

public sealed class HolidayRow
{
    public string Jurisdiction { get; set; } = "";
    public DateOnly Day { get; set; }
    public string Note { get; set; } = "";
}

public sealed class DsarDb(DbContextOptions<DsarDb> options) : DbContext(options)
{
    public DbSet<RequestRow> Requests => Set<RequestRow>();
    public DbSet<EventRow> Events => Set<EventRow>();
    public DbSet<HolidayRow> Holidays => Set<HolidayRow>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var b = modelBuilder;
        b.Entity<RequestRow>(e =>
        {
            e.ToTable("requests");
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.Type).HasColumnName("type");
            e.Property(x => x.SubjectReference).HasColumnName("subject_reference");
            e.Property(x => x.ReceivedOn).HasColumnName("received_on");
            e.Property(x => x.Jurisdiction).HasColumnName("jurisdiction");
            e.Property(x => x.Status).HasColumnName("status");
            e.Property(x => x.Extended).HasColumnName("extended");
            e.Property(x => x.ClosedOn).HasColumnName("closed_on");
            // Postgres's system column as the optimistic-concurrency token: two
            // people acting on the same request at once get a 409, not a lost update.
            e.Property(x => x.Version).HasColumnName("xmin").HasColumnType("xid").ValueGeneratedOnAddOrUpdate().IsConcurrencyToken();
            e.HasMany(x => x.Events).WithOne().HasForeignKey(x => x.RequestId);
        });
        b.Entity<EventRow>(e =>
        {
            e.ToTable("request_events");
            e.Property(x => x.Id).HasColumnName("id").UseIdentityAlwaysColumn();
            e.Property(x => x.RequestId).HasColumnName("request_id");
            e.Property(x => x.OnDate).HasColumnName("on_date");
            e.Property(x => x.Kind).HasColumnName("kind");
            e.Property(x => x.Detail).HasColumnName("detail");
            e.Property(x => x.Actor).HasColumnName("actor");
        });
        b.Entity<HolidayRow>(e =>
        {
            e.ToTable("extra_holidays");
            e.HasKey(x => new { x.Jurisdiction, x.Day });
            e.Property(x => x.Jurisdiction).HasColumnName("jurisdiction");
            e.Property(x => x.Day).HasColumnName("day");
            e.Property(x => x.Note).HasColumnName("note");
        });
    }

    public static async Task ApplySchemaAsync(DsarDb db, CancellationToken ct = default)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("DsarClock.Api.Schema.sql")
            ?? throw new InvalidOperationException("Schema.sql not embedded");
        using var reader = new StreamReader(stream);
        string sql = await reader.ReadToEndAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        // Several replicas may start together; the first applies, the rest wait and find nothing to do.
        await db.Database.ExecuteSqlRawAsync("select pg_advisory_xact_lock(4312019)", ct);
        await db.Database.ExecuteSqlRawAsync(sql, ct);
        await tx.CommitAsync(ct);
    }
}

/// <summary>Moves requests between the domain and the rows. The domain never sees EF.</summary>
public static class Mapping
{
    public static DsarRequest ToDomain(RequestRow r) => DsarRequest.Restore(
        r.Id, Enum.Parse<RequestType>(r.Type), r.SubjectReference, r.ReceivedOn, Enum.Parse<Jurisdiction>(r.Jurisdiction),
        Enum.Parse<RequestStatus>(r.Status), r.Extended, r.ClosedOn,
        r.Events.OrderBy(e => e.Id).Select(e => new RequestEvent(e.OnDate, e.Kind, e.Detail, e.Actor)));

    /// <summary>Copies state back and appends only the events the row does not have yet.</summary>
    public static void Apply(DsarRequest d, RequestRow row)
    {
        row.Id = d.Id;
        row.Type = d.Type.ToString();
        row.SubjectReference = d.SubjectReference;
        row.ReceivedOn = d.ReceivedOn;
        row.Jurisdiction = d.Jurisdiction.ToString();
        row.Status = d.Status.ToString();
        row.Extended = d.Extended;
        row.ClosedOn = d.ClosedOn;
        foreach (var e in d.Events.Skip(row.Events.Count))
        {
            row.Events.Add(new EventRow { RequestId = d.Id, OnDate = e.On, Kind = e.Kind, Detail = e.Detail, Actor = e.Actor });
        }
    }
}
