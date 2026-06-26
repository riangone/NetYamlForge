# 病院管理システム (hospital-mgmt) 設計書

## 概要

日本の病院・クリニック向け管理システム。患者台帳から予約、診療記録、処方箋、病床管理、請求まで一元管理する。

---

## エンティティ設計

### 1. Patient（患者台帳）
| カラム | 型 | 説明 |
|---|---|---|
| Id | int (PK) | 患者ID |
| PatientCode | string | 患者番号（P-XXXXXX）|
| LastName | string | 姓 |
| FirstName | string | 名 |
| LastNameKana | string | 姓（カナ）|
| FirstNameKana | string | 名（カナ）|
| Gender | string | 性別（male/female/other）|
| BirthDate | date | 生年月日 |
| BloodType | string | 血液型 |
| Phone | string | 電話番号 |
| Address | string | 住所 |
| InsuranceNumber | string | 保険証番号 |
| EmergencyContact | string | 緊急連絡先 |
| Notes | text | 備考 |
| CreatedAt | datetime | 登録日時 |
| UpdatedAt | datetime | 更新日時 |

### 2. Doctor（医師）
| カラム | 型 | 説明 |
|---|---|---|
| Id | int (PK) | 医師ID |
| EmployeeCode | string | 職員番号 |
| LastName | string | 姓 |
| FirstName | string | 名 |
| Department | string | 診療科 |
| Specialization | string | 専門分野 |
| LicenseNumber | string | 医師免許番号 |
| Phone | string | 内線番号 |
| IsActive | bool | 在籍フラグ |
| CreatedAt | datetime | 登録日時 |

### 3. Appointment（予約）
| カラム | 型 | 説明 |
|---|---|---|
| Id | int (PK) | 予約ID |
| PatientId | int (FK) | 患者ID |
| DoctorId | int (FK) | 医師ID |
| AppointmentDate | date | 予約日 |
| AppointmentTime | string | 予約時間 |
| Department | string | 診療科 |
| ReasonForVisit | string | 受診理由 |
| Status | string | ステータス（scheduled/completed/cancelled/no_show）|
| Notes | text | 備考 |
| CreatedAt | datetime | 登録日時 |

### 4. MedicalRecord（診療記録）
| カラム | 型 | 説明 |
|---|---|---|
| Id | int (PK) | 記録ID |
| PatientId | int (FK) | 患者ID |
| DoctorId | int (FK) | 医師ID |
| AppointmentId | int (FK, nullable) | 予約ID |
| VisitDate | date | 受診日 |
| ChiefComplaint | text | 主訴 |
| Diagnosis | text | 診断名 |
| TreatmentPlan | text | 治療方針 |
| VitalSigns | string | バイタルサイン（体温/血圧/脈拍）|
| Notes | text | 医師メモ |
| CreatedAt | datetime | 登録日時 |

### 5. Prescription（処方箋）
| カラム | 型 | 説明 |
|---|---|---|
| Id | int (PK) | 処方ID |
| MedicalRecordId | int (FK) | 診療記録ID |
| PatientId | int (FK) | 患者ID |
| DoctorId | int (FK) | 医師ID |
| MedicineName | string | 薬品名 |
| Dosage | string | 用量 |
| Frequency | string | 服用頻度 |
| DaysSupply | int | 処方日数 |
| Instructions | text | 服用指示 |
| PrescribedAt | datetime | 処方日時 |

### 6. WardBed（病棟・病床）
| カラム | 型 | 説明 |
|---|---|---|
| Id | int (PK) | 病床ID |
| WardName | string | 病棟名 |
| RoomNumber | string | 部屋番号 |
| BedNumber | string | ベッド番号 |
| BedType | string | 病床種別（general/icu/private/semi_private）|
| Status | string | 状態（available/occupied/maintenance）|
| PatientId | int (FK, nullable) | 入院患者ID |
| AdmissionDate | date | 入院日 |
| ExpectedDischarge | date | 退院予定日 |
| Notes | text | 備考 |
| UpdatedAt | datetime | 更新日時 |

### 7. Billing（請求）
| カラム | 型 | 説明 |
|---|---|---|
| Id | int (PK) | 請求ID |
| PatientId | int (FK) | 患者ID |
| MedicalRecordId | int (FK, nullable) | 診療記録ID |
| BillingDate | date | 請求日 |
| TotalAmount | decimal | 合計金額 |
| InsuranceCoverage | decimal | 保険適用額 |
| PatientShare | decimal | 患者負担額 |
| PaymentStatus | string | 支払状況（unpaid/partial/paid）|
| PaymentMethod | string | 支払方法（cash/card/insurance）|
| PaidAt | datetime | 支払日時 |
| Notes | text | 備考 |
| CreatedAt | datetime | 登録日時 |

---

## ER図（概略）

```
Patient ──< Appointment >── Doctor
Patient ──< MedicalRecord >── Doctor
MedicalRecord ──< Prescription
Patient ──< WardBed
Patient ──< Billing
MedicalRecord ── Billing
```

---

## ナビゲーション構成

```
🏥 ホーム
📊 ダッシュボード
── 患者管理
   👤 患者台帳
── 医師・スタッフ
   👨‍⚕️ 医師一覧
── 予約管理
   📅 予約一覧
── 診療
   📋 診療記録
   💊 処方箋
── 病棟管理
   🛏 病床管理
── 会計
   💴 請求管理
```

---

## 技術スタック

- Framework: NetYamlForge (ASP.NET Core 10)
- DB: SQLite
- 言語: 日本語 (ja) / 英語 (en)
- テーマ: workspace (ダッシュボード)
