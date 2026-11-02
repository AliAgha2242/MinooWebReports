using System.Data;

namespace MinooWebReports.Models;

public sealed class ReportRunViewModel
{
    public ReportDefinition Report { get; set; } = new();
    public Dictionary<string, string?> Values { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public DataTable? Result { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 25;
    public int TotalRows { get; set; }
    public string? Error { get; set; }
    public GridPreferences Preferences { get; set; } = new();
}
