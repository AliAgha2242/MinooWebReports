using Microsoft.Data.SqlClient;
using MinooWebReports.Models;

namespace MinooWebReports.Services;

public sealed class SqlConnectionStringFactory(IConfiguration configuration) : IConnectionStringFactory
{
    public string Create(VahedTejari connection)
    {
        ArgumentNullException.ThrowIfNull(connection);

        if (string.IsNullOrWhiteSpace(connection.Server))
            throw new ArgumentException("Server is required.", nameof(connection));

        if (string.IsNullOrWhiteSpace(connection.DataBAset))
            throw new ArgumentException("Database is required.", nameof(connection));

        var userName = configuration["Reports:TargetSql:UserName"];
        var password = configuration["Reports:TargetSql:Password"];

        if (string.IsNullOrWhiteSpace(userName) || string.IsNullOrWhiteSpace(password))
            throw new InvalidOperationException(
                "Reports:TargetSql:UserName and Reports:TargetSql:Password must be configured.");

        var builder = new SqlConnectionStringBuilder
        {
            DataSource = connection.Server.Trim(),
            InitialCatalog = connection.DataBAset.Trim(),
            IntegratedSecurity = false,
            TrustServerCertificate = true,
            ConnectTimeout = 15,
            ApplicationName = "MinooWebReports",
            UserID = userName,
            Password = password
        };

        return builder.ConnectionString;
    }
}
