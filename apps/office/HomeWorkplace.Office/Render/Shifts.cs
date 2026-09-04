using System.Globalization;
using HomeWorkplace.Client;

namespace HomeWorkplace.Office.Render;

/// <summary>Office shifts from the employees' wake/sleep times; anything unparseable is the default day.</summary>
public static class Shifts
{
    public static readonly Shift Default = new(new TimeOnly(9, 0), new TimeOnly(20, 0));

    public static IReadOnlyList<Shift> From(IEnumerable<EmployeeDto> employees)
    {
        var shifts = employees.Select(e =>
            TimeOnly.TryParseExact(e.Wake, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var wake)
            && TimeOnly.TryParseExact(e.Sleep, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var sleep)
                ? new Shift(wake, sleep)
                : Default).ToList();
        return shifts.Count == 0 ? new[] { Default } : shifts;
    }
}
