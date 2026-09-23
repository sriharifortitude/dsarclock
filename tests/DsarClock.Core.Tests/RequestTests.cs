using DsarClock.Core;

namespace DsarClock.Core.Tests;

public class RequestTests
{
    private static readonly HolidayCalendar De = new(Jurisdiction.DE);

    private static DateOnly D(string s) => DateOnly.Parse(s, System.Globalization.CultureInfo.InvariantCulture);

    private static DsarRequest Received(string on = "2026-01-31") =>
        DsarRequest.Receive(Guid.NewGuid(), RequestType.Access, "CRM-4471", D(on), Jurisdiction.DE, "email", D(on), "dpo");

    [Fact]
    public void AnExtensionInsideTheFirstMonthMovesTheDeadline()
    {
        var r = Received();
        Assert.Equal(D("2026-03-02"), r.Deadline(De));
        r.Extend("412 mailboxes to search", D("2026-03-02"), De, "dpo"); // the last day of the first month is still inside it
        Assert.Equal(D("2026-04-28"), r.Deadline(De));
        Assert.Equal("extended", r.Events[^1].Kind);
        Assert.Contains("new deadline 2026-04-28", r.Events[^1].Detail);
    }

    [Fact]
    public void AnExtensionAfterTheFirstMonthIsRefusedWithTheRule()
    {
        var r = Received();
        var e = Assert.Throws<RuleViolationException>(() => r.Extend("late", D("2026-03-03"), De, "dpo"));
        Assert.Equal("Art. 12(3): the data subject must be told of an extension within one month; that ended 2026-03-02", e.Message);
        Assert.False(r.Extended);
    }

    [Fact]
    public void ExtensionNeedsReasonsAndHappensOnce()
    {
        var r = Received();
        Assert.Throws<RuleViolationException>(() => r.Extend(" ", D("2026-02-10"), De, "dpo"));
        r.Extend("volume", D("2026-02-10"), De, "dpo");
        Assert.Throws<RuleViolationException>(() => r.Extend("more", D("2026-02-11"), De, "dpo"));
    }

    [Fact]
    public void ARefusalIsLateAfterTheOriginalMonthEvenIfExtended()
    {
        var r = Received();
        r.Extend("volume", D("2026-02-10"), De, "dpo");
        r.Refuse(RefusalGround.ManifestlyUnfounded, "repetitive request, third this month", D("2026-03-10"), De, "dpo");
        Assert.Equal(RequestStatus.Refused, r.Status);
        Assert.EndsWith("-- LATE: deadline was 2026-03-02", r.Events[^1].Detail);
    }

    [Fact]
    public void CompletionIsJudgedAgainstTheExtendedDeadline()
    {
        var r = Received();
        r.Extend("volume", D("2026-02-10"), De, "dpo");
        r.Complete("copy of data sent via portal", D("2026-04-28"), De, "dpo");
        Assert.DoesNotContain("LATE", r.Events[^1].Detail);
        Assert.Equal(D("2026-04-28"), r.ClosedOn);
    }

    [Fact]
    public void IdentityChecksDoNotStopTheClockButBlockCompletion()
    {
        var r = Received("2026-03-15");
        r.RequestIdentity("copy of ID card", D("2026-03-16"), "dpo");
        Assert.Equal(D("2026-04-15"), r.Deadline(De));
        Assert.Throws<RuleViolationException>(() => r.Complete("x", D("2026-03-20"), De, "dpo"));
        Assert.True(r.IsOverdue(D("2026-04-16"), De));
        r.IdentityConfirmed(D("2026-04-16"), "dpo");
        r.Complete("data sent", D("2026-04-17"), De, "dpo");
        Assert.EndsWith("-- LATE: deadline was 2026-04-15", r.Events[^1].Detail);
        Assert.False(r.IsOverdue(D("2026-04-20"), De));
    }

    [Fact]
    public void ClosedRequestsStayClosedAndInputIsChecked()
    {
        var r = Received();
        r.Complete("done", D("2026-02-01"), De, "dpo");
        Assert.Equal("request is already completed", Assert.Throws<RuleViolationException>(() => r.Refuse(RefusalGround.Excessive, "x", D("2026-02-02"), De, "dpo")).Message);
        Assert.Throws<RuleViolationException>(() => DsarRequest.Receive(Guid.NewGuid(), RequestType.Erasure, "", D("2026-01-01"), Jurisdiction.DE, "web", D("2026-01-01"), "dpo"));
        Assert.Throws<RuleViolationException>(() => DsarRequest.Receive(Guid.NewGuid(), RequestType.Erasure, "X", D("2026-01-02"), Jurisdiction.DE, "web", D("2026-01-01"), "dpo"));
        Assert.Equal(["received", "completed"], r.Events.Select(e => e.Kind));
    }
}
