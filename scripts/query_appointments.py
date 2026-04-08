#!/usr/bin/env python3
"""
query_appointments.py - 查询预约数据

用法:
  python3 scripts/query_appointments.py
"""

import sqlite3
import os
import json
from datetime import datetime

# 数据库路径
DB_PATH = "/home/ubuntu/ws/NetYamlForge/NetYamlForge/projects/auto-dealer-demo/database/auto-dealer-demo.db"

def query_appointments():
    """查询预约数据"""
    if not os.path.exists(DB_PATH):
        print(f"❌ 数据库文件不存在: {DB_PATH}")
        # 尝试查找其他可能的数据库文件
        data_dir = "/home/ubuntu/ws/NetYamlForge/data"
        if os.path.exists(data_dir):
            files = os.listdir(data_dir)
            print(f"📁 data 目录中的文件: {files}")
        return

    conn = sqlite3.connect(DB_PATH)
    conn.row_factory = sqlite3.Row
    cur = conn.cursor()

    try:
        # 查询所有预约
        sql = """
        SELECT 
            sa.appointment_id as id,
            sa.customer_id,
            c.name as customer_name,
            sa.appointment_type,
            sa.preferred_date,
            sa.status,
            sa.customer_request as notes,
            sa.created_at
        FROM service_appointments sa
        LEFT JOIN customers c ON sa.customer_id = c.customer_id
        ORDER BY sa.preferred_date DESC
        LIMIT 20
        """
        
        cur.execute(sql)
        rows = cur.fetchall()
        
        if not rows:
            print("📭 暂无预约记录")
            return
        
        print(f"## 预约一覧\n")
        print(f"該当件数：**{len(rows)} 件**\n")
        
        # 按状态分类
        status_counts = {}
        for row in rows:
            status = row['status'] or 'unknown'
            status_counts[status] = status_counts.get(status, 0) + 1
        
        print("### 📊 統計\n")
        status_labels = {
            'pending': '🟡 未確認',
            'confirmed': '🟢 確定',
            'completed': '🔵 完了',
            'cancelled': '🔴 キャンセル',
            'unknown': '⚪ 不明'
        }
        
        for status, count in status_counts.items():
            label = status_labels.get(status, status)
            print(f"- **{label}**: {count} 件")
        
        print(f"\n### 📋 詳細リスト\n")
        
        # 预约类型标签
        type_labels = {
            'test_drive': '試乗',
            'service': '整備',
            'consultation': '相談'
        }
        
        # 状态标签
        status_labels_detail = {
            'pending': '未確認',
            'confirmed': '確定',
            'completed': '完了',
            'cancelled': 'キャンセル'
        }
        
        for row in rows:
            customer_name = row['customer_name'] or f"顧客ID:{row['customer_id']}"
            appt_type = type_labels.get(row['appointment_type'], row['appointment_type'])
            status = status_labels_detail.get(row['status'], row['status'])
            preferred_date = row['preferred_date'] or '未設定'
            notes = row['notes'] or ''
            
            print(f"- **{customer_name}** ({appt_type} | {status}) — 希望日：{preferred_date} — [詳細を見る](/auto-dealer-demo/DynamicEntity/DetailPage?entity=service_appointments&id={row['id']})")
            if notes:
                print(f"  - 備考：{notes}")
        
        print(f"\n💡 洞察")
        print(f"- 未確認の予約が {status_counts.get('pending', 0)} 件あります")
        print(f"- 本日以降の予約を確認してください")
        
        print(f"\n📋 推奨アクション")
        print(f"1. **未確認予約の確認連絡** — 顧客に確定連絡を行う")
        print(f"2. **本日の予約準備** — 試乗車両・担当者の準備を確認")
        
    except sqlite3.OperationalError as e:
        print(f"❌ データベースエラー: {e}")
        print("📋 テーブル構造を確認中...")
        
        # 尝试列出所有表
        cur.execute("SELECT name FROM sqlite_master WHERE type='table'")
        tables = cur.fetchall()
        print(f"📁 利用可能なテーブル: {[t['name'] for t in tables]}")
        
    finally:
        conn.close()

if __name__ == "__main__":
    query_appointments()
