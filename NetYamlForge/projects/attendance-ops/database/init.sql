PRAGMA foreign_keys = ON;

CREATE TABLE IF NOT EXISTS Department (
  Id INTEGER PRIMARY KEY AUTOINCREMENT,
  Name TEXT NOT NULL,
  Code TEXT NOT NULL UNIQUE,
  Status TEXT NOT NULL DEFAULT 'active',
  Description TEXT
);

CREATE TABLE IF NOT EXISTS Employee (
  Id INTEGER PRIMARY KEY AUTOINCREMENT,
  EmployeeNo TEXT NOT NULL UNIQUE,
  FullName TEXT NOT NULL,
  DepartmentId INTEGER NOT NULL,
  EmploymentType TEXT NOT NULL,
  HireDate TEXT NOT NULL,
  Phone TEXT,
  Email TEXT,
  Status TEXT NOT NULL DEFAULT 'active',
  FOREIGN KEY (DepartmentId) REFERENCES Department(Id)
);

CREATE TABLE IF NOT EXISTS Shift (
  Id INTEGER PRIMARY KEY AUTOINCREMENT,
  ShiftCode TEXT NOT NULL UNIQUE,
  ShiftName TEXT NOT NULL,
  StartTime TEXT NOT NULL,
  EndTime TEXT NOT NULL,
  BreakMinutes INTEGER DEFAULT 60,
  Status TEXT NOT NULL DEFAULT 'active'
);

CREATE TABLE IF NOT EXISTS AttendanceRecord (
  Id INTEGER PRIMARY KEY AUTOINCREMENT,
  EmployeeId INTEGER NOT NULL,
  WorkDate TEXT NOT NULL,
  ShiftId INTEGER,
  CheckInTime TEXT,
  CheckOutTime TEXT,
  AttendanceStatus TEXT NOT NULL DEFAULT 'present',
  OvertimeHours REAL DEFAULT 0,
  Source TEXT DEFAULT 'web',
  Remark TEXT,
  FOREIGN KEY (EmployeeId) REFERENCES Employee(Id),
  FOREIGN KEY (ShiftId) REFERENCES Shift(Id)
);

CREATE TABLE IF NOT EXISTS LeaveRequest (
  Id INTEGER PRIMARY KEY AUTOINCREMENT,
  EmployeeId INTEGER NOT NULL,
  LeaveType TEXT NOT NULL,
  StartDate TEXT NOT NULL,
  EndDate TEXT NOT NULL,
  Days REAL NOT NULL,
  Reason TEXT,
  Status TEXT NOT NULL DEFAULT 'pending',
  Approver TEXT,
  ApprovedAt TEXT,
  AppliedAt TEXT,
  FOREIGN KEY (EmployeeId) REFERENCES Employee(Id)
);

CREATE TABLE IF NOT EXISTS OvertimeRequest (
  Id INTEGER PRIMARY KEY AUTOINCREMENT,
  EmployeeId INTEGER NOT NULL,
  WorkDate TEXT NOT NULL,
  StartTime TEXT NOT NULL,
  EndTime TEXT NOT NULL,
  Hours REAL NOT NULL,
  Reason TEXT,
  Status TEXT NOT NULL DEFAULT 'pending',
  Approver TEXT,
  ApprovedAt TEXT,
  FOREIGN KEY (EmployeeId) REFERENCES Employee(Id)
);

INSERT INTO Department (Name, Code, Status, Description) VALUES
  ('研发部', 'RND', 'active', '产品研发与技术支持'),
  ('销售部', 'SAL', 'active', '销售与客户拓展'),
  ('人事行政', 'HR', 'active', '人力资源与行政管理')
ON CONFLICT(Code) DO NOTHING;

INSERT INTO Employee (EmployeeNo, FullName, DepartmentId, EmploymentType, HireDate, Phone, Email, Status) VALUES
  ('E1001', '张晨', 1, 'full_time', '2022-04-01', '13800000001', 'zhangchen@example.com', 'active'),
  ('E1002', '李娜', 1, 'full_time', '2021-09-15', '13800000002', 'lina@example.com', 'active'),
  ('E2001', '王磊', 2, 'full_time', '2020-03-20', '13800000003', 'wanglei@example.com', 'active'),
  ('E3001', '赵敏', 3, 'full_time', '2019-11-05', '13800000004', 'zhaomin@example.com', 'active')
ON CONFLICT(EmployeeNo) DO NOTHING;

INSERT INTO Shift (ShiftCode, ShiftName, StartTime, EndTime, BreakMinutes, Status) VALUES
  ('A', '早班', '09:00', '18:00', 60, 'active'),
  ('B', '晚班', '13:00', '22:00', 60, 'active'),
  ('FLEX', '弹性班', '10:00', '19:00', 60, 'active')
ON CONFLICT(ShiftCode) DO NOTHING;

INSERT INTO AttendanceRecord (EmployeeId, WorkDate, ShiftId, CheckInTime, CheckOutTime, AttendanceStatus, OvertimeHours, Source, Remark) VALUES
  (1, date('now','localtime'), 1, datetime('now','localtime','-8 hours'), datetime('now','localtime'), 'present', 0.0, 'terminal', '正常出勤'),
  (2, date('now','localtime'), 1, datetime('now','localtime','-7 hours','-40 minutes'), datetime('now','localtime'), 'late', 1.0, 'mobile', '上午堵车迟到'),
  (3, date('now','localtime'), 3, datetime('now','localtime','-8 hours'), datetime('now','localtime'), 'present', 2.0, 'web', '项目上线支持')
;

INSERT INTO LeaveRequest (EmployeeId, LeaveType, StartDate, EndDate, Days, Reason, Status, Approver, ApprovedAt, AppliedAt) VALUES
  (4, 'annual', date('now','localtime','+2 day'), date('now','localtime','+4 day'), 3.0, '家庭事务', 'pending', '', '', datetime('now','localtime','-1 day')),
  (2, 'sick', date('now','localtime','-3 day'), date('now','localtime','-2 day'), 2.0, '感冒发烧', 'approved', '王经理', datetime('now','localtime','-3 day'), datetime('now','localtime','-4 day'))
;

INSERT INTO OvertimeRequest (EmployeeId, WorkDate, StartTime, EndTime, Hours, Reason, Status, Approver, ApprovedAt) VALUES
  (1, date('now','localtime','+1 day'), datetime('now','localtime','+1 day','18 hours'), datetime('now','localtime','+1 day','21 hours'), 3.0, '版本发布准备', 'pending', '', ''),
  (3, date('now','localtime','-1 day'), datetime('now','localtime','-1 day','18 hours'), datetime('now','localtime','-1 day','20 hours'), 2.0, '客户演示材料整理', 'approved', '李总监', datetime('now','localtime'))
;
