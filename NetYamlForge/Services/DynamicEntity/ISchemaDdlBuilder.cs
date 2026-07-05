using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;
using NetYamlForge.Models;

namespace NetYamlForge.Services.DynamicEntity;

public interface ISchemaDdlBuilder
{
    bool Supports(string dbType);
    Task<IReadOnlyList<ColumnSchemaInfo>> GetPhysicalColumnsAsync(IDbConnection conn, string tableName);
    (IReadOnlyList<string> UpSql, IReadOnlyList<string> DownSql, string BackupTableName) GenerateSql(MigrationPlan plan, EntityDefinition entity);
}
