using System.Data;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace NetYamlForge.Services.Hooks;

/// <summary>
/// プロジェクト固有の RLS (Row-Level Security) コンテキストを動的に評価するインターフェース。
/// </summary>
public interface IProjectRlsContextEvaluator
{
    /// <summary>
    /// プロジェクト名。
    /// </summary>
    string ProjectName { get; }

    /// <summary>
    /// 指定されたエンティティとユーザー情報に基づいて、RLS クエリにバインドするコンテキスト変数を計算します。
    /// </summary>
    Task<Dictionary<string, object?>> EvaluateRlsContextAsync(string entity, string? userName, int userId, IDbConnection db, IDbTransaction? tx = null);
}
