// 目的: CLAUDE.md「禁止パターン」をビルド時エラー/警告として検出するRoslynアナライザー。
//
// 検出ルール:
//   DCS001 - SQL文字列補間 ($"...{var}...") の禁止
//   DCS002 - 非同期メソッドのブロッキング呼び出し (.Result / .Wait()) の禁止
//   DCS003 - IDbConnection の直接インスタンス化 (new SqliteConnection / new SqlConnection) の禁止
//   DCS004 - ロール名のハードコード ("Admin" / "User" / "Manager" リテラル直接比較) の禁止

using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace NetYamlForge.Analyzers
{
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class ForbiddenPatternAnalyzer : DiagnosticAnalyzer
    {
        // ─── 診断 ID 定義 ────────────────────────────────────────────────

        private static readonly DiagnosticDescriptor SqlInterpolationRule = new DiagnosticDescriptor(
            id: "DCS001",
            title: "SQL文字列への変数直接埋め込みは禁止",
            messageFormat: "SQL文字列に変数を直接埋め込まないでください（SQLインジェクション脆弱性）。DynamicCrudRepositoryまたはパラメータ化クエリを使用してください。",
            category: "Security",
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true,
            description: "SQL variable interpolation can cause SQL injection. Use DynamicCrudRepository or parameterized queries."
        );

        private static readonly DiagnosticDescriptor BlockingCallRule = new DiagnosticDescriptor(
            id: "DCS002",
            title: "非同期メソッドのブロッキング呼び出しは禁止",
            messageFormat: "'.{0}' によるブロッキング呼び出しは禁止です。'await' を使用してください。",
            category: "Reliability",
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true,
            description: "Using .Result or .Wait() on async methods can cause thread deadlocks."
        );

        private static readonly DiagnosticDescriptor DirectConnectionRule = new DiagnosticDescriptor(
            id: "DCS003",
            title: "IDbConnection の直接インスタンス化は禁止",
            messageFormat: "'{0}' を直接 new してはいけません。DI コンテナから IDbConnection を受け取ってください。",
            category: "Architecture",
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true,
            description: "Manage database connections through the DI container to ensure proper lifecycle management."
        );

        private static readonly DiagnosticDescriptor HardcodedRoleRule = new DiagnosticDescriptor(
            id: "DCS004",
            title: "ロール名のハードコードは禁止",
            messageFormat: "ロール名 \"{0}\" を文字列リテラルで直接比較しないでください。UserRoles 定数を使用してください。",
            category: "Maintainability",
            defaultSeverity: DiagnosticSeverity.Warning,
            isEnabledByDefault: true,
            description: "Use constants from Models/Auth/UserRoles.cs instead of hardcoded role name strings."
        );

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
            ImmutableArray.Create(SqlInterpolationRule, BlockingCallRule, DirectConnectionRule, HardcodedRoleRule);

        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();

            // DCS001: SQL文字列補間
            context.RegisterSyntaxNodeAction(AnalyzeSqlInterpolation, SyntaxKind.InterpolatedStringExpression);

            // DCS002: .Result / .Wait()
            context.RegisterSyntaxNodeAction(AnalyzeBlockingCall, SyntaxKind.SimpleMemberAccessExpression);

            // DCS003: new SqliteConnection / new SqlConnection
            context.RegisterSyntaxNodeAction(AnalyzeDirectConnection, SyntaxKind.ObjectCreationExpression);

            // DCS004: ロール名ハードコード
            context.RegisterSyntaxNodeAction(AnalyzeHardcodedRole, SyntaxKind.EqualsExpression, SyntaxKind.NotEqualsExpression);
        }

        // ─── DCS001: SQL文字列補間 ────────────────────────────────────

        private static void AnalyzeSqlInterpolation(SyntaxNodeAnalysisContext context)
        {
            var interpolated = (InterpolatedStringExpressionSyntax)context.Node;

            // 補間部分が1つ以上ある（単なる補間文字列すべてには反応しない）
            if (!interpolated.Contents.OfType<InterpolationSyntax>().Any())
                return;

            // 近傍のコンテキストでSQL変数へ代入されているか確認
            if (IsAssignedToSqlVariable(interpolated) || IsPassedToSqlLikeMethod(interpolated))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    SqlInterpolationRule,
                    interpolated.GetLocation()
                ));
            }
        }

        private static bool IsAssignedToSqlVariable(SyntaxNode node)
        {
            // var sql = $"...{var}..."  /  string query = $"..."
            var assignment = node.Parent as AssignmentExpressionSyntax
                ?? node.Parent?.Parent as AssignmentExpressionSyntax;
            if (assignment != null)
            {
                var name = (assignment.Left as IdentifierNameSyntax)?.Identifier.ValueText?.ToLowerInvariant();
                return IsSqlVariableName(name);
            }

            var varDeclarator = node.Parent as EqualsValueClauseSyntax;
            if (varDeclarator?.Parent is VariableDeclaratorSyntax declarator)
            {
                var name = declarator.Identifier.ValueText?.ToLowerInvariant();
                return IsSqlVariableName(name);
            }

            return false;
        }

        private static bool IsPassedToSqlLikeMethod(SyntaxNode node)
        {
            // QueryAsync($"...") / ExecuteAsync($"...") の第一引数
            if (node.Parent is ArgumentSyntax arg
                && arg.Parent is ArgumentListSyntax argList
                && argList.Arguments.IndexOf(arg) == 0)
            {
                var invocation = argList.Parent as InvocationExpressionSyntax;
                var methodName = (invocation?.Expression as MemberAccessExpressionSyntax)
                    ?.Name.Identifier.ValueText?.ToLowerInvariant();
                return methodName is "queryasync" or "executeasync" or "queryfirstasync"
                    or "queryfirstordefaultasync" or "querysingleasync" or "executescalarasync"
                    or "query" or "execute" or "queryfirst" or "queryfirstordefault";
            }
            return false;
        }

        private static bool IsSqlVariableName(string? name)
            => name is "sql" or "query" or "commandtext" or "sqltext" or "rawsql" or "sqlquery";

        // ─── DCS002: ブロッキング呼び出し ─────────────────────────────

        private static void AnalyzeBlockingCall(SyntaxNodeAnalysisContext context)
        {
            var memberAccess = (MemberAccessExpressionSyntax)context.Node;
            var memberName = memberAccess.Name.Identifier.ValueText;

            if (memberName is "Result" or "Wait" or "GetResult")
            {
                // Thread.Sleep は Task でないので除外
                // .Result が Task/ValueTask 型かどうか確認
                var exprType = context.SemanticModel.GetTypeInfo(memberAccess.Expression).Type;
                if (exprType == null) return;

                var typeName = exprType.OriginalDefinition.ToDisplayString();
                bool isTaskLike = typeName.StartsWith("System.Threading.Tasks.Task")
                    || typeName.StartsWith("System.Threading.Tasks.ValueTask");

                if (isTaskLike)
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        BlockingCallRule,
                        memberAccess.Name.GetLocation(),
                        memberName
                    ));
                }
            }
            else if (memberName == "Wait")
            {
                // Wait() メソッド呼び出し（Task.Wait()）
                if (memberAccess.Parent is InvocationExpressionSyntax)
                {
                    var exprType = context.SemanticModel.GetTypeInfo(memberAccess.Expression).Type;
                    if (exprType == null) return;
                    var typeName = exprType.OriginalDefinition.ToDisplayString();
                    if (typeName.StartsWith("System.Threading.Tasks.Task"))
                    {
                        context.ReportDiagnostic(Diagnostic.Create(
                            BlockingCallRule,
                            memberAccess.Name.GetLocation(),
                            memberName
                        ));
                    }
                }
            }
        }

        // ─── DCS003: IDbConnection 直接インスタンス化 ─────────────────

        private static readonly string[] ForbiddenConnectionTypes =
        {
            "SqliteConnection",
            "SqlConnection",
            "NpgsqlConnection",
            "MySqlConnection",
        };

        private static void AnalyzeDirectConnection(SyntaxNodeAnalysisContext context)
        {
            var creation = (ObjectCreationExpressionSyntax)context.Node;
            var typeName = creation.Type.ToString();

            // 型名の末尾部分だけで判断（名前空間付きでも対応）
            foreach (var forbidden in ForbiddenConnectionTypes)
            {
                if (typeName == forbidden || typeName.EndsWith("." + forbidden))
                {
                    // テスト用インメモリ接続は許可: "Data Source=:memory:"
                    if (IsInMemoryTestConnection(creation))
                        return;

                    context.ReportDiagnostic(Diagnostic.Create(
                        DirectConnectionRule,
                        creation.GetLocation(),
                        forbidden
                    ));
                    return;
                }
            }
        }

        private static bool IsInMemoryTestConnection(ObjectCreationExpressionSyntax creation)
        {
            // new SqliteConnection("Data Source=:memory:")
            var arg = creation.ArgumentList?.Arguments.FirstOrDefault();
            if (arg?.Expression is LiteralExpressionSyntax lit)
            {
                var val = lit.Token.ValueText;
                return val.Contains(":memory:") || val.Contains("Data Source=:memory:");
            }
            return false;
        }

        // ─── DCS004: ロール名ハードコード ─────────────────────────────

        private static readonly string[] KnownRoles = { "Admin", "User", "Manager" };

        private static void AnalyzeHardcodedRole(SyntaxNodeAnalysisContext context)
        {
            var binaryExpr = (BinaryExpressionSyntax)context.Node;

            // 左辺または右辺が文字列リテラル "Admin"/"User"/"Manager"
            CheckSideForHardcodedRole(context, binaryExpr, binaryExpr.Left);
            CheckSideForHardcodedRole(context, binaryExpr, binaryExpr.Right);
        }

        private static void CheckSideForHardcodedRole(
            SyntaxNodeAnalysisContext context,
            BinaryExpressionSyntax expr,
            ExpressionSyntax side)
        {
            if (side is not LiteralExpressionSyntax lit) return;
            if (!lit.IsKind(SyntaxKind.StringLiteralExpression)) return;

            var value = lit.Token.ValueText;
            if (!KnownRoles.Contains(value)) return;

            // 反対側が Role 関連の識別子かチェック
            var otherSide = ReferenceEquals(side, expr.Left) ? expr.Right : expr.Left;
            if (!IsRoleExpression(otherSide)) return;

            context.ReportDiagnostic(Diagnostic.Create(
                HardcodedRoleRule,
                lit.GetLocation(),
                value
            ));
        }

        private static bool IsRoleExpression(ExpressionSyntax expr)
        {
            // user.Role  /  claims.Role  /  role  /  userRole
            var text = expr.ToString().ToLowerInvariant();
            return text.Contains("role") || text.Contains("claim");
        }
    }
}
