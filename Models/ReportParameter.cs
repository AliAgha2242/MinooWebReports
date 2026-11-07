namespace MinooWebReports.Models;

public sealed class ReportParameter
{
    public int Id { get; set; }
    public int ParameterTypeId { get; set; }
    public int ReportDefinitionId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Type { get; set; } = "Text";
    public string? DataSource { get; set; }
    public string? DisplayNameField { get; set; }
    public string? ValueField { get; set; }
    public string? Options { get; set; }
    public int SortOrder { get; set; }
    public string? CalendarType { get; set; }
    public string? DateFormat { get; set; }
    public string? TimeFormat { get; set; }
    public string? DateTimeSeparator { get; set; }
    public string? DefaultValue { get; set; }
}
