-- sim: name=database-exists
-- sim: minversion=11.0
-- sim: azure=supported
-- sim: columns=Exists
SELECT CASE WHEN DB_ID(@DatabaseName) IS NULL THEN CAST(0 AS bit) ELSE CAST(1 AS bit) END AS [Exists];
