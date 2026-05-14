// ファイル概要: HTMX 部分更新後のレスポンスヘッダーを管理するサービスです。
// HX-Push-Url でブラウザのアドレスバーを現在の検索・フィルター状態に同期し、
// HX-Trigger でクライアント側のカスタムイベントを発火してモーダルを閉じる等の UI 制御を行います。

using Microsoft.AspNetCore.Http;

namespace NetYamlForge.Services;

/// <summary>
/// HTMX レスポンスヘッダーを設定するサービス。
/// 部分更新リクエストの応答にブラウザ状態同期のためのヘッダーを追加します。
/// </summary>
public sealed class DynamicEntityListHttpResponseService
{
    /// <summary>
    /// HX-Push-Url または HX-Replace-Url ヘッダーを設定してブラウザ URL を現在の一覧状態に同期します。
    /// 検索・フィルター・ページ・ソートが URL に反映されるため、
    /// ブラウザの戻るボタンや URL 共有が正常に機能します。
    /// baseIndexUrl が null/空 の場合は何もしません。
    /// </summary>
    public void TrySetPushUrl(
        HttpRequest request,
        HttpResponse response,
        string? baseIndexUrl,
        IQueryCollection query,
        string entity,
        string? returnUrl)
    {
        if (string.IsNullOrWhiteSpace(baseIndexUrl))
        {
            return;
        }

        var stateUrl = ListStateUrlBuilder.BuildIndexStateUrl(baseIndexUrl, query, entity, returnUrl);

        // ブラウザの「戻る」ボタンの問題（2回押す必要がある）を解決するためのロジック：
        // 1. 初期ロード時 (hx-trigger="load" を持つ #list-container からのリクエスト)
        // 2. 更新後の URL が現在の URL (HX-Current-Url) と同一である場合
        // これらの場合は Push ではなく Replace を使用することで、履歴に重複したエントリが追加されるのを防ぎます。
        var trigger = request.Headers["HX-Trigger"].ToString();
        var currentUrl = request.Headers["HX-Current-Url"].ToString();

        bool shouldReplace = trigger == "list-container" || IsSameUrl(currentUrl, stateUrl);

        if (shouldReplace)
        {
            response.Headers["HX-Replace-Url"] = stateUrl;
        }
        else
        {
            response.Headers["HX-Push-Url"] = stateUrl;
        }
    }

    private bool IsSameUrl(string? currentUrl, string stateUrl)
    {
        if (string.IsNullOrWhiteSpace(currentUrl)) return false;

        try
        {
            var currentUri = new Uri(currentUrl);
            var stateUri = new Uri("http://dummy" + (stateUrl.StartsWith("/") ? "" : "/") + stateUrl);

            // パスとクエリを比較（ドメインは無視）
            return string.Equals(currentUri.AbsolutePath, stateUri.AbsolutePath, StringComparison.OrdinalIgnoreCase)
                   && string.Equals(currentUri.Query, stateUri.Query, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            // URL のパースに失敗した場合は安全のため Push を許可する（false を返す）
            return false;
        }
    }

    /// <summary>
    /// フォーム保存成功後のレスポンスに HTMX 制御ヘッダーを設定します。
    /// HX-Retarget: 応答 HTML の描画先を #list-container に上書き指定
    /// HX-Trigger: クライアント側で "entity-form-saved" カスタムイベントを発火し、
    ///             モーダルを閉じてサクセストースト表示などを実行します。
    /// </summary>
    public void SetEntityFormSavedHeaders(HttpResponse response)
    {
        response.Headers["HX-Retarget"] = "#list-container";
        response.Headers["HX-Trigger"] = "entity-form-saved";
    }
}

