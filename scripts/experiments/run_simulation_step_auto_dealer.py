#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
AI User Simulation - auto-dealer-demo (汽车销售子项目) 版本。

复用 task-management 实验（scripts/experiments/run_simulation_step.py）验证过的框架：
固定 Persona API Token + JSONL 审计日志 + 不变量检查器，但 Persona 心智与不变量
全部按 auto-dealer-demo 的实体/角色重新设计（不是简单复制 task-management 的规则）。

Persona:
  mgr  -> yamada (sales_manager)  可审批 AI 决定，拥有跨线索/报价的读写权限
  rep  -> suzuki (sales_rep)      可读写自己的线索/试乘，但不应能审批 AI 决定
  op   -> takahashi (operator)    仅服务预约/工单相关操作
  cust -> sato (customer, CUST001) 仅应读取与自己相关的数据，不能写业务实体，
                                    也不能看到其他顾客 (如 CUST002/伊藤) 的数据

运行前置条件: scripts/experiments/setup-simulation-users-auto-dealer.sql 已执行
（为 yamada/suzuki/takahashi/sato 写入固定 api_token，并修正 project_role 使其
与 employees.role 一致）。

Usage:
    API_BASE_URL=http://localhost:5001 python3 run_simulation_step_auto_dealer.py \\
        --user rep --method POST --path /nyf/api/auto-dealer-demo/sales_leads --data '{...}'
"""

import os
import sys
import json
import urllib.request
import urllib.error
from datetime import datetime
import argparse

API_BASE_URL = os.environ.get("API_BASE_URL", "http://localhost:5001")
LOG_DIR = "/home/ubuntu/ws/NetYamlForge/logs/experiments"
LOG_FILE = os.path.join(LOG_DIR, "simulation_auto_dealer.jsonl")

# 角色令牌映射（由 setup-simulation-users-auto-dealer.sql 写入 app_user.api_token）
USER_TOKENS = {
    "mgr": ("token_ad_mgr_user", "yamada"),
    "rep": ("token_ad_rep_user", "suzuki"),
    "op": ("token_ad_op_user", "takahashi"),
    "cust": ("token_ad_cust_user", "sato"),
}

# 该角色在业务上被允许"拥有"的客户 ID / 员工 ID，用于越权检测
OWN_CUSTOMER_ID = {"cust": "CUST001"}
FOREIGN_CUSTOMER_IDS = ["CUST002", "CUST-001"]  # ito, customer1 —— sato 不应看到这些人的数据

# 仅 sales_manager / executive 应该能审批/驳回 AI 决定（对齐 project.yaml 中
# AiDecisionApproval 页面的 roles: [executive, sales_manager] 声明）
AI_DECISION_APPROVAL_ROLES = {"mgr"}

# customer 角色不应该对任何业务实体进行写操作（顾客侧交互应走专门的
# CustomerAppointments/CustomerAiChat 等受限入口，而不是直接写主实体表）
CUSTOMER_WRITE_METHODS = {"POST", "PUT", "PATCH", "DELETE"}


def setup_logging():
    if not os.path.exists(LOG_DIR):
        os.makedirs(LOG_DIR, exist_ok=True)


def send_request(user_key, method, path, data=None):
    if user_key not in USER_TOKENS:
        print(f"Error: Unknown user '{user_key}'. Choose from: {', '.join(USER_TOKENS)}", file=sys.stderr)
        sys.exit(1)

    token, username = USER_TOKENS[user_key]
    url = f"{API_BASE_URL}{path}"

    headers = {
        "Authorization": f"Bearer {token}",
        "Content-Type": "application/json",
    }

    req_body = None
    if data:
        req_body = data.encode("utf-8") if isinstance(data, str) else json.dumps(data).encode("utf-8")

    req = urllib.request.Request(url, data=req_body, headers=headers, method=method.upper())

    start_time = datetime.now()
    response_data = None
    status_code = None
    error_msg = None

    try:
        with urllib.request.urlopen(req, timeout=10) as response:
            status_code = response.status
            response_data = response.read().decode("utf-8")
    except urllib.error.HTTPError as e:
        status_code = e.code
        response_data = e.read().decode("utf-8")
        error_msg = str(e)
    except Exception as e:
        status_code = 500
        error_msg = str(e)

    duration_ms = int((datetime.now() - start_time).total_seconds() * 1000)

    parsed_response = None
    if response_data:
        try:
            parsed_response = json.loads(response_data)
        except json.JSONDecodeError:
            parsed_response = response_data

    return {
        "timestamp": datetime.now().isoformat(),
        "user": username,
        "role": user_key,
        "method": method,
        "path": path,
        "request_payload": data,
        "status_code": status_code,
        "response_payload": parsed_response,
        "duration_ms": duration_ms,
        "error": error_msg,
    }


def _extract_data_item(res_payload):
    if isinstance(res_payload, dict):
        if isinstance(res_payload.get("data"), dict):
            return res_payload["data"]
        if "UpdatedAt" in res_payload or "updated_at" in res_payload:
            return res_payload
    return None


def _payload_mentions_customer(payload, customer_id):
    """粗略检测响应体（字符串化）中是否出现了指定 customer_id，用于越权/串号检测。"""
    try:
        blob = json.dumps(payload, ensure_ascii=False)
    except Exception:
        blob = str(payload)
    return customer_id in blob


def check_invariants(log_entry):
    """
    针对 auto-dealer-demo 的不变量检查。每条规则都对应一个真实的业务/权限假设，
    而不是从 task-management 照搬——具体见各规则前的注释。
    """
    findings = []
    role = log_entry["role"]
    method = log_entry["method"]
    path = log_entry["path"]
    status_code = log_entry["status_code"]
    res_payload = log_entry["response_payload"]
    req_payload = log_entry["request_payload"]

    req_dict = {}
    if req_payload:
        try:
            req_dict = json.loads(req_payload) if isinstance(req_payload, str) else req_payload
        except Exception:
            pass

    # 1. 协议层：任何角色都不应该触发 5xx
    if status_code and status_code >= 500:
        findings.append({
            "type": "protocol_error",
            "message": f"Server returned 5xx status code ({status_code}). Detail: {log_entry['error']}",
        })

    # 2. AI 决定审批越权 (AI_DECISION_APPROVAL_BYPASS)
    # 依据：project.yaml 中 AiDecisionApproval 页面 roles: [executive, sales_manager]，
    # 但 ai_decisions 实体本身声明 isPublic: true，且 ApproveAiDecisionHandler /
    # RejectAiDecisionHandler（Hooks/AiEngineHooks.cs）在执行时不做任何调用者角色校验。
    # 因此存在“菜单层隐藏、但 API 层未拦截”的经典越权风险 —— 与 task-management
    # 实验中发现的 AUTH_LEAK 是同一类问题，这里专门为 ai_decisions 单独建规则。
    if "ai_decisions" in path and "actions/" in path and method == "POST":
        action = path.rstrip("/").split("actions/")[-1]
        if action in ("approve", "reject") and role not in AI_DECISION_APPROVAL_ROLES:
            if status_code in (200, 201):
                findings.append({
                    "type": "auth_leak_ai_decision_approval",
                    "message": (
                        f"Role '{role}' ({log_entry['user']}) was able to {action} an AI decision "
                        f"via API (status {status_code}), but only sales_manager/executive should be "
                        "allowed per project.yaml menu role gating. Menu-level role hiding is not "
                        "enforced at the API/action-handler level."
                    ),
                })

    # 3. 顾客写操作越权 (AUTH_LEAK_CUSTOMER_WRITE)
    # 依据：customer 角色的 UI 入口（CustomerDashboard/CustomerAppointments/CustomerAiChat）
    # 都是只读展示或走专用受限操作，顾客不应能直接对主业务实体（sales_leads/vehicles/
    # employees/sales_quotas/ai_decisions/test_drives 等）做写操作。
    if role == "cust" and method in CUSTOMER_WRITE_METHODS:
        if status_code in (200, 201, 204):
            findings.append({
                "type": "auth_leak_customer_write",
                "message": (
                    f"Customer role ({log_entry['user']}) successfully performed a write operation "
                    f"({method} {path}) with status {status_code}. Expected 403/401."
                ),
            })

    # 4. 跨顾客数据越权 (CROSS_CUSTOMER_LEAK)
    # 依据：sato 只应该看到与 CUST001 相关的记录；如果响应体里出现了别的顾客
    # (CUST002=伊藤 / CUST-001=customer1) 的 customer_id，说明查询没有按当前登录
    # 顾客做行级过滤。
    if role in OWN_CUSTOMER_ID and method == "GET" and status_code == 200:
        for foreign_id in FOREIGN_CUSTOMER_IDS:
            if _payload_mentions_customer(res_payload, foreign_id):
                findings.append({
                    "type": "cross_customer_leak",
                    "message": (
                        f"Customer '{log_entry['user']}' (owns {OWN_CUSTOMER_ID[role]}) received data "
                        f"referencing another customer_id '{foreign_id}' from {path}. Row-level "
                        "scoping by logged-in customer may be missing."
                    ),
                })

    # 5. 更新时间戳与审计日志 (AUDIT_MISSING_UPDATED_AT)
    # 与 task-management 实验一致：写操作成功后 UpdatedAt/updated_at 不应为空。
    if method in ("PUT", "PATCH") or ("actions" in path and status_code in (200, 201)):
        data_item = _extract_data_item(res_payload)
        if data_item:
            updated_at = data_item.get("UpdatedAt") or data_item.get("updated_at")
            if not updated_at:
                findings.append({
                    "type": "audit_missing_updated_at",
                    "message": "Update/action completed successfully but UpdatedAt/updated_at is null/empty.",
                })

    # 6. 试乘重复预约 (TEST_DRIVE_DOUBLE_BOOKING) —— 需要结合前一次请求判断，
    # 单次请求无法独立判定，这里仅在 scheduled_at 冲突且服务端仍返回成功时提示，
    # 留给调用方在编排多步场景时对同一 vehicle_id + overlapping scheduled_at 做二次校验。
    if "test_drives" in path and method == "POST" and status_code in (200, 201):
        if req_dict.get("vehicle_id") and req_dict.get("scheduled_at"):
            findings.append({
                "type": "info_test_drive_created",
                "message": (
                    f"Test drive created for vehicle_id={req_dict.get('vehicle_id')} at "
                    f"{req_dict.get('scheduled_at')}. Caller should verify no overlapping booking "
                    "exists for the same vehicle (double-booking invariant needs cross-step check)."
                ),
                "severity": "info",
            })

    return findings


def main():
    parser = argparse.ArgumentParser(description="NetYamlForge AI User Simulation Step Runner (auto-dealer-demo)")
    parser.add_argument("--user", required=True, choices=list(USER_TOKENS.keys()), help="Role to simulate")
    parser.add_argument("--method", required=True, choices=["GET", "POST", "PUT", "DELETE", "PATCH"], help="HTTP Method")
    parser.add_argument("--path", required=True, help="API path (e.g. /nyf/api/auto-dealer-demo/sales_leads)")
    parser.add_argument("--data", help="JSON data payload for POST/PUT requests")

    args = parser.parse_args()

    setup_logging()

    parsed_data = None
    if args.data:
        try:
            parsed_data = json.loads(args.data)
        except json.JSONDecodeError:
            print("Error: Invalid JSON string provided to --data", file=sys.stderr)
            sys.exit(1)

    print(f"[*] Simulating User: {args.user.upper()} ({USER_TOKENS[args.user][1]})")
    print(f"[*] Executing Request: {args.method} {args.path}")
    if parsed_data:
        print(f"[*] Payload: {json.dumps(parsed_data, indent=2, ensure_ascii=False)}")

    log_entry = send_request(args.user, args.method, args.path, parsed_data)

    findings = check_invariants(log_entry)
    log_entry["invariant_violations"] = findings

    with open(LOG_FILE, "a", encoding="utf-8") as f:
        f.write(json.dumps(log_entry, ensure_ascii=False) + "\n")

    print(f"\n[+] Status Code: {log_entry['status_code']}")
    print(f"[+] Duration: {log_entry['duration_ms']}ms")
    print("[+] Response Payload:")
    print(json.dumps(log_entry["response_payload"], indent=2, ensure_ascii=False))

    hard_findings = [f for f in findings if f.get("severity") != "info"]
    if findings:
        print("\n[!] INVARIANT FINDINGS:")
        for finding in findings:
            tag = "INFO" if finding.get("severity") == "info" else "VIOLATION"
            print(f"  - [{tag}:{finding['type'].upper()}]: {finding['message']}")
    else:
        print("\n[+] All invariants satisfied.")

    sys.exit(2 if hard_findings else 0)


if __name__ == "__main__":
    main()
