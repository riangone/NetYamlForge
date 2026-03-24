#!/bin/bash
# ─────────────────────────────────────────────────────────
# NetYamlForge Docker entrypoint
#
# Copies seed SQLite databases to the volume-mounted directories
# on first start (when the target file does not yet exist).
# When using an external database (PostgreSQL / MySQL / SQL Server),
# the seed copy is skipped.
# ─────────────────────────────────────────────────────────
set -e

DB_TYPE="${NYFORGE_TODO_APP_DB_TYPE:-sqlite}"

if [ "$DB_TYPE" = "sqlite" ]; then
    echo "[init] SQLite mode — checking seed databases..."
    for seed_db in /app/data/seeds/*.db; do
        [ -f "$seed_db" ] || continue
        project_name="$(basename "$seed_db" .db)"
        target_dir="/app/projects/${project_name}/database"
        target_db="${target_dir}/${project_name}.db"

        mkdir -p "$target_dir"
        if [ ! -f "$target_db" ]; then
            echo "[init] Initialising ${project_name} from seed ($(wc -c < "$seed_db") bytes)..."
            cp "$seed_db" "$target_db"
            echo "[init] ${project_name}.db ready."
        else
            echo "[init] ${project_name}.db already exists — skipping."
        fi
    done
else
    echo "[init] DB type is '${DB_TYPE}' — skipping SQLite seed copy."
fi

echo "[init] Starting NetYamlForge..."
exec dotnet NetYamlForge.dll "$@"
