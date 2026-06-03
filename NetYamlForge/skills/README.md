# NetYamlForge Skills ガイド

## 概要

gstack フレームワークの設計原則に基づき、NetYamlForge 框架と auto-dealer-demo サブプロジェクト专用的 AI スキルシステムを実装しました。

---

## スキル一覧

### フレームワーク核心スキル (`NetYamlForge/skills/framework/`)

| スキル | 用途 | Tier |
|-------|------|------|
| `/nyf-review` | コードレビュー（マージ前） | 1 |
| `/nyf-ship` | 自動化リリース | 1 |
| `/nyf-scaffold` | 足場作り・コード生成 | 1 |
| `/nyf-security` | セキュリティ診断 | 1 |

### auto-dealer-demo 業務スキル (`NetYamlForge/skills/auto-dealer/`)

| スキル | 用途 | Tier |
|-------|------|------|
| `/dealer-inventory` | 在庫管理・分析 | 1 |
| `/dealer-sales` | 営業リード管理 | 1 |
| `/dealer-customer` | 顧客管理 | 1 |
| `/dealer-appointment` | 予約管理 | 1 |

---

## スキル標準フォーマット

各スキルは `SKILL.md` ファイルで定義され、以下のセクションを含みます：

```markdown
---
name: {skill-name}
tier: 1-3
version: 1.0.0
description: |
  スキルの説明
allowed-tools:
  - Bash
  - Read
  - ...
---

## Preamble (run first)
初期化スクリプト

## Voice
トーンと書き方のルール

## Completion Status Protocol
完了状態の報告標準

## 工作流程
主要なワークフロー

## 出力形式
レスポンスフォーマット

## 他スキルとの連携
連携情報

## Command Reference
コマンドリファレンス

## Tips
ベストプラクティス
```

---

## 使用方法

### フレームワーク AI の場合

```bash
# コードレビューを実行
/nyf-review

# リリースを実行
/nyf-ship

# 新規エンティティを生成
/nyf-scaffold entity

# セキュリティスキャン
/nyf-security
```

### auto-dealer-demo AI の場合

```bash
# 在庫照会
/dealer-inventory

# 営業リード管理
/dealer-sales --priority

# 顧客情報照会
/dealer-customer --vip

# 予約管理
/dealer-appointment
```

---

## スキル路由

CLAUDE.md ファイルでスキル路由を定義：

### NetYamlForge/CLAUDE.md
```markdown
## Skill Routing
| ユーザーリクエスト | 呼び出すスキル |
|------------------|---------------|
| コードレビュー | `/nyf-review` |
| リリース | `/nyf-ship` |
```

### auto-dealer-demo/CLAUDE.md
```markdown
## Skill Routing
| ユーザーリクエスト | 呼び出すスキル |
|------------------|---------------|
| 在庫照会 | `/dealer-inventory` |
| 営業管理 | `/dealer-sales` |
```

---

## gstack との比較

| 機能 | gstack | NetYamlForge Skills |
|------|--------|---------------------|
| コードレビュー | `/review` | `/nyf-review` |
| リリース | `/ship` | `/nyf-ship` |
| 足場作り | ー | `/nyf-scaffold` |
| セキュリティ | `/cso` | `/nyf-security` |
| 在庫管理 | ー | `/dealer-inventory` |
| 営業管理 | ー | `/dealer-sales` |
| 顧客管理 | ー | `/dealer-customer` |

---

## 拡張方法

### 新しいスキルを追加

1. `NetYamlForge/skills/{category}/{skill-name}/` ディレクトリを作成
2. `SKILL.md` テンプレートをコピー
3. スキルの実装を記述
4. CLAUDE.md に路由ルールを追加

### テンプレートファイル

`NetYamlForge/skills/SKILL.md.template` を使用してください。

---

## 最佳实践

1. **スキル優先**: マッチするスキルがある場合は直接答えない
2. **完了状態**: 常に Completion Status Protocol を使用する
3. **建設的**: 問題 + 具体的な解決策を提示
4. **データ駆動**: 数値と根拠を示す

---

## 関連ドキュメント

- [gstack 元プロジェクト](https://github.com/garrytan/gstack)
- [SKILL.md 標準](NetYamlForge/skills/SKILL.md.template)
- [フレームワーク CLAUDE.md](CLAUDE.md)
- [auto-dealer-demo CLAUDE.md](NetYamlForge/projects/auto-dealer-demo/CLAUDE.md)

---

*最終更新：2026 年 4 月 1 日*
