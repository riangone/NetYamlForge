import os
import sqlite3
import datetime
import uuid
import random

REPO_ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
DB_PATH = os.path.join(REPO_ROOT, "NetYamlForge/projects/auto-dealer-demo/database/auto-dealer-demo.db")

def get_now():
    return datetime.datetime.now().strftime('%Y-%m-%d %H:%M:%S')

def execute_sql(cursor, query, params=None):
    if params is None:
        params = ()
    cursor.execute(query, params)

def generate_seed_data():
    conn = sqlite3.connect(DB_PATH)
    c = conn.cursor()

    # Users and Roles
    users = [
        ("admin", "Admin User", "admin"),
        ("yamada", "Yamada Tarou", "sales_manager"),
        ("suzuki", "Suzuki Ichiro", "sales_rep"),
        ("takahashi", "Takahashi Ken", "operator"),
        ("sato", "Sato Hanako", "customer"),
        ("ito", "Ito Jiro", "customer"),
    ]

    for user, display, role in users:
        c.execute("INSERT OR IGNORE INTO AppUser (UserName, PasswordHash, DisplayName, CreatedAt) VALUES (?, 'hash', ?, ?)", 
                  (user, display, get_now()))
        c.execute("INSERT OR IGNORE INTO AppUserRole (UserName, RoleName, CreatedAt) VALUES (?, ?, ?)", 
                  (user, role, get_now()))

    # Employees
    employees = [
        ("EMP001", "yamada", "E001", "山田 太郎", "ヤマダ タロウ", "sales_manager", "Sales", "Manager"),
        ("EMP002", "suzuki", "E002", "鈴木 一郎", "スズキ イチロウ", "sales_rep", "Sales", "Staff"),
        ("EMP003", "takahashi", "E003", "高橋 健", "タカハシ ケン", "operator", "CS", "Staff")
    ]
    for emp_id, user, emp_num, name, kana, role, dept, pos in employees:
        c.execute("INSERT OR IGNORE INTO employees (employee_id, user_name, employee_number, name, name_kana, email, role, department, position, hire_date, employment_type, created_at, updated_at) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, 'full_time', ?, ?)",
                  (emp_id, user, emp_num, name, kana, f"{user}@example.com", role, dept, pos, "2020-04-01", get_now(), get_now()))

    # Customers
    customers = [
        ("CUST001", "佐藤 花子", "サトウ ハナコ", "sato@example.com", "090-1111-2222", "sato"),
        ("CUST002", "伊藤 次郎", "イトウ ジロウ", "ito@example.com", "090-3333-4444", "ito"),
        ("CUST003", "渡辺 三郎", "ワタナベ サブロウ", "watanabe@example.com", "090-5555-6666", None),
        ("CUST004", "小林 四郎", "コバヤシ シロウ", "kobayashi@example.com", "090-7777-8888", None),
        ("CUST005", "加藤 五郎", "カトウ ゴロウ", "kato@example.com", "090-9999-0000", None)
    ]
    for cust_id, name, kana, email, phone, username in customers:
        c.execute("INSERT OR IGNORE INTO customers (customer_id, name, name_kana, email, phone, user_name, created_at, updated_at) VALUES (?, ?, ?, ?, ?, ?, ?, ?)",
                  (cust_id, name, kana, email, phone, username, get_now(), get_now()))

    # Vehicles (Inventory)
    vehicles = [
        ("VEH001", None, "Toyota", "Prius", 2023, "White", 100, 3200000.00, "sedan"),
        ("VEH002", None, "Honda", "Vezel", 2022, "Black", 5000, 2800000.00, "suv"),
        ("VEH003", None, "Nissan", "Ariya", 2024, "Silver", 10, 5500000.00, "suv"),
        ("VEH004", None, "Mazda", "CX-5", 2021, "Red", 15000, 2500000.00, "suv"),
        ("VEH005", None, "Subaru", "Levorg", 2023, "Blue", 2000, 3800000.00, "wagon")
    ]
    for veh_id, cust_id, maker, model, year, color, mileage, price, vtype in vehicles:
        c.execute("INSERT OR IGNORE INTO vehicles (vehicle_id, customer_id, maker, brand, model, year, color, mileage, price, vehicle_type, created_at, updated_at) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)",
                  (veh_id, cust_id, maker, maker, model, year, color, mileage, price, vtype, get_now(), get_now()))

    # Sales Leads
    leads = [
        ("LEAD001", "CUST001", "Prius Interest", 3500000, 85, "new", "suzuki"),
        ("LEAD002", "CUST002", "SUV search", 4000000, 60, "contacted", "suzuki"),
        ("LEAD003", "CUST003", "EV transition", 6000000, 95, "negotiation", "suzuki"),
        ("LEAD004", "CUST004", "Family car", 3000000, 45, "new", "suzuki"),
        ("LEAD005", "CUST005", "Used SUV", 2000000, 75, "qualified", "suzuki")
    ]
    for lead_id, cust_id, interest, budget, score, status, assignee in leads:
        c.execute("INSERT OR IGNORE INTO sales_leads (lead_id, customer_id, vehicle_interest, budget, lead_score, status, assigned_to_user_id, created_at, updated_at) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?)",
                  (lead_id, cust_id, interest, budget, score, status, assignee, get_now(), get_now()))

    # Lead Nurturing Tasks
    tasks = [
        ("TASK001", "LEAD001", "CUST001", "email", "Follow up on Prius", 90, "pending", "suzuki", "Send test drive invitation for Prius", "High interest shown in EV efficiency"),
        ("TASK002", "LEAD003", "CUST003", "call", "Discuss Ariya pricing", 95, "pending", "suzuki", "Call to propose a quote with 5% discount", "Customer is ready to buy"),
        ("TASK003", "LEAD002", "CUST002", "email", "Send SUV catalog", 70, "pending", "suzuki", "Send digital catalog for Vezel", "Customer looking for compact SUV"),
        ("TASK004", "LEAD005", "CUST005", "appointment", "Used car viewing", 80, "pending", "suzuki", "Schedule a visit for CX-5", "Customer interested in recent used cars")
    ]
    for task_id, lead_id, cust_id, t_type, reason, score, status, assignee, ai_rec, ai_reas in tasks:
        c.execute("INSERT OR IGNORE INTO lead_nurturing_tasks (task_id, lead_id, customer_id, task_type, trigger_reason, priority_score, status, assigned_to, ai_recommendation, ai_reasoning, created_at, updated_at) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)",
                  (task_id, lead_id, cust_id, t_type, reason, score, status, assignee, ai_rec, ai_reas, get_now(), get_now()))

    # AI Quotes
    quotes = [
        ("QUOTE001", "LEAD003", "CUST003", "VEH003", 5500000, 200000, 5300000, "Applying 3.6% discount to close the deal on EV", "pending", 88)
    ]
    for q_id, lead_id, cust_id, veh_id, base, discount, final, reason, status, conf in quotes:
        c.execute("INSERT OR IGNORE INTO ai_quotes (quote_id, lead_id, customer_id, vehicle_id, base_price, discount_amount, final_price, ai_reasoning, status, ai_confidence, created_at, updated_at) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)",
                  (q_id, lead_id, cust_id, veh_id, base, discount, final, reason, status, conf, get_now(), get_now()))
    
    # AI Decisions
    decisions = [
        ("DEC001", "discount_approval", "ai_quotes", "QUOTE001", "High probability of closing if 200,000 JPY discount is applied. Customer has EV subsidy.", 88.5, "pending", 1)
    ]
    for d_id, d_type, e_type, e_id, reason, conf, status, req_human in decisions:
         c.execute("INSERT OR IGNORE INTO ai_decisions (decision_id, decision_type, entity_type, entity_id, ai_reasoning, confidence_score, status, requires_human, created_at) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?)",
                   (d_id, d_type, e_type, e_id, reason, conf, status, req_human, get_now()))

    conn.commit()
    conn.close()
    print("Seed data generated successfully.")

if __name__ == '__main__':
    generate_seed_data()
