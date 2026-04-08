#!/usr/bin/env python3
"""
migrate_chat_history.py - 将 system.db 中的项目聊天历史记录迁移到正确的项目数据库

此脚本会：
1. 读取 system.db 中 ChatContext 不是 'framework' 的记录
2. 根据 ChatContext 创建对应项目的 chat.db
3. 将记录迁移到正确的项目数据库
4. 从 system.db 中删除已迁移的记录

用法:
  python3 scripts/migrate_chat_history.py [--dry-run]

参数:
  --dry-run    仅显示迁移计划，不实际执行
"""

import sqlite3
import os
import sys
import argparse
from datetime import datetime

BASE_DIR = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SYSTEM_DB_PATH = os.path.join(BASE_DIR, "NetYamlForge", "system.db")
PROJECTS_DIR = os.path.join(BASE_DIR, "NetYamlForge", "projects")

def get_project_db_path(project_name):
    """获取项目数据库路径"""
    project_dir = os.path.join(PROJECTS_DIR, project_name)
    return os.path.join(project_dir, "chat.db")

def init_project_db(db_path):
    """初始化项目数据库的 schema"""
    os.makedirs(os.path.dirname(db_path), exist_ok=True)
    
    conn = sqlite3.connect(db_path)
    conn.executescript("""
CREATE TABLE IF NOT EXISTS AIChatHistory (
    Id          INTEGER PRIMARY KEY AUTOINCREMENT,
    UserId      TEXT NOT NULL,
    Content     TEXT NOT NULL,
    Type        TEXT NOT NULL,
    Provider    TEXT,
    ChatContext TEXT NOT NULL DEFAULT 'framework',
    CreatedAt   TEXT NOT NULL
);
CREATE INDEX IF NOT EXISTS idx_aichat_user ON AIChatHistory(UserId, Id);
CREATE INDEX IF NOT EXISTS idx_aichat_context ON AIChatHistory(UserId, ChatContext, Id);

CREATE TABLE IF NOT EXISTS AICommandLog (
    Id          INTEGER PRIMARY KEY AUTOINCREMENT,
    UserId      TEXT NOT NULL,
    TaskId      TEXT NOT NULL UNIQUE,
    CliTool     TEXT NOT NULL,
    InputText   TEXT NOT NULL,
    ProjectName TEXT,
    SessionId   TEXT,
    Status      TEXT NOT NULL DEFAULT 'Pending',
    ResultText  TEXT,
    ErrorText  TEXT,
    DurationMs  INTEGER,
    CreatedAt   TEXT NOT NULL,
    CompletedAt TEXT
);
CREATE INDEX IF NOT EXISTS idx_aicommand_user ON AICommandLog(UserId, Id);
CREATE INDEX IF NOT EXISTS idx_aicommand_task ON AICommandLog(TaskId);
""")
    return conn

def main():
    parser = argparse.ArgumentParser(description="迁移项目聊天历史记录到正确的数据库")
    parser.add_argument("--dry-run", action="store_true", help="仅显示迁移计划")
    args = parser.parse_args()

    if not os.path.exists(SYSTEM_DB_PATH):
        print(f"❌ 系统数据库不存在: {SYSTEM_DB_PATH}")
        sys.exit(1)

    # 连接系统数据库
    sys_conn = sqlite3.connect(SYSTEM_DB_PATH)
    sys_cursor = sys_conn.cursor()

    # 查找所有非 framework 上下文的记录
    sys_cursor.execute("""
        SELECT DISTINCT ChatContext, COUNT(*) as cnt
        FROM AIChatHistory
        WHERE ChatContext != 'framework'
        GROUP BY ChatContext
    """)
    
    contexts = sys_cursor.fetchall()
    
    if not contexts:
        print("✅ 没有需要迁移的记录")
        sys_conn.close()
        return

    print(f"📊 发现 {len(contexts)} 个项目需要迁移:\n")
    
    for context, count in contexts:
        print(f"  - {context}: {count} 条记录")
    
    print()

    if args.dry_run:
        print("🔍 Dry-run 模式：不执行实际迁移")
        sys_conn.close()
        return

    # 执行迁移
    migrated_total = 0
    
    for context, _ in contexts:
        # ChatContext 可能是项目名称，也可能是项目内的子上下文（如 auto-dealer-demo/dealer-staff）
        # 格式可能是 "project" 或 "project-subcontext"
        # 我们需要找到对应的项目目录
        
        # 尝试匹配项目目录
        project_name = None
        if os.path.exists(os.path.join(PROJECTS_DIR, context)):
            # 精确匹配
            project_name = context
        else:
            # 尝试匹配前缀（如 auto-dealer-demo/dealer-staff → auto-dealer-demo）
            for proj_dir in os.listdir(PROJECTS_DIR):
                if context.startswith(proj_dir):
                    project_name = proj_dir
                    break
        
        if project_name is None:
            print(f"  ⚠️  警告：找不到项目 {context}，跳过")
            continue
        
        print(f"📦 迁移项目: {project_name} (context: {context})")
        
        # 获取该项目的所有记录
        sys_cursor.execute("""
            SELECT Id, UserId, Content, Type, Provider, ChatContext, CreatedAt
            FROM AIChatHistory
            WHERE ChatContext = ?
            ORDER BY Id
        """, (project_name,))
        
        records = sys_cursor.fetchall()
        
        if not records:
            continue
        
        # 初始化项目数据库
        project_db_path = get_project_db_path(project_name)
        project_conn = init_project_db(project_db_path)
        
        # 插入记录到项目数据库
        project_conn.executemany("""
            INSERT INTO AIChatHistory (UserId, Content, Type, Provider, ChatContext, CreatedAt)
            VALUES (?, ?, ?, ?, ?, ?)
        """, [(r[1], r[2], r[3], r[4], r[5], r[6]) for r in records])
        
        project_conn.commit()
        
        # 从系统数据库删除已迁移的记录
        sys_cursor.execute("""
            DELETE FROM AIChatHistory
            WHERE ChatContext = ?
        """, (project_name,))
        
        sys_conn.commit()
        project_conn.close()
        
        print(f"  ✅ 已迁移 {len(records)} 条记录 → {project_db_path}")
        migrated_total += len(records)

    sys_conn.close()
    
    print(f"\n✅ 迁移完成！共迁移 {migrated_total} 条记录")
    print("💡 提示：请检查项目数据库中的记录是否正确")

if __name__ == "__main__":
    main()
