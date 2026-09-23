using DsarClock.Core;

namespace DsarClock.Api;

public sealed record NewRequest(RequestType Type, string? SubjectReference, DateOnly? ReceivedOn, Jurisdiction Jurisdiction, string? Channel);

public sealed record Note(string? Text);

public sealed record Refusal(RefusalGround Ground, string? Explanation);

public sealed record RequestView(
    Guid Id, RequestType Type, string SubjectReference, DateOnly ReceivedOn, Jurisdiction Jurisdiction, RequestStatus Status,
    bool Extended, DateOnly InitialDeadline, DateOnly Deadline, int WorkingDaysLeft, bool Overdue, DateOnly? ClosedOn,
    IReadOnlyList<RequestEvent> Events)
{
    public static RequestView Of(DsarRequest r, HolidayCalendar calendar, DateOnly today)
    {
        DateOnly deadline = r.Deadline(calendar);
        return new RequestView(r.Id, r.Type, r.SubjectReference, r.ReceivedOn, r.Jurisdiction, r.Status, r.Extended,
            r.InitialDeadline(calendar), deadline, GdprDeadline.WorkingDaysLeft(today, deadline, calendar), r.IsOverdue(today, calendar),
            r.ClosedOn, r.Events);
    }
}
