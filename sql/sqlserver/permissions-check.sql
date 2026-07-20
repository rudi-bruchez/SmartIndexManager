-- sim: name=permissions-check
-- sim: minversion=11.0
-- sim: azure=supported
-- sim: columns=CanViewState,CanAlter,CanAccessQueryStore
SELECT
    CASE WHEN HAS_PERMS_BY_NAME(NULL, NULL, 'VIEW SERVER STATE') = 1
           OR HAS_PERMS_BY_NAME(DB_NAME(), 'DATABASE', 'VIEW DATABASE STATE') = 1
         THEN CAST(1 AS bit) ELSE CAST(0 AS bit) END AS CanViewState,
    CASE WHEN HAS_PERMS_BY_NAME(DB_NAME(), 'DATABASE', 'ALTER') = 1
         THEN CAST(1 AS bit) ELSE CAST(0 AS bit) END AS CanAlter,
    CASE WHEN HAS_PERMS_BY_NAME(DB_NAME(), 'DATABASE', 'VIEW DATABASE STATE') = 1
         THEN CAST(1 AS bit) ELSE CAST(0 AS bit) END AS CanAccessQueryStore;
