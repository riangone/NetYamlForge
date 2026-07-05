// ファイル概要：フックソースコードの安全性検証（誤用防止ガードレール）。
// SemanticModel でシンボルを解決し、禁止型のメンバー使用を検出します。
// 悪意ある攻撃者向けのサンドボックスではありません（フックは信頼済みコードとして扱う）。

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace NetYamlForge.Services;

/// <summary>
/// フックソースコードの安全性検証（誤用防止ガードレール）。
/// SemanticModel でシンボルを解決し、禁止型のメンバー使用を検出します。
/// 悪意ある攻撃者向けのサンドボックスではありません（フックは信頼済みコードとして扱う）。
/// </summary>
public sealed class HookSecurityValidator
{
    // 禁止「型」リスト（名前空間ごと禁止ではなく型単位。Stopwatch/Debug 等は許可される）
    private static readonly HashSet<string> BannedTypes = new(StringComparer.Ordinal)
    {
        "System.Diagnostics.Process",
        "System.Diagnostics.ProcessStartInfo",
        "System.Reflection.Assembly",          // Load/LoadFrom/LoadFile など
        "System.Runtime.InteropServices.Marshal",
        "System.Runtime.InteropServices.DllImportAttribute",
        "System.Runtime.Loader.AssemblyLoadContext",
        "System.Activator",                    // Activator.CreateInstance(Type) 反射起動
        "System.AppDomain",
    };

    // 例外的に許可するメンバー（型は禁止だがこのメンバーだけは安全）
    private static readonly HashSet<string> AllowedMembers = new(StringComparer.Ordinal)
    {
        "System.Reflection.Assembly.GetExecutingAssembly",
        "System.Reflection.Assembly.GetName",
        "System.Activator.CreateInstance",
    };

    /// <summary>
    /// コンパイル全体を検証し、禁止 API の使用箇所を返します。
    /// </summary>
    public IReadOnlyList<string> Validate(CSharpCompilation compilation)
    {
        var violations = new List<string>();
        foreach (var tree in compilation.SyntaxTrees)
        {
            var model = compilation.GetSemanticModel(tree);
            var walker = new SemanticWalker(model, BannedTypes, AllowedMembers, violations);
            walker.Visit(tree.GetRoot());
        }
        return violations;
    }

    private sealed class SemanticWalker : CSharpSyntaxWalker
    {
        private readonly SemanticModel _model;
        private readonly HashSet<string> _bannedTypes;
        private readonly HashSet<string> _allowedMembers;
        private readonly List<string> _violations;

        public SemanticWalker(
            SemanticModel model,
            HashSet<string> bannedTypes,
            HashSet<string> allowedMembers,
            List<string> violations)
        {
            _model = model;
            _bannedTypes = bannedTypes;
            _allowedMembers = allowedMembers;
            _violations = violations;
        }

        public override void VisitInvocationExpression(InvocationExpressionSyntax node)
        {
            var symbolInfo = _model.GetSymbolInfo(node);
            if (symbolInfo.Symbol is IMethodSymbol methodSymbol)
            {
                var containingType = methodSymbol.ContainingType.ToDisplayString();
                if (_bannedTypes.Contains(containingType))
                {
                    var memberKey = $"{containingType}.{methodSymbol.Name}";
                    if (!_allowedMembers.Contains(memberKey))
                    {
                        ReportViolation(node, containingType, methodSymbol.Name);
                    }
                }
            }
            base.VisitInvocationExpression(node);
        }

        public override void VisitMemberAccessExpression(MemberAccessExpressionSyntax node)
        {
            var symbolInfo = _model.GetSymbolInfo(node);
            if (symbolInfo.Symbol != null)
            {
                var containingType = symbolInfo.Symbol.ContainingType?.ToDisplayString();
                if (containingType != null && _bannedTypes.Contains(containingType))
                {
                    var memberName = node.Name.Identifier.Text;
                    var memberKey = $"{containingType}.{memberName}";
                    if (!_allowedMembers.Contains(memberKey))
                    {
                        ReportViolation(node, containingType, memberName);
                    }
                }
            }
            base.VisitMemberAccessExpression(node);
        }

        public override void VisitObjectCreationExpression(ObjectCreationExpressionSyntax node)
        {
            var typeInfo = _model.GetTypeInfo(node);
            if (typeInfo.Type != null)
            {
                var typeName = typeInfo.Type.ToDisplayString();
                if (_bannedTypes.Contains(typeName))
                {
                    ReportViolation(node, typeName, ".ctor");
                }
            }
            base.VisitObjectCreationExpression(node);
        }

        public override void VisitImplicitObjectCreationExpression(ImplicitObjectCreationExpressionSyntax node)
        {
            var typeInfo = _model.GetTypeInfo(node);
            if (typeInfo.Type != null)
            {
                var typeName = typeInfo.Type.ToDisplayString();
                if (_bannedTypes.Contains(typeName))
                {
                    ReportViolation(node, typeName, ".ctor");
                }
            }
            base.VisitImplicitObjectCreationExpression(node);
        }

        public override void VisitAttribute(AttributeSyntax node)
        {
            var typeInfo = _model.GetTypeInfo(node);
            if (typeInfo.Type != null)
            {
                var typeName = typeInfo.Type.ToDisplayString();
                if (typeName == "System.Runtime.InteropServices.DllImportAttribute" ||
                    typeName == "System.Runtime.InteropServices.UnmanagedCallersOnlyAttribute")
                {
                    ReportViolation(node, typeName, typeName);
                }
            }
            base.VisitAttribute(node);
        }

        public override void VisitTypeOfExpression(TypeOfExpressionSyntax node)
        {
            var typeInfo = _model.GetTypeInfo(node.Type);
            if (typeInfo.Type != null)
            {
                var typeName = typeInfo.Type.ToDisplayString();
                if (_bannedTypes.Contains(typeName))
                {
                    ReportViolation(node, typeName, "(typeof)");
                }
            }
            base.VisitTypeOfExpression(node);
        }

        private void ReportViolation(SyntaxNode node, string typeName, string memberName)
        {
            var lineSpan = node.GetLocation().GetLineSpan();
            var fileName = Path.GetFileName(lineSpan.Path);
            var line = lineSpan.StartLinePosition.Line + 1;
            var column = lineSpan.StartLinePosition.Character + 1;
            _violations.Add($"{fileName}({line},{column}): 禁止 API の使用: {typeName}.{memberName}");
        }
    }
}
