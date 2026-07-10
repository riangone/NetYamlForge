// ファイル概要：appsettings.json の特定セクションを実行時に安全に書き換えるための汎用サービス。
// システム設定画面（Controllers/SystemSettingsController.cs）から、AI CLI チェーン設定などの
// グローバル設定をコード変更・再ビルドなしで更新できるようにする。

using System.Text.Json;
using System.Text.Json.Nodes;

namespace NetYamlForge.Services.Config;

/// <summary>
/// appsettings.json のトップレベルセクションを読み書きする。
/// 対象は appsettings.json 1ファイルのみ（appsettings.{Environment}.json のオーバーライドは対象外）。
/// 複数リクエストからの同時書き込みで JSON が壊れないよう、プロセス内ロックで直列化する。
/// </summary>
public interface IAppSettingsWriter
{
    /// <summary>
    /// 指定したトップレベルセクションを <paramref name="sectionValue"/> の内容でまるごと置き換えて保存する。
    /// 他のセクションはそのまま維持される。
    /// </summary>
    Task UpdateSectionAsync(string sectionName, object sectionValue, CancellationToken cancellationToken = default);
}

public class AppSettingsWriter : IAppSettingsWriter
{
    // appsettings.json への書き込みはプロセス全体で直列化する（同時保存による JSON 破損を防ぐため）。
    private static readonly SemaphoreSlim WriteLock = new(1, 1);
    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

    private readonly string _filePath;
    private readonly ILogger<AppSettingsWriter> _logger;

    public AppSettingsWriter(IWebHostEnvironment env, ILogger<AppSettingsWriter> logger)
    {
        _filePath = Path.Combine(env.ContentRootPath, "appsettings.json");
        _logger = logger;
    }

    public async Task UpdateSectionAsync(string sectionName, object sectionValue, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sectionName))
            throw new ArgumentException("sectionName is required", nameof(sectionName));

        await WriteLock.WaitAsync(cancellationToken);
        try
        {
            if (!File.Exists(_filePath))
                throw new FileNotFoundException($"設定ファイルが見つかりません: {_filePath}", _filePath);

            var currentJson = await File.ReadAllTextAsync(_filePath, cancellationToken);
            var parsed = JsonNode.Parse(
                currentJson,
                documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });

            if (parsed is not JsonObject root)
                throw new InvalidDataException("appsettings.json のルートはオブジェクトである必要があります");

            var newSectionNode = JsonSerializer.SerializeToNode(sectionValue, SerializerOptions);
            root[sectionName] = newSectionNode;

            var output = root.ToJsonString(SerializerOptions);

            // 書き込み失敗・想定外の内容になった場合に備え、直前の内容をバックアップしておく
            File.Copy(_filePath, _filePath + ".bak", overwrite: true);
            await File.WriteAllTextAsync(_filePath, output, cancellationToken);

            _logger.LogInformation("appsettings.json のセクション {Section} を更新しました", sectionName);
        }
        finally
        {
            WriteLock.Release();
        }
    }
}
