-- クリーンアップ用 SQL
DELETE FROM HookDemo WHERE IsArchived = 1 AND CompletedAt < date('now', '-90 days');
DELETE FROM BatchJobDemo WHERE Status = 'failed' AND LastRunAt < date('now', '-30 days');
