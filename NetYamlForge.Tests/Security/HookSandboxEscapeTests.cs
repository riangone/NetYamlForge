// ファイル概要: R2-05 フック サンドボックス逃逸 回帰スイート。
// 攻撃者視点で、悪意あるフック ソースが HookSecurityValidator（コンパイル前の静的拦截）で
// 拒否されることを、逃逸カテゴリ別（プロセス実行 / ネイティブ相互運用 / 反射・動的ロード /
// ネットワーク / ファイルシステム / 環境変数）に検証する。合法フックの正向対照も含む。
//
// 二層の方針（設計文書 R2-05 §3 と現実の整合）:
//   - 既定モード: 「正規のフックでは決して使わない」逃逸型を硬禁止（Process/Marshal/DllImport/
//     Assembly.Load/AppDomain/AssemblyLoadContext/Socket 等）。
//   - strict モード: File/HttpClient/Environment など「信頼済みフックでは正当だが攻撃面にもなる」型を
//     追加で拦截できることを検証（既定では正当利用を壊さないため許可）。

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NetYamlForge.Services;
using Xunit;

namespace NetYamlForge.Tests.Security;

[Trait("Category", "Security")]
public sealed class HookSandboxEscapeTests
{
    // TRUSTED_PLATFORM_ASSEMBLIES から参照を構築し、Socket/File/HttpClient/Environment 等の
    // 型が確実に SemanticModel で解決できるようにする（読み込み済みアセンブリのみだと取りこぼす）。
    private static readonly IReadOnlyList<MetadataReference> References = BuildReferences();

    private static IReadOnlyList<MetadataReference> BuildReferences()
    {
        var tpa = (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string) ?? string.Empty;
        var refs = new List<MetadataReference>();
        foreach (var path in tpa.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try { refs.Add(MetadataReference.CreateFromFile(path)); }
            catch { /* ベストエフォート */ }
        }
        return refs;
    }

    private static IReadOnlyList<string> Validate(string source, bool strict)
    {
        var tree = CSharpSyntaxTree.ParseText(source, path: "MaliciousHook.cs");
        var compilation = CSharpCompilation.Create(
            "HookEscapeCompilation",
            new[] { tree },
            References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        return new HookSecurityValidator(strict).Validate(compilation);
    }

    // ─── 既定モードで硬禁止される逃逸ベクター ───────────────────────────

    [Theory]
    // プロセス/コマンド実行
    [InlineData("System.Diagnostics.Process.Start(\"/bin/sh\");", "Process")]
    [InlineData("var psi = new System.Diagnostics.ProcessStartInfo();", "ProcessStartInfo")]
    // ネイティブ相互運用
    [InlineData("var p = System.Runtime.InteropServices.Marshal.AllocHGlobal(8);", "Marshal")]
    // 反射・動的アセンブリ ロード
    [InlineData("var a = System.Reflection.Assembly.LoadFile(\"/x.dll\");", "Assembly")]
    [InlineData("var ctx = new System.Runtime.Loader.AssemblyLoadContext(\"x\");", "AssemblyLoadContext")]
    [InlineData("var d = System.AppDomain.CurrentDomain;", "AppDomain")]
    // ネットワーク外連（既定で硬禁止する低レベル ソケット）
    [InlineData("var s = new System.Net.Sockets.Socket(System.Net.Sockets.AddressFamily.InterNetwork, System.Net.Sockets.SocketType.Stream, System.Net.Sockets.ProtocolType.Tcp);", "Socket")]
    [InlineData("var t = new System.Net.Sockets.TcpClient();", "TcpClient")]
    public void DefaultMode_RejectsHardEscapeVectors(string statement, string expectedTypeFragment)
    {
        var source = $@"
class Hooks {{
    void Run() {{
        {statement}
    }}
}}";
        var violations = Validate(source, strict: false);
        Assert.NotEmpty(violations);
        Assert.Contains(violations, v => v.Contains(expectedTypeFragment));
    }

    [Fact]
    public void DefaultMode_RejectsDllImportPInvoke()
    {
        var source = @"
using System.Runtime.InteropServices;
class Hooks {
    [DllImport(""libc"")]
    static extern int system(string cmd);
}";
        var violations = Validate(source, strict: false);
        Assert.NotEmpty(violations);
        Assert.Contains(violations, v => v.Contains("DllImport"));
    }

    [Fact]
    public void DefaultMode_RejectsTypeofBannedType()
    {
        // typeof(Process).Assembly のような反射逃逸の起点も typeof で拦截される。
        var source = @"
class Hooks {
    void Run() {
        var t = typeof(System.Diagnostics.Process);
        System.Console.WriteLine(t);
    }
}";
        var violations = Validate(source, strict: false);
        Assert.NotEmpty(violations);
    }

    // ─── strict モードでのみ拦截される「正当だが攻撃面」ベクター ──────────

    [Theory]
    // ファイルシステム
    [InlineData("System.IO.File.ReadAllText(\"/etc/passwd\");", "File")]
    [InlineData("System.IO.Directory.Delete(\"/\", true);", "Directory")]
    // ネットワーク（高レベル）
    [InlineData("var c = new System.Net.Http.HttpClient();", "HttpClient")]
    // 環境変数（秘密の読み取り）
    [InlineData("var k = System.Environment.GetEnvironmentVariable(\"DB_PASSWORD\");", "Environment")]
    public void StrictMode_RejectsPolicyGatedVectors(string statement, string expectedTypeFragment)
    {
        var source = $@"
class Hooks {{
    void Run() {{
        {statement}
    }}
}}";
        // strict: 拦截される
        var strictViolations = Validate(source, strict: true);
        Assert.NotEmpty(strictViolations);
        Assert.Contains(strictViolations, v => v.Contains(expectedTypeFragment));

        // 既定: 信頼済みフックの正当利用を壊さないため許可される（誤検知回帰の防止）。
        var defaultViolations = Validate(source, strict: false);
        Assert.DoesNotContain(defaultViolations, v => v.Contains(expectedTypeFragment));
    }

    // ─── 合法フックの正向対照（誤検知しないこと） ─────────────────────────

    [Fact]
    public void LegitimateHook_UsingAllowedApis_HasNoViolations_EvenInStrict()
    {
        var source = @"
using System;
using System.Linq;
using System.Text;
using System.Collections.Generic;
class Hooks {
    public string Transform(IEnumerable<string> items) {
        var sb = new StringBuilder();
        foreach (var s in items.Where(x => !string.IsNullOrEmpty(x)).OrderBy(x => x))
            sb.AppendLine(s.Trim().ToUpperInvariant());
        return sb.ToString();
    }
}";
        Assert.Empty(Validate(source, strict: false));
        Assert.Empty(Validate(source, strict: true));
    }

    [Fact]
    public void LegitimateHook_UsingFile_PassesDefault_ButFlaggedInStrict()
    {
        // File を使う既存プロジェクト フック（例: 画像処理）は既定で通り、strict で可視化される。
        var source = @"
class Hooks {
    string Read() => System.IO.File.ReadAllText(""data.json"");
}";
        Assert.Empty(Validate(source, strict: false));      // 既定: 壊さない
        Assert.NotEmpty(Validate(source, strict: true));    // strict: 検出できる
    }
}
