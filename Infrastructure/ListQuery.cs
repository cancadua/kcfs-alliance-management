using System.Collections;
using System.Collections.Concurrent;
using System.Globalization;
using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;

namespace AllianceRewards.Api.Infrastructure;

/// <summary>
/// Filters and sorts list responses by any column of the response type, from the query string.
/// Column names are the JSON (camelCase) property names. Lists are small (one alliance), so this runs
/// in memory after the database query.
/// <list type="bullet">
/// <item><c>?column=value</c> filters: text contains (case-insensitive); enums match by name, several as
/// <c>a,b</c>; bool, Guid and numbers match exactly; a date without time matches that whole day (UTC).
/// <c>null</c> / <c>notnull</c> test for a missing value.</item>
/// <item><c>?columnMin=</c> / <c>?columnMax=</c> give an inclusive range for numbers and dates.</item>
/// <item><c>?sortBy=column&amp;sortDir=asc|desc</c> sorts.</item>
/// </list>
/// Query parameters that do not name a column are ignored, so endpoint-specific parameters keep working.
/// </summary>
public static class ListQuery
{
    private static readonly ConcurrentDictionary<Type, Dictionary<string, PropertyInfo>> ColumnCache = new();

    private static readonly IComparer<object?> ValueComparer = Comparer<object?>.Create((a, b) =>
        a is string sa && b is string sb
            ? StringComparer.InvariantCultureIgnoreCase.Compare(sa, sb)
            : Comparer.Default.Compare(a, b));

    /// <summary>Applies the query string to <paramref name="items"/>; returns false with an error for invalid input.</summary>
    public static bool TryApply<T>(IEnumerable<T> items, IQueryCollection query, out List<T> result, out string? error)
    {
        IEnumerable<T> current = items;
        result = [];
        error = null;
        var columns = ColumnsOf<T>();

        foreach (var (key, values) in query)
        {
            var raw = values.ToString().Trim();
            if (raw.Length == 0 || key is "sortBy" or "sortDir") continue;

            Func<object?, bool>? predicate;
            if (columns.TryGetValue(key, out var column))
                predicate = ValueFilter(column, raw, out error);
            else if (RangeColumn(columns, key, "Min", out column))
                predicate = RangeFilter(column, raw, isMax: false, out error);
            else if (RangeColumn(columns, key, "Max", out column))
                predicate = RangeFilter(column, raw, isMax: true, out error);
            else
                continue;

            if (predicate is null) return false;
            var prop = column;
            current = current.Where(x => predicate(prop.GetValue(x)));
        }

        result = current.ToList();
        return Sort(ref result, columns, query, out error);
    }

    /// <summary>Returns the list filtered and sorted per the request's query string, or 400 with an error.</summary>
    public static ActionResult<List<T>> ListResult<T>(this ControllerBase controller, IEnumerable<T> items) =>
        TryApply(items, controller.Request.Query, out var result, out var error)
            ? result
            : controller.BadRequest(new { error });

    private static bool Sort<T>(ref List<T> items, Dictionary<string, PropertyInfo> columns, IQueryCollection query, out string? error)
    {
        error = null;
        var sortBy = query["sortBy"].ToString().Trim();
        var sortDir = query["sortDir"].ToString().Trim();
        if (sortBy.Length == 0) return true;

        if (!columns.TryGetValue(sortBy, out var column))
        {
            error = $"Unknown sort column '{sortBy}'. Allowed: {string.Join(", ", columns.Keys)}.";
            return false;
        }

        bool descending;
        if (sortDir.Length == 0 || sortDir.Equals("asc", StringComparison.OrdinalIgnoreCase)) descending = false;
        else if (sortDir.Equals("desc", StringComparison.OrdinalIgnoreCase)) descending = true;
        else
        {
            error = "sortDir must be 'asc' or 'desc'.";
            return false;
        }

        // OrderBy is stable, so the endpoint's default order breaks ties.
        items = descending
            ? items.OrderByDescending(x => column.GetValue(x), ValueComparer).ToList()
            : items.OrderBy(x => column.GetValue(x), ValueComparer).ToList();
        return true;
    }

    private static Func<object?, bool>? ValueFilter(PropertyInfo column, string raw, out string? error)
    {
        error = null;
        var type = Nullable.GetUnderlyingType(column.PropertyType) ?? column.PropertyType;
        var name = ColumnName(column);

        if (raw.Equals("null", StringComparison.OrdinalIgnoreCase)) return v => v is null;
        if (raw.Equals("notnull", StringComparison.OrdinalIgnoreCase)) return v => v is not null;

        if (type == typeof(string))
            return v => v is string s && s.Contains(raw, StringComparison.InvariantCultureIgnoreCase);

        if (type.IsEnum)
        {
            var allowed = new HashSet<object>();
            foreach (var part in raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (!Enum.TryParse(type, part, ignoreCase: true, out var parsed) || int.TryParse(part, out _))
                {
                    error = $"Invalid value '{part}' for '{name}'. Allowed: {string.Join(", ", Enum.GetNames(type))}.";
                    return null;
                }
                allowed.Add(parsed!);
            }
            return v => v is not null && allowed.Contains(v);
        }

        if (type == typeof(DateTime))
        {
            if (!TryParseDate(raw, out var date, out var dateOnly))
            {
                error = $"Invalid date '{raw}' for '{name}'.";
                return null;
            }
            return dateOnly
                ? v => v is DateTime d && d.ToUniversalTime().Date == date
                : v => v is DateTime d && d.ToUniversalTime() == date;
        }

        if (!TryParseScalar(type, raw, out var expected))
        {
            error = $"Invalid value '{raw}' for '{name}'.";
            return null;
        }
        return v => Equals(v, expected);
    }

    private static Func<object?, bool>? RangeFilter(PropertyInfo column, string raw, bool isMax, out string? error)
    {
        error = null;
        var type = Nullable.GetUnderlyingType(column.PropertyType) ?? column.PropertyType;
        var name = ColumnName(column) + (isMax ? "Max" : "Min");

        if (type == typeof(DateTime))
        {
            if (!TryParseDate(raw, out var date, out var dateOnly))
            {
                error = $"Invalid date '{raw}' for '{name}'.";
                return null;
            }
            // A date-only maximum includes that whole day.
            if (isMax && dateOnly) return v => v is DateTime d && d.ToUniversalTime() < date.AddDays(1);
            return isMax
                ? v => v is DateTime d && d.ToUniversalTime() <= date
                : v => v is DateTime d && d.ToUniversalTime() >= date;
        }

        if (!TryParseScalar(type, raw, out var bound))
        {
            error = $"Invalid value '{raw}' for '{name}'.";
            return null;
        }
        return isMax
            ? v => v is not null && Comparer.Default.Compare(v, bound) <= 0
            : v => v is not null && Comparer.Default.Compare(v, bound) >= 0;
    }

    /// <summary>Matches <c>columnMin</c> / <c>columnMax</c> for numeric and date columns.</summary>
    private static bool RangeColumn(Dictionary<string, PropertyInfo> columns, string key, string suffix, out PropertyInfo column)
    {
        column = null!;
        if (key.Length <= suffix.Length || !key.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) return false;
        if (!columns.TryGetValue(key[..^suffix.Length], out var found)) return false;

        var type = Nullable.GetUnderlyingType(found.PropertyType) ?? found.PropertyType;
        if (type != typeof(DateTime) && !IsNumeric(type)) return false;
        column = found;
        return true;
    }

    private static bool TryParseDate(string raw, out DateTime date, out bool dateOnly)
    {
        dateOnly = !raw.Contains('T') && !raw.Contains(':');
        var ok = DateTime.TryParse(raw, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out date);
        if (ok && dateOnly) date = date.Date;
        return ok;
    }

    private static bool TryParseScalar(Type type, string raw, out object value)
    {
        value = null!;
        var inv = CultureInfo.InvariantCulture;
        if (type == typeof(Guid) && Guid.TryParse(raw, out var g)) value = g;
        else if (type == typeof(bool) && bool.TryParse(raw, out var b)) value = b;
        else if (type == typeof(int) && int.TryParse(raw, NumberStyles.Integer, inv, out var i)) value = i;
        else if (type == typeof(long) && long.TryParse(raw, NumberStyles.Integer, inv, out var l)) value = l;
        else if (type == typeof(double) && double.TryParse(raw, NumberStyles.Float, inv, out var d)) value = d;
        else if (type == typeof(decimal) && decimal.TryParse(raw, NumberStyles.Number, inv, out var m)) value = m;
        return value is not null;
    }

    private static bool IsNumeric(Type type) =>
        type == typeof(int) || type == typeof(long) || type == typeof(double) || type == typeof(decimal);

    private static string ColumnName(PropertyInfo p) => JsonNamingPolicy.CamelCase.ConvertName(p.Name);

    private static Dictionary<string, PropertyInfo> ColumnsOf<T>() =>
        ColumnCache.GetOrAdd(typeof(T), t => t
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => IsScalar(p.PropertyType))
            .ToDictionary(ColumnName, StringComparer.OrdinalIgnoreCase));

    private static bool IsScalar(Type type)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;
        return type.IsPrimitive || type.IsEnum || type == typeof(string) || type == typeof(decimal) ||
               type == typeof(DateTime) || type == typeof(Guid);
    }
}
