using DocumentFormat.OpenXml.Spreadsheet;
using MinooWebReports.Models;

namespace MinooWebReports.Services
{
    public interface ICompanyAppLocationService
    {
        Task<VahedTejari> GetAllLocationAsync(UserModel UserId);
    }
}
