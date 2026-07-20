-- sim: name=index-used-by-queries
-- sim: minversion=11.0
-- sim: azure=supported
-- sim: columns=QueryText,ExecutionCount,LastExecutionUtc
-- Queries in the plan cache whose plan references the given index by name.
SELECT
    SUBSTRING(st.text, 1, 4000) AS QueryText,
    qs.execution_count          AS ExecutionCount,
    qs.last_execution_time      AS LastExecutionUtc
FROM sys.dm_exec_query_stats qs
CROSS APPLY sys.dm_exec_sql_text(qs.sql_handle) st
CROSS APPLY sys.dm_exec_query_plan(qs.plan_handle) qp
WHERE CAST(qp.query_plan AS nvarchar(max)) LIKE '%' + @IndexName + '%'
ORDER BY qs.execution_count DESC;
