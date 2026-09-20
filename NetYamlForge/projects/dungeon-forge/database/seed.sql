-- dungeon-forge: 地下城探索游戏种子数据
-- 实体表已由 YAML 自动创建，此处仅插入种子数据

-- 角色表索引
CREATE INDEX IF NOT EXISTS idx_characters_owner ON characters(OwnerUserId);
CREATE INDEX IF NOT EXISTS idx_characters_room ON characters(CurrentRoomId);

-- 战斗日志索引
CREATE INDEX IF NOT EXISTS idx_battle_logs_character ON battle_logs(CharacterId);
CREATE INDEX IF NOT EXISTS idx_battle_logs_created ON battle_logs(CreatedAt);

-- 房间索引
CREATE INDEX IF NOT EXISTS idx_rooms_monster ON rooms(MonsterId);

-- 种子数据：起始房间
INSERT OR IGNORE INTO rooms (Id, Name, Description, ExitsJson, MonsterId, ItemId, IsBossRoom, IsCleared)
VALUES (1, '破旧的入口大厅', '一座废弃堡垒的入口大厅，石壁上爬满藤蔓，空气中弥漫着潮湿的霉味。你脚下的石板已经松动，远处传来滴水的声音。',
        '{"north": 2}', NULL, NULL, 0, 0);

INSERT OR IGNORE INTO rooms (Id, Name, Description, ExitsJson, MonsterId, ItemId, IsBossRoom, IsCleared)
VALUES (2, '阴暗的走廊', '一条狭窄的走廊，墙壁上插着几支即将燃尽的火把。影子在火光中扭曲舞动，你似乎听到了什么在暗处呼吸。',
        '{"south": 1, "east": 3, "west": 4}', 1, 1, 0, 0);

INSERT OR IGNORE INTO rooms (Id, Name, Description, ExitsJson, MonsterId, ItemId, IsBossRoom, IsCleared)
VALUES (3, '废弃的兵器库', '散落着锈蚀刀剑的房间，角落里有一个落满灰尘的宝箱。墙上挂着的一面破损盾牌上刻着古老的徽章。',
        '{"west": 2}', NULL, 2, 0, 0);

INSERT OR IGNORE INTO rooms (Id, Name, Description, ExitsJson, MonsterId, ItemId, IsBossRoom, IsCleared)
VALUES (4, '潮湿的牢房区', '一排锈蚀的铁栅栏分隔出数个狭小牢房，其中一间的地面上画着诡异的符文。空气冰冷刺骨。',
        '{"east": 2, "north": 5}', 2, NULL, 0, 0);

INSERT OR IGNORE INTO rooms (Id, Name, Description, ExitsJson, MonsterId, ItemId, IsBossRoom, IsCleared)
VALUES (5, 'Boss 房间：古老祭坛', '宽阔的圆形大厅中央矗立着一座散发着暗红色光芒的古老祭坛，周围的石柱上缠绕着锁链。一个巨大的身影在祭坛前等待着你。',
        '{"south": 4}', 3, 3, 1, 0);

-- 种子数据：怪物
INSERT OR IGNORE INTO monsters (Id, Name, Level, Hp, Attack, Defense, LootItemId, FlavorText)
VALUES (1, '洞穴巨鼠', 1, 20, 5, 1, NULL, '一只体型如野猫般巨大的老鼠，赤红色的眼睛在黑暗中闪烁，发出威胁性的吱吱声。');

INSERT OR IGNORE INTO monsters (Id, Name, Level, Hp, Attack, Defense, LootItemId, FlavorText)
VALUES (2, '骷髅兵', 2, 35, 8, 3, NULL, '一具身披残破铠甲的不死骷髅，空洞的眼眶中跳动着一缕幽蓝的灵魂之火，手持锈剑缓缓向你走来。');

INSERT OR IGNORE INTO monsters (Id, Name, Level, Hp, Attack, Defense, LootItemId, FlavorText)
VALUES (3, '暗影领主', 5, 150, 15, 8, 4, '一个由纯粹暗影构成的巨大形体，两颗猩红的光点在"面部"位置燃烧，低沉的声音在厅中回荡："又一个误入禁地的蠢货……"');

-- 种子数据：物品
INSERT OR IGNORE INTO items (Id, Name, Type, EffectJson, Value)
VALUES (1, '生命药水', 'potion', '{"heal": 30}', 15);

INSERT OR IGNORE INTO items (Id, Name, Type, EffectJson, Value)
VALUES (2, '铁剑', 'weapon', '{"atk_bonus": 5}', 50);

INSERT OR IGNORE INTO items (Id, Name, Type, EffectJson, Value)
VALUES (3, '古老钥匙', 'key', '{"unlock": "boss_door"}', 0);

INSERT OR IGNORE INTO items (Id, Name, Type, EffectJson, Value)
VALUES (4, '暗影之核', 'quest', '{"quest": "shadow_core"}', 200);
