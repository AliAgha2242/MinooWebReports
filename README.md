# MinooWebReports

ASP.NET Core MVC foundation for a read-only dynamic reporting application.

## Requirements
- .NET 10 SDK
- Redis reachable at `rabbit-srv:6379`

## Run
```bash
dotnet restore
dotnet run
```

Redis is configured in `appsettings.json`. Override it with the standard
`ConnectionStrings__Redis` environment variable when needed.

## Current foundation
- Temporary business-unit connection list.
- Per-browser-session selected SQL Server/database context.
- SQL Server connection-string factory using Windows Integrated Security.
- Redis-backed per-user/per-report grid preference service.
- MVC/Razor UI for selecting the active business unit.

## Redis report preference key
Authenticated users use:

```
MinooWebReports:UserReport:{userId}:{reportId}
```

The value is JSON containing page size, hidden columns, column filters, and sorting.
Until authentication is configured, anonymous sessions are isolated using their
ASP.NET session identifier. Once authentication is added, the stable user ID claim
(`NameIdentifier` or `sub`) is used automatically.

## Important
The selected connection currently stores only server/database metadata in session;
no database credentials are stored in Redis. Report execution and the dynamic grid
will be added in the next implementation step. The report SQL must be treated as
read-only and parameterized; never concatenate user input into SQL.
