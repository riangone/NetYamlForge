using System;
using System.IO;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using WebPush;

namespace NetYamlForge.Services.WebPush;

/// <summary>
/// VAPID鍵ペアを提供します。既存の鍵ファイル（Paths:Data 配下）があればそれを読み込み、
/// なければ起動時に自動生成して永続化します。全プロジェクト/全テナント共通の単一鍵ペアです
/// （WebPush の仕様上、鍵ペアはオリジン単位で1組あれば足ります）。
/// </summary>
public interface IVapidKeyProvider
{
    VapidKeyPair GetKeys();
}

public class VapidKeyProvider : IVapidKeyProvider
{
    private readonly VapidKeyPair _keys;

    public VapidKeyProvider(IConfiguration configuration, ILogger<VapidKeyProvider> logger)
    {
        var dataDir = configuration["Paths:Data"];
        if (string.IsNullOrWhiteSpace(dataDir))
        {
            dataDir = "var/data";
        }

        var fullDataDir = Path.Combine(Directory.GetCurrentDirectory(), dataDir);
        Directory.CreateDirectory(fullDataDir);

        var keyFilePath = Path.Combine(fullDataDir, "vapid-keys.json");

        if (File.Exists(keyFilePath))
        {
            try
            {
                var json = File.ReadAllText(keyFilePath);
                var loaded = JsonSerializer.Deserialize<VapidKeyPair>(json);
                if (loaded != null && !string.IsNullOrEmpty(loaded.PublicKey) && !string.IsNullOrEmpty(loaded.PrivateKey))
                {
                    _keys = loaded;
                    return;
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "VAPID鍵ファイルの読み込みに失敗したため、新しい鍵を再生成します: {Path}", keyFilePath);
            }
        }

        var generated = VapidHelper.GenerateVapidKeys();
        _keys = new VapidKeyPair { PublicKey = generated.PublicKey, PrivateKey = generated.PrivateKey };

        try
        {
            File.WriteAllText(keyFilePath, JsonSerializer.Serialize(_keys, new JsonSerializerOptions { WriteIndented = true }));
            logger.LogInformation("VAPID鍵ペアを新規生成し保存しました: {Path}", keyFilePath);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "VAPID鍵ペアの永続化に失敗しました。プロセス再起動のたびに新しい鍵が生成されるため、既存の購読は無効になります。");
        }
    }

    public VapidKeyPair GetKeys() => _keys;
}
