-- sim: name=server-info
-- sim: minversion=11.0
-- sim: azure=unsupported
-- sim: columns=ServerName,ProductVersion,Edition,EngineEdition,UptimeDays
SELECT
    CAST(SERVERPROPERTY('ServerName')       AS nvarchar(256)) AS ServerName,
    CAST(SERVERPROPERTY('ProductVersion')   AS nvarchar(64))  AS ProductVersion,
    CAST(SERVERPROPERTY('Edition')          AS nvarchar(128)) AS Edition,
    CAST(SERVERPROPERTY('EngineEdition')    AS int)           AS EngineEdition,
    DATEDIFF(DAY, si.sqlserver_start_time, SYSUTCDATETIME())  AS UptimeDays
FROM sys.dm_os_sys_info AS si;
