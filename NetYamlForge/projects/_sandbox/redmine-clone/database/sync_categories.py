import os
import sqlite3
from datetime import datetime

projects_dir = "/home/ubuntu/ws/NetYamlForge/NetYamlForge/projects"
db_path = "/home/ubuntu/ws/NetYamlForge/NetYamlForge/projects/redmine-clone/database/redmine-clone.db"

# Get all directories in the projects folder
subdirs = [d for d in os.listdir(projects_dir) if os.path.isdir(os.path.join(projects_dir, d))]
subdirs.sort()

conn = sqlite3.connect(db_path)
cursor = conn.cursor()

# Get existing categories
cursor.execute("SELECT Name FROM RmCategory")
existing_categories = {row[0] for row in cursor.fetchall()}

added = []
now_str = datetime.now().strftime("%Y-%m-%d %H:%M:%S")

for project in subdirs:
    if project not in existing_categories:
        cursor.execute(
            "INSERT INTO RmCategory (Name, DefaultAssignee, CreatedAt) VALUES (?, ?, ?)",
            (project, "", now_str)
        )
        added.append(project)

conn.commit()
conn.close()

if added:
    print(f"Added categories: {', '.join(added)}")
else:
    print("No new categories to add.")
