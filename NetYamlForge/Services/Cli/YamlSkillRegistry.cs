using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;
using Microsoft.Extensions.Logging;

namespace NetYamlForge.Services.Cli
{
    /// <summary>
    /// YAML スキルメタデータを管理するレジストリ
    /// skills/ ディレクトリから *.yml ファイルを読み込み、
    /// スキル定義をメモリにキャッシュして提供
    /// </summary>
    public interface IYamlSkillRegistry
    {
        /// <summary>
        /// 全スキルを読み込んでレジストリを初期化
        /// </summary>
        Task InitializeAsync();

        /// <summary>
        /// スキル名からスキル定義を取得
        /// </summary>
        YamlSkillDefinition? GetSkill(string skillName);

        /// <summary>
        /// 全スキル定義を取得
        /// </summary>
        IEnumerable<YamlSkillDefinition> GetAllSkills();

        /// <summary>
        /// カテゴリ別にスキルをフィルタリング
        /// </summary>
        IEnumerable<YamlSkillDefinition> GetSkillsByCategory(string category);

        /// <summary>
        /// タグでスキルをフィルタリング
        /// </summary>
        IEnumerable<YamlSkillDefinition> GetSkillsByTag(string tag);

        /// <summary>
        /// スキル依存関係グラフを構築
        /// </summary>
        SkillDependencyGraph BuildDependencyGraph();
    }

    public class YamlSkillRegistry : IYamlSkillRegistry
    {
        private readonly string _skillsPath;
        private readonly ILogger<YamlSkillRegistry> _logger;
        private Dictionary<string, YamlSkillDefinition> _skillCache = new();
        private IDeserializer _deserializer;

        public YamlSkillRegistry(
            ILogger<YamlSkillRegistry> logger,
            string skillsPath = "NetYamlForge/skills")
        {
            _skillsPath = skillsPath;
            _logger = logger;
            
            // YAML deserializer を初期化（snake_case キーマッピング対応）
            _deserializer = new DeserializerBuilder()
                .WithNamingConvention(CamelCaseNamingConvention.Instance)
                .IgnoreUnmatchedProperties()
                .Build();
        }

        /// <summary>
        /// skills/ ディレクトリから全 *.yml ファイルを読み込む
        /// </summary>
        public async Task InitializeAsync()
        {
            _logger.LogInformation("🔄 YAML スキルレジストリを初期化中...");
            
            var skillsDir = new DirectoryInfo(_skillsPath);
            if (!skillsDir.Exists)
            {
                _logger.LogWarning("⚠️  スキルディレクトリが見つかりません: {path}", _skillsPath);
                return;
            }

            // skills/*.yml ファイルをスキャン（サブディレクトリ内の auto-dealer 等は除外）
            var skillFiles = skillsDir
                .EnumerateFiles("*.yml", SearchOption.TopDirectoryOnly)
                .Where(f => !f.Name.StartsWith("_")) // _schema.yml などは除外
                .ToList();

            _logger.LogInformation("📁 {count} 個のスキル YAML ファイルを検出しました", skillFiles.Count);

            foreach (var file in skillFiles)
            {
                try
                {
                    var content = await File.ReadAllTextAsync(file.FullName);
                    var skill = _deserializer.Deserialize<YamlSkillDefinition>(content);
                    
                    if (skill != null)
                    {
                        _skillCache[skill.Metadata.Name] = skill;
                        _logger.LogInformation("✅ スキル読み込み: {name} (v{version})", 
                            skill.Metadata.Name, skill.Metadata.Version);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "❌ スキル読み込みエラー: {file}", file.Name);
                }
            }

            // バリデーション
            ValidateRegistry();
            
            _logger.LogInformation("✨ YAML スキルレジストリ初期化完了（{count} スキル）", 
                _skillCache.Count);
        }

        public YamlSkillDefinition? GetSkill(string skillName)
        {
            if (_skillCache.TryGetValue(skillName, out var skill))
            {
                return skill;
            }

            _logger.LogWarning("⚠️  スキルが見つかりません: {skillName}", skillName);
            return null;
        }

        public IEnumerable<YamlSkillDefinition> GetAllSkills()
            => _skillCache.Values;

        public IEnumerable<YamlSkillDefinition> GetSkillsByCategory(string category)
            => _skillCache.Values
                .Where(s => s.Metadata.Category.Equals(category, StringComparison.OrdinalIgnoreCase));

        public IEnumerable<YamlSkillDefinition> GetSkillsByTag(string tag)
            => _skillCache.Values
                .Where(s => s.Metadata.Tags?.Contains(tag, StringComparer.OrdinalIgnoreCase) ?? false);

        public SkillDependencyGraph BuildDependencyGraph()
        {
            var graph = new SkillDependencyGraph();
            
            foreach (var skill in _skillCache.Values)
            {
                graph.AddNode(skill.Metadata.Name);
                
                // 依存関係を追加
                if (skill.Dependencies?.Requires != null)
                {
                    foreach (var dep in skill.Dependencies.Requires)
                    {
                        graph.AddEdge(skill.Metadata.Name, dep);
                    }
                }
            }

            // 循環依存をチェック
            var cycles = graph.DetectCycles();
            if (cycles.Any())
            {
                _logger.LogError("❌ 循環依存が検出されました: {cycles}", 
                    string.Join(", ", cycles.Select(c => $"[{string.Join(" -> ", c)}]")));
            }

            return graph;
        }

        /// <summary>
        /// レジストリの整合性をバリデーション
        /// </summary>
        private void ValidateRegistry()
        {
            var issues = new List<string>();

            // 1. スキル名の一意性チェック
            var duplicateNames = _skillCache.Keys
                .GroupBy(k => k.ToLower())
                .Where(g => g.Count() > 1)
                .Select(g => g.Key);

            foreach (var name in duplicateNames)
            {
                issues.Add($"重複するスキル名: {name}");
            }

            // 2. 依存関係の妥当性チェック
            foreach (var skill in _skillCache.Values)
            {
                if (skill.Dependencies?.Requires != null)
                {
                    foreach (var dep in skill.Dependencies.Requires)
                    {
                        if (!_skillCache.ContainsKey(dep))
                        {
                            issues.Add($"スキル '{skill.Metadata.Name}' が存在しないスキル '{dep}' に依存しています");
                        }
                    }
                }

                // 推奨スキルもチェック（警告レベル）
                if (skill.Dependencies?.Recommends != null)
                {
                    foreach (var rec in skill.Dependencies.Recommends)
                    {
                        if (!_skillCache.ContainsKey(rec))
                        {
                            _logger.LogWarning("⚠️  スキル '{skill}' が存在しないスキル '{rec}' を推奨しています",
                                skill.Metadata.Name, rec);
                        }
                    }
                }
            }

            if (issues.Any())
            {
                _logger.LogError("❌ レジストリバリデーションエラー:\n{issues}", 
                    string.Join("\n", issues.Select(i => $"  - {i}")));
            }
            else
            {
                _logger.LogInformation("✅ レジストリバリデーション成功");
            }
        }
    }

    /// <summary>
    /// YAML スキル定義のルートモデル
    /// </summary>
    public class YamlSkillDefinition
    {
        public string ApiVersion { get; set; } = "skills.netyamlforge.io/v1";
        public string Kind { get; set; } = "Skill";
        public SkillMetadata Metadata { get; set; } = new();
        public SkillSpec Spec { get; set; } = new();
        public SkillDependencies? Dependencies { get; set; }
        public List<Limitation>? Limitations { get; set; }
    }

    public class SkillMetadata
    {
        public string Name { get; set; } = "";
        public string Version { get; set; } = "1.0.0";
        public string DisplayName { get; set; } = "";
        public string Description { get; set; } = "";
        public string Category { get; set; } = "system";
        public int Tier { get; set; } = 5;
        public string Icon { get; set; } = "⚙️";
        public List<string>? Tags { get; set; }
        public string Author { get; set; } = "NetYamlForge Team";
        public List<SkillMaintainer>? Maintainers { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

    public class SkillMaintainer
    {
        public string Name { get; set; } = "";
        public string Email { get; set; } = "";
    }

    public class SkillSpec
    {
        public SkillInputs Inputs { get; set; } = new();
        public SkillRequirements? Requirements { get; set; }
        public SkillExecution Execution { get; set; } = new();
        public SkillOutputs Outputs { get; set; } = new();
        public List<string>? AllowedTools { get; set; }
        public SkillLearning? Learning { get; set; }
    }

    public class SkillInputs
    {
        public List<string>? Required { get; set; }
        public Dictionary<string, InputProperty>? Properties { get; set; }
    }

    public class InputProperty
    {
        public string Type { get; set; } = "string";
        public string Description { get; set; } = "";
        public string? Default { get; set; }
        public List<string>? Enum { get; set; }
        public string? Pattern { get; set; }
        public int? MinLength { get; set; }
        public int? MaxLength { get; set; }
        public string? Example { get; set; }
    }

    public class SkillRequirements
    {
        public string? DotnetVersion { get; set; }
        public List<string>? Tools { get; set; }
        public List<EnvironmentVar>? Environment { get; set; }
        public bool ProjectExists { get; set; }
        public List<string>? FileExists { get; set; }
    }

    public class EnvironmentVar
    {
        public string Name { get; set; } = "";
        public bool Required { get; set; }
    }

    public class SkillExecution
    {
        public string Type { get; set; } = "bash";
        public List<ExecutionStep>? Steps { get; set; }
    }

    public class ExecutionStep
    {
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        public string Type { get; set; } = "execution";
        public string Command { get; set; } = "";
        public string OnError { get; set; } = "abort";
        public int Timeout { get; set; } = 300;
    }

    public class SkillOutputs
    {
        public List<string>? SuccessCriteria { get; set; }
        public OutputFormat OutputFormat { get; set; } = new();
        public List<OutputFile>? Files { get; set; }
    }

    public class OutputFormat
    {
        public string Type { get; set; } = "markdown";
        public Dictionary<string, string>? Structure { get; set; }
    }

    public class OutputFile
    {
        public string Pattern { get; set; } = "";
        public string Description { get; set; } = "";
    }

    public class SkillLearning
    {
        public List<SuccessMetric>? SuccessMetrics { get; set; }
        public string? FeedbackTemplate { get; set; }
        public List<ImprovementRecord>? ImprovementLog { get; set; }
    }

    public class SuccessMetric
    {
        public string Name { get; set; } = "";
        public string Type { get; set; } = "boolean";
        public string? Unit { get; set; }
        public object? Target { get; set; }
        public string? Description { get; set; }
    }

    public class ImprovementRecord
    {
        public DateTime Date { get; set; }
        public string Change { get; set; } = "";
        public string? Impact { get; set; }
    }

    public class SkillDependencies
    {
        public List<string>? Requires { get; set; }
        public List<string>? Recommends { get; set; }
        public List<string>? Incompatible { get; set; }
    }

    public class Limitation
    {
        public string Description { get; set; } = "";
        public string? Workaround { get; set; }
    }

    /// <summary>
    /// スキル依存関係グラフ
    /// 循環依存検出やトポロジカルソートに使用
    /// </summary>
    public class SkillDependencyGraph
    {
        private Dictionary<string, List<string>> _edges = new();

        public void AddNode(string skillName)
        {
            if (!_edges.ContainsKey(skillName))
            {
                _edges[skillName] = new List<string>();
            }
        }

        public void AddEdge(string from, string to)
        {
            if (!_edges.ContainsKey(from))
                _edges[from] = new List<string>();
            
            _edges[from].Add(to);
        }

        /// <summary>
        /// 循環依存を検出（DFS を使用）
        /// </summary>
        public List<List<string>> DetectCycles()
        {
            var cycles = new List<List<string>>();
            var visited = new HashSet<string>();
            var recursionStack = new HashSet<string>();

            foreach (var node in _edges.Keys)
            {
                if (!visited.Contains(node))
                {
                    DfsDetectCycles(node, visited, recursionStack, new List<string>(), cycles);
                }
            }

            return cycles;
        }

        private void DfsDetectCycles(
            string node,
            HashSet<string> visited,
            HashSet<string> recursionStack,
            List<string> path,
            List<List<string>> cycles)
        {
            visited.Add(node);
            recursionStack.Add(node);
            path.Add(node);

            if (_edges.TryGetValue(node, out var neighbors))
            {
                foreach (var neighbor in neighbors)
                {
                    if (!visited.Contains(neighbor))
                    {
                        DfsDetectCycles(neighbor, visited, recursionStack, new List<string>(path), cycles);
                    }
                    else if (recursionStack.Contains(neighbor))
                    {
                        // 循環検出
                        var cycleStart = path.IndexOf(neighbor);
                        var cycle = path.Skip(cycleStart).ToList();
                        cycle.Add(neighbor);
                        cycles.Add(cycle);
                    }
                }
            }

            recursionStack.Remove(node);
        }

        /// <summary>
        /// トポロジカルソート（Kahn のアルゴリズム）
        /// </summary>
        public List<string> TopologicalSort()
        {
            var inDegree = new Dictionary<string, int>();
            var tempEdges = new Dictionary<string, List<string>>();

            // 初期化
            foreach (var node in _edges.Keys)
            {
                inDegree[node] = 0;
                tempEdges[node] = new List<string>(_edges[node]);
            }

            // 入次数を計算
            foreach (var node in _edges.Keys)
            {
                foreach (var neighbor in _edges[node])
                {
                    if (inDegree.ContainsKey(neighbor))
                    {
                        inDegree[neighbor]++;
                    }
                }
            }

            var queue = new Queue<string>();
            foreach (var node in inDegree.Where(kv => kv.Value == 0).Select(kv => kv.Key))
            {
                queue.Enqueue(node);
            }

            var sorted = new List<string>();
            while (queue.Count > 0)
            {
                var node = queue.Dequeue();
                sorted.Add(node);

                if (tempEdges.TryGetValue(node, out var neighbors))
                {
                    foreach (var neighbor in neighbors.ToList())
                    {
                        inDegree[neighbor]--;
                        if (inDegree[neighbor] == 0)
                        {
                            queue.Enqueue(neighbor);
                        }
                    }
                }
            }

            return sorted;
        }
    }
}
