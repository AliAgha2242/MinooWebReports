namespace MinooWebReports.Models;

public sealed class ReportDefinition
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string SourceType { get; set; } = "Sql";
    public string? SqlQuery { get; set; }
    public string? ExcelFileName { get; set; }
    public byte[]? ExcelContent { get; set; }
    public bool NeedsPagination { get; set; }
    public int PageSize { get; set; } = 25;
    public IReadOnlyList<ReportParameter> Parameters { get; set; } = [];
}
