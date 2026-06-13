namespace NetYamlForge.Models;

public sealed record ColumnSchemaInfo(string Name, string SqlType, bool NotNull, bool IsPrimaryKey);

public enum MigrationOpType
{
    AddColumn,
    DropColumn,
    AlterColumnType,
    AlterNullability
}

public sealed record MigrationOperation(
    MigrationOpType OpType,
    string ColumnName,
    string? OldSqlType,
    string? NewSqlType,
    bool? NewNotNull);

public sealed record MigrationPlan(
    string EntityName,
    string TableName,
    IReadOnlyList<MigrationOperation> Operations)
{
    public bool RequiresTableRebuild =>
        Operations.Any(o => o.OpType is MigrationOpType.AlterColumnType or MigrationOpType.AlterNullability
                             or MigrationOpType.DropColumn);
}

public sealed record MigrationRecord(
    string Id,
    string ProjectName,
    string EntityName,
    string TableName,
    string Description,
    string UpSql,
    string DownSql,
    DateTime AppliedAt,
    DateTime? RolledBackAt);

public sealed record MigrationApplyResult(bool Applied, string MigrationId, IReadOnlyList<string> ExecutedSql);
