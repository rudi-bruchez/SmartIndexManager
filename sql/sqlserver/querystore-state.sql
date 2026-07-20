-- sim: name=querystore-state
-- sim: minversion=13.0
-- sim: azure=supported
-- sim: columns=ActualState
SELECT TOP (1) CAST(actual_state AS int) AS ActualState
FROM sys.database_query_store_options;
