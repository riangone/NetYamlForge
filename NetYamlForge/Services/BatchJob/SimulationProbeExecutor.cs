// ファイル概要：AI ユーザーシミュレーション（探測プローブ）バッチ実行器。
// run_simulation_step.py（scripts/experiments/）を外部プロセスとして起動し、
// 指定ロール・メソッド・パスで API を叩かせ、不変量違反を検出したら失敗として扱う。
//
// 設計メモ（2026-07-07 時点の経緯）：
// - 元々 task-management 向けに手動運用していた AI ユーザーシミュレーション実験
//   （docs/experiments/ai-user-simulation*.md）を、NetYamlForge 自身のバッチジョブ
//   エンジン（BatchJobHostedService → Outbox → RealBatchJobExecutor）に載せて
//   「セッション常駐に依存しない、真に定期的な探測」にするためのハンドラー。
// - スクリプト自体（run_simulation_step.py）は現状 task-management 専用のロール
//   語彙（pm/dev/obs）とトークンをハードコードしている。auto-dealer-demo など
//   他プロジェクトに使い回す場合は、settings.params.script でプロジェクト専用の
//   スクリプトパスに差し替える前提。ここでは「呼び出し方」だけを汎用化し、
//   ロール/不変量の中身には一切踏み込まない。
// - 本番バッチと同じ jobs.yml に混在させると、シミュレーション失敗のリトライ/
//   通知チャンネルが本番ジョブのそれと混ざってしまう。運用時は専用の
//   jobs/simulation_probe.yml に分離し、sim: プレフィックス／専用 persona
//   アカウントの隔離ルールを踏襲すること。
// - exit code 契約（run_simulation_step.py 側）：0 = 不変量違反なし、
//   2 = 不変量違反を検出（クラッシュではない）、その他 = スクリプト実行自体の失敗。
//   ここでは 2 も「ジョブ失敗」として扱い、onFailure.notify 経由でアラートが
//   飛ぶようにしている（＝探測できたこと自体は成功だが、見つかった問題を
//   ダッシュボード上は「失敗」として目立たせる方針）。

using System.Data;
using System.Diagnostics;
using System.Text;

namespace NetYamlForge.Services.BatchJob;

/// <summary>
/// AI ユーザーシミュレーション探測ジョブ実行器。
/// job.Settings.Params で以下を受け取る（すべて省略可、デフォルトは task-management 実験時の値）：
///   script         : 実行する Python スクリプトの相対パス（既定: scripts/experiments/run_simulation_step.py）
///   user           : シミュレートするロールキー（スクリプト側の --user、既定: obs）
///   method         : HTTP メソッド（既定: GET）
///   path           : API パス（既定: /）
///   data           : POST/PUT 用 JSON ペイロード文字列（省略可）
///   apiBaseUrl     : スクリプトに渡す API_BASE_URL 環境変数（省略時はスクリプト側デフォルトに委ねる）
///   timeoutSeconds : プロセスタイムアウト秒数（既定: 30）
///   jitterMaxSeconds: 実行前に 0〜N 秒のランダム待機を挟む（既定: 0 = 無効。cron の固定間隔に
///                     「不定期らしさ」を足したい場合に使う。playbook のジッタスケジューラの簡易版）
/// </summary>
public class SimulationProbeExecutor : IBatchStepHandler
{
    public string StepType => "ai_user_simulation";

    private readonly IWebHostEnvironment _env;
    private readonly ILogger<SimulationProbeExecutor> _logger;

    public SimulationProbeExecutor(IWebHostEnvironment env, ILogger<SimulationProbeExecutor> logger)
    {
        _env = env;
        _logger = logger;
    }

    public async Task ExecuteAsync(
        BatchJobDefinition job,
        string? projectName,
        IDbConnection db,
        IDbTransaction tx,
        BatchJobResult result,
        CancellationToken ct)
    {
        var p = job.Settings.Params ?? new Dictionary<string, string>();

        string Get(string key, string fallback) =>
            p.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v) ? v : fallback;

        var scriptRel = Get("script", "scripts/experiments/run_simulation_step.py");
        var user = Get("user", "obs");
        var method = Get("method", "GET");
        var path = Get("path", "/");
        var data = p.TryGetValue("data", out var d) ? d : null;
        var apiBaseUrl = p.TryGetValue("apiBaseUrl", out var url) ? url : null;
        var timeoutSeconds = int.TryParse(Get("timeoutSeconds", "30"), out var t) ? t : 30;
        var jitterMaxSeconds = int.TryParse(Get("jitterMaxSeconds", "0"), out var j) ? j : 0;

        try
        {
            if (jitterMaxSeconds > 0)
            {
                var jitter = Random.Shared.Next(0, jitterMaxSeconds + 1);
                _logger.LogInformation(
                    "[SimulationProbe] {JobId}: jitter wait {Jitter}s before probing", job.Id, jitter);
                await Task.Delay(TimeSpan.FromSeconds(jitter), ct);
            }

            // scriptRel はリポジトリルート基準の相対パス（scripts/experiments/... ）を想定しているが、
            // _env.ContentRootPath は NetYamlForge/ サブディレクトリ（プロジェクトディレクトリ）を指す
            // （restart.sh が ASPNETCORE_CONTENTROOT をそのサブディレクトリに明示設定しているため）。
            // そのため単純な Path.Combine(ContentRootPath, scriptRel) では一段ネストした誤ったパスに
            // なる。まず ContentRootPath 直下を試し、無ければ一段上（リポジトリルート想定）を試す。
            var scriptPath = Path.Combine(_env.ContentRootPath, scriptRel);
            if (!File.Exists(scriptPath))
            {
                var repoRootCandidate = Path.GetFullPath(Path.Combine(_env.ContentRootPath, "..", scriptRel));
                if (File.Exists(repoRootCandidate))
                {
                    scriptPath = repoRootCandidate;
                }
                else
                {
                    result.Success = false;
                    result.ErrorMessage = $"Simulation script not found: {scriptPath} (also tried {repoRootCandidate})";
                    result.EndedAt = DateTime.UtcNow;
                    _logger.LogError("[SimulationProbe] {JobId}: script missing at {Path} and {Alt}", job.Id, scriptPath, repoRootCandidate);
                    return;
                }
            }

            var psi = new ProcessStartInfo
            {
                FileName = "python3",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = _env.ContentRootPath
            };
            // 注意：ProcessStartInfo.Arguments（単一文字列）は使わない。
            // UseShellExecute=false の場合、.NET はこの文字列を独自の argv 分割規則
            // （Windows の CommandLineToArgvW 相当）でトークン化する。この規則では
            // シングルクォートには一切の特殊な意味がなく、ダブルクォートだけが
            // グループ化記号として解釈されて最終的な引数から取り除かれる。
            // data は JSON（ダブルクォートを含む）なので、シングルクォートで囲む
            // 旧実装（下記コメントアウト参照）は JSON 内部のダブルクォートを
            // 引数境界と誤認されて破壊し、Python 側の json.loads() が失敗していた。
            // ArgumentList はトークンをそのまま exec() に渡すため、この種の
            // 文字列解析の曖昧さを根本的に回避できる。
            psi.ArgumentList.Add(scriptPath);
            psi.ArgumentList.Add("--user");
            psi.ArgumentList.Add(user);
            psi.ArgumentList.Add("--method");
            psi.ArgumentList.Add(method);
            psi.ArgumentList.Add("--path");
            psi.ArgumentList.Add(path);
            if (!string.IsNullOrEmpty(data))
            {
                psi.ArgumentList.Add("--data");
                psi.ArgumentList.Add(data);
            }
            if (!string.IsNullOrEmpty(apiBaseUrl))
            {
                psi.Environment["API_BASE_URL"] = apiBaseUrl;
            }

            using var process = new Process { StartInfo = psi };
            var stdout = new StringBuilder();
            var stderr = new StringBuilder();
            process.OutputDataReceived += (_, e) => { if (e.Data != null) stdout.AppendLine(e.Data); };
            process.ErrorDataReceived += (_, e) => { if (e.Data != null) stderr.AppendLine(e.Data); };

            _logger.LogInformation(
                "[SimulationProbe] {JobId}: probing as {User} {Method} {Path}", job.Id, user, method, path);

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

            try
            {
                await process.WaitForExitAsync(cts.Token);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                // タイムアウト（ct 自体のキャンセルではない）：プロセスを強制終了して失敗扱いにする
                TryKill(process);
                result.Success = false;
                result.ErrorMessage = $"Simulation probe timed out after {timeoutSeconds}s";
                result.EndedAt = DateTime.UtcNow;
                _logger.LogWarning("[SimulationProbe] {JobId}: timed out ({Timeout}s)", job.Id, timeoutSeconds);
                return;
            }

            var exitCode = process.ExitCode;
            var stdoutText = stdout.ToString();
            var stderrText = stderr.ToString();

            // exit code 契約: 0=違反なし, 2=不変量違反検出（スクリプトは正常完了）, その他=実行自体の失敗
            switch (exitCode)
            {
                case 0:
                    result.Success = true;
                    break;
                case 2:
                    result.Success = false;
                    result.ErrorMessage = "Invariant violation(s) detected during simulation probe. See stdout for details.";
                    break;
                default:
                    result.Success = false;
                    result.ErrorMessage = $"Simulation script exited with code {exitCode}: {stderrText}";
                    break;
            }

            result.RowsAffected = 1;
            result.ErrorDetail = exitCode == 0 ? null : stdoutText;
            result.EndedAt = DateTime.UtcNow;

            _logger.LogInformation(
                "[SimulationProbe] {JobId}: exit={Exit}, success={Success}, duration={Duration}ms",
                job.Id, exitCode, result.Success, result.DurationMs);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[SimulationProbe] {JobId}: unexpected failure", job.Id);
            result.Success = false;
            result.ErrorMessage = ex.Message;
            result.ErrorDetail = ex.ToString();
            result.EndedAt = DateTime.UtcNow;
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
        catch
        {
            // ベストエフォート。既に終了している等は無視する。
        }
    }
}
