# salesforce-crm UAT シナリオ（日本語）

## 1. 目的
Salesforce 風 CRM ページ群が、実運用で必要な導線（営業・サポート・管理）を満たすことを確認する。

## 2. 前提
- URL: `/salesforce-crm`
- ロール: `Admin` と `一般ユーザー`
- 言語: `en-US / zh-CN / ja-JP / ko-KR`

## 3. 営業シナリオ
### SC-01 リード受付から商談化
1. `Lead Inbox` を開く
2. 任意の行で担当者割当を実行
3. `Lead Detail 360` で活動を確認
4. `Opportunity Detail` でステージ更新

期待結果:
- 画面更新が成功し、最新ステータスが表示される
- `Audit Trail` に関連操作が記録される

### SC-02 見積と受注追跡
1. `Quote Builder` を開く
2. 見積候補を確認
3. `Order Management` で対象注文の状態を確認
4. `Invoice & Payment` で請求状況を確認

期待結果:
- 金額情報と状態が一貫して表示される

## 4. サポートシナリオ
### SV-01 ケース振分と解決
1. `Case Queue` を開く
2. `Escalate` / `Resolve` を実行
3. `Case Detail` で最新ステータスと活動履歴を確認
4. `SLA Monitor` で違反予兆を確認

期待結果:
- ケース状態が更新される
- `Case Detail` の活動履歴に監査イベントが反映される

### SV-02 ナレッジ・オムニチャネル確認
1. `Knowledge Base` を開く
2. `Omni-Channel Console` を開く

期待結果:
- キュー/記事の一覧が表示される
- フィルタとソートが機能する

## 5. 管理シナリオ
### AD-01 承認処理
1. `Approval Inbox` を開く
2. `Approve` または `Reject` を実行
3. `Audit Trail` で操作を確認

期待結果:
- ステータス更新が成功
- 監査ログに記録が残る

### AD-02 ユーザー権限変更
1. `User Role Profile` を開く
2. 任意ユーザーで `Enable/Disable`、`Grant/Remove Admin` を実行
3. `Role Access Matrix` で反映を確認

期待結果:
- 権限・状態の変更が反映される
- 変更操作が `AuditLog` に残る

## 6. 多言語シナリオ
### I18N-01 韓国語表示
1. 言語を `ko-KR` に切替
2. `Executive Cockpit`, `Case Detail`, `User Role Profile` を表示

期待結果:
- 見出し、列名、操作ラベルが韓国語で表示される
- `MissingManifestResourceException` が発生しない

## 7. 合否判定
- すべてのシナリオで 500 エラーなし
- 主要操作で監査ログ確認可能
- 4言語で未翻訳キー表示（キー文字列露出）がない
