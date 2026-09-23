namespace DsarClock.Core;

/// <summary>
/// The Art. 12(3) GDPR response deadline, computed under Regulation (EEC,
/// Euratom) No 1182/71 on periods, dates and time limits, which governs how
/// "one month" is counted in EU law:
///
/// <list type="bullet">
/// <item>Art. 3(1): the day of the event (receipt) is not counted.</item>
/// <item>Art. 3(2)(c): a period in months ends on the day of the last month
/// with the same date as the day of receipt -- or, if that month has no such
/// date, on its last day. Received 31 January, due 28 (or 29) February.</item>
/// <item>Art. 3(4): if the last day is a public holiday, a Saturday or a
/// Sunday, the period ends on the next working day.</item>
/// </list>
///
/// The extension of "two further months" admits two readings: three months
/// from receipt, or two months from the end of the first month. They differ
/// when the first month was cut short (received 31 January: 30 April versus
/// 28 April). The clock uses the earlier of the two. A compliance tool that
/// has to guess should guess the date that cannot be late.
/// </summary>
public static class GdprDeadline
{
    public static DateOnly Initial(DateOnly receivedOn, HolidayCalendar calendar) =>
        calendar.NextWorkingDayOnOrAfter(AddMonths(receivedOn, 1));

    public static DateOnly Extended(DateOnly receivedOn, HolidayCalendar calendar)
    {
        DateOnly threeFromReceipt = AddMonths(receivedOn, 3);
        DateOnly twoFromFirstEnd = AddMonths(AddMonths(receivedOn, 1), 2);
        DateOnly earlier = threeFromReceipt < twoFromFirstEnd ? threeFromReceipt : twoFromFirstEnd;
        return calendar.NextWorkingDayOnOrAfter(earlier);
    }

    /// <summary>Same date n months later, clamped to the month's last day (1182/71 Art. 3(2)(c)).</summary>
    public static DateOnly AddMonths(DateOnly from, int months)
    {
        DateOnly firstOfTarget = new DateOnly(from.Year, from.Month, 1).AddMonths(months);
        int day = Math.Min(from.Day, DateTime.DaysInMonth(firstOfTarget.Year, firstOfTarget.Month));
        return new DateOnly(firstOfTarget.Year, firstOfTarget.Month, day);
    }

    /// <summary>Working days from <paramref name="today"/> (exclusive) to <paramref name="due"/> (inclusive); negative when overdue.</summary>
    public static int WorkingDaysLeft(DateOnly today, DateOnly due, HolidayCalendar calendar)
    {
        if (today == due) return 0;
        int sign = due > today ? 1 : -1;
        int count = 0;
        for (DateOnly d = today; d != due;)
        {
            d = d.AddDays(sign);
            if (calendar.IsWorkingDay(d)) count += sign;
        }
        return count;
    }
}
