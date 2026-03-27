// ファイル概要: 管理者専用の操作（システム再起動など）を処理するコントローラー。
using System.Diagnostics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace NetYamlForge.Controllers;

[Authorize(Policy = "AdminOnly")]
[Route("{project}/Admin/{action=Index}")]
public class AdminController(
    IHostApplicationLifetime appLifetime,
    IWebHostEnvironment env,
    ILogger<AdminController> logger) : BaseProjectController
{
    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult Restart(string project)
    {
        logger.LogWarning("システム再起動をリクエスト: ユーザー={User}", User.Identity?.Name);

        // ContentRootPath はプロジェクトディレクトリ。restart.sh はその一つ上（リポジトリルート）にある。
        var scriptPath = Path.GetFullPath(Path.Combine(env.ContentRootPath, "..", "restart.sh"));

        if (System.IO.File.Exists(scriptPath))
        {
            // nohup + バックグラウンド実行: アプリ停止後も restart.sh が継続するようにする
            var psi = new ProcessStartInfo("bash")
            {
                Arguments = $"-c \"nohup bash '{scriptPath}' > /tmp/netyamlforge-restart.log 2>&1 &\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = false,
                RedirectStandardError = false,
            };
            Process.Start(psi);
            logger.LogInformation("restart.sh を起動しました: {Path}", scriptPath);
        }
        else
        {
            logger.LogWarning("restart.sh が見つかりません: {Path}", scriptPath);
        }

        appLifetime.StopApplication();
        return Ok();
    }
}
