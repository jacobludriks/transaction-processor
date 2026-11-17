-- Creates the two managed identities as contained database users and grants
-- them access. Run through setup-sql-users.sh (it supplies the variables), as
-- the Microsoft Entra admin of the SQL server, against the POC database.
--
-- Idempotent: running it again changes nothing that is already correct, and it
-- removes any role the processor user should not have.
--
--   $(ProcessorName)      name of the processor identity (the SQL user name)
--   $(ToolingName)        name of the tooling identity (the SQL user name)
--   $(ProcessorClientId)  client ID, used only when $(UseSid) is 1
--   $(ToolingClientId)    client ID, used only when $(UseSid) is 1
--   $(UseSid)             0 = CREATE USER ... FROM EXTERNAL PROVIDER (by name)
--                         1 = CREATE USER ... WITH SID (needs no directory lookup)
--
-- No password appears anywhere: the users are contained users backed by Entra.

SET NOCOUNT ON;

DECLARE @sql nvarchar(max);

-- ---- Processor identity: read only ------------------------------------------

IF DATABASE_PRINCIPAL_ID(N'$(ProcessorName)') IS NULL
BEGIN
    IF $(UseSid) = 1
        SET @sql = N'CREATE USER ' + QUOTENAME(N'$(ProcessorName)')
                 + N' WITH SID = ' + CONVERT(nvarchar(34), CAST(CAST(N'$(ProcessorClientId)' AS uniqueidentifier) AS varbinary(16)), 1)
                 + N', TYPE = E;';
    ELSE
        SET @sql = N'CREATE USER ' + QUOTENAME(N'$(ProcessorName)') + N' FROM EXTERNAL PROVIDER;';
    EXEC sys.sp_executesql @sql;
    PRINT N'Created user $(ProcessorName)';
END
ELSE
    PRINT N'User $(ProcessorName) already exists';

ALTER ROLE db_datareader ADD MEMBER [$(ProcessorName)];

-- The processor must never be able to write or change the schema.
IF IS_ROLEMEMBER(N'db_datawriter', N'$(ProcessorName)') = 1 ALTER ROLE db_datawriter DROP MEMBER [$(ProcessorName)];
IF IS_ROLEMEMBER(N'db_ddladmin',   N'$(ProcessorName)') = 1 ALTER ROLE db_ddladmin   DROP MEMBER [$(ProcessorName)];

-- ---- Tooling identity: read, write and create tables (seeding) --------------

IF DATABASE_PRINCIPAL_ID(N'$(ToolingName)') IS NULL
BEGIN
    IF $(UseSid) = 1
        SET @sql = N'CREATE USER ' + QUOTENAME(N'$(ToolingName)')
                 + N' WITH SID = ' + CONVERT(nvarchar(34), CAST(CAST(N'$(ToolingClientId)' AS uniqueidentifier) AS varbinary(16)), 1)
                 + N', TYPE = E;';
    ELSE
        SET @sql = N'CREATE USER ' + QUOTENAME(N'$(ToolingName)') + N' FROM EXTERNAL PROVIDER;';
    EXEC sys.sp_executesql @sql;
    PRINT N'Created user $(ToolingName)';
END
ELSE
    PRINT N'User $(ToolingName) already exists';

ALTER ROLE db_datareader ADD MEMBER [$(ToolingName)];
ALTER ROLE db_datawriter ADD MEMBER [$(ToolingName)];
-- db_ddladmin lets the seeder create the item and SKU tables and their indexes.
ALTER ROLE db_ddladmin   ADD MEMBER [$(ToolingName)];

-- ---- Result -----------------------------------------------------------------

SELECT u.name AS [user], u.type_desc AS [type], r.name AS [role]
FROM sys.database_principals AS u
LEFT JOIN sys.database_role_members AS m ON m.member_principal_id = u.principal_id
LEFT JOIN sys.database_principals  AS r ON r.principal_id = m.role_principal_id
WHERE u.name IN (N'$(ProcessorName)', N'$(ToolingName)')
ORDER BY u.name, r.name;
