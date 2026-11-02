using System.Data;
using Microsoft.Data.SqlClient;
using MinooWebReports.Models;
using MinooWebReports.Persistence.StaticConfiguration;

namespace MinooWebReports.Services;

public sealed class CompanyAppLocationService(
    IConfiguration configuration,
    IMinooConfiguration minooConfiguration) : ICompanyAppLocationService
{
    private const string AccessSql = """
        SELECT DISTINCT paVahedeTejari.VahedeTejariSN, paVahedeTejari.VahedeTejariDs
        FROM LocUser
        INNER JOIN CompAppLocation
            ON CompAppLocation.CompAppLocationKeyID = LocUser.CompAppLocationKeyID
        INNER JOIN CompanyApp
            ON CompanyApp.CompanyAppKeyID = CompAppLocation.CompanyAppKeyID
        INNER JOIN Application
            ON Application.ApplicationKeyID = CompanyApp.ApplicationKeyID
        INNER JOIN Company
            ON Company.CompanyKeyID = CompanyApp.CompanyKeyID
        INNER JOIN paVahedeTejari
            ON paVahedeTejari.CompanyID = Company.CompanyID
        WHERE LocUser.UsersKeyID = (
            SELECT UsersKeyID FROM Users WHERE UserCode = @UserCode
        )
          AND ApplicationID = '5';
        """;

    public async Task<IReadOnlyList<VahedTejari>> GetAllLocationAsync(
        UserModel user, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);
        if (string.IsNullOrWhiteSpace(user.UserCode))
            throw new ArgumentException("کد کاربری برای دریافت واحدهای مجاز الزامی است.", nameof(user));

        var connectionString = configuration.GetConnectionString("ReportsMetadata");
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException("ConnectionStrings:ReportsMetadata تنظیم نشده است.");

        var servers = minooConfiguration.GetAllSerevr()
            .Where(x => x.VahedeTejariSN > 0)
            .GroupBy(x => x.VahedeTejariSN)
            .ToDictionary(x => x.Key, x => x.First());

        var result = new List<VahedTejari>();
        var seen = new HashSet<decimal>();
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(AccessSql, connection)
        {
            CommandType = CommandType.Text,
            CommandTimeout = 30
        };
        command.Parameters.Add("@UserCode", SqlDbType.NVarChar, 100).Value = user.UserCode.Trim();

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var snOrdinal = reader.GetOrdinal("VahedeTejariSN");
        var nameOrdinal = reader.GetOrdinal("VahedeTejariDs");
        while (await reader.ReadAsync(cancellationToken))
        {
            if (await reader.IsDBNullAsync(snOrdinal, cancellationToken))
                continue;

            var sn = Convert.ToDecimal(reader.GetValue(snOrdinal), System.Globalization.CultureInfo.InvariantCulture);
            if (sn <= 0 || !seen.Add(sn) || !servers.TryGetValue(sn, out var server))
                continue;

            var name = await reader.IsDBNullAsync(nameOrdinal, cancellationToken)
                ? string.Empty
                : Convert.ToString(reader.GetValue(nameOrdinal), System.Globalization.CultureInfo.CurrentCulture)?.Trim()
                    ?? string.Empty;

            result.Add(new VahedTejari
            {
                VahedetejariSn = sn,
                VahedetejariDs = string.IsNullOrWhiteSpace(name) ? server.VahedetejariDs : name,
                Server = string.IsNullOrWhiteSpace(server.ServerName) ? server.ServerIp : server.ServerName,
                ServerIp = server.ServerIp,
                DataBAset = server.DatabaseName
            });
        }

        return result.OrderBy(x => x.VahedetejariDs, StringComparer.CurrentCulture).ToArray();
    }
}