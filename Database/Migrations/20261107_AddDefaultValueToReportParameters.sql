-- Adds an optional default value to report parameters.
-- Run once against the MinooWebReports metadata database.

IF COL_LENGTH(N'dbo.ReportParameters', N'DefaultValue') IS NULL
BEGIN
    ALTER TABLE dbo.ReportParameters
        ADD DefaultValue NVARCHAR(MAX) NULL;
END;
GO
