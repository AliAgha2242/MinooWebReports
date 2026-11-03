using System.Data;
using Microsoft.Data.SqlClient;
using Decode;

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
        WHERE UserCode = @UserCode;
        """;

    public async Task<AuthenticatedUser?> ValidateCredentialsAsync(
        string username,
        string password,
        CancellationToken cancellationToken = default)
    {
        // Match the current MinooWebCore implementation: the login identifier is Users.UserCode.
        if (string.IsNullOrWhiteSpace(username))
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
        command.Parameters.Add("@UserCode", SqlDbType.NVarChar, 100).Value = username.Trim();

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
            : Convert.ToString(reader.GetValue(reader.GetOrdinal("UserCode"))) ?? string.Empty;

        if (string.IsNullOrWhiteSpace(userCode))
            return null;

        var authenticatedUserId = reader.GetInt32(reader.GetOrdinal("UserId"));
        return new AuthenticatedUser(authenticatedUserId, name, userCode);
    }
}
