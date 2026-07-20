-- sim: name=fk-support
-- sim: minversion=11.0
-- sim: azure=supported
-- sim: columns=ObjectId,IndexId
-- Conservative heuristic: an index is flagged FK-supporting when its first key column is a
-- referencing column of some foreign key on the same table. This deliberately over-flags: it
-- does not verify that the whole FK column set is an ordered prefix of the index key. For a
-- guard-rail warning a false "supports a foreign key" is safe; a missed one is not.
SELECT DISTINCT i.object_id AS ObjectId, i.index_id AS IndexId
FROM sys.indexes i
JOIN sys.index_columns ic
      ON ic.object_id = i.object_id AND ic.index_id = i.index_id
     AND ic.is_included_column = 0 AND ic.key_ordinal = 1
WHERE i.index_id >= 1
  AND EXISTS (
      SELECT 1
      FROM sys.foreign_key_columns fkc
      JOIN sys.foreign_keys fk ON fk.object_id = fkc.constraint_object_id
      WHERE fk.parent_object_id = i.object_id
        AND fkc.parent_column_id = ic.column_id);
