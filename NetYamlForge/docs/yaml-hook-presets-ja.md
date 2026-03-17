# YAML Hook Presets ガイド（共通ロジックを YAML で定義）

## 概要

`hooks.presets` を使うと、よく使う Hook の組み合わせを YAML で再利用できます。  
特殊処理は従来どおり C# の `IEntityHook` 実装を使います。

---

## 1. 使い方

```yaml
entities:
  post:
    hooks:
      presets:
        common_before:
          - validate_required
          - trim
        publish_guard:
          - validate_required:Title,Content
          - validate_regex:Slug:^[a-z0-9-]+$
      beforeCreate:
        - blog_post_slug_generator     # C# の特殊ロジック
        - "@common_before"             # YAML プリセット
      beforeUpdate:
        - "@publish_guard"
        - blog_post_slug_generator
      afterUpdate:
        - audit_log
```

ポイント:
1. プリセット呼び出しは `@presetName`
2. プリセット内に通常 Hook 文字列をそのまま書ける
3. プリセットのネスト（`@anotherPreset`）も可能

---

## 2. 設計方針（推奨）

1. 共通ルールは YAML
- 例: 必須、trim、メール形式、範囲チェック

2. 業務特化は C#
- 例: 外部 API 呼び出し、複雑な DB 照会、複数テーブル整合性

3. 1エンティティ内で併用
- `@common_before` + `special_business_hook`

---

## 3. 実行順序

`beforeCreate` に以下を書いた場合:

```yaml
beforeCreate:
  - special_a
  - "@common_before"
  - special_b
```

実行順序は:
1. `special_a`
2. `common_before` 内の Hook 群（定義順）
3. `special_b`

---

## 4. 注意事項

1. 未定義プリセット
- 警告ログを出してスキップされます

2. 循環参照
- 例: `a -> @b`, `b -> @a`
- 循環を検出した時点でその展開を中止し、警告ログを出します

3. 文字列パラメータ
- 既存の `validate_email:Email` 形式はそのまま利用可能

---

## 5. 実ファイル例

- `projects/blog/entities/post.yml`
  - `hooks.presets.blog_common_before`
  - `beforeCreate` / `beforeUpdate` で `@blog_common_before` を利用
