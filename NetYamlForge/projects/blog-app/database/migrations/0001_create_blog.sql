CREATE TABLE IF NOT EXISTS blog_category (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    name TEXT NOT NULL,
    slug TEXT NOT NULL UNIQUE,
    description TEXT,
    sort_order INTEGER DEFAULT 0,
    created_at TEXT
);

CREATE TABLE IF NOT EXISTS blog_post (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    title TEXT NOT NULL,
    slug TEXT NOT NULL UNIQUE,
    summary TEXT,
    content TEXT NOT NULL,
    category_id INTEGER REFERENCES blog_category(id),
    author TEXT DEFAULT 'Admin',
    status TEXT DEFAULT 'draft',
    view_count INTEGER DEFAULT 0,
    published_at TEXT,
    created_at TEXT,
    updated_at TEXT
);
