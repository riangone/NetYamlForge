-- photo-vault AI 配置初始化
-- 幂等操作：INSERT OR IGNORE 不覆盖已有设置
-- 用法：sqlite3 database/photo-vault.db < database/init_seed.sql

CREATE TABLE IF NOT EXISTS project_settings (
    id            INTEGER PRIMARY KEY AUTOINCREMENT,
    section_group TEXT    NOT NULL,
    setting_key   TEXT    NOT NULL UNIQUE,
    label         TEXT    NOT NULL,
    value         TEXT,
    default_value TEXT,
    description   TEXT,
    updated_at    TEXT
);

INSERT OR IGNORE INTO project_settings
    (section_group, setting_key, label, value, default_value, description, updated_at)
VALUES
    ('annotation', 'annotation_provider',
     '标注 AI 提供商', 'lmstudio', 'lmstudio',
     '可选值：lmstudio / ollama / gemini / antigravity',
     datetime('now')),

    ('annotation', 'lmstudio_base_url',
     'LM Studio 接口地址', 'http://localhost:1234/v1', 'http://localhost:1234/v1',
     'LM Studio 本地服务的 OpenAI 兼容接口地址',
     datetime('now')),

    ('annotation', 'lmstudio_annotation_model',
     'LM Studio 视觉模型', 'google/gemma-4-e4b', 'google/gemma-4-e4b',
     'LM Studio 中加载的视觉语言模型 ID',
     datetime('now')),

    ('annotation', 'ollama_base_url',
     'Ollama 接口地址', 'http://localhost:11434', 'http://localhost:11434',
     'Ollama 本地服务地址',
     datetime('now')),

    ('annotation', 'ollama_vision_model',
     'Ollama 视觉模型', 'llava:13b', 'llava:13b',
     'Ollama 视觉模型名称（需先 ollama pull llava:13b）',
     datetime('now')),

    ('annotation', 'gemini_api_key',
     'Gemini API 密钥', '', '',
     '留空则从 GEMINI_API_KEY 环境变量读取',
     datetime('now')),

    ('embedding', 'embedding_provider',
     '嵌入 AI 提供商', 'lmstudio', 'lmstudio',
     '可选值：lmstudio / gemini',
     datetime('now')),

    ('embedding', 'embedding_lmstudio_base_url',
     'LM Studio 嵌入接口地址', 'http://localhost:1234/v1', 'http://localhost:1234/v1',
     'LM Studio 嵌入服务地址（通常与标注接口相同）',
     datetime('now')),

    ('embedding', 'embedding_lmstudio_model',
     'LM Studio 嵌入模型', 'text-embedding-nomic-embed-text-v1.5', 'text-embedding-nomic-embed-text-v1.5',
     'LM Studio 嵌入模型名称',
     datetime('now'));
