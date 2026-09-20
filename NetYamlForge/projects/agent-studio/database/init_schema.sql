CREATE TABLE IF NOT EXISTS agent_task (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    title TEXT NOT NULL,
    description TEXT,
    status TEXT DEFAULT 'Draft',
    architect_cli TEXT,
    developer_cli TEXT,
    tester_cli TEXT,
    architect_output TEXT,
    developer_output TEXT,
    tester_output TEXT,
    created_at TEXT,
    updated_at TEXT
);

CREATE TABLE IF NOT EXISTS agent_task_log (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    task_id INTEGER,
    stage TEXT,
    role TEXT,
    provider TEXT,
    success INTEGER DEFAULT 1,
    log_content TEXT,
    error_message TEXT,
    created_at TEXT
);
