/*
    Multi-server report execution metadata.
    Run this script against the database that contains dbo.ReportDefinitions.
    It is safe to run more than once.
*/
IF COL_LENGTH(N'dbo.ReportDefinitions', N'UseDefaultServer') IS NULL
BEGIN
    ALTER TABLE dbo.ReportDefinitions
        ADD UseDefaultServer BIT NOT NULL
            CONSTRAINT DF_ReportDefinitions_UseDefaultServer DEFAULT (1) WITH VALUES;
END;
GO

IF OBJECT_ID(N'dbo.ReportDefinitionServers', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ReportDefinitionServers
    (
        ReportDefinitionId INT NOT NULL,
        VahedeTejariSN DECIMAL(18,3) NOT NULL,
        CONSTRAINT PK_ReportDefinitionServers
            PRIMARY KEY CLUSTERED (ReportDefinitionId, VahedeTejariSN),
        CONSTRAINT FK_ReportDefinitionServers_ReportDefinitions
            FOREIGN KEY (ReportDefinitionId)
            REFERENCES dbo.ReportDefinitions(Id)
            ON DELETE CASCADE
    );

    CREATE INDEX IX_ReportDefinitionServers_VahedeTejariSN
        ON dbo.ReportDefinitionServers(VahedeTejariSN);
END;
GO

/*
    Migrate existing single-server reports to the new relation.
    After migration, clear the legacy column so rerunning this script
    cannot overwrite a later server-mode change.
*/
IF COL_LENGTH(N'dbo.ReportDefinitions', N'SpecificServerVahedeTejariSN') IS NOT NULL
BEGIN
    UPDATE dbo.ReportDefinitions
    SET UseDefaultServer = 0
    WHERE SpecificServerVahedeTejariSN IS NOT NULL;

    INSERT INTO dbo.ReportDefinitionServers (ReportDefinitionId, VahedeTejariSN)
    SELECT report.Id, report.SpecificServerVahedeTejariSN
    FROM dbo.ReportDefinitions AS report
    WHERE report.SpecificServerVahedeTejariSN IS NOT NULL
      AND NOT EXISTS
      (
          SELECT 1
          FROM dbo.ReportDefinitionServers AS existing
          WHERE existing.ReportDefinitionId = report.Id
            AND existing.VahedeTejariSN = report.SpecificServerVahedeTejariSN
      );

    UPDATE dbo.ReportDefinitions
    SET SpecificServerVahedeTejariSN = NULL
    WHERE SpecificServerVahedeTejariSN IS NOT NULL;
END;
GO
