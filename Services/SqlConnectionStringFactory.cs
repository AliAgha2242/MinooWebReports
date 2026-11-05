using Microsoft.Data.SqlClient;
using MinooWebReports.Models;

namespace MinooWebReports.Services;

public sealed class SqlConnectionStringFactory : IConnectionStringFactory
{
    public string Create(VahedTejari connection)
    {
        ArgumentNullException.ThrowIfNull(connection);

        if (string.IsNullOrWhiteSpace(connection.Server))
            throw new ArgumentException("Server is required.", nameof(connection));

        if (string.IsNullOrWhiteSpace(connection.DataBAset))
            throw new ArgumentException("Database is required.", nameof(connection));

        var builder = new SqlConnectionStringBuilder
        {
            DataSource = connection.Server.Trim(),
            InitialCatalog = connection.DataBAset.Trim(),
            IntegratedSecurity = false,
            TrustServerCertificate = true,
            ConnectTimeout = 15,
            ApplicationName = "MinooWebReports",
            UserID="tavakoli",
            Password="11"
        };

        return builder.ConnectionString;
    }
}
