-- sim: name=index-list
-- sim: minversion=11.0
-- sim: azure=supported
-- sim: columns=DatabaseName,SchemaName,TableName,IndexName,ObjectId,IndexId,IndexTypeCode,IsUnique,IsPrimaryKey,IsUniqueConstraint,IsDisabled,HasFilter,FilterDefinition,FillFactor,IsPadded,AllowRowLocks,AllowPageLocks,IgnoreDupKey,DataCompressionCode,IsOnView,IsSystemObject,DataSpaceName,DataSpaceType,IsPartitioned
SELECT
    DB_NAME()               AS DatabaseName,
    s.name                  AS SchemaName,
    o.name                  AS TableName,
    i.name                  AS IndexName,
    i.object_id             AS ObjectId,
    i.index_id              AS IndexId,
    i.type                  AS IndexTypeCode,
    i.is_unique             AS IsUnique,
    i.is_primary_key        AS IsPrimaryKey,
    i.is_unique_constraint  AS IsUniqueConstraint,
    i.is_disabled           AS IsDisabled,
    i.has_filter            AS HasFilter,
    i.filter_definition     AS FilterDefinition,
    i.fill_factor           AS FillFactor,
    i.is_padded             AS IsPadded,
    i.allow_row_locks       AS AllowRowLocks,
    i.allow_page_locks      AS AllowPageLocks,
    i.ignore_dup_key        AS IgnoreDupKey,
    ISNULL((SELECT TOP (1) p.data_compression
              FROM sys.partitions p
             WHERE p.object_id = i.object_id AND p.index_id = i.index_id
             ORDER BY p.partition_number), 0) AS DataCompressionCode,
    CASE WHEN o.type = 'V' THEN CAST(1 AS bit) ELSE CAST(0 AS bit) END AS IsOnView,
    o.is_ms_shipped         AS IsSystemObject,
    ds.name                 AS DataSpaceName,
    ds.type                 AS DataSpaceType,
    CASE WHEN (SELECT COUNT(*) FROM sys.partitions p2
                WHERE p2.object_id = i.object_id AND p2.index_id = i.index_id) > 1
         THEN CAST(1 AS bit) ELSE CAST(0 AS bit) END AS IsPartitioned
FROM sys.indexes i
JOIN sys.objects o        ON o.object_id = i.object_id
JOIN sys.schemas s        ON s.schema_id = o.schema_id
LEFT JOIN sys.data_spaces ds ON ds.data_space_id = i.data_space_id
WHERE o.type IN ('U', 'V') AND i.index_id >= 0 AND i.name IS NOT NULL
ORDER BY s.name, o.name, i.index_id;
