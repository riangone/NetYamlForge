// ファイル概要：管理者専用の操作（システム再起動など）を処理するコントローラー。
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
    public async Task<IActionResult> Restart(string project)
    {
        logger.LogWarning("システム再起動をリクエスト：ユーザー={User}", User.Identity?.Name);

        // リポジトリルートを取得（NetYamlForge/NetYamlForge の一つ上）
        var repoRoot = Path.GetFullPath(Path.Combine(env.ContentRootPath, ".."));
        var scriptPath = Path.Combine(repoRoot, "restart.sh");

        if (System.IO.File.Exists(scriptPath))
        {
            try
            {
                // 再起動スクリプトをバックグラウンドで実行
                // アプリ停止後も継続できるよう、独立したプロセスとして起動
                var psi = new ProcessStartInfo("bash")
                {
                    Arguments = $"-c \"nohup bash '{scriptPath}' > /tmp/netyamlforge-restart.log 2>&1 &\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = false,
                    RedirectStandardError = false,
                    WorkingDirectory = repoRoot,
                };
                
                using var process = Process.Start(psi);
                logger.LogInformation("restart.sh を起動しました：{Path} (PID: {Pid})", scriptPath, process?.Id);
                
                // スクリプト起動後にアプリケーションを停止
                // 短い遅延を入れてから停止することで、スクリプトが確実に起動する
                await Task.Delay(500);
                appLifetime.StopApplication();
                
                return Ok(new { message = "システムを再起動しています..." });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "restart.sh の起動中にエラーが発生しました：{Path}", scriptPath);
                return StatusCode(500, new { error = "再起動スクリプトの実行に失敗しました" });
            }
        }
        else
        {
            logger.LogWarning("restart.sh が見つかりません：{Path}", scriptPath);
            return StatusCode(500, new { error = "再起動スクリプトが見つかりません" });
        }
    }
}
