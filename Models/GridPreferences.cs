namespace MinooWebReports.Models;

/// <summary>
/// Per-user, per-report presentation preferences. These settings never modify report data.
/// </summary>
public sealed class GridPreferences
{
    public int PageSize { get; set; } = 25;
    public List<string> HiddenColumns { get; set; } = [];
    public Dictionary<string, string> ColumnFilters { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public string? SortColumn { get; set; }
    public bool SortDescending { get; set; }
}
