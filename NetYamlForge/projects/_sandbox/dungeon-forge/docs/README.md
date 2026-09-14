# 地下城纪元 (Dungeon Forge)

文字交互式地下城探索游戏管理系统。

## 项目定位

AI **不参与**游戏规则判定（战斗结算、房间导航、物品效果），这些由确定性 C# + SQL 实现。
AI 只负责"包装"——将结构化的战斗结果、房间描述转成沉浸式叙事文本，以及扮演 NPC 对话。

## 目录结构

```
projects/dungeon-forge/
├── project.yaml              # 项目配置
├── entities/                 # 实体 YAML 定义
│   ├── characters.yml        # 冒险者角色
│   ├── rooms.yml             # 地下城房间
│   ├── monsters.yml          # 怪物
│   ├── items.yml             # 物品
│   └── battle_logs.yml       # 战斗日志
├── ai/
│   └── scenarios.yaml        # AI 创角对话场景（Slot Filling）
├── Hooks/                    # C# Hook 实现
│   ├── CharacterCreationHooks.cs
│   ├── CombatResolutionHooks.cs
│   └── RoomNarrationHooks.cs
├── pages/                    # 页面定义
│   ├── CharacterSheet.yaml
│   ├── DungeonMap.yaml
│   └── BattleLog.yaml
├── views/Dungeon/
│   └── Explore.cshtml        # 自定义探索界面
├── database/
│   └── init.sql              # 种子数据 + 索引
└── config/
    └── home-page.yml         # 首页配置
```

## 实体关系

- **characters** — 玩家角色，关联 `CurrentRoomId` 指向当前位置
- **rooms** — 地下城房间，`ExitsJson` 存储邻接表，`MonsterId`/`ItemId` 可选关联
- **monsters** — 怪物数据，`LootItemId` 指向掉落物品
- **items** — 物品（武器/药水/钥匙/任务道具），`EffectJson` 定义效果
- **battle_logs** — 每回合战斗记录，含 AI 生成的 `NarrativeText`

## 战斗方案（方案 A）

当前 MVP 采用**方案 A**：游戏行为通过 API Controller 触发 → C# 做骰子结算写入 `battle_logs` → 结算后调用 AI 将结构化结果转为叙事文本。
AI 不做规则判定，只做文字渲染。

## 种子数据

数据库初始化包含 5 个房间、3 种怪物和 4 件物品，形成一个可通关的迷你地下城：

```
入口大厅(1) → 阴暗走廊(2) → 兵器库(3)
                   ↓
              牢房区(4) → Boss房间(5)
```

## 开发路线

| 阶段 | 内容 | 状态 |
|---|---|---|
| 0. 技术预研 | SlotFillingManager 行为验证 | 待开始 |
| 1. 数据层 | 实体 YAML + 种子数据 | ✅ 完成 |
| 2. 核心玩法 | 房间移动 API、战斗结算 Service、角色创建 Hook | 待开始 |
| 3. AI 叙事层 | 创角对话、战斗叙事、NPC 对话 Prompt | 待开始 |
| 4. 界面 | 页面 + Explore.cshtml | 待开始 |
