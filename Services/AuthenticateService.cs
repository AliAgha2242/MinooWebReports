using System.Data;
using System.Globalization;
using Microsoft.Data.SqlClient;
using MinooWebReports.Security;

namespace MinooWebReports.Services;

public interface IAuthenticateService
{
    Task<AuthenticatedUser?> ValidateCredentialsAsync(
        string username,
        string password,
        CancellationToken cancellationToken = default);
}

public sealed record AuthenticatedUser(int UserId, string UserName, string UserCode);

public sealed class AuthenticateService(IConfiguration configuration) : IAuthenticateService
{
    private const string UserSql = """
        SELECT TOP (1) UserId, UserName, UserPassword, UserCode
        FROM dbo.Users
        WHERE UserId = @UserId;
        """;

    public async Task<AuthenticatedUser?> ValidateCredentialsAsync(
        string username,
        string password,
        CancellationToken cancellationToken = default)
    {
        // This deliberately follows MinooWebCore: the login identifier is Users.UserId.
        if (!int.TryParse(username, NumberStyles.Integer, CultureInfo.InvariantCulture, out var userId))
            return null;

        var connectionString = configuration.GetConnectionString("CoreAuthentication");
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException(
                "ConnectionStrings:CoreAuthentication must point to the same database used by MinooWebCore.");

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = new SqlCommand(UserSql, connection)
        {
            CommandType = CommandType.Text,
            CommandTimeout = 15
        };
        command.Parameters.Add("@UserId", SqlDbType.Int).Value = userId;

        await using var reader = await command.ExecuteReaderAsync(
            CommandBehavior.SingleRow, cancellationToken);

        if (!await reader.ReadAsync(cancellationToken))
            return null;

        var storedPassword = reader.IsDBNull(reader.GetOrdinal("UserPassword"))
            ? null
            : reader.GetString(reader.GetOrdinal("UserPassword"));
        if (string.IsNullOrEmpty(storedPassword))
            return null;

        string? decodedPassword;
        try
        {
            // Use the same legacy decoder used by MinooWebCore's DecriptServices.
            decodedPassword = DecodePass.Security.Decode(storedPassword);
        }
        catch (Exception)
        {
            return null;
        }

        if (!string.Equals(decodedPassword, password, StringComparison.Ordinal))
            return null;

        var name = reader.IsDBNull(reader.GetOrdinal("UserName"))
            ? string.Empty
            : reader.GetString(reader.GetOrdinal("UserName"));
        var userCode = reader.IsDBNull(reader.GetOrdinal("UserCode"))
            ? string.Empty
            : Convert.ToString(reader.GetValue(reader.GetOrdinal("UserCode")), CultureInfo.InvariantCulture)
                ?? string.Empty;

        if (string.IsNullOrWhiteSpace(userCode))
            return null;

        return new AuthenticatedUser(userId, name, userCode);
    }
}
