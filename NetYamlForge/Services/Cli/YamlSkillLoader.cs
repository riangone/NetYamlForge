using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace NetYamlForge.Services.Cli
{
    /// <summary>
    /// YAML スキルレジストリの初期化を管理するローダー
    /// DI コンテナに登録するスキル定義を自動ロードする
    /// </summary>
    public interface IYamlSkillLoader
    {
        /// <summary>
        /// スキルレジストリを初期化し、すべてのスキルをロード
        /// </summary>
        Task LoadSkillsAsync();

        /// <summary>
        /// ロード完了フラグを確認
        /// </summary>
        bool IsLoaded { get; }

        /// <summary>
        /// ロード中のエラーを取得
        /// </summary>
        List<string> LoadErrors { get; }
    }

    public class YamlSkillLoader : IYamlSkillLoader
    {
        private readonly IYamlSkillRegistry _registry;
        private readonly ILogger<YamlSkillLoader> _logger;
        public bool IsLoaded { get; private set; }
        public List<string> LoadErrors { get; } = new();

        public YamlSkillLoader(
            IYamlSkillRegistry registry,
            ILogger<YamlSkillLoader> logger)
        {
            _registry = registry;
            _logger = logger;
        }

        /// <summary>
        /// skills/ ディレクトリから全スキルをロード
        /// </summary>
        public async Task LoadSkillsAsync()
        {
            try
            {
                _logger.LogInformation("🚀 YAML スキルローダーを開始");
                
                // レジストリを初期化
                await _registry.InitializeAsync();
                
                // 依存関係グラフを構築・検証
                var graph = _registry.BuildDependencyGraph();
                
                // トポロジカルソートで実行順序を決定
                var executionOrder = graph.TopologicalSort();
                
                _logger.LogInformation(
                    "✅ スキル実行推奨順序（{count}）:\n{order}",
                    executionOrder.Count,
                    string.Join(" → ", executionOrder));

                IsLoaded = true;
                _logger.LogInformation("✨ YAML スキルローダー完了");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ YAML スキル読み込みエラー");
                LoadErrors.Add(ex.Message);
                IsLoaded = false;
            }
        }
    }

    /// <summary>
    /// ホストされサービスとしてのスキルローダー
    /// アプリケーション起動時に自動実行
    /// </summary>
    public class YamlSkillInitializationHostedService : Microsoft.Extensions.Hosting.IHostedService
    {
        private readonly IYamlSkillLoader _loader;
        private readonly ILogger<YamlSkillInitializationHostedService> _logger;

        public YamlSkillInitializationHostedService(
            IYamlSkillLoader loader,
            ILogger<YamlSkillInitializationHostedService> logger)
        {
            _loader = loader;
            _logger = logger;
        }

        public async Task StartAsync(System.Threading.CancellationToken cancellationToken)
        {
            _logger.LogInformation("🔧 ホストされサービス開始: YAML スキル初期化");
            await _loader.LoadSkillsAsync();
            
            if (!_loader.IsLoaded)
            {
                _logger.LogError("❌ スキル読み込みに失敗しました。エラー:\n{errors}",
                    string.Join("\n", _loader.LoadErrors));
            }
        }

        public Task StopAsync(System.Threading.CancellationToken cancellationToken)
        {
            _logger.LogInformation("🛑 ホストされサービス停止");
            return Task.CompletedTask;
        }
    }
}
