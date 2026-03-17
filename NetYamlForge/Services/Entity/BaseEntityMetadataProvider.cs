// ファイル概要: プロジェクトスコープ外からグローバル（config/entities/）の
// エンティティ定義を取得するベースプロバイダーです。
// DynamicEntityConfigDiagnosticsService が「プロジェクト適用前の基底設定」と
// 「プロジェクトオーバーライド後の設定」を比較する際に使用します。

using NetYamlForge.Models;

namespace NetYamlForge.Services;

/// <summary>
/// グローバルエンティティメタデータへのアクセスインターフェース。
/// プロジェクトスコープ（ProjectScope）を使わずに基底の config/entities/ を参照します。
/// </summary>
public interface IBaseEntityMetadataProvider
{
    /// <summary>
    /// エンティティ名でグローバル定義を検索します。
    /// </summary>
    /// <returns>見つかった場合 true、definition に EntityDefinition が設定されます。</returns>
    bool TryGet(string entityName, out EntityDefinition? definition);
}

/// <summary>
/// グローバルエンティティメタデータプロバイダーの実装。
/// 呼び出しのたびに EntityMetadataProvider を生成してグローバル設定を読み取ります。
/// パフォーマンスより正確性を優先した設計（診断目的での使用を想定）。
/// </summary>
public sealed class BaseEntityMetadataProvider : IBaseEntityMetadataProvider
{
    private readonly IWebHostEnvironment _env;
    private readonly IConfiguration _configuration;

    public BaseEntityMetadataProvider(IWebHostEnvironment env, IConfiguration configuration)
    {
        _env = env;
        _configuration = configuration;
    }

    /// <summary>
    /// グローバルの config/entities/ からエンティティ定義を取得します。
    /// プロジェクト固有オーバーライドは適用されません。
    /// </summary>
    public bool TryGet(string entityName, out EntityDefinition? definition)
    {
        // 毎回新しい EntityMetadataProvider を生成してグローバル設定を読む
        // （キャッシュではなくオンデマンド読み取り：診断用途のため許容）
        var provider = new EntityMetadataProvider(_env, _configuration);
        return provider.TryGet(entityName, out definition);
    }
}
