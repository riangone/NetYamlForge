# AI システムプロンプト設定ガイド

## 概要

NetYamlForge では、AI 助手のシステムプロンプトを MD ファイルで管理しています。
これにより、コードを再コンパイルせずにプロンプトを簡単に更新できます。

## 設定ファイル一覧

| AI 助手 | 設定ファイル | 用途 |
|---------|-------------|------|
| **AI Assistant** | `skills/_system-prompt.md` | フレームワーク開発用 |
| **AI 業務アシスタント** | `skills/auto-dealer/_system-prompt-staff.md` | 社員向け業務支援 |
| **AI 顧客アシスタント** | `skills/auto-dealer/_system-prompt-customer.md` | 顧客向けサポート |

## ファイル構造

```
NetYamlForge/
└── NetYamlForge/
    └── skills/
        ├── _system-prompt.md              # フレームワーク開発 AI 用
        └── auto-dealer/
            ├── _system-prompt-staff.md    # 社員向け
            └── _system-prompt-customer.md # 顧客向け
```

## 各 AI 助手の責務

### 1. AI Assistant（フレームワーク開発）

**場所**: `skills/_system-prompt.md`

**責務**:
- ✅ フレームワークのコード開発・保守
- ✅ YAML 設定ファイルの編集
- ✅ 新規機能の実装
- ✅ テストコードの作成

**制限**:
- ❌ 顧客業務データへのアクセス禁止
- ❌ auto-dealer-demo の業務ロジック変更禁止

### 2. AI 業務アシスタント（社員向け）

**場所**: `skills/auto-dealer/_system-prompt-staff.md`

**責務**:
- ✅ リード管理・予約確認
- ✅ 在庫照会・顧客情報照会
- ✅ データ分析・検索支援

**制限**:
- ❌ **コードの変更・削除・追加禁止**
- ❌ **フレームワーク構造変更禁止**
- ❌ **データベース書き込み操作禁止**

### 3. AI 顧客アシスタント（顧客向け）

**場所**: `skills/auto-dealer/_system-prompt-customer.md`

**責務**:
- ✅ 車両のご案内・在庫検索
- ✅ 試乗・サービス予約受付
- ✅ 購入相談・アフターフォロー

**制限**:
- ❌ 技術的な質問・コード関連
- ❌ 特別割引・値引き交渉（「担当者にお繋ぎします」）
- ❌ システム設定変更要求（「開発担当者にお繋ぎします」）

## 実装詳細

### AutoDealerChatService.cs

`BuildSystemPrompt` メソッドが MD ファイルからプロンプトを読み込みます：

```csharp
private string BuildSystemPrompt(bool isStaff, string? dbContextMarkdown = null)
{
    // MD ファイルからシステムプロンプトを読み込む
    var systemPromptMd = LoadSystemPromptFromMd(isStaff);
    
    // 動的な値を埋め込み
    systemPromptMd = systemPromptMd
        .Replace("{current_datetime}", DateTime.Now.ToString("yyyy-MM-dd HH:mm"))
        .Replace("{business_hours}", BusinessHours);

    // DB 検索結果がある場合は追加
    if (!string.IsNullOrWhiteSpace(dbContextMarkdown))
    {
        systemPromptMd += Environment.NewLine + Environment.NewLine;
        systemPromptMd += "## DB 検索結果（参考）" + Environment.NewLine;
        systemPromptMd += dbContextMarkdown;
    }

    return systemPromptMd;
}
```

### フォールバック機構

MD ファイルが見つからない場合、`BuildFallbackSystemPrompt` メソッドがハードコードされたプロンプトを返します。

```csharp
private string LoadSystemPromptFromMd(bool isStaff)
{
    var fileName = isStaff ? "_system-prompt-staff.md" : "_system-prompt-customer.md";
    var filePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "skills", "auto-dealer", fileName);
    
    // フォールバック：開発環境では相対パスも試行
    if (!File.Exists(filePath))
    {
        var relativePath = Path.Combine("NetYamlForge", "skills", "auto-dealer", fileName);
        if (File.Exists(relativePath))
        {
            filePath = relativePath;
        }
    }

    try
    {
        if (File.Exists(filePath))
        {
            _logger?.LogDebug("システムプロンプトを読み込みました：{FilePath}", filePath);
            return File.ReadAllText(filePath, Encoding.UTF8);
        }
        else
        {
            _logger?.LogWarning("システムプロンプトファイルが見つかりませんでした：{FilePath}", filePath);
            return BuildFallbackSystemPrompt(isStaff);
        }
    }
    catch (Exception ex)
    {
        _logger?.LogError(ex, "システムプロンプトの読み込みに失敗しました：{FilePath}", filePath);
        return BuildFallbackSystemPrompt(isStaff);
    }
}
```

## 更新手順

### システムプロンプトを更新する

1. 該当する MD ファイルを編集
2. アプリケーションを再起動（または開発モードでは自動リロード）
3. 動作確認

**例**: 顧客アシスタントの応答ルールを変更

```markdown
## 応答ルール
- 丁寧な敬語で回答してください
- 在庫データに基づいて具体的な車種・価格をご案内してください
- [新しいルールを追加]
```

### 新規 AI 助手を追加する

1. `skills/` ディレクトリ配下に新しい MD ファイルを作成
2. `AutoDealerChatService.cs` または該当サービスに読み込みロジックを追加
3. テストを実施

## 変数プレースホルダー

MD ファイルで使用可能な変数：

| 変数 | 説明 | 例 |
|------|------|-----|
| `{current_datetime}` | 現在の日時 | `2026-03-31 14:30` |
| `{business_hours}` | 営業時間 | `月〜土 9:00〜18:00` |
| `{dealer_name}` | ディーラー名 | 設定値から自動取得 |

## トラブルシューティング

### プロンプトが読み込まれない

1. **ファイルパスを確認**:
   ```bash
   ls -la NetYamlForge/skills/auto-dealer/
   ```

2. **ログを確認**:
   ```
   [Warning] システムプロンプトファイルが見つかりませんでした
   ```

3. **フォールバックが動作**: MD ファイルがない場合、ハードコードされたプロンプトが使用されます

### 変更が反映されない

- 開発モード：ファイル変更から 500ms 後に自動リロード
- 本番モード：アプリケーション再起動が必要

## ベストプラクティス

1. **簡潔に**: 必要な情報だけを記載
2. **具体的に**: 具体的な例示を含める
3. **一貫性**: 複数の AI 助手で用語を統一
4. **テスト**: 変更後は必ず動作確認

## 関連ドキュメント

- [AI 助手完全ガイド](./ai-assistant-guide.md)
- [ローカルモデル設定](./guides/ai-local-model-setup.md)
- [設定リファレンス](./configuration-reference.md)

---

*最終更新：2026-03-31*
