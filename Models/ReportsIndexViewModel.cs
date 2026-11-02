namespace MinooWebReports.Models;

public sealed class ReportsIndexViewModel
{
    public IReadOnlyList<ReportDefinition> Reports { get; set; } = [];
    public VahedTejari? SelectedConnection { get; set; }
}
