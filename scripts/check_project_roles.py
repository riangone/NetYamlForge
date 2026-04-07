#!/usr/bin/env python3
"""
检查并修复 system.db 中的项目注册和 admin 用户权限
"""

import sqlite3
import sys
import os
from pathlib import Path

DB_PATH = Path("/home/ubuntu/ws/NetYamlForge/system.db")
PROJECTS_DIR = Path("/home/ubuntu/ws/NetYamlForge/NetYamlForge/projects")

def get_connection():
    conn = sqlite3.connect(DB_PATH)
    conn.row_factory = sqlite3.Row
    return conn

def check_projects_in_db():
    """检查数据库中注册的项目"""
    conn = get_connection()
    cursor = conn.cursor()
    
    print("=" * 60)
    print("📊 数据库中注册的项目列表:")
    print("=" * 60)
    
    cursor.execute("SELECT name, display_name, created_at FROM projects ORDER BY name")
    db_projects = {row['name']: row for row in cursor.fetchall()}
    
    for name, project in db_projects.items():
        print(f"  ✅ {name} - {project['display_name']}")
    
    conn.close()
    return db_projects

def check_file_system_projects():
    """检查文件系统上的项目目录"""
    print("\n" + "=" * 60)
    print("📁 文件系统上的项目目录:")
    print("=" * 60)
    
    fs_projects = {}
    if PROJECTS_DIR.exists():
        for proj_dir in PROJECTS_DIR.iterdir():
            if proj_dir.is_dir():
                project_yaml = proj_dir / "project.yaml"
                if project_yaml.exists():
                    # 读取 displayName
                    import yaml
                    with open(project_yaml, 'r', encoding='utf-8') as f:
                        config = yaml.safe_load(f)
                        display_name = config.get('displayName', proj_dir.name)
                        fs_projects[proj_dir.name] = display_name
                        print(f"  ✅ {proj_dir.name} - {display_name}")
                else:
                    print(f"  ⚠️  {proj_dir.name} (缺少 project.yaml)")
    
    return fs_projects

def check_admin_roles():
    """检查 admin 用户的项目角色"""
    conn = get_connection()
    cursor = conn.cursor()
    
    print("\n" + "=" * 60)
    print("👤 admin 用户的项目角色:")
    print("=" * 60)
    
    cursor.execute("""
        SELECT 
            u.user_name,
            u.display_name,
            pr.project_name,
            pr.role_name
        FROM app_user u
        LEFT JOIN app_user_project_role pr ON u.id = pr.user_id
        WHERE u.user_name = 'admin'
        ORDER BY pr.project_name
    """)
    
    rows = cursor.fetchall()
    if not rows:
        print("  ❌ admin 用户不存在")
    else:
        for row in rows:
            print(f"  ✅ {row['project_name']} - {row['role_name']}")
    
    conn.close()
    return rows

def sync_missing_projects():
    """同步缺失的项目并给 admin 分配角色"""
    conn = get_connection()
    cursor = conn.cursor()
    
    # 获取文件系统上的项目
    import yaml
    fs_projects = {}
    for proj_dir in PROJECTS_DIR.iterdir():
        if proj_dir.is_dir():
            project_yaml = proj_dir / "project.yaml"
            if project_yaml.exists():
                with open(project_yaml, 'r', encoding='utf-8') as f:
                    config = yaml.safe_load(f)
                    fs_projects[config['name']] = config.get('displayName', config['name'])
    
    # 获取数据库中已有的项目
    cursor.execute("SELECT name FROM projects")
    db_projects = {row['name'] for row in cursor.fetchall()}
    
    # 同步缺失的项目
    from datetime import datetime
    now = datetime.utcnow().isoformat()
    
    synced = []
    for name, display_name in fs_projects.items():
        if name not in db_projects:
            cursor.execute("""
                INSERT OR REPLACE INTO projects (name, display_name, description, created_at, updated_at)
                VALUES (?, ?, '', ?, ?)
            """, (name, display_name, now, now))
            synced.append(name)
            print(f"  ✅ 注册项目: {name} - {display_name}")
    
    if synced:
        conn.commit()
        print(f"\n  📝 已同步 {len(synced)} 个项目到数据库")
    else:
        print("\n  ℹ️  所有项目已注册")
    
    # 给 admin 分配所有项目的 Admin 角色
    cursor.execute("SELECT id FROM app_user WHERE user_name = 'admin'")
    admin_row = cursor.fetchone()
    if admin_row:
        admin_id = admin_row['id']
        
        cursor.execute("SELECT name FROM projects")
        all_projects = [row['name'] for row in cursor.fetchall()]
        
        assigned = []
        for project_name in all_projects:
            cursor.execute("""
                SELECT COUNT(*) as cnt FROM app_user_project_role 
                WHERE user_id = ? AND project_name = ?
            """, (admin_id, project_name))
            
            if cursor.fetchone()['cnt'] == 0:
                cursor.execute("""
                    INSERT INTO app_user_project_role (user_id, project_name, role_name, created_at)
                    VALUES (?, ?, 'Admin', ?)
                """, (admin_id, project_name, now))
                assigned.append(project_name)
        
        if assigned:
            conn.commit()
            print(f"  ✅ 已为 admin 分配 {len(assigned)} 个项目的 Admin 角色: {', '.join(assigned)}")
        else:
            print("  ℹ️  admin 已拥有所有项目的 Admin 角色")
    
    conn.close()
    return True

def main():
    print("\n🔍 NetYamlForge 项目权限诊断工具")
    print("=" * 60)
    
    if not DB_PATH.exists():
        print(f"\n❌ system.db 不存在: {DB_PATH}")
        print("请先启动应用以初始化数据库")
        sys.exit(1)
    
    # 检查当前状态
    db_projects = check_projects_in_db()
    fs_projects = check_file_system_projects()
    admin_roles = check_admin_roles()
    
    # 找出差异
    missing_projects = set(fs_projects.keys()) - set(db_projects.keys())
    
    if missing_projects:
        print(f"\n⚠️  发现 {len(missing_projects)} 个项目未注册到数据库:")
        for name in sorted(missing_projects):
            print(f"  ❌ {name}")
        
        print(f"\n🔧 正在同步项目并修复 admin 权限...")
        sync_missing_projects()
        
        # 重新检查
        print("\n" + "=" * 60)
        print("🔄 修复后状态:")
        print("=" * 60)
        check_projects_in_db()
        check_admin_roles()
    else:
        print("\n✅ 所有项目已注册，admin 权限正常")
    
    print("\n" + "=" * 60)
    print("💡 提示:")
    print("  - 如果应用正在运行，请重启以加载新项目")
    print("  - admin 密码默认: Admin123!")
    print("=" * 60)

if __name__ == "__main__":
    main()
