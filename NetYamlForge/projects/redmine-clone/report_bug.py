#!/usr/bin/env python3
import sys
import os
import sqlite3
import argparse
import urllib.request
import json
from datetime import datetime

# Configuration
API_URL = "http://localhost:5001/api/redmine-clone/issue"
API_TOKEN = "admin-api-token"
DB_PATH = "/home/ubuntu/ws/NetYamlForge/NetYamlForge/projects/redmine-clone/database/redmine-clone.db"

def get_category_id(project_name):
    """Get the category ID for a project by name, creating it if it doesn't exist."""
    conn = sqlite3.connect(DB_PATH)
    cursor = conn.cursor()
    try:
        cursor.execute("SELECT Id FROM RmCategory WHERE Name = ?", (project_name,))
        row = cursor.fetchone()
        if row:
            return row[0]
        else:
            # Insert new project category
            now_str = datetime.now().strftime("%Y-%m-%d %H:%M:%S")
            cursor.execute(
                "INSERT INTO RmCategory (Name, DefaultAssignee, CreatedAt) VALUES (?, ?, ?)",
                (project_name, "", now_str)
            )
            conn.commit()
            return cursor.lastrowid
    finally:
        conn.close()

def report_via_api(payload):
    """Attempt to report the issue via HTTP API."""
    req = urllib.request.Request(
        API_URL,
        data=json.dumps(payload).encode('utf-8'),
        headers={
            'Content-Type': 'application/json',
            'X-Api-Token': API_TOKEN
        },
        method='POST'
    )
    try:
        with urllib.request.urlopen(req, timeout=5) as response:
            res_data = json.loads(response.read().decode('utf-8'))
            print("Successfully submitted bug report via API.")
            print(f"Issue ID: {res_data.get('Id') or res_data.get('id')}")
            return True
    except Exception as e:
        print(f"API submission failed: {e}. Falling back to direct database insertion...", file=sys.stderr)
        return False

def report_via_db(payload):
    """Fallback: write directly to the SQLite database."""
    conn = sqlite3.connect(DB_PATH)
    cursor = conn.cursor()
    now_str = datetime.now().strftime("%Y-%m-%d %H:%M:%S")
    
    try:
        cursor.execute(
            """
            INSERT INTO RmIssue (
                Tracker, Subject, Status, Priority, AssignedTo, DueDate, 
                DoneRatio, EstimatedHours, AuthorName, CreatedAt, UpdatedAt, 
                Module, StepsToReproduce, RelatedCommit, Severity, CategoryId
            ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
            """,
            (
                payload.get("Tracker", "bug"),
                payload["Subject"],
                payload.get("Status", "new"),
                payload.get("Priority", "normal"),
                payload.get("AssignedTo", ""),
                payload.get("DueDate", ""),
                payload.get("DoneRatio", 0),
                payload.get("EstimatedHours", 0),
                "admin",
                now_str,
                now_str,
                payload.get("Module", ""),
                payload.get("StepsToReproduce", ""),
                payload.get("RelatedCommit", ""),
                payload.get("Severity", "minor"),
                payload.get("CategoryId")
            )
        )
        conn.commit()
        print(f"Successfully inserted bug report directly into database. Issue ID: {cursor.lastrowid}")
        return True
    except Exception as e:
        print(f"Database insertion failed: {e}", file=sys.stderr)
        return False

def main():
    parser = argparse.ArgumentParser(description="Report a project bug to redmine-clone")
    parser.add_argument("-p", "--project", required=True, help="Project name (e.g., todo-app, ai-chat-pro)")
    parser.add_argument("-s", "--subject", required=True, help="Bug subject / title")
    parser.add_argument("-d", "--description", default="", help="Description of the bug")
    parser.add_argument("-r", "--steps", default="", help="Steps to reproduce")
    parser.add_argument("-m", "--module", default="ai_logic", choices=["frontend", "backend", "ai_logic", "devops"], help="Module name")
    parser.add_argument("-e", "--severity", default="major", choices=["blocker", "critical", "major", "minor", "trivial"], help="Bug severity")
    parser.add_argument("-i", "--priority", default="normal", choices=["low", "normal", "high", "urgent", "immediate"], help="Bug priority")
    parser.add_argument("-c", "--commit", default="", help="Related Git commit SHA")
    
    args = parser.parse_args()
    
    # Get or create Category ID for the project name
    try:
        category_id = get_category_id(args.project)
    except Exception as e:
        print(f"Failed to find/create category for project '{args.project}': {e}", file=sys.stderr)
        sys.exit(1)
        
    payload = {
        "Subject": args.subject,
        "Tracker": "bug",
        "Status": "new",
        "Priority": args.priority,
        "Severity": args.severity,
        "CategoryId": category_id,
        "Module": args.module,
        "StepsToReproduce": args.steps,
        "RelatedCommit": args.commit,
        "Description": args.description,
        "DoneRatio": 0
    }
    
    # Attempt API first, fallback to DB
    if not report_via_api(payload):
        if not report_via_db(payload):
            print("Failed to report issue.", file=sys.stderr)
            sys.exit(1)

if __name__ == "__main__":
    main()
