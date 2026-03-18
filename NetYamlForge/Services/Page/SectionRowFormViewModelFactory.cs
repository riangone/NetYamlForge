// ファイル概要: Components/_SectionRowForm.cshtml に渡す SectionRowFormModel を構築するファクトリ。
// PageController が複数箇所でインラインに組み立てていたモデル構築ロジックを一元化します。
// DynamicEntityFormViewModelFactory の Section 版に相当します。

using NetYamlForge.Models;

namespace NetYamlForge.Services.Page;

/// <summary>
/// セクション行フォームビューモデルのファクトリ。
/// PageController の SectionRowForm / InsertRow / UpdateAllFields アクションから呼ばれます。
/// </summary>
public sealed class SectionRowFormViewModelFactory
{
    /// <summary>
    /// 新規作成フォーム用の SectionRowFormModel を構築します。
    /// </summary>
    public SectionRowFormModel BuildCreate(
        SectionDefinition section,
        string project,
        string pageName)
    {
        return new SectionRowFormModel
        {
            Sec = section,
            Row = null,
            Project = project,
            PageName = pageName
        };
    }

    /// <summary>
    /// 編集フォーム用の SectionRowFormModel を構築します。
    /// </summary>
    public SectionRowFormModel BuildEdit(
        SectionDefinition section,
        Dictionary<string, object>? row,
        string project,
        string pageName)
    {
        return new SectionRowFormModel
        {
            Sec = section,
            Row = row,
            Project = project,
            PageName = pageName
        };
    }

    /// <summary>
    /// バリデーション失敗時のエラー表示用 SectionRowFormModel を構築します。
    /// </summary>
    /// <param name="section">セクション定義</param>
    /// <param name="row">編集対象行（新規作成時は null）</param>
    /// <param name="project">プロジェクト名</param>
    /// <param name="pageName">ページ名</param>
    /// <param name="errorMessage">フォーム上部に表示するエラーメッセージ</param>
    /// <param name="submittedValues">バリデーション失敗時に送信値を再表示するためのマップ</param>
    public SectionRowFormModel BuildWithError(
        SectionDefinition section,
        Dictionary<string, object>? row,
        string project,
        string pageName,
        string? errorMessage,
        Dictionary<string, string?>? submittedValues = null)
    {
        return new SectionRowFormModel
        {
            Sec = section,
            Row = row,
            Project = project,
            PageName = pageName,
            ErrorMessage = errorMessage,
            SubmittedValues = submittedValues
        };
    }
}
