// ファイル概要：HookSecurityValidator のセマンティック検証テスト。

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NetYamlForge.Services;
using Xunit;

namespace NetYamlForge.Tests.Services;

public class HookSecurityValidatorTests
{
    private readonly HookSecurityValidator _validator = new();

    private IReadOnlyList<string> Validate(string source)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source, path: "TestHook.cs");
        var references = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
            .Select(a => MetadataReference.CreateFromFile(a.Location))
            .Cast<MetadataReference>()
            .ToList();
        var compilation = CSharpCompilation.Create(
            "TestHookCompilation",
            new[] { syntaxTree },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        return _validator.Validate(compilation);
    }

    // ===== 誤報回帰テスト（0 違反어야 함） =====

    [Fact]
    public void LocalVariableNamedStart_NoViolation()
    {
        var source = @"
using System;
class Hooks {
    void DoWork() {
        var start = DateTime.Now;
        Console.WriteLine(start);
    }
}";
        var violations = Validate(source);
        Assert.Empty(violations);
    }

    [Fact]
    public void MethodNamedStart_NoViolation()
    {
        var source = @"
class Hooks {
    public void Start() { }
    public void Process() { }
}";
        var violations = Validate(source);
        Assert.Empty(violations);
    }

    [Fact]
    public void UsingSystemDiagnostics_WithStopwatch_NoViolation()
    {
        var source = @"
using System;
using System.Diagnostics;
class Hooks {
    void Measure() {
        var sw = Stopwatch.StartNew();
        sw.Stop();
    }
}";
        var violations = Validate(source);
        Assert.Empty(violations);
    }

    [Fact]
    public void PropertyNamedProcess_NoViolation()
    {
        var source = @"
class Hooks {
    public string Process { get; set; }
    void Use() {
        var p = Process;
    }
}";
        var violations = Validate(source);
        Assert.Empty(violations);
    }

    // ===== 直接使用（違反어야 함） =====

    [Fact]
    public void NewProcess_Violation()
    {
        var source = @"
using System.Diagnostics;
class Hooks {
    void Run() {
        var p = new Process();
    }
}";
        var violations = Validate(source);
        Assert.NotEmpty(violations);
        Assert.Contains(violations, v => v.Contains("System.Diagnostics.Process"));
    }

    [Fact]
    public void ProcessStart_Violation()
    {
        var source = @"
using System.Diagnostics;
class Hooks {
    void Run() {
        Process.Start(""ls"");
    }
}";
        var violations = Validate(source);
        Assert.NotEmpty(violations);
        Assert.Contains(violations, v => v.Contains("System.Diagnostics.Process"));
    }

    // ===== 絕過パス（違反어야 함） =====

    [Fact]
    public void FullyQualifiedProcessStart_Violation()
    {
        var source = @"
class Hooks {
    void Run() {
        System.Diagnostics.Process.Start(""ls"");
    }
}";
        var violations = Validate(source);
        Assert.NotEmpty(violations);
        Assert.Contains(violations, v => v.Contains("System.Diagnostics.Process"));
    }

    [Fact]
    public void AliasUsing_Process_Violation()
    {
        var source = @"
using P = System.Diagnostics.Process;
class Hooks {
    void Run() {
        P.Start(""ls"");
    }
}";
        var violations = Validate(source);
        Assert.NotEmpty(violations);
    }

    [Fact]
    public void AssemblyLoadFile_Violation()
    {
        var source = @"
class Hooks {
    void Run() {
        System.Reflection.Assembly.LoadFile(""/x.dll"");
    }
}";
        var violations = Validate(source);
        Assert.NotEmpty(violations);
        Assert.Contains(violations, v => v.Contains("System.Reflection.Assembly"));
    }

    [Fact]
    public void ActivatorCreateInstance_Allowed()
    {
        var source = @"
using System;
class Hooks {
    void Run() {
        var t = typeof(string);
        Activator.CreateInstance(t);
    }
}";
        var violations = Validate(source);
        // Activator.CreateInstance は DCS003 回避のために許可
        Assert.Empty(violations);
    }

    [Fact]
    public void DllImportAttribute_Violation()
    {
        var source = @"
using System.Runtime.InteropServices;
class Hooks {
    [DllImport(""libc"")]
    static extern void SomeMethod();
}";
        var violations = Validate(source);
        Assert.NotEmpty(violations);
        Assert.Contains(violations, v => v.Contains("DllImport"));
    }

    [Fact]
    public void TypeofProcess_Violation()
    {
        var source = @"
using System;
class Hooks {
    void Run() {
        var t = typeof(System.Diagnostics.Process);
        Console.WriteLine(t);
    }
}";
        var violations = Validate(source);
        Assert.NotEmpty(violations);
        Assert.Contains(violations, v => v.Contains("System.Diagnostics.Process"));
    }

    // ===== 許可例外 =====

    [Fact]
    public void AssemblyGetExecutingAssembly_NoViolation()
    {
        var source = @"
using System;
using System.Reflection;
class Hooks {
    void Run() {
        var asm = Assembly.GetExecutingAssembly();
        var name = asm.GetName();
        Console.WriteLine(name);
    }
}";
        var violations = Validate(source);
        Assert.Empty(violations);
    }

    // ===== 違反メッセージにファイル名と行番号が含まれる =====

    [Fact]
    public void ViolationMessageContainsFileNameAndLine()
    {
        var source = @"
class Hooks {
    void Run() {
        System.Diagnostics.Process.Start(""ls"");
    }
}";
        var violations = Validate(source);
        Assert.NotEmpty(violations);
        Assert.Contains(violations, v => v.Contains("TestHook.cs"));
        Assert.Contains(violations, v => v.Contains("("));
    }

    // ===== ProcessStartInfo は禁止 =====

    [Fact]
    public void NewProcessStartInfo_Violation()
    {
        var source = @"
using System.Diagnostics;
class Hooks {
    void Run() {
        var psi = new ProcessStartInfo();
    }
}";
        var violations = Validate(source);
        Assert.NotEmpty(violations);
        Assert.Contains(violations, v => v.Contains("System.Diagnostics.ProcessStartInfo"));
    }

    // ===== Marshal は禁止 =====

    [Fact]
    public void MarshalStructureToPtr_Violation()
    {
        var source = @"
using System;
using System.Runtime.InteropServices;
class Hooks {
    void Run() {
        var ptr = Marshal.AllocHGlobal(100);
        Marshal.FreeHGlobal(ptr);
    }
}";
        var violations = Validate(source);
        Assert.NotEmpty(violations);
        Assert.Contains(violations, v => v.Contains("System.Runtime.InteropServices.Marshal"));
    }
}
