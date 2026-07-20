-- sim: name=index-used-by-queries-query-store
-- sim: minversion=13.0
-- sim: azure=supported
-- sim: columns=QueryText,ExecutionCount,LastExecutionUtc
SELECT
    SUBSTRING(qt.query_sql_text, 1, 4000) AS QueryText,
    SUM(rs.count_executions)              AS ExecutionCount,
    MAX(rs.last_execution_time)           AS LastExecutionUtc
FROM sys.query_store_plan qp
JOIN sys.query_store_query q  ON q.query_id = qp.query_id
JOIN sys.query_store_query_text qt ON qt.query_text_id = q.query_text_id
JOIN sys.query_store_runtime_stats rs ON rs.plan_id = qp.plan_id
WHERE CAST(qp.query_plan AS nvarchar(max)) LIKE '%' + @IndexName + '%'
GROUP BY qt.query_sql_text
ORDER BY SUM(rs.count_executions) DESC;
