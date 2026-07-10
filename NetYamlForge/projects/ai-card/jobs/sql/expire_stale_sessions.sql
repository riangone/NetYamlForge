-- 期限切れセッション処理
-- 有効期限を過ぎた pending/connected セッションを expired に遷移させる

UPDATE handshake_session
SET state = 'expired',
    updated_at = datetime('now')
WHERE state IN ('pending', 'connected')
  AND expires_at IS NOT NULL
  AND expires_at < datetime('now');
