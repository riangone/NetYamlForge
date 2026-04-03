#!/usr/bin/env python3
"""
save_last_response.py - 将 AI 助手的最后回复保存到 system.db 的 AIChatHistory 表

用法:
  python3 scripts/save_last_response.py "回复内容"
  echo "回复内容" | python3 scripts/save_last_response.py --stdin
  python3 scripts/save_last_response.py --file /path/to/response.md

可选参数:
  --user-id     用户ID (默认: qwen-assistant)
  --provider    提供商 (默认: qwen)
  --context     聊天上下文 (默认: framework)
  --stdin       从标准输入读取
  --file        从文件读取
"""

import sqlite3
import sys
import os
import argparse
from datetime import datetime

DB_PATH = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), 
                       "NetYamlForge", "system.db")

def save_response(content: str, user_id: str = "qwen-assistant", 
                  provider: str = "qwen", context: str = "framework") -> int:
    """保存回复到 AIChatHistory 表"""
    if not os.path.exists(DB_PATH):
        print(f"❌ 数据库文件不存在: {DB_PATH}")
        sys.exit(1)
    
    conn = sqlite3.connect(DB_PATH)
    cur = conn.cursor()
    
    cur.execute(
        "INSERT INTO AIChatHistory (UserId, Content, Type, Provider, ChatContext, CreatedAt) "
        "VALUES (?, ?, 'assistant', ?, ?, ?)",
        (user_id, content, provider, context, datetime.now().strftime("%Y-%m-%d %H:%M:%S"))
    )
    conn.commit()
    row_id = cur.lastrowid
    conn.close()
    
    print(f"✅ 已保存到 AIChatHistory: ID={row_id}, 长度={len(content)}字节")
    return row_id

def main():
    parser = argparse.ArgumentParser(description="保存 AI 助手回复到聊天历史数据库")
    parser.add_argument("content", nargs="?", help="回复内容")
    parser.add_argument("--stdin", action="store_true", help="从标准输入读取")
    parser.add_argument("--file", type=str, help="从文件读取")
    parser.add_argument("--user-id", default="qwen-assistant", help="用户ID")
    parser.add_argument("--provider", default="qwen", help="提供商")
    parser.add_argument("--context", default="framework", help="聊天上下文")
    
    args = parser.parse_args()
    
    if args.stdin:
        content = sys.stdin.read()
    elif args.file:
        with open(args.file, 'r', encoding='utf-8') as f:
            content = f.read()
    elif args.content:
        content = args.content
    else:
        parser.print_help()
        sys.exit(1)
    
    if not content.strip():
        print("❌ 内容为空")
        sys.exit(1)
    
    save_response(content.strip(), args.user_id, args.provider, args.context)

if __name__ == "__main__":
    main()
