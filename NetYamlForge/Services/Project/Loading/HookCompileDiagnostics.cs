namespace NetYamlForge.Services.Project.Loading;

internal static class HookCompileDiagnostics
{
    public static string Hint(string diagnosticId)
    {
        return diagnosticId switch
        {
            "CS0246" => "型/名前空間が見つかりません。using 追加または参照アセンブリを確認してください。",
            "CS0103" => "名前が現在のコンテキストに存在しません。変数名・スコープを確認してください。",
            "CS1061" => "メンバーが見つかりません。型定義と拡張メソッド using を確認してください。",
            "CS1503" => "引数の型が一致していません。メソッド定義と渡す値 of を確認してください。",
            _ => string.Empty
        };
    }
}
