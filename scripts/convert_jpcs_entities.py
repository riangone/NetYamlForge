#!/usr/bin/env python3
"""Convert jpcs entity YAML files from iDempiere export format to NetYamlForge format."""

import os
import re
import yaml

ENTITIES_DIR = os.path.join(
    os.path.dirname(os.path.dirname(__file__)),
    "NetYamlForge", "projects", "jpcs", "entities"
)

TYPE_MAP = {
    "numeric": "decimal",
    "text": "string",
    "date": "datetime",
    "timestamp": "datetime",
    "time": "string",
    "binary": "string",
    "list": "string",
    "table": "string",
    "button": "string",
    "pattribute": "string",
    "image": "image",
    "url": "url",
    "color": "string",
    "memo": "text",
    "location": "string",
    "password": "password",
    "integer": "int",
    "long": "long",
    "double": "double",
    "boolean": "boolean",
    "yesno": "boolean",
    "string": "string",
    "amount": "money",
}

def convert_file(filepath):
    with open(filepath, "r", encoding="utf-8") as f:
        content = f.read()

    data = yaml.safe_load(content)
    if data is None:
        return False

    name = data.get("name", "")
    table_name = data.get("tableName", "")
    primary_key = data.get("primaryKey", "")
    fields = data.get("fields", [])

    if not name or not table_name:
        return False

    entity_key = name.lower().replace(" ", "_")

    columns = {}
    for field in fields:
        field_name = field.get("name", "")
        field_type = field.get("type", "text")
        mapped_type = TYPE_MAP.get(field_type.lower(), "string")

        col_def = {"type": mapped_type}

        if field_name == primary_key:
            col_def["identity"] = True

        columns[field_name] = col_def

    new_data = {
        "entities": {
            entity_key: {
                "table": table_name,
                "key": primary_key,
                "displayName": name,
                "columns": columns,
            }
        }
    }

    with open(filepath, "w", encoding="utf-8") as f:
        yaml.dump(new_data, f, default_flow_style=None, allow_unicode=True, sort_keys=False)

    return True

def main():
    count = 0
    errors = []
    for filename in sorted(os.listdir(ENTITIES_DIR)):
        if not filename.endswith((".yml", ".yaml")):
            continue
        filepath = os.path.join(ENTITIES_DIR, filename)
        try:
            if convert_file(filepath):
                count += 1
            else:
                errors.append(f"{filename}: empty or invalid format")
        except Exception as e:
            errors.append(f"{filename}: {e}")

    print(f"Converted {count} entity files")
    if errors:
        print(f"Errors ({len(errors)}):")
        for e in errors:
            print(f"  - {e}")

if __name__ == "__main__":
    main()
