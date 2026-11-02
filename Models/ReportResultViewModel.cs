using System.Data;

namespace MinooWebReports.Models;

public sealed class ReportResultViewModel
{
    public ReportDefinition Report { get; set; } = new();
    public Dictionary<string, string?> Values { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string?> Filters { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public DataTable Result { get; set; } = new();
    public IReadOnlyList<string> AllColumns { get; set; } = [];
    public List<string> HiddenColumns { get; set; } = [];
    public string? SortColumn { get; set; }
    public bool SortDescending { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 25;
    public int TotalRows { get; set; }
    public int TotalPages => Math.Max(1, (int)Math.Ceiling(TotalRows / (double)PageSize));
    public string? Error { get; set; }
}
