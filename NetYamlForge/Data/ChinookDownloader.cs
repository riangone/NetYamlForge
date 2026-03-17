// ファイル概要: Chinook サンプルデータベースの取得・初期化を行うクラスです。
// 優先順: ローカル既存 DB → GitHub からのダウンロード → オフライン時エラーログのみ。
// chinook プロジェクトの DbInitializer から呼ばれます。

using Microsoft.Data.Sqlite;

namespace NetYamlForge.Data;

/// <summary>
/// Chinook サンプルデータベースの初期化・ダウンロード。
/// SQLite プロジェクトで使用されます。
/// </summary>
public class ChinookDownloader
{
    private const string ChinookUrl = "https://github.com/lerocha/chinook-database/releases/download/v1.4.5/Chinook_Sqlite.sqlite";

    /// <summary>
    /// Chinook データベースファイルが存在しない場合、ダウンロードまたはコピーします。
    /// 優先順: ローカル既存DB > GitHub からのダウンロード
    /// </summary>
    public async Task EnsureChinookDatabaseAsync(
        string dbPath,
        string projectDir,
        ILogger logger)
    {
        if (File.Exists(dbPath))
        {
            logger.LogInformation("既存の DB ファイルを使用: {Path}", dbPath);
            return;
        }

        var fullPath = Path.GetFullPath(dbPath);
        var dir = Path.GetDirectoryName(fullPath) ?? Directory.GetCurrentDirectory();
        Directory.CreateDirectory(dir);

        // projects/{name}/database/ 配下の *.db ファイルを探す（拡張子問わず）
        var databaseDir = Path.Combine(projectDir, "database");
        if (Directory.Exists(databaseDir))
        {
            var existingDb = Directory.GetFiles(databaseDir, "*.db").FirstOrDefault()
                          ?? Directory.GetFiles(databaseDir, "*.sqlite").FirstOrDefault();
            if (existingDb != null && existingDb != fullPath)
            {
                // 既存 DB をコピー
                File.Copy(existingDb, fullPath);
                logger.LogInformation("既存 DB をコピー: {From} → {To}", existingDb, fullPath);
                return;
            }
        }

        logger.LogInformation("Chinook DB をダウンロード中: {Url}", ChinookUrl);
        using var http = new HttpClient();
        await using var source = await http.GetStreamAsync(ChinookUrl);
        await using var target = File.Create(fullPath);
        await source.CopyToAsync(target);
        logger.LogInformation("Chinook DB を保存: {Path}", fullPath);
    }
}
