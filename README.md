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

The report-target connection factory currently uses Windows Integrated Security. Under IIS, the application-pool identity must have the required read permissions on the selected report databases.

## Run

```bash
dotnet restore
dotnet run
```

## Implemented

- Per-session business-unit selection (currently a temporary list in `HomeController`).
- SQL Server connection-string construction for the selected server/database.
- Active report catalog from `dbo.ReportDefinitions`.
- Dynamic parameter form based on `dbo.ReportParameters`:
  - Text, Number, Date, Checkbox, RadioButton, ListBox.
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
- Configure authentication and per-report authorization before exposing this application to end users. Those policies are not yet integrated in this foundation.
- Store connection strings and credentials in environment variables or a secret store, never in source control.
- The metadata connection string must be configured as `ConnectionStrings__ReportsMetadata`; it is intentionally not hard-coded.
- The temporary business-unit list must be replaced by the trusted unit directory before production.
