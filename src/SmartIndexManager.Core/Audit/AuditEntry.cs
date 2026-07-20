namespace SmartIndexManager.Core.Audit;

public enum AuditAction { Drop, Restore, EnableQueryStore, GenerateScript }

public sealed record AuditEntry(
    DateTime TimestampUtc,
    AuditAction Action,
    string Server,
    string Database,
    string Operator,
    string Detail);
