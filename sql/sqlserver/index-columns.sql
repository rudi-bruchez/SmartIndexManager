-- sim: name=index-columns
-- sim: minversion=11.0
-- sim: azure=supported
-- sim: columns=ObjectId,IndexId,ColumnName,KeyOrdinal,IsIncluded,IsDescending
SELECT
    ic.object_id          AS ObjectId,
    ic.index_id           AS IndexId,
    c.name                AS ColumnName,
    ic.key_ordinal        AS KeyOrdinal,
    ic.is_included_column AS IsIncluded,
    ic.is_descending_key  AS IsDescending
FROM sys.index_columns ic
JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
JOIN sys.objects o ON o.object_id = ic.object_id
WHERE o.type IN ('U', 'V')
ORDER BY ic.object_id, ic.index_id, ic.is_included_column, ic.key_ordinal;
