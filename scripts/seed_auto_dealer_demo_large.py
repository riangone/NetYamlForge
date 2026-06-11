import os
import sqlite3
import datetime
import uuid
import random

REPO_ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
DB_PATH = os.path.join(REPO_ROOT, "NetYamlForge/projects/auto-dealer-demo/database/auto-dealer-demo.db")

def get_now(delta_days=0, delta_hours=0):
    return (datetime.datetime.now() - datetime.timedelta(days=delta_days, hours=delta_hours)).strftime('%Y-%m-%d %H:%M:%S')

def generate_large_seed_data():
    conn = sqlite3.connect(DB_PATH)
    c = conn.cursor()

    # Generate more customers
    base_customers = 5
    for i in range(1, 21):
        cust_id = f"CUST{str(i).zfill(3)}"
        name = f"テスト顧客{i}号"
        kana = f"テストコキャク{i}ゴウ"
        email = f"customer{i}@example.com"
        phone = f"090-{str(random.randint(1000, 9999))}-{str(random.randint(1000, 9999))}"
        username = f"customer{i}"
        
        c.execute("INSERT OR IGNORE INTO AppUser (UserName, PasswordHash, DisplayName, CreatedAt) VALUES (?, 'AQAAAAIAAYagAAAAEKelJrS1r5J7lXzF0OTB4VRJSZSljiXgSdVP6FJxCtCbRIqK5+A/nI9zBw9Pz8bcww==', ?, ?)", 
                  (username, name, get_now()))
        c.execute("INSERT OR IGNORE INTO AppUserRole (UserName, RoleName, CreatedAt) VALUES (?, 'customer', ?)", 
                  (username, get_now()))

        c.execute("INSERT OR IGNORE INTO customers (customer_id, name, name_kana, email, phone, user_name, created_at, updated_at) VALUES (?, ?, ?, ?, ?, ?, ?, ?)",
                  (cust_id, name, kana, email, phone, username, get_now(), get_now()))

    # Generate more vehicles
    makers = ["Toyota", "Honda", "Nissan", "Mazda", "Subaru"]
    models = {
        "Toyota": ["Prius", "Camry", "Corolla", "RAV4", "Harrier"],
        "Honda": ["Civic", "Fit", "Vezel", "CR-V", "Accord"],
        "Nissan": ["Leaf", "Ariya", "X-Trail", "Note", "Kicks"],
        "Mazda": ["CX-5", "CX-30", "Mazda3", "Mazda6", "CX-8"],
        "Subaru": ["Levorg", "Forester", "Outback", "Impreza", "XV"]
    }
    vtypes = {"Prius": "sedan", "Camry": "sedan", "Corolla": "sedan", "RAV4": "suv", "Harrier": "suv", "Civic": "sedan", "Fit": "compact", "Vezel": "suv", "CR-V": "suv", "Accord": "sedan", "Leaf": "compact", "Ariya": "suv", "X-Trail": "suv", "Note": "compact", "Kicks": "suv", "CX-5": "suv", "CX-30": "suv", "Mazda3": "sedan", "Mazda6": "sedan", "CX-8": "suv", "Levorg": "wagon", "Forester": "suv", "Outback": "suv", "Impreza": "sedan", "XV": "suv"}

    for i in range(1, 31):
        veh_id = f"VEH{str(i).zfill(3)}"
        maker = random.choice(makers)
        model = random.choice(models[maker])
        year = random.randint(2018, 2024)
        color = random.choice(["White", "Black", "Silver", "Red", "Blue"])
        mileage = random.randint(10, 50000)
        price = random.randint(150, 600) * 10000
        vtype = vtypes[model]
        c.execute("INSERT OR IGNORE INTO vehicles (vehicle_id, maker, brand, model, year, color, mileage, price, vehicle_type, status, created_at, updated_at) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, 'available', ?, ?)",
                  (veh_id, maker, maker, model, year, color, mileage, price, vtype, get_now(), get_now()))

    # Generate leads
    statuses = ["new", "contacted", "qualified", "proposal", "negotiation"]
    for i in range(1, 21):
        lead_id = f"LEAD{str(i).zfill(3)}"
        cust_id = f"CUST{str(i).zfill(3)}"
        interest = random.choice(["SUV search", "EV transition", "Family car", "Used compact", "Sports car upgrade"])
        budget = random.randint(200, 500) * 10000
        score = random.randint(30, 95)
        status = random.choice(statuses)
        assignee = random.choice(["suzuki", "yamada"])
        c.execute("INSERT OR IGNORE INTO sales_leads (lead_id, customer_id, vehicle_interest, budget, lead_score, status, assigned_to_user_id, created_at, updated_at) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?)",
                  (lead_id, cust_id, interest, budget, score, status, assignee, get_now(delta_days=random.randint(0, 10)), get_now()))

        # Generate some tasks for leads
        if random.random() > 0.3:
            task_id = f"TASK_L_{i}"
            t_type = random.choice(["email", "call", "appointment"])
            reason = f"Follow up on {interest}"
            c.execute("INSERT OR IGNORE INTO lead_nurturing_tasks (task_id, lead_id, customer_id, task_type, trigger_reason, priority_score, status, assigned_to, ai_recommendation, ai_reasoning, created_at, updated_at) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)",
                      (task_id, lead_id, cust_id, t_type, reason, score, "pending", assignee, f"Recommend sending {t_type} regarding {interest}", f"Lead score is {score}", get_now(), get_now()))

    # Generate some AI decisions waiting for approval
    for i in range(1, 6):
        d_id = f"DEC_A_{i}"
        e_id = f"QUOTE_A_{i}"
        conf = random.uniform(80.0, 95.0)
        c.execute("INSERT OR IGNORE INTO ai_decisions (decision_id, decision_type, entity_type, entity_id, ai_reasoning, confidence_score, status, requires_human, created_at) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?)",
                  (d_id, "discount_approval", "ai_quotes", e_id, "High probability of closing if discount is applied. Customer has high intent.", conf, "pending", 1, get_now(delta_hours=random.randint(1, 5))))

        lead_id = f"LEAD{str(i).zfill(3)}"
        cust_id = f"CUST{str(i).zfill(3)}"
        veh_id = f"VEH{str(random.randint(1, 30)).zfill(3)}"
        base_price = random.randint(300, 500) * 10000
        discount = random.randint(10, 30) * 10000
        c.execute("INSERT OR IGNORE INTO ai_quotes (quote_id, lead_id, customer_id, vehicle_id, base_price, discount_amount, final_price, ai_reasoning, status, ai_confidence, created_at, updated_at) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)",
                  (e_id, lead_id, cust_id, veh_id, base_price, discount, base_price-discount, "Applying discount to close the deal", "pending", conf, get_now(), get_now()))

    conn.commit()
    conn.close()
    print("Large seed data generated successfully.")

if __name__ == '__main__':
    generate_large_seed_data()