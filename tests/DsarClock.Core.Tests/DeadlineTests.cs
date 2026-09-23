using DsarClock.Core;

namespace DsarClock.Core.Tests;

/// <summary>
/// Every expected date was worked out by hand from Regulation 1182/71 and
/// checked against an independent calendar (weekdays, Easter 2024-2028)
/// before being written here.
/// </summary>
public class DeadlineTests
{
    private static readonly HolidayCalendar De = new(Jurisdiction.DE);
    private static readonly HolidayCalendar At = new(Jurisdiction.AT);
    private static readonly HolidayCalendar Fr = new(Jurisdiction.FR);

    private static DateOnly D(string s) => DateOnly.Parse(s, System.Globalization.CultureInfo.InvariantCulture);

    [Theory]
    [InlineData(2024, "2024-03-31")]
    [InlineData(2025, "2025-04-20")]
    [InlineData(2026, "2026-04-05")]
    [InlineData(2027, "2027-03-28")]
    [InlineData(2028, "2028-04-16")]
    public void EasterMatchesPublishedDates(int year, string expected) =>
        Assert.Equal(D(expected), HolidayCalendar.Easter(year));

    [Fact]
    public void MovableFeastsFollowEaster2026()
    {
        var days = De.HolidaysIn(2026);
        Assert.Contains(D("2026-04-03"), days); // Good Friday
        Assert.Contains(D("2026-04-06"), days); // Easter Monday
        Assert.Contains(D("2026-05-14"), days); // Ascension
        Assert.Contains(D("2026-05-25"), days); // Whit Monday
        Assert.DoesNotContain(D("2026-06-04"), days); // Corpus Christi: regional in DE
        Assert.Contains(D("2026-06-04"), At.HolidaysIn(2026)); // national in AT
    }

    [Theory]
    // received       due          why
    [InlineData("2026-03-15", "2026-04-15")] // plain: same date next month, a Wednesday
    [InlineData("2026-01-31", "2026-03-02")] // no 31 February -> 28th, a Saturday -> Monday 2 March
    [InlineData("2026-04-06", "2026-05-06")] // received on Easter Monday: receipt day is not counted anyway
    [InlineData("2026-04-14", "2026-05-15")] // 14 May is Ascension -> Friday
    [InlineData("2026-11-25", "2026-12-28")] // 25 Dec holiday, 26 Dec holiday and Saturday, 27 Sunday -> Monday
    [InlineData("2026-12-31", "2027-02-01")] // 31 Jan 2027 is a Sunday -> Monday; crosses the year
    public void InitialDeadlineGermany(string received, string due) =>
        Assert.Equal(D(due), GdprDeadline.Initial(D(received), De));

    [Fact]
    public void TheHolidaysAreTheControllersCountry()
    {
        // 4 June 2026, Corpus Christi: a working day in Germany nationally, a holiday in Austria.
        Assert.Equal(D("2026-06-04"), GdprDeadline.Initial(D("2026-05-04"), De));
        Assert.Equal(D("2026-06-05"), GdprDeadline.Initial(D("2026-05-04"), At));
        // 14 July 2026: Bastille Day in France only.
        Assert.Equal(D("2026-07-14"), GdprDeadline.Initial(D("2026-06-14"), De));
        Assert.Equal(D("2026-07-15"), GdprDeadline.Initial(D("2026-06-14"), Fr));
    }

    [Fact]
    public void RegionalHolidaysCanBeAdded()
    {
        var bavaria = new HolidayCalendar(Jurisdiction.DE, [D("2027-01-06")]); // Epiphany
        Assert.Equal(D("2027-01-06"), GdprDeadline.Initial(D("2026-12-06"), De));
        Assert.Equal(D("2027-01-07"), GdprDeadline.Initial(D("2026-12-06"), bavaria));
    }

    [Theory]
    [InlineData("2026-03-15", "2026-06-15")] // both readings agree: 15 June, a Monday
    [InlineData("2026-01-31", "2026-04-28")] // 3 months from receipt = 30 Apr; 2 months from 28 Feb = 28 Apr; earlier wins
    public void ExtendedDeadlineTakesTheEarlierReading(string received, string due) =>
        Assert.Equal(D(due), GdprDeadline.Extended(D(received), De));

    [Fact]
    public void WorkingDaysLeftSkipsWeekendsAndHolidays()
    {
        // Tue 12 May -> Fri 15 May 2026: Wed 13, (Thu 14 Ascension), Fri 15 = 2 working days.
        Assert.Equal(2, GdprDeadline.WorkingDaysLeft(D("2026-05-12"), D("2026-05-15"), De));
        Assert.Equal(-1, GdprDeadline.WorkingDaysLeft(D("2026-05-18"), D("2026-05-15"), De)); // Mon after a Fri deadline
        Assert.Equal(0, GdprDeadline.WorkingDaysLeft(D("2026-05-15"), D("2026-05-15"), De));
    }
}
