// ファイル概要: フックが HookResult.Abort() を返した際の拒否理由メッセージを
// 機械可読なエラーコードに分類するユーティリティです。
// 監査ログへの記録時（TryWriteHookRejectAuditAsync）に呼ばれ、
// ログ分析やアラート条件の絞り込みに使います。

namespace NetYamlForge.Services.Hooks;

/// <summary>
/// フック拒否理由のテキストを構造化コードに分類するユーティリティ。
/// <para>
/// 分類コード一覧:
/// <list type="bullet">
///   <item><term>STATUS_TRANSITION</term><description>ステータス遷移の制約違反</description></item>
///   <item><term>STATUS_LOCK</term><description>出荷済み・キャンセル済みなど変更不可状態</description></item>
///   <item><term>INVENTORY</term><description>在庫不足・在庫上限超過</description></item>
///   <item><term>REQUIRED_FIELD</term><description>必須フィールドの未入力</description></item>
///   <item><term>DUPLICATE</term><description>一意制約違反・重複データ</description></item>
///   <item><term>DELETE_GUARD</term><description>削除禁止ルール違反</description></item>
///   <item><term>BUSINESS_RULE</term><description>上記以外の業務ルール違反（デフォルト）</description></item>
/// </list>
/// </para>
/// </summary>
public static class HookRejectReasonClassifier
{
    /// <summary>
    /// フック拒否メッセージを解析し、対応するエラーコード文字列を返します。
    /// キーワードマッチングによる簡易分類です（日本語・英語両対応）。
    /// </summary>
    /// <param name="reason">HookResult.Abort() に渡されたキャンセルメッセージ</param>
    /// <returns>分類コード文字列（例: "INVENTORY", "REQUIRED_FIELD"）</returns>
    public static string Classify(string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
            return "BUSINESS_RULE";

        // ステータス遷移エラー（例: "無効な状態遷移です"）
        if (reason.Contains("遷移", StringComparison.OrdinalIgnoreCase))
            return "STATUS_TRANSITION";
        // ステータスロック（出荷済み・キャンセル済みへの変更禁止）
        if (reason.Contains("Shipped", StringComparison.OrdinalIgnoreCase) ||
            reason.Contains("Cancelled", StringComparison.OrdinalIgnoreCase))
            return "STATUS_LOCK";
        // 在庫不足・在庫エラー（例: "在庫が不足しています", "out of stock"）
        if (reason.Contains("在庫", StringComparison.OrdinalIgnoreCase) ||
            reason.Contains("stock", StringComparison.OrdinalIgnoreCase))
            return "INVENTORY";
        // 必須フィールド未入力（例: "必須項目です", "field is required"）
        if (reason.Contains("必須", StringComparison.OrdinalIgnoreCase) ||
            reason.Contains("required", StringComparison.OrdinalIgnoreCase))
            return "REQUIRED_FIELD";
        // 重複・一意制約違反（例: "既に存在するメールアドレスです", "duplicate entry"）
        if (reason.Contains("重複", StringComparison.OrdinalIgnoreCase) ||
            reason.Contains("既に存在", StringComparison.OrdinalIgnoreCase) ||
            reason.Contains("duplicate", StringComparison.OrdinalIgnoreCase))
            return "DUPLICATE";
        // 削除禁止（例: "参照されているため削除できません", "cannot delete"）
        if (reason.Contains("削除できません", StringComparison.OrdinalIgnoreCase) ||
            reason.Contains("delete", StringComparison.OrdinalIgnoreCase))
            return "DELETE_GUARD";
        // 上記に当てはまらない汎用ビジネスルール違反
        return "BUSINESS_RULE";
    }
}
