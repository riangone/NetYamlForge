import sqlite3
import datetime

DB_PATH = "NetYamlForge/projects/auto-dealer-demo/database/auto-dealer-demo.db"

def refresh_dates():
    conn = sqlite3.connect(DB_PATH)
    c = conn.cursor()
    
    # Set dates to recent (last 1-10 days)
    now = datetime.datetime.now()
    
    tables_to_refresh = [
        ("sales_leads", "updated_at"),
        ("sales_leads", "created_at"),
        ("sales_leads", "last_contact_at"),
        ("ai_quotes", "updated_at"),
        ("ai_quotes", "created_at"),
        ("lead_activities", "created_at"),
        ("service_appointments", "preferred_date"),
        ("service_appointments", "created_at"),
        ("ai_conversations", "created_at")
    ]
    
    for table, col in tables_to_refresh:
        try:
            # We'll just set them all to something in the last 7 days for now to be safe
            # Use SQLite's date functions to do it in one go
            # But we want some variety, so let's do it in python if we want variety or just use a simple SQL
            c.execute(f"UPDATE {table} SET {col} = datetime('now', '-' || (ABS(RANDOM()) % 7) || ' days', '-' || (ABS(RANDOM()) % 24) || ' hours')")
            print(f"Refreshed {table}.{col}")
        except Exception as e:
            print(f"Error refreshing {table}.{col}: {e}")

    # Also make sure we have some 'won' leads for the chart
    c.execute("UPDATE sales_leads SET status = 'won' WHERE lead_id IN (SELECT lead_id FROM sales_leads LIMIT 5)")
    
    # Make sure we have some 'pending' ai_quotes
    c.execute("UPDATE ai_quotes SET status = 'pending' WHERE status != 'pending' LIMIT 5")

    conn.commit()
    conn.close()
    print("Demo dates refreshed successfully.")

if __name__ == '__main__':
    refresh_dates()
