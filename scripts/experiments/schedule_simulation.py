#!/usr/bin/env python3
# -*- coding: utf-8 -*-

import os
import sys
import time
import random
import subprocess
import json
from datetime import datetime

PLAYBOOK_DIR = "/home/ubuntu/ws/NetYamlForge"
RUNNER_PATH = os.path.join(PLAYBOOK_DIR, "scripts/experiments/run_simulation_step.py")
FINDINGS_FILE = os.path.join(PLAYBOOK_DIR, "logs/experiments/findings.jsonl")

# 模拟动作库
PERSONA_ACTIONS = {
    "pm": [
        # Action 1: 获取任务列表
        {"method": "GET", "path": "/api/task-management/task", "data": None},
        # Action 2: 创建新任务 (正常日期)
        {"method": "POST", "path": "/api/task-management/task", "data": lambda: {
            "Title": f"sim: PM新任务-{random.randint(100, 999)}",
            "AssignedTo": random.choice(["taskmgr_worker1", "taskmgr_worker2"]),
            "DueDate": datetime.now().strftime("%Y-%m-%d"),
            "Priority": random.choice(["high", "medium", "low"]),
            "Status": "not_started"
        }},
        # Action 3: 重排优先级（更新操作，模拟更新 ID 1）
        {"method": "PUT", "path": "/api/task-management/task/1", "data": lambda: {
            "Title": "sim: 优先重排任务",
            "AssignedTo": "taskmgr_worker1",
            "DueDate": "2026-07-25",
            "Priority": "high",
            "Status": "not_started"
        }},
        # Action 4: 创建任务 (过去日期 - 预期触发 validate_due_date 拦截)
        {"method": "POST", "path": "/api/task-management/task", "data": {
            "Title": "sim: 过去任务(应被拦截)",
            "AssignedTo": "taskmgr_worker1",
            "DueDate": "2020-01-01",
            "Priority": "low",
            "Status": "not_started"
        }},
        # Action 5: 发表任务评论
        {"method": "POST", "path": "/api/task-management/comment", "data": lambda: {
            "TaskId": random.randint(1, 10),
            "CommentText": f"sim: PM在 {datetime.now().strftime('%H:%M:%S')} 发表的评论建议。"
        }}
    ],
    "dev": [
        # Action 1: 获取分配给我的任务
        {"method": "GET", "path": "/api/task-management/task?filters[AssignedTo]=taskmgr_worker1", "data": None},
        # Action 2: 更新任务进度 (模拟更新 ID 2)
        {"method": "PUT", "path": "/api/task-management/task/2", "data": lambda: {
            "Title": "sim: 开发进度更新",
            "AssignedTo": "taskmgr_worker1",
            "DueDate": "2026-07-15",
            "Priority": "medium",
            "Status": "in_progress",
            "Notes": f"进度随机更新，测试时间：{datetime.now().strftime('%H:%M:%S')}"
        }},
        # Action 3: 触发 mark_completed 动作 (正常更新状态)
        {"method": "POST", "path": "/api/task-management/task/2/actions/mark_completed", "data": None},
        # Action 4: 触发 reopen 动作 (异常: 缺 Reason 输入 - 预期被拦截并返回 400)
        {"method": "POST", "path": "/api/task-management/task/2/actions/reopen", "data": {}},
        # Action 5: 触发 reopen 动作 (正常: 提供 Reason - 预期成功)
        {"method": "POST", "path": "/api/task-management/task/2/actions/reopen", "data": {
            "Reason": "Need further QA verification"
        }},
        # Action 6: 发表任务评论
        {"method": "POST", "path": "/api/task-management/comment", "data": lambda: {
            "TaskId": random.randint(1, 10),
            "CommentText": f"sim: Dev反馈进度良好，时间：{datetime.now().strftime('%H:%M:%S')}。"
        }}
    ],
    "obs": [
        # Action 1: 浏览评论列表
        {"method": "GET", "path": "/api/task-management/comment", "data": None},
        # Action 2: 越权写尝试 (POST) - 预期被拦并返 403
        {"method": "POST", "path": "/api/task-management/task", "data": {
            "Title": "sim: obs越权写尝试",
            "AssignedTo": "taskmgr_worker2",
            "DueDate": "2026-07-30",
            "Priority": "low",
            "Status": "not_started"
        }},
        # Action 3: 越权修改尝试 (PUT) - 预期被拦并返 403
        {"method": "PUT", "path": "/api/task-management/task/1", "data": {
            "Title": "sim: obs越权修改尝试",
            "AssignedTo": "taskmgr_worker2",
            "DueDate": "2026-07-30",
            "Priority": "low",
            "Status": "not_started"
        }},
        # Action 4: 越权执行 action 尝试 - 预期被拦并返 403
        {"method": "POST", "path": "/api/task-management/task/1/actions/mark_completed", "data": None},
        # Action 5: 越权写评论尝试 (POST) - 预期被拦并返 403
        {"method": "POST", "path": "/api/task-management/comment", "data": {
            "TaskId": 1,
            "CommentText": "sim: obs越权评论尝试"
        }}
    ]
}

def run_step(user, method, path, data=None):
    cmd = [sys.executable, RUNNER_PATH, "--user", user, "--method", method, "--path", path]
    
    # 解析动态生成的 data payload
    payload = data() if callable(data) else data
    if payload:
        cmd += ["--data", json.dumps(payload)]
        
    print(f"\n[Scheduler] [{datetime.now().strftime('%Y-%m-%d %H:%M:%S')}] Triggering Action for {user.upper()}: {method} {path}")
    
    result = subprocess.run(cmd, capture_output=True, text=True, cwd=PLAYBOOK_DIR)
    print(result.stdout)
    
    if result.returncode == 2:
        # 不变量违规
        print(f"[ALERT] Invariant violation detected for user {user}!")
        log_finding(user, method, path, payload, result.stdout)
    elif result.returncode != 0:
        print(f"[ERROR] Process exited with code {result.returncode}. Stderr: {result.stderr}")

def log_finding(user, method, path, payload, output):
    os.makedirs(os.path.dirname(FINDINGS_FILE), exist_ok=True)
    finding = {
        "timestamp": datetime.now().isoformat(),
        "user": user,
        "method": method,
        "path": path,
        "payload": payload,
        "runner_output": output
    }
    with open(FINDINGS_FILE, "a", encoding="utf-8") as f:
        f.write(json.dumps(finding, ensure_ascii=False) + "\n")
    print(f"[Scheduler] Finding logged to {FINDINGS_FILE}")

def main():
    print("====================================================")
    print("  NetYamlForge AI User Simulation Scheduler Running  ")
    print("====================================================")
    
    # 默认模式：每步间隔 2 到 8 秒的 jitter 随机触发
    # 可通过 CTRL+C 终止
    try:
        while True:
            # 随机挑选角色
            user = random.choice(["pm", "dev", "obs"])
            # 随机挑选角色下的一个动作
            action = random.choice(PERSONA_ACTIONS[user])
            
            run_step(user, action["method"], action["path"], action["data"])
            
            # Jitter 延时，模拟人类操作随机停顿
            sleep_time = random.uniform(2.0, 8.0)
            print(f"[Scheduler] Sleeping for {sleep_time:.2f} seconds...")
            time.sleep(sleep_time)
            
    except KeyboardInterrupt:
        print("\n[Scheduler] Simulation stopped by user.")

if __name__ == "__main__":
    main()
