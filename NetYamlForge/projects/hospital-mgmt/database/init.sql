-- =====================================================
-- 病院管理システム (hospital-mgmt) - 初期化SQL
-- =====================================================

-- 患者台帳
CREATE TABLE IF NOT EXISTS "Patient" (
    "Id"               INTEGER PRIMARY KEY AUTOINCREMENT,
    "PatientCode"      TEXT    NOT NULL UNIQUE,
    "LastName"         TEXT    NOT NULL,
    "FirstName"        TEXT    NOT NULL,
    "LastNameKana"     TEXT,
    "FirstNameKana"    TEXT,
    "Gender"           TEXT    NOT NULL DEFAULT 'male',
    "BirthDate"        TEXT    NOT NULL,
    "BloodType"        TEXT,
    "Phone"            TEXT,
    "Address"          TEXT,
    "InsuranceNumber"  TEXT,
    "EmergencyContact" TEXT,
    "Notes"            TEXT,
    "CreatedAt"        TEXT,
    "UpdatedAt"        TEXT
);

-- 医師マスタ
CREATE TABLE IF NOT EXISTS "Doctor" (
    "Id"              INTEGER PRIMARY KEY AUTOINCREMENT,
    "EmployeeCode"    TEXT    NOT NULL UNIQUE,
    "LastName"        TEXT    NOT NULL,
    "FirstName"       TEXT    NOT NULL,
    "Department"      TEXT    NOT NULL,
    "Specialization"  TEXT,
    "LicenseNumber"   TEXT,
    "Phone"           TEXT,
    "IsActive"        INTEGER NOT NULL DEFAULT 1,
    "CreatedAt"       TEXT
);

-- 予約
CREATE TABLE IF NOT EXISTS "Appointment" (
    "Id"               INTEGER PRIMARY KEY AUTOINCREMENT,
    "PatientId"        INTEGER NOT NULL,
    "DoctorId"         INTEGER NOT NULL,
    "AppointmentDate"  TEXT    NOT NULL,
    "AppointmentTime"  TEXT    NOT NULL DEFAULT '09:00',
    "Department"       TEXT    NOT NULL,
    "ReasonForVisit"   TEXT,
    "Status"           TEXT    NOT NULL DEFAULT 'scheduled',
    "Notes"            TEXT,
    "CreatedAt"        TEXT,
    FOREIGN KEY ("PatientId") REFERENCES "Patient"("Id"),
    FOREIGN KEY ("DoctorId")  REFERENCES "Doctor"("Id")
);

-- 診療記録
CREATE TABLE IF NOT EXISTS "MedicalRecord" (
    "Id"              INTEGER PRIMARY KEY AUTOINCREMENT,
    "PatientId"       INTEGER NOT NULL,
    "DoctorId"        INTEGER NOT NULL,
    "AppointmentId"   INTEGER,
    "VisitDate"       TEXT    NOT NULL,
    "ChiefComplaint"  TEXT,
    "Diagnosis"       TEXT,
    "TreatmentPlan"   TEXT,
    "VitalSigns"      TEXT,
    "Notes"           TEXT,
    "CreatedAt"       TEXT,
    FOREIGN KEY ("PatientId")    REFERENCES "Patient"("Id"),
    FOREIGN KEY ("DoctorId")     REFERENCES "Doctor"("Id"),
    FOREIGN KEY ("AppointmentId") REFERENCES "Appointment"("Id")
);

-- 処方箋
CREATE TABLE IF NOT EXISTS "Prescription" (
    "Id"              INTEGER PRIMARY KEY AUTOINCREMENT,
    "MedicalRecordId" INTEGER NOT NULL,
    "PatientId"       INTEGER NOT NULL,
    "DoctorId"        INTEGER NOT NULL,
    "MedicineName"    TEXT    NOT NULL,
    "Dosage"          TEXT    NOT NULL,
    "Frequency"       TEXT    NOT NULL DEFAULT '1日3回',
    "DaysSupply"      INTEGER NOT NULL DEFAULT 7,
    "Instructions"    TEXT,
    "PrescribedAt"    TEXT,
    FOREIGN KEY ("MedicalRecordId") REFERENCES "MedicalRecord"("Id"),
    FOREIGN KEY ("PatientId")       REFERENCES "Patient"("Id"),
    FOREIGN KEY ("DoctorId")        REFERENCES "Doctor"("Id")
);

-- 病棟・病床
CREATE TABLE IF NOT EXISTS "WardBed" (
    "Id"               INTEGER PRIMARY KEY AUTOINCREMENT,
    "WardName"         TEXT    NOT NULL,
    "RoomNumber"       TEXT    NOT NULL,
    "BedNumber"        TEXT    NOT NULL,
    "BedType"          TEXT    NOT NULL DEFAULT 'general',
    "Status"           TEXT    NOT NULL DEFAULT 'available',
    "PatientId"        INTEGER,
    "AdmissionDate"    TEXT,
    "ExpectedDischarge" TEXT,
    "Notes"            TEXT,
    "UpdatedAt"        TEXT,
    FOREIGN KEY ("PatientId") REFERENCES "Patient"("Id")
);

-- 請求
CREATE TABLE IF NOT EXISTS "Billing" (
    "Id"                INTEGER PRIMARY KEY AUTOINCREMENT,
    "PatientId"         INTEGER NOT NULL,
    "MedicalRecordId"   INTEGER,
    "BillingDate"       TEXT    NOT NULL,
    "TotalAmount"       REAL    NOT NULL DEFAULT 0,
    "InsuranceCoverage" REAL    NOT NULL DEFAULT 0,
    "PatientShare"      REAL    NOT NULL DEFAULT 0,
    "PaymentStatus"     TEXT    NOT NULL DEFAULT 'unpaid',
    "PaymentMethod"     TEXT,
    "PaidAt"            TEXT,
    "Notes"             TEXT,
    "CreatedAt"         TEXT,
    FOREIGN KEY ("PatientId")       REFERENCES "Patient"("Id"),
    FOREIGN KEY ("MedicalRecordId") REFERENCES "MedicalRecord"("Id")
);

-- =====================================================
-- サンプルデータ
-- =====================================================

INSERT INTO "Doctor" ("EmployeeCode","LastName","FirstName","Department","Specialization","LicenseNumber","Phone","IsActive","CreatedAt") VALUES
('D-001','田中','一郎','内科','消化器内科','1234567','101',1,datetime('now','localtime')),
('D-002','鈴木','花子','外科','整形外科','2345678','102',1,datetime('now','localtime')),
('D-003','佐藤','次郎','小児科','小児科全般','3456789','103',1,datetime('now','localtime'));

INSERT INTO "WardBed" ("WardName","RoomNumber","BedNumber","BedType","Status","UpdatedAt") VALUES
('北病棟','101','A','general','available',datetime('now','localtime')),
('北病棟','101','B','general','available',datetime('now','localtime')),
('北病棟','102','A','private','available',datetime('now','localtime')),
('南病棟','ICU-1','1','icu','available',datetime('now','localtime')),
('南病棟','201','A','semi_private','available',datetime('now','localtime')),
('南病棟','201','B','semi_private','available',datetime('now','localtime'));
