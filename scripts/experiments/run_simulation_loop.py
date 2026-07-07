#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
Continuous simulation loop with random jitter.
Runs PM, Dev, Observer role scenarios periodically.
Usage:
    API_BASE_URL=http://localhost:5009 python3 run_simulation_loop.py
    API_BASE_URL=http://localhost:5009 python3 run_simulation_loop.py --iterations 50 --interval 30
"""

import os
import sys
import json
import time
import random
import subprocess
import argparse
from datetime import datetime, timezone

SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
SIM_STEP = os.path.join(SCRIPT_DIR, "run_simulation_step.py")

SCENARIOS = [
    {"user": "pm", "method": "POST", "path": "/nyf/api/task-management/task",
     "data": lambda i: json.dumps({
         "Title": f"sim: PM自动任务-{random.randint(100,999)}",
         "AssignedTo": random.choice(["taskmgr_worker1", "taskmgr_worker2"]),
         "DueDate": f"2026-07-{random.randint(10,31)}",
         "Priority": random.choice(["high", "medium", "low"]),
         "Status": "not_started"
     })},
    {"user": "dev", "method": "PATCH", "path": None,
     "data": lambda i: json.dumps({"Status": random.choice(["in_progress", "completed"])}),
     "type": "transition"},
    {"user": "obs", "method": "GET", "path": "/nyf/api/task-management/task",
     "data": None},
    {"user": "obs", "method": "POST", "path": "/nyf/api/task-management/task",
     "data": lambda i: json.dumps({
         "Title": f"sim: OBS越权尝试-{random.randint(100,999)}",
         "AssignedTo": "taskmgr_worker2",
         "DueDate": "2026-07-20",
         "Priority": "low",
         "Status": "not_started"
     }), "expect_block": True},
]


def get_latest_task_id():
    """Fetch the latest task ID from the API using PM token."""
    token = "token_pm_user"
    base_url = os.environ.get("API_BASE_URL", "http://localhost:5000")
    import urllib.request
    try:
        req = urllib.request.Request(
            f"{base_url}/nyf/api/task-management/task?pageSize=1&sort=Id&dir=desc&count=true",
            headers={"Authorization": f"Bearer {token}"}
        )
        with urllib.request.urlopen(req, timeout=10) as resp:
            data = json.loads(resp.read().decode())
            items = data.get("data", [])
            if items:
                return items[0]["data"].get("Id")
    except Exception as e:
        print(f"[WARN] Failed to fetch latest task: {e}")
    return None


def run_scenario(scenario, iteration):
    """Run a single simulation step and return success status."""
    user = scenario["user"]
    method = scenario["method"]
    path = scenario.get("path")
    data = scenario.get("data")
    expect_block = scenario.get("expect_block", False)

    if path is None and scenario.get("type") == "transition":
        task_id = get_latest_task_id()
        if task_id is None:
            print(f"[SKIP] No task available for transition")
            return True
        path = f"/nyf/api/task-management/task/{task_id}"

    body = data(iteration) if data else None

    cmd = [
        sys.executable, SIM_STEP,
        "--user", user,
        "--method", method,
        "--path", path,
    ]
    if body:
        cmd.extend(["--data", body])

    try:
        start = time.time()
        result = subprocess.run(
            cmd,
            capture_output=True, text=True, timeout=30,
            env={**os.environ}
        )
        elapsed = time.time() - start

        status_code = None
        for line in result.stdout.split("\n"):
            if "Status Code:" in line:
                try:
                    status_code = int(line.split(":")[-1].strip())
                except:
                    pass

        if expect_block:
            blocked = status_code and status_code in (403, 401)
            if blocked:
                print(f"[OK] Iteration {iteration}: {user} {method} blocked ({status_code}) [{elapsed:.1f}s]")
                return True
            else:
                print(f"[WARN] Iteration {iteration}: {user} {method} expected block but got {status_code}")
                return False
        else:
            ok = status_code and status_code < 500
            if ok:
                print(f"[OK] Iteration {iteration}: {user} {method} {status_code} [{elapsed:.1f}s]")
            else:
                print(f"[FAIL] Iteration {iteration}: {user} {method} {status_code} [{elapsed:.1f}s]")
            return ok
    except subprocess.TimeoutExpired:
        print(f"[TIMEOUT] Iteration {iteration}: {user} {method}")
        return False
    except Exception as e:
        print(f"[ERROR] Iteration {iteration}: {user} {method}: {e}")
        return False


def main():
    parser = argparse.ArgumentParser(description="Continuous simulation loop with jitter")
    parser.add_argument("--iterations", type=int, default=0,
                        help="Number of iterations (0 = infinite)")
    parser.add_argument("--interval", type=int, default=45,
                        help="Base interval between iterations in seconds")
    parser.add_argument("--jitter", type=float, default=0.3,
                        help="Random jitter fraction (0.0-1.0)")
    args = parser.parse_args()

    print(f"[{datetime.now(timezone.utc).isoformat()}] Simulation loop started")
    print(f"  Iterations: {'infinite' if args.iterations == 0 else args.iterations}")
    print(f"  Interval: {args.interval}s ± {args.jitter*100:.0f}%")
    print(f"  API: {os.environ.get('API_BASE_URL', 'http://localhost:5000')}")
    print()

    iteration = 0
    stats = {"ok": 0, "fail": 0, "skip": 0}

    try:
        while args.iterations == 0 or iteration < args.iterations:
            iteration += 1
            scenario = random.choice(SCENARIOS)

            print(f"[{datetime.now(timezone.utc).isoformat()}] --- Iteration {iteration} ---")
            success = run_scenario(scenario, iteration)

            if success:
                stats["ok"] += 1
            else:
                stats["fail"] += 1

            # Report stats every 10 iterations
            if iteration % 10 == 0:
                total = stats["ok"] + stats["fail"]
                rate = stats["ok"] / total * 100 if total > 0 else 0
                print(f"[STATS] {total} runs: {stats['ok']} ok, {stats['fail']} fail ({rate:.1f}%)")

            if args.iterations == 0 or iteration < args.iterations:
                jitter = random.uniform(1 - args.jitter, 1 + args.jitter)
                delay = args.interval * jitter
                print(f"  Next in {delay:.0f}s...")
                print()
                time.sleep(delay)

    except KeyboardInterrupt:
        print()
        print(f"[{datetime.now(timezone.utc).isoformat()}] Simulation stopped by user")

    total = stats["ok"] + stats["fail"]
    rate = stats["ok"] / total * 100 if total > 0 else 0
    print(f"[SUMMARY] {total} runs: {stats['ok']} ok, {stats['fail']} fail ({rate:.1f}%)")


if __name__ == "__main__":
    main()
