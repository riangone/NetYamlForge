# Calendar Ui Lab

YAML定義でカレンダーUIを検証するためのサブプロジェクトです。

## 主な画面

- `/calendar-ui-lab/Page/CalendarWorkbench`

## 日本祝日データソース設定

`project.yaml` で共通設定できます。

```yaml
calendar:
  japanHoliday:
    provider: hybrid # hybrid | api | builtin
    apiUrlTemplate: https://date.nager.at/api/v3/PublicHolidays/{year}/JP
    apiTimeoutMs: 4000
```

`pages/CalendarWorkbench.yaml` の `calendar_ui` でも同名設定を記述できます。

## 設定の優先順位

1. ページ設定（`pages/CalendarWorkbench.yaml` の `calendar_ui`）
2. プロジェクト共通設定（`project.yaml` の `calendar.japanHoliday`）
3. 内部デフォルト値

## 補足

- `provider=hybrid` は API優先、失敗時に内蔵計算へフォールバックします。
- 画面上の `JP source` バッジで、`API / Builtin / Mixed` を確認できます。
- API失敗時はトースト通知が表示されます。
