import sqlite3
import os
import glob

sys_db = "/home/ubuntu/ws/NetYamlForge/NetYamlForge/system.db"
conn_sys = sqlite3.connect(sys_db)
c_sys = conn_sys.cursor()

# Get all projects
projects = glob.glob("/home/ubuntu/ws/NetYamlForge/NetYamlForge/projects/*/database/*.db")

for p_db in projects:
    if "jpcs.db" in p_db:
        # jpcs uses ad_user
        conn_p = sqlite3.connect(p_db)
        c_p = conn_p.cursor()
        try:
            c_p.execute("SELECT name FROM ad_user")
            for row in c_p.fetchall():
                username = row[0].replace(' ', '').lower()
                c_sys.execute("UPDATE app_user SET owning_project = 'jpcs' WHERE user_name = ?", (username,))
        except Exception as e:
            print(f"Error in {p_db}: {e}")
        conn_p.close()
    else:
        proj_name = os.path.basename(os.path.dirname(os.path.dirname(p_db)))
        conn_p = sqlite3.connect(p_db)
        c_p = conn_p.cursor()
        try:
            c_p.execute("SELECT UserName FROM AppUser")
            for row in c_p.fetchall():
                username = row[0]
                c_sys.execute("UPDATE app_user SET owning_project = ? WHERE user_name = ?", (proj_name, username))
        except Exception as e:
            pass # Some don't have AppUser
        conn_p.close()

# Special handling for admin and globaladmin
c_sys.execute("UPDATE app_user SET owning_project = NULL WHERE user_name IN ('admin', 'globaladmin')")

# Framework users
c_sys.execute("UPDATE app_user SET owning_project = 'framework' WHERE user_name IN ('framework_admin', 'framework_editor', 'framework_user1', 'framework_user2')")

conn_sys.commit()
conn_sys.close()
print("Done!")
