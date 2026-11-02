# MinooWebReports

ASP.NET Core MVC reporting workspace. The UI follows the shared MinooWebCore visual language.

## Requirements
- .NET 10 SDK
- Redis reachable at `rabbit-srv:6379`
- SQL Server metadata database containing `dbo.ReportDefinitions` and `dbo.ReportParameters`
- Read-only SQL Server access for every report target

## Configuration

Redis can be overridden with:

```text
ConnectionStrings__Redis=rabbit-srv:6379,abortConnect=false
```

Configure the metadata database connection separately. Do not reuse a report-target connection for metadata unless that is explicitly intended:

```text
ConnectionStrings__ReportsMetadata=<metadata-database-connection-string>
```

Report execution limits:

```text
Reports__CommandTimeoutSeconds=60
Reports__MaxRows=5000
Reports__MaxExcelBytes=20971520
```

The report-target connection factory currently uses Windows Integrated Security. Under IIS, the application-pool identity must have the required read permissions on the selected report databases.\n\nThe shared Minoo server configuration file must be available beside the published application as `AppConfig.json` (or set `MinooConfiguration__FilePath` to its full path). The file must contain a `ServersInfo` array with `ServerName` (or `Ip`), `databaseName`, and `VahedeTejariSN`. An optional business-unit display name is read from `VahedetejariDs`, `VahedeTejariDs`, `VahedeTejariDS`, `VahedeTejariName`, `BusinessUnitName`, `UnitName`, or `Name`. The configuration file is environment-specific and must not be committed with secrets.

## Single sign-on configuration

Configure these values on the Reports deployment. The client secret must be a high-entropy secret shared with MinooWebCore and supplied through a secret store or environment variable, never committed to source control:

```text
Sso__Parent__LoginUrl=https://<minoo-webcore-host>/Account/Login
Sso__Parent__LaunchPath=/Account/LaunchReports
Sso__Parent__RedeemUrl=https://<minoo-webcore-host>/api/sso/reports/redeem
Sso__Parent__ClientSecret=<shared-secret>
```

MinooWebReports accepts a short-lived one-time ticket from the parent application, redeems it server-to-server, and issues its own secure authentication cookie. Direct access redirects to the parent login. Both applications must use HTTPS and share the same secret.

Business-unit choices are loaded from the user's `UserCode` access rows for `ApplicationID = '5'`; only units also present in `ServersInfo` are offered. The selected unit is revalidated against the user's current access list on report execution.

## Run

```bash
dotnet restore
dotnet run
```

## Implemented

- Per-session business-unit selection from `ServersInfo` in the shared Minoo `AppConfig.json` configuration.
- Server configuration is loaded through the Reports integration of `MinooConfiguration` and automatically reloaded when the file changes.
- SQL connection strings are generated from the selected configured server and database; client-submitted server/database values are never trusted.
- The report execution screen displays the selected business unit, server and database, with a loading overlay during execution.
- Report output is rendered in a dedicated full-page workspace, separate from parameter entry.
- SQL Server connection-string construction for the selected server/database.
- Active report catalog from `dbo.ReportDefinitions`.
- Dynamic parameter form based on `dbo.ReportParameters`:
  - Text, Number, Date, Time, DateTime, Checkbox, RadioButton, ListBox, ComboBox.
  - Radio/list options can be static JSON (`Options`) or loaded from a read-only `DataSource` SELECT query using `ValueField` and `DisplayNameField`.
- Parameterized SQL report execution against the selected business unit.
- Read-only SELECT-only query gate, command timeout and maximum row count.
- Read-only results grid with per-column filtering, sorting, column visibility and optional pagination.
- Per-user/per-report grid preferences stored in Redis.

## Metadata database

The application expects these columns:

- `ReportDefinitions`: `Id`, `Name`, `SourceType`, `SqlQuery`, `ExcelFileName`, `ExcelContent`, `NeedsPagination`, `PageSize`, `IsDeleted`.
- `ReportParameters`: `Id`, `ReportDefinitionId`, `Name`, `DisplayName`, `Type`, `DataSource`, `DisplayNameField`, `ValueField`, `Options`, `SortOrder`.

Both `SourceType=Sql` and stored `.xlsx` content are executable. Excel reports read the first worksheet, use its first used row as headers, and can filter rows by report parameters whose names match column names. Query-backed parameter options execute a single read-only SELECT against the selected target database and are limited to 2,000 options.

## Redis preference key

Authenticated users use:

```text
MinooWebReports:UserReport:{userId}:{reportId}
```

Until authentication is integrated, anonymous sessions are isolated by their ASP.NET session identifier. Preferences include page size, hidden columns, filters and sorting.

## Production security requirements

- Give the report execution identity **SELECT-only permissions** on report databases. The SQL text gate is defense-in-depth, not a substitute for database permissions.
- Never grant report users write access to target databases.
- Per-user authentication through MinooWebCore SSO and per-request business-unit access validation are integrated. Per-report authorization policies still need to be defined.
- Store connection strings and credentials in environment variables or a secret store, never in source control.
- The metadata connection string must be configured as `ConnectionStrings__ReportsMetadata`; it is intentionally not hard-coded.
- The Minoo `AppConfig.json` server directory must be present and maintained in each deployment environment.
