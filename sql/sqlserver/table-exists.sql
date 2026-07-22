-- sim: name=table-exists
-- sim: minversion=11.0
-- sim: azure=supported
-- sim: columns=Exists
SELECT CASE WHEN EXISTS (
    SELECT 1 FROM sys.objects o
    JOIN sys.schemas s ON s.schema_id = o.schema_id
    WHERE s.name = @SchemaName AND o.name = @TableName AND o.type = 'U'
) THEN CAST(1 AS bit) ELSE CAST(0 AS bit) END AS Exists;
