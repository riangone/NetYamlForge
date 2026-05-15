-- Categories
CREATE TABLE categories (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    name TEXT NOT NULL,
    slug TEXT NOT NULL UNIQUE,
    description TEXT,
    created_at DATETIME DEFAULT CURRENT_TIMESTAMP
);

-- Tags
CREATE TABLE tags (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    name TEXT NOT NULL UNIQUE
);

-- Posts
CREATE TABLE posts (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    title TEXT NOT NULL,
    slug TEXT NOT NULL UNIQUE,
    summary TEXT,
    content TEXT NOT NULL,
    author_id INTEGER NOT NULL, -- Link to blog_users.id
    category_id INTEGER,
    status TEXT NOT NULL DEFAULT 'draft', -- draft, published, archived
    is_featured INTEGER DEFAULT 0,
    view_count INTEGER DEFAULT 0,
    published_at DATETIME,
    created_at DATETIME DEFAULT CURRENT_TIMESTAMP,
    updated_at DATETIME DEFAULT CURRENT_TIMESTAMP,
    FOREIGN KEY (category_id) REFERENCES categories(id)
);

-- PostTags
CREATE TABLE post_tags (
    post_id INTEGER NOT NULL,
    tag_id INTEGER NOT NULL,
    PRIMARY KEY (post_id, tag_id),
    FOREIGN KEY (post_id) REFERENCES posts(id) ON DELETE CASCADE,
    FOREIGN KEY (tag_id) REFERENCES tags(id) ON DELETE CASCADE
);

-- Comments
CREATE TABLE comments (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    post_id INTEGER NOT NULL,
    author_name TEXT NOT NULL,
    author_email TEXT,
    content TEXT NOT NULL,
    status TEXT NOT NULL DEFAULT 'pending', -- pending, approved, spam
    created_at DATETIME DEFAULT CURRENT_TIMESTAMP,
    FOREIGN KEY (post_id) REFERENCES posts(id) ON DELETE CASCADE
);

-- BlogUsers (Sub-system specific user data)
CREATE TABLE blog_users (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    app_user_id INTEGER NOT NULL, -- Link to global app_user.id in system.db
    display_name TEXT NOT NULL,
    bio TEXT,
    avatar_url TEXT,
    created_at DATETIME DEFAULT CURRENT_TIMESTAMP,
    updated_at DATETIME DEFAULT CURRENT_TIMESTAMP
);

-- Initial Data
INSERT INTO categories (name, slug, description) VALUES ('Technology', 'tech', 'Tech related posts');
INSERT INTO categories (name, slug, description) VALUES ('Life', 'life', 'Personal life stories');

INSERT INTO tags (name) VALUES ('ASP.NET Core');
INSERT INTO tags (name) VALUES ('YAML');
INSERT INTO tags (name) VALUES ('AI');

-- Sample Author
INSERT INTO blog_users (app_user_id, display_name, bio) VALUES (1, 'Admin Blogger', 'The main administrator of this blog.');

INSERT INTO posts (title, slug, summary, content, author_id, category_id, status, published_at) 
VALUES ('Hello NetYamlForge', 'hello-netyamlforge', 'Welcome to my new blog powered by NetYamlForge.', 
'This is the first post. NetYamlForge is amazing!', 1, 1, 'published', CURRENT_TIMESTAMP);
