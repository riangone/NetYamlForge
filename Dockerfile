# ─────────────────────────────────────────────────────────
# Stage 1: build
# ─────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# sqlite3 is used to pre-seed the todo-app database
RUN apt-get update && apt-get install -y --no-install-recommends sqlite3 \
    && rm -rf /var/lib/apt/lists/*

# Restore dependencies (layer-cached separately for faster rebuilds)
COPY NetYamlForge.Analyzers/NetYamlForge.Analyzers.csproj NetYamlForge.Analyzers/
COPY NetYamlForge/NetYamlForge.csproj                     NetYamlForge/
RUN dotnet restore NetYamlForge/NetYamlForge.csproj

# Copy source
COPY NetYamlForge.Analyzers/ NetYamlForge.Analyzers/
COPY NetYamlForge/            NetYamlForge/

# Build & publish
RUN dotnet publish NetYamlForge/NetYamlForge.csproj \
        -c Release \
        -o /app/publish \
        --no-restore

# Copy project configuration files (YAML, SQL, hooks) to publish output.
# dotnet publish does not automatically include the projects/ directory.
# Exclude runtime-generated database files.
RUN cp -r /src/NetYamlForge/projects /app/publish/projects && \
    find /app/publish/projects -name "*.db" -delete && \
    find /app/publish/projects -name "*.db-shm" -delete && \
    find /app/publish/projects -name "*.db-wal" -delete && \
    echo "[build] projects/ copied ($(find /app/publish/projects -name '*.yml' -o -name '*.yaml' | wc -l) YAML files)"

# ─── Prepare seed databases ───────────────────────────────
# todo-app: create from init_seed.sql so the image ships with demo data
RUN mkdir -p /seeds && \
    sqlite3 /seeds/todo-app.db \
        < /src/NetYamlForge/projects/todo-app/database/init_seed.sql && \
    echo "[seed] todo-app.db created ($(wc -c < /seeds/todo-app.db) bytes)"

# biz-docs: create from init_seed.sql
RUN sqlite3 /seeds/biz-docs.db \
        < /src/NetYamlForge/projects/biz-docs/database/init_seed.sql && \
    echo "[seed] biz-docs.db created ($(wc -c < /seeds/biz-docs.db) bytes)"

# ui-showcase: copy the tracked demo database (already seeded)
RUN if [ -f /src/NetYamlForge/projects/ui-showcase/database/ui-showcase.db ]; then \
        cp /src/NetYamlForge/projects/ui-showcase/database/ui-showcase.db /seeds/ui-showcase.db && \
        echo "[seed] ui-showcase.db copied"; \
    fi

# ─────────────────────────────────────────────────────────
# Stage 2: runtime
# ─────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# tzdata for timezone-aware cron jobs
RUN apt-get update && apt-get install -y --no-install-recommends tzdata curl \
    && rm -rf /var/lib/apt/lists/*

# Copy published application
COPY --from=build /app/publish .

# Copy seed databases to a fixed location inside the image
COPY --from=build /seeds /app/data/seeds

# Copy entrypoint script
COPY docker/entrypoint.sh /app/entrypoint.sh
RUN chmod +x /app/entrypoint.sh

# Ensure directories for SQLite volumes and logs exist
RUN mkdir -p \
        /app/projects/todo-app/database \
        /app/projects/ui-showcase/database \
        /app/projects/biz-docs/database \
        /app/logs

EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    TZ=UTC

ENTRYPOINT ["/app/entrypoint.sh"]
