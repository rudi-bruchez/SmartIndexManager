namespace SmartIndexManager.Core.Ddl;

public abstract record DdlResult;
public sealed record DdlSuccess(string Sql) : DdlResult;
public sealed record DdlNotBackupable(string Reason) : DdlResult;
