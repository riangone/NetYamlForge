#!/usr/bin/env python3
# -*- coding: utf-8 -*-

import os
import sys
import json
import urllib.request
import urllib.error
from datetime import datetime
import argparse

# 默认配置
API_BASE_URL = os.environ.get("API_BASE_URL", "http://localhost:5000")
LOG_DIR = "/home/ubuntu/ws/NetYamlForge/logs/experiments"
LOG_FILE = os.path.join(LOG_DIR, "simulation.jsonl")

# 角色令牌映射
USER_TOKENS = {
    "pm": ("token_pm_user", "taskmgr_manager"),
    "dev": ("token_dev_user", "taskmgr_worker1"),
    "obs": ("token_obs_user", "taskmgr_viewer")
}

def setup_logging():
    if not os.path.exists(LOG_DIR):
        os.makedirs(LOG_DIR, exist_ok=True)

def send_request(user_key, method, path, data=None):
    if user_key not in USER_TOKENS:
        print(f"Error: Unknown user '{user_key}'. Choose from: pm, dev, obs", file=sys.stderr)
        sys.exit(1)
        
    token, username = USER_TOKENS[user_key]
    url = f"{API_BASE_URL}{path}"
    
    headers = {
        "Authorization": f"Bearer {token}",
        "Content-Type": "application/json"
    }
    
    req_body = None
    if data:
        if isinstance(data, str):
            req_body = data.encode('utf-8')
        else:
            req_body = json.dumps(data).encode('utf-8')
            
    req = urllib.request.Request(url, data=req_body, headers=headers, method=method.upper())
    
    start_time = datetime.now()
    response_data = None
    status_code = None
    error_msg = None
    
    try:
        with urllib.request.urlopen(req, timeout=10) as response:
            status_code = response.status
            response_data = response.read().decode('utf-8')
    except urllib.error.HTTPError as e:
        status_code = e.code
        response_data = e.read().decode('utf-8')
        error_msg = str(e)
    except Exception as e:
        status_code = 500
        error_msg = str(e)
        
    duration_ms = int((datetime.now() - start_time).total_seconds() * 1000)
    
    # 尝试解析响应为 JSON
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
        "error": error_msg
    }

def check_invariants(log_entry):
    """
    检查不变量约束，并返回发现的问题列表。
    """
    findings = []
    role = log_entry["role"]
    method = log_entry["method"]
    path = log_entry["path"]
    status_code = log_entry["status_code"]
    res_payload = log_entry["response_payload"]
    req_payload = log_entry["request_payload"]
    
    # 转换请求 payload 为字典方便检查
    req_dict = {}
    if req_payload:
        try:
            req_dict = json.loads(req_payload) if isinstance(req_payload, str) else req_payload
        except Exception:
            pass

    # 1. 协议层检查 (HTTP Status invariants)
    if status_code >= 500:
        findings.append({
            "type": "protocol_error",
            "message": f"Server returned 5xx status code ({status_code}). Detail: {log_entry['error']}"
        })
        
    # 2. 权限不变量检查 (Auth boundaries invariant)
    # Observer 应该是只读角色，任何写操作 (POST/PUT/DELETE) 应该返回 403 Forbidden
    if role == "obs" and method in ["POST", "PUT", "DELETE", "PATCH"]:
        if status_code not in [403, 401]:
            findings.append({
                "type": "auth_leak",
                "message": f"Observer (read-only user) successfully performed a write operation ({method} {path}) with status {status_code}. Expected 403."
            })

    # 3. 业务不变量检查 1: 截止日期校验 (validate_due_date)
    # 实验逻辑：如果创建或更新任务时，DueDate 晚于今天，或者 DueDate 是过去的时间，
    # 框架的 validate_due_date 钩子是否应该拦截（这里模拟规则：如果传入了过去的时间，应当拦截）
    if method in ["POST", "PUT"] and "DueDate" in req_dict and status_code in [200, 201]:
        try:
            due_date = datetime.strptime(req_dict["DueDate"], "%Y-%m-%d").date()
            today = datetime.now().date()
            if due_date < today:
                # 检查是否真的成功入库了
                findings.append({
                    "type": "validation_bypass",
                    "message": f"Task allowed a past DueDate ({req_dict['DueDate']}) to be saved (Status: {status_code}). 'validate_due_date' hook may have bypassed it."
                })
        except Exception as e:
            pass

    # 4. 业务不变量检查 2: 更新时间戳与审计日志 (UpdatedAt & AuditLog invariant)
    # 实验逻辑：只要是 PUT 更新操作，或者调用特定 actions 导致状态变更，UpdatedAt 必须被设置，不能为 null
    if method in ["PUT"] or ("actions" in path and status_code in [200, 201]):
        # 如果是成功返回
        data_item = None
        if isinstance(res_payload, dict):
            if "data" in res_payload:
                data_item = res_payload["data"]
            elif "UpdatedAt" in res_payload:
                data_item = res_payload
        
        if data_item and isinstance(data_item, dict):
            # 获取更新后的实体字段
            updated_at = data_item.get("UpdatedAt")
            if not updated_at:
                findings.append({
                    "type": "audit_missing_updated_at",
                    "message": f"Update operation completed successfully but 'UpdatedAt' is null/empty. Invariant violated."
                })

    # 5. 业务不变量检查 3: reopen 动作必须携带必填的 Reason
    if "actions/reopen" in path and method == "POST":
        # 检查请求是否带了 Reason
        reason = req_dict.get("Reason")
        if not reason or str(reason).strip() == "":
            if status_code in [200, 201]:
                findings.append({
                    "type": "action_input_bypass",
                    "message": "reopen action succeeded without providing a valid 'Reason' input. Required check bypassed."
                })
                
    return findings

def main():
    parser = argparse.ArgumentParser(description="NetYamlForge AI User Simulation Step Runner")
    parser.add_argument("--user", required=True, choices=["pm", "dev", "obs"], help="Role to simulate")
    parser.add_argument("--method", required=True, choices=["GET", "POST", "PUT", "DELETE", "PATCH"], help="HTTP Method")
    parser.add_argument("--path", required=True, help="API path (e.g. /api/task-management/task)")
    parser.add_argument("--data", help="JSON data payload for POST/PUT requests")
    
    args = parser.parse_args()
    
    setup_logging()
    
    # 格式化 JSON 数据，防止格式错误
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
        print(f"[*] Payload: {json.dumps(parsed_data, indent=2)}")
        
    log_entry = send_request(args.user, args.method, args.path, parsed_data)
    
    # 运行不变量检查
    findings = check_invariants(log_entry)
    log_entry["invariant_violations"] = findings
    
    # 写入 JSONL 审计日志
    with open(LOG_FILE, "a", encoding="utf-8") as f:
        f.write(json.dumps(log_entry, ensure_ascii=False) + "\n")
        
    print(f"\n[+] Status Code: {log_entry['status_code']}")
    print(f"[+] Duration: {log_entry['duration_ms']}ms")
    print("[+] Response Payload:")
    print(json.dumps(log_entry["response_payload"], indent=2, ensure_ascii=False))
    
    if findings:
        print("\n[!] INVARIANT VIOLATIONS DETECTED:")
        for finding in findings:
            print(f"  - [{finding['type'].upper()}]: {finding['message']}")
    else:
        print("\n[+] All invariants satisfied.")
        
    # 如果有不变量违规，脚本以退出码 2 结束，方便 CLI / CI 判定
    if findings:
        sys.exit(2)
    sys.exit(0)

if __name__ == "__main__":
    main()
