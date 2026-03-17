# attendance-ops

勤怠管理向けサブプロジェクトです。以下の一般機能を含みます。

- 部門管理
- 社員管理
- シフト管理
- 勤怠記録管理（出勤・退勤・ステータス）
- 休暇申請管理
- 残業申請管理
- ダッシュボード（件数統計と分布チャート）
- 専用業務ページ（主入口、打刻、社員、管理、承認）

## エンティティ一覧

- `department`
- `employee`
- `shift`
- `attendance_record`
- `leave_request`
- `overtime_request`

## 専用ページ一覧

- `/attendance-ops/Page/AttendancePortal` : 考勤主入口
- `/attendance-ops/Page/ClockInCenter` : 打刻ページ
- `/attendance-ops/Page/EmployeeHub` : 社員ページ
- `/attendance-ops/Page/ManagementConsole` : 管理ページ
- `/attendance-ops/Page/ApprovalCenter` : 承認ページ

## 承認ページの更新仕様

- `ApprovalCenter` の「同意/驳回」は `LeaveRequest` / `OvertimeRequest` の `Status` を更新します。
- `approved` / `rejected` への更新時、`Approver` と `ApprovedAt` を自動記録します。

## 初期データ

`database/init.sql` にテーブル作成とサンプルデータ投入SQLを同梱しています。
