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

The shared Minoo server configuration file must be available beside the published application as `AppConfig.json` (or set `MinooConfiguration__FilePath` to its full path). The file must contain a `ServersInfo` array with `ServerName` (or `Ip`), `databaseName`, and `VahedeTejariSN`. An optional business-unit display name is read from `VahedetejariDs`, `VahedeTejariDs`, `VahedeTejariDS`, `VahedeTejariName`, `BusinessUnitName`, `UnitName`, or `Name`. The configuration file is environment-specific and must not be committed with secrets.

## Authentication and single sign-on

MinooWebReports supports both SSO and standalone login:

- When the configured MinooWebCore login page responds successfully, unauthenticated users are redirected to the parent login and returned through the one-time SSO ticket flow.
- The parent login endpoint is probed with a short timeout. If Core is stopped, unreachable, or returns an unsuccessful response, Reports displays its own login page.
- Standalone login uses the same credential validation behavior as MinooWebCore: the submitted username is matched against `Users.UserCode`, and the stored password is decoded with the same legacy `DecodePass.Security.Decode` routine before ordinal comparison.
- Configure `ConnectionStrings__CoreAuthentication` to the same database connection string used by MinooWebCore's `ConnectionStrings:DefaultConnection`. This is required for standalone login and must be supplied through environment variables or a secret store.
- Reports issues its own authentication cookie and includes `UserCode` as a claim so the existing business-unit access query continues to work.

Example:

```text
ConnectionStrings__CoreAuthentication=<same database connection string as MinooWebCore>
```

## Single sign-on configuration

Configure these values on the Reports deployment. The client secret must be a high-entropy secret shared with MinooWebCore and supplied through a secret store or environment variable, never committed to source control:

```text
Sso__Parent__LoginUrl=https://<minoo-webcore-host>/Account/Login
Sso__Parent__LaunchPath=/Account/LaunchReports
Sso__Parent__RedeemUrl=https://<minoo-webcore-host>/api/sso/reports/redeem
Sso__Parent__ClientSecret=<shared-secret>
```

MinooWebReports accepts a short-lived one-time ticket from the parent application, redeems it server-to-server, and issues its own secure authentication cookie. When the parent is reachable, direct access redirects to its login. When it is unavailable, local login remains available. The SSO ticket flow still requires the shared client secret.

Business-unit choices are loaded from the user's `UserCode` access rows for `ApplicationID = '5'`; only units also present in `ServersInfo` are offered. The selected unit is revalidated against the user's current access list on report execution.

## Run

```bash
dotnet restore
dotnet run
```

## Implemented

- Per-session selection from the current user's authorized business units; server and database details are resolved from `ServersInfo` in the shared Minoo `AppConfig.json` configuration.
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
- Read-only results grid with per-column header filters, sorting, checkbox-based column visibility, resizable columns (90px minimum), optional pagination and drag-and-drop multi-level grouping (up to three columns).
- Four report-grid themes (Midnight, Light, Ocean and High Contrast), with layout preferences saved in browser `localStorage` and synchronized to per-user Redis settings on grid submission.
- Grouping is applied after filtering and before pagination; group keys are sorted ascending to keep each group's rows together. A group that crosses a page boundary is shown with its heading again on the next page.
- Per-user/per-report grid preferences, including grouping columns, are stored in Redis.

## Default parameter values and administration

Configure the user codes allowed to manage parameter defaults on the Reports deployment:

```text
Reports__AdminUserCodes__0=<admin-user-code>
Reports__AdminUserCodes__1=<another-admin-user-code>
```

Only configured users can open `/AdminReports` and update defaults. The feature stores defaults in `dbo.ReportParameters.DefaultValue`. Apply `Database/Migrations/20261107_AddDefaultValueToReportParameters.sql` once before deploying the application changes.

- Text, Number, Date, Time and DateTime defaults are stored as text in the parameter's configured format.
- Checkbox defaults are `true` or `false`.
- RadioButton and ComboBox defaults are a single option value.
- ListBox defaults are JSON arrays containing numeric values, for example `[12, 14.5]`.
- On the run form, defaults are prefilled and identified to the user. Blank or missing values fall back to the default; an unchecked checkbox is submitted as `false`.

## Metadata database

The application expects these columns:

- `ReportDefinitions`: `Id`, `Name`, `SourceType`, `SqlQuery`, `ExcelFileName`, `ExcelContent`, `NeedsPagination`, `PageSize`, `IsDeleted`.
- `ReportParameters`: `Id`, `ReportDefinitionId`, `Name`, `DisplayName`, `Type`, `DataSource`, `DisplayNameField`, `ValueField`, `Options`, `SortOrder`, `DefaultValue`.

Both `SourceType=Sql` and stored `.xlsx` content are executable. Excel reports read the first worksheet, use its first used row as headers, and can filter rows by report parameters whose names match column names. Query-backed parameter options execute a single read-only SELECT against the selected target database and are limited to 2,000 options.

## Redis preference key

Authenticated users use:

```text
MinooWebReports:UserReport:{userId}:{reportId}
```

Preferences include page size, hidden columns, filters, sorting, column widths, selected theme and up to three ordered grouping columns, scoped to the authenticated user's report workspace.

## Production security requirements

- Give the report execution identity **SELECT-only permissions** on report databases. The SQL text gate is defense-in-depth, not a substitute for database permissions.
- Never grant report users write access to target databases.
- Per-user authentication through MinooWebCore SSO or standalone credential validation, plus per-request business-unit access validation. Per-report authorization policies still need to be defined.
- Store connection strings and credentials in environment variables or a secret store, never in source control.
- The metadata connection string must be configured as `ConnectionStrings__ReportsMetadata`; it is intentionally not hard-coded.
- The Minoo `AppConfig.json` server directory must be present and maintained in each deployment environment.

## Multi-server report execution

SQL report definitions now support two execution modes:

- UseDefaultServer = 1: execute against the user's currently selected and authorized business unit.
- UseDefaultServer = 0: execute against every VahedeTejariSN stored in dbo.ReportDefinitionServers.

Before a multi-server run, Reports checks the current user's access to all configured targets. If any target is unauthorized or missing, the report is rejected rather than returning a partial result. Results from the configured servers are merged by column name before filtering, sorting, grouping and pagination.

Apply Database/Migrations/20261108_AddMultiServerReportTargets.sql to the database containing dbo.ReportDefinitions before deploying. If MinooWebCore and MinooWebReports point to the same metadata database, run the script only once. Existing single-server selections are migrated into ReportDefinitionServers.

### Multi-server Rabbit query contract

Multi-server SQL reports are sent through RabbitSender by NotConfiguredMultiServerReportQueryService. The Rabbit message keeps the existing `querytext` property and now also sends:

- `parameters`: a dictionary of report parameter names to submitted values (or null).
- `parameterTypes`: a dictionary of parameter names to their configured UI types.

The SQL text is sent unchanged. The Rabbit receiver must bind values from `parameters` to `SqlParameter` instances; it must never inject values into SQL by string replacement. The `parameterTypes` map lets the receiver convert Number, Date, Time, DateTime and Checkbox values and handle ListBox's comma-separated numeric values explicitly. The receiver must continue returning a JSON-serialized DataTable in each successful Rabbit result.

The producer validates the report's read-only SELECT query and parameter values before sending. It also rejects missing, duplicate, unexpected, empty or non-deserializable server responses rather than merging incomplete results. The RabbitSender API currently used by the project exposes a synchronous `SendTransactionalParallelism` call, which is offloaded from the ASP.NET request thread; cancellation cannot interrupt a call already running inside that synchronous library method.

Dynamic ListBox/ComboBox option queries are executed against every selected server and their options are merged by value (the first label for a duplicate value is retained). Static options are loaded once.

**Receiver-side change required:** if the current Rabbit receiver only reads `querytext`, it will ignore the new parameter dictionaries. Update that receiver to bind `parameters` and `parameterTypes` safely before relying on parameterized multi-server reports. Default-server reports continue to use the existing local `IReportExecutionService`.
