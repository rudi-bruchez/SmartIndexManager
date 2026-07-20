-- sim: name=index-hints-plancache
-- sim: minversion=11.0
-- sim: azure=supported
-- sim: columns=Reference,Kind
-- Plan guides plus cached queries that force an index by name via a table hint.
SELECT
    pg.name AS Reference,
    'plan guide' AS Kind
FROM sys.plan_guides pg
WHERE pg.hints LIKE '%INDEX%' + @IndexName + '%'
   OR pg.query_text LIKE '%INDEX%' + @IndexName + '%'
UNION ALL
SELECT
    SUBSTRING(st.text, 1, 256) AS Reference,
    'query hint' AS Kind
FROM sys.dm_exec_cached_plans cp
CROSS APPLY sys.dm_exec_sql_text(cp.plan_handle) st
WHERE st.text LIKE '%INDEX%(%' + @IndexName + '%)%'
  AND st.text LIKE '%WITH%';
