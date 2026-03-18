# ForeignKey: 複数表示列とカスタムQuery

`foreignKey` 定義で、表示ラベルを複数列から構成し、候補データ取得SQLを個別に指定できます。

## 対応した設定

- `displayColumn`: 既存互換（単一列、または `","` 区切り文字列）
- `displayColumns`: 推奨（複数列の配列）
- `query`: 参照候補取得用のカスタム SQL

## YAML 例（フォーム）

```yaml
forms:
  CustomerId:
    type: int
    foreignKey:
      entity: customer
      displayColumns:
        - FirstName
        - LastName
        - Email
      query: |
        SELECT CustomerId AS Id, FirstName, LastName, Email
        FROM Customer
        WHERE IsDeleted = 0
```

表示ラベルは `FirstName / LastName / Email` の形式で表示されます。

## picker / multipicker での挙動

- `foreignKey.picker: true`（単一選択）でも `displayColumns` の複数列ラベルが使われます。
- `foreignKey.multiPicker: true`（複数選択）でもチップ表示に同じ複数列ラベルが使われます。
- ピッカーモーダルの行選択IDは `Id` 列を優先して使用します（`query` 利用時も同じ）。

## YAML 例（フィルタ）

```yaml
filters:
  CustomerId:
    type: entity-picker
    foreignKey:
      entity: customer
      displayColumns: [FirstName, LastName]
      query: |
        SELECT CustomerId AS Id, FirstName, LastName
        FROM Customer
```

## `query` 利用時の注意

- `SELECT` で始まる SQL を指定してください。
- 返却列には必ず `Id` を含めてください（例: `CustomerId AS Id`）。
- 危険なトークン（`;`, `--`, `/*`, `*/`）を含む SQL は拒否されます。
