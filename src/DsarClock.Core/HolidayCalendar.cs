namespace DsarClock.Core;

/// <summary>A Member State whose national public holidays the clock knows.</summary>
public enum Jurisdiction
{
    DE,
    AT,
    FR,
}

/// <summary>
/// National public holidays for a jurisdiction, plus any regional or
/// company-specific dates the controller adds (a Bavarian office observes
/// Epiphany; the national calendar does not).
///
/// Regulation 1182/71 Art. 2(1) counts the public holidays of the Member
/// State where the act is to be done -- the controller's -- so the calendar
/// belongs to the organisation, not to the data subject.
/// </summary>
public sealed class HolidayCalendar
{
    private readonly Jurisdiction _jurisdiction;
    private readonly HashSet<DateOnly> _additional;
    private readonly Dictionary<int, HashSet<DateOnly>> _cache = [];

    public HolidayCalendar(Jurisdiction jurisdiction, IEnumerable<DateOnly>? additionalHolidays = null)
    {
        _jurisdiction = jurisdiction;
        _additional = [.. additionalHolidays ?? []];
    }

    public Jurisdiction Jurisdiction => _jurisdiction;

    public bool IsHoliday(DateOnly date) => _additional.Contains(date) || HolidaysIn(date.Year).Contains(date);

    public bool IsWorkingDay(DateOnly date) =>
        date.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday) && !IsHoliday(date);

    public DateOnly NextWorkingDayOnOrAfter(DateOnly date)
    {
        while (!IsWorkingDay(date)) date = date.AddDays(1);
        return date;
    }

    public IReadOnlySet<DateOnly> HolidaysIn(int year)
    {
        if (_cache.TryGetValue(year, out var cached)) return cached;
        DateOnly easter = Easter(year);
        DateOnly Fixed(int month, int day) => new(year, month, day);
        HashSet<DateOnly> days = _jurisdiction switch
        {
            Jurisdiction.DE =>
            [
                Fixed(1, 1), easter.AddDays(-2), easter.AddDays(1), Fixed(5, 1), easter.AddDays(39),
                easter.AddDays(50), Fixed(10, 3), Fixed(12, 25), Fixed(12, 26),
            ],
            Jurisdiction.AT =>
            [
                Fixed(1, 1), Fixed(1, 6), easter.AddDays(1), Fixed(5, 1), easter.AddDays(39), easter.AddDays(50),
                easter.AddDays(60), Fixed(8, 15), Fixed(10, 26), Fixed(11, 1), Fixed(12, 8), Fixed(12, 25), Fixed(12, 26),
            ],
            Jurisdiction.FR =>
            [
                Fixed(1, 1), easter.AddDays(1), Fixed(5, 1), Fixed(5, 8), easter.AddDays(39), easter.AddDays(50),
                Fixed(7, 14), Fixed(8, 15), Fixed(11, 1), Fixed(11, 11), Fixed(12, 25),
            ],
            _ => throw new ArgumentOutOfRangeException(nameof(year), _jurisdiction, "unknown jurisdiction"),
        };
        _cache[year] = days;
        return days;
    }

    /// <summary>Gregorian Easter Sunday (Meeus/Jones/Butcher). Pure integer arithmetic.</summary>
    public static DateOnly Easter(int year)
    {
        int a = year % 19, b = year / 100, c = year % 100, d = b / 4, e = b % 4;
        int f = (b + 8) / 25, g = (b - f + 1) / 3, h = ((19 * a) + b - d - g + 15) % 30;
        int i = c / 4, k = c % 4, l = (32 + (2 * e) + (2 * i) - h - k) % 7;
        int m = (a + (11 * h) + (22 * l)) / 451;
        int month = (h + l - (7 * m) + 114) / 31, day = ((h + l - (7 * m) + 114) % 31) + 1;
        return new DateOnly(year, month, day);
    }
}
