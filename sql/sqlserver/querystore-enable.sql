-- sim: name=querystore-enable
-- sim: minversion=13.0
-- sim: azure=supported
-- sim: columns=Applied
-- @DatabaseName, @MaxStorageSizeMb and @StaleQueryThresholdDays are supplied as parameters.
-- ALTER DATABASE cannot be parameterized, so the name is quoted with QUOTENAME and the
-- numeric options are concatenated from integer parameters only (never free text).
DECLARE @sql nvarchar(max) =
    N'ALTER DATABASE ' + QUOTENAME(@DatabaseName) + N' SET QUERY_STORE = ON (' +
    N' OPERATION_MODE = READ_WRITE,' +
    N' QUERY_CAPTURE_MODE = AUTO,' +
    N' SIZE_BASED_CLEANUP_MODE = AUTO,' +
    N' MAX_STORAGE_SIZE_MB = ' + CAST(@MaxStorageSizeMb AS nvarchar(20)) + N',' +
    N' CLEANUP_POLICY = (STALE_QUERY_THRESHOLD_DAYS = ' + CAST(@StaleQueryThresholdDays AS nvarchar(20)) + N')' +
    N');';
EXEC sys.sp_executesql @sql;
SELECT CAST(1 AS bit) AS Applied;
