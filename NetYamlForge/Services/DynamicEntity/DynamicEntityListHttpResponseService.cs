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
    /// HX-Push-Url ヘッダーを設定してブラウザ URL を現在の一覧状態に同期します。
    /// 検索・フィルター・ページ・ソートが URL に反映されるため、
    /// ブラウザの戻るボタンや URL 共有が正常に機能します。
    /// baseIndexUrl が null/空 の場合は何もしません。
    /// </summary>
    public void TrySetPushUrl(
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
        response.Headers["HX-Push-Url"] = stateUrl;
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

