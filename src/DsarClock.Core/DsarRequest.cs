namespace DsarClock.Core;

public enum RequestType { Access, Rectification, Erasure, Restriction, Portability, Objection }

public enum RequestStatus { Open, AwaitingIdentity, Completed, Refused }

/// <summary>Art. 12(5) and 12(2) grounds for not acting, plus restrictions under Art. 23 national law.</summary>
public enum RefusalGround { ManifestlyUnfounded, Excessive, IdentityNotEstablished, RestrictionUnderArt23 }

public sealed record RequestEvent(DateOnly On, string Kind, string Detail, string Actor);

/// <summary>A rule of Art. 12 would be broken. The message names the rule.</summary>
public sealed class RuleViolationException(string message) : Exception(message);

/// <summary>
/// One data subject request and everything that happened to it. Every
/// change goes through a method that checks the Art. 12 rule it touches and
/// appends an event; nothing edits the history.
///
/// The subject is identified by a reference into the controller's own
/// systems, never by name or email: a register of who exercised their
/// rights is itself personal data, and this one holds as little as it can.
/// </summary>
public sealed class DsarRequest
{
    private readonly List<RequestEvent> _events = [];

    public Guid Id { get; }
    public RequestType Type { get; }
    public string SubjectReference { get; }
    public DateOnly ReceivedOn { get; }
    public Jurisdiction Jurisdiction { get; }
    public RequestStatus Status { get; private set; } = RequestStatus.Open;
    public bool Extended { get; private set; }
    public DateOnly? ClosedOn { get; private set; }
    public IReadOnlyList<RequestEvent> Events => _events;

    private DsarRequest(Guid id, RequestType type, string subjectReference, DateOnly receivedOn, Jurisdiction jurisdiction)
    {
        Id = id;
        Type = type;
        SubjectReference = subjectReference;
        ReceivedOn = receivedOn;
        Jurisdiction = jurisdiction;
    }

    public static DsarRequest Receive(Guid id, RequestType type, string subjectReference, DateOnly receivedOn, Jurisdiction jurisdiction,
        string channel, DateOnly today, string actor)
    {
        if (string.IsNullOrWhiteSpace(subjectReference) || subjectReference.Length > 64)
            throw new RuleViolationException("subjectReference: a reference of 1-64 characters into your own records");
        if (receivedOn > today) throw new RuleViolationException("receivedOn: a request cannot be received in the future");
        var r = new DsarRequest(id, type, subjectReference.Trim(), receivedOn, jurisdiction);
        r.Append(today, "received", $"{type} request via {channel}", actor);
        return r;
    }

    /// <summary>Rebuild from storage. Events are replayed as recorded, not re-validated.</summary>
    public static DsarRequest Restore(Guid id, RequestType type, string subjectReference, DateOnly receivedOn, Jurisdiction jurisdiction,
        RequestStatus status, bool extended, DateOnly? closedOn, IEnumerable<RequestEvent> events)
    {
        var r = new DsarRequest(id, type, subjectReference, receivedOn, jurisdiction) { Status = status, Extended = extended, ClosedOn = closedOn };
        r._events.AddRange(events);
        return r;
    }

    public DateOnly InitialDeadline(HolidayCalendar calendar) => GdprDeadline.Initial(ReceivedOn, calendar);

    public DateOnly Deadline(HolidayCalendar calendar) =>
        Extended ? GdprDeadline.Extended(ReceivedOn, calendar) : InitialDeadline(calendar);

    /// <summary>
    /// Art. 12(6): additional information may be requested to confirm
    /// identity. The clock does not stop -- see ADR 2 for why.
    /// </summary>
    public void RequestIdentity(string what, DateOnly today, string actor)
    {
        RequireOpen();
        if (string.IsNullOrWhiteSpace(what)) throw new RuleViolationException("what: say which information was requested");
        Status = RequestStatus.AwaitingIdentity;
        Append(today, "identity-requested", what, actor);
    }

    public void IdentityConfirmed(DateOnly today, string actor)
    {
        if (Status != RequestStatus.AwaitingIdentity) throw new RuleViolationException("identity was not being awaited");
        Status = RequestStatus.Open;
        Append(today, "identity-confirmed", "", actor);
    }

    /// <summary>
    /// Art. 12(3): extendable by two further months "where necessary, taking
    /// into account the complexity and number of the requests", and the
    /// data subject must be told, with reasons, within the first month.
    /// </summary>
    public void Extend(string reason, DateOnly today, HolidayCalendar calendar, string actor)
    {
        RequireOpen();
        if (Extended) throw new RuleViolationException("Art. 12(3): the period can be extended once, by two further months");
        if (string.IsNullOrWhiteSpace(reason)) throw new RuleViolationException("Art. 12(3): an extension needs reasons, which the data subject must receive");
        DateOnly initial = InitialDeadline(calendar);
        if (today > initial)
            throw new RuleViolationException($"Art. 12(3): the data subject must be told of an extension within one month; that ended {initial:yyyy-MM-dd}");
        Extended = true;
        Append(today, "extended", $"{reason} (new deadline {GdprDeadline.Extended(ReceivedOn, calendar):yyyy-MM-dd})", actor);
    }

    public void Complete(string summary, DateOnly today, HolidayCalendar calendar, string actor)
    {
        RequireOpen();
        if (Status == RequestStatus.AwaitingIdentity) throw new RuleViolationException("identity has not been confirmed");
        if (string.IsNullOrWhiteSpace(summary)) throw new RuleViolationException("summary: record what was provided or done");
        Close(RequestStatus.Completed, today);
        Append(today, "completed", summary + Lateness(today, Deadline(calendar)), actor);
    }

    /// <summary>
    /// Art. 12(4): a decision not to act must be communicated "without delay
    /// and at the latest within one month of receipt" -- the original month,
    /// even if the request had been extended.
    /// </summary>
    public void Refuse(RefusalGround ground, string explanation, DateOnly today, HolidayCalendar calendar, string actor)
    {
        RequireOpen();
        if (string.IsNullOrWhiteSpace(explanation))
            throw new RuleViolationException("Art. 12(4): the data subject must be told the reasons and of their right to complain");
        Close(RequestStatus.Refused, today);
        Append(today, "refused", $"{ground}: {explanation}{Lateness(today, InitialDeadline(calendar))}", actor);
    }

    public bool IsOverdue(DateOnly today, HolidayCalendar calendar) =>
        Status is RequestStatus.Open or RequestStatus.AwaitingIdentity && today > Deadline(calendar);

    private static string Lateness(DateOnly on, DateOnly due) =>
        on > due ? $" -- LATE: deadline was {due:yyyy-MM-dd}" : "";

    private void RequireOpen()
    {
        if (Status is RequestStatus.Completed or RequestStatus.Refused) throw new RuleViolationException($"request is already {Status.ToString().ToLowerInvariant()}");
    }

    private void Close(RequestStatus status, DateOnly on)
    {
        Status = status;
        ClosedOn = on;
    }

    private void Append(DateOnly on, string kind, string detail, string actor) =>
        _events.Add(new RequestEvent(on, kind, detail, string.IsNullOrWhiteSpace(actor) ? "unknown" : actor));
}
