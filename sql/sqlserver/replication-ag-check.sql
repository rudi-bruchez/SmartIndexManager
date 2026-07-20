-- sim: name=replication-ag-check
-- sim: minversion=11.0
-- sim: azure=unsupported
-- sim: columns=InReplicationOrAg
SELECT
    CASE WHEN d.is_published = 1 OR d.is_subscribed = 1 OR d.is_merge_published = 1
              OR EXISTS (SELECT 1 FROM sys.dm_hadr_database_replica_states rs
                          WHERE rs.database_id = d.database_id)
         THEN CAST(1 AS bit) ELSE CAST(0 AS bit) END AS InReplicationOrAg
FROM sys.databases d
WHERE d.database_id = DB_ID();
