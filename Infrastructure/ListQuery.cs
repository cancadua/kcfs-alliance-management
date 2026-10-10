using System.Collections;
using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;

namespace AllianceRewards.Api.Infrastructure;

/// <summary>
/// Sorts list responses by any column of the response type, from the query string:
/// <c>?sortBy=column&amp;sortDir=asc|desc</c>. Column names are the JSON (camelCase) property names.
/// Lists are small (one alliance), so this runs in memory after the database query.
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
        result = items.ToList();
        error = null;

        var sortBy = query["sortBy"].ToString().Trim();
        var sortDir = query["sortDir"].ToString().Trim();
        if (sortBy.Length == 0) return true;

        var columns = ColumnsOf<T>();
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
        result = descending
            ? result.OrderByDescending(x => column.GetValue(x), ValueComparer).ToList()
            : result.OrderBy(x => column.GetValue(x), ValueComparer).ToList();
        return true;
    }

    /// <summary>Returns the list sorted per the request's query string, or 400 with an error.</summary>
    public static ActionResult<List<T>> ListResult<T>(this ControllerBase controller, IEnumerable<T> items) =>
        TryApply(items, controller.Request.Query, out var result, out var error)
            ? result
            : controller.BadRequest(new { error });

    private static Dictionary<string, PropertyInfo> ColumnsOf<T>() =>
        ColumnCache.GetOrAdd(typeof(T), t => t
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => IsScalar(p.PropertyType))
            .ToDictionary(p => JsonNamingPolicy.CamelCase.ConvertName(p.Name), StringComparer.OrdinalIgnoreCase));

    private static bool IsScalar(Type type)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;
        return type.IsPrimitive || type.IsEnum || type == typeof(string) || type == typeof(decimal) ||
               type == typeof(DateTime) || type == typeof(Guid);
    }
}
