using MinooWebReports.Models;

namespace MinooWebReports.Models;

public sealed class ReportParameterAdminViewModel
{
    public IReadOnlyList<ReportDefinition> Reports { get; set; } = [];
}
