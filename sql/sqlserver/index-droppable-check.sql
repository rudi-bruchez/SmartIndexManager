-- sim: name=index-droppable-check
-- sim: minversion=11.0
-- sim: azure=supported
-- sim: columns=IsDroppable
-- Defence-in-depth check: is this index a plain nonclustered rowstore non-unique DROP INDEX target?
-- type = 2 is NONCLUSTERED rowstore; excludes unique/constraint-backed/disabled indexes, system
-- tables and views. Returns no row when the index does not exist (caller treats absent as not droppable).
SELECT CASE
         WHEN i.type = 2 AND i.is_unique = 0 AND i.is_primary_key = 0 AND i.is_unique_constraint = 0
              AND i.is_disabled = 0 AND o.is_ms_shipped = 0 AND o.type = 'U'
         THEN CAST(1 AS bit) ELSE CAST(0 AS bit) END AS IsDroppable
FROM sys.indexes i
JOIN sys.objects o ON o.object_id = i.object_id
JOIN sys.schemas s ON s.schema_id = o.schema_id
WHERE s.name = @SchemaName AND o.name = @TableName AND i.name = @IndexName;
