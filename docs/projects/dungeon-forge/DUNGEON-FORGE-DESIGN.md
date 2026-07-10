# dungeon-forge 详细设计书

> 状态：设计草案 v0.1（未创建实际项目目录，本文档是唯一落地产物）
> 编写依据：直接阅读 `NetYamlForge/NetYamlForge/Services/AI/*.cs`、`NetYamlForge/NetYamlForge/projects/auto-dealer-demo/*`、
> `NetYamlForge/NetYamlForge/projects/diary-companion/*` 的**真实源码**得出，不是转述之前对话里的描述。

## 0. 重要更正（先纠偏，再设计）

之前几轮对话里的描述存在**没有验证代码就下结论**的问题，这里先澄清，避免设计书建立在错误假设上：

| 之前的说法 | 实际情况（已读源码验证） |
|---|---|
| 项目放在 `Areas/` 目录下 | ❌ 实际约定是 `NetYamlForge/NetYamlForge/projects/<project-name>/`（见 `diary-companion`、`auto-dealer-demo`） |
| "7 个 AI Tool（attack/use_item/search_room...）可以直接 YAML 配置出来" | ❌ 框架目前**只有 2 个自动注册的通用 Tool**：`query_data`（通用实体查询）和 `create_appointment_request`（**硬编码**在汽车经销商预约领域，字段是 `vehicle_model/preferred_date/customer_name` 等），不存在通用的"自定义动作 Tool"机制。想要 `attack`/`use_item` 这类工具，必须新写 C# 代码注册 `ToolDefinition`（见第 5 章），不是配置项。 |
| "DynamicConversationFsm 的 Transition 可以当地牢地图用" | ⚠️ 部分属实但有陷阱：`DynamicConversationFsm` 确实读取 `ScenarioConfig.Transitions`（From/Trigger/To），机制本身通用。但**实际生产项目的 `ai/scenarios.yaml`（auto-dealer-demo）里根本没有写 `transitions:` 字段**，只写了 `tools:` 按状态名分组。也就是说这条路径在真实项目里基本没被使用验证过，直接拿来做"地下城房间图"是没有先例的、需要从零验证的技术方案，不是"复用现成模式"。 |
| Guard Script 动态编译 RCE 风险 | 与本项目关系不大（本项目不依赖 WorkflowEngine 的 Guard 脚本），之前提醒放错了地方。 |

**结论**：这个项目做起来是可行的，但真实工作量比之前描述的"复用度极高、11 天出 MVP"要大，因为战斗/探索这类"自定义动作"必须写后端代码，AI 更多是**叙事者和 NPC**的角色，而不是"自动生成游戏规则引擎"。下面按这个更现实的定位重新设计。

---

## 1. 项目定位

`dungeon-forge`：一个基于文字交互的地下城探索小游戏管理系统。

- **游戏规则（战斗结算、房间导航、物品效果）用普通 C# + SQL 实现**，走 NetYamlForge 标准的 Entity/Hook 体系，稳定可测。
- **AI 只负责"包装"**：把结构化的战斗结果、房间描述转成沉浸式叙事文本（哥特奇幻风格），以及扮演 NPC 对话。
- 不勉强套用 `DynamicConversationFsm` 的 slot-filling 引擎去做房间图——那是为"填表单式对话"（如预约）设计的，语义不匹配，勉强用只会增加不确定性。房间导航直接用 `rooms.exits`（JSON 邻接表）+ 一个 C# 内的 `MoveHook`/Controller 方法实现，比复用 FSM 更可靠。

## 2. 目录结构（遵循真实约定）

```
NetYamlForge/NetYamlForge/projects/dungeon-forge/
├── project.yaml
├── entities/
│   ├── characters.yml
│   ├── rooms.yml
│   ├── monsters.yml
│   ├── items.yml
│   └── battle_logs.yml
├── ai/
│   └── scenarios.yaml          # 仅用于"创角对话"场景，见第 4 章
├── Hooks/
│   ├── CharacterCreationHooks.cs
│   ├── CombatResolutionHooks.cs
│   └── RoomNarrationHooks.cs
├── pages/
│   ├── CharacterSheet.yaml
│   ├── DungeonMap.yaml
│   └── BattleLog.yaml
├── views/
│   └── Dungeon/
│       └── Explore.cshtml       # 自定义交互式探索界面（房间/战斗渲染）
├── database/
│   └── init.sql
├── config/
│   └── home-page.yml
└── docs/
    └── README.md
```

## 3. project.yaml 设计

```yaml
name: dungeon-forge
displayName: "地下城纪元"
version: "0.1.0"
description: "文字交互式地下城探索游戏 —— AI 担任地下城主(DM)叙事者"

database:
  type: sqlite
  path: database/dungeon-forge.db

apiWriteRoles:
  - player
  - game_master

features:
  multiLanguage: false
  userAuthentication: true
  dashboard: true
  pages: true
  api: true

layout:
  dashboardTheme: workspace
  landingPageByRole:
    player: /dungeon-forge/Page/DungeonMap
    game_master: /dungeon-forge/Page/BattleLog
  navigation:
    showDashboard: false
    items:
      - label: 角色面板
        url: /dungeon-forge/Page/CharacterSheet
        icon: 🧙
      - label: 地下城探索
        url: /dungeon-forge/Page/DungeonMap
        icon: 🗺️
      - label: 战斗日志
        url: /dungeon-forge/Page/BattleLog
        icon: ⚔️

settings:
  locale: zh-CN
  timezone: Asia/Shanghai
```

## 4. 实体设计

参照 `diary_entry.yml` 的真实字段规范（`forms` / `columns` / `hooks` / `layout` / `paging`）。以下只列关键字段，完整 YAML 后续按需生成。

### 4.1 characters.yml
- `Name`, `Race`(dropdown), `Class`(dropdown), `Level`(int), `Hp`/`MaxHp`(int), `Mp`/`MaxMp`(int), `Gold`(int), `Experience`(int), `CurrentRoomId`(int, FK 概念), `InventoryJson`(textarea/json), `Status`(dropdown: alive/dead), `OwnerUserId`(关联登录用户)
- hooks: `beforeCreate: [now:CreatedAt, init_starting_stats]`

### 4.2 rooms.yml
- `Name`, `Description`(textarea, 给 AI 叙事用的原始素材), `ExitsJson`（如 `{"north":2,"south":null}`）, `MonsterId`(nullable), `ItemId`(nullable), `RequiredKeyItemId`(nullable，进入门槛), `IsBossRoom`(bool), `IsCleared`(bool)

### 4.3 monsters.yml
- `Name`, `Level`, `Hp`, `Attack`, `Defense`, `LootItemId`(nullable), `FlavorText`(textarea，AI 叙事素材)

### 4.4 items.yml
- `Name`, `Type`(dropdown: weapon/armor/potion/key/quest), `EffectJson`(如 `{"heal":20}` 或 `{"atk_bonus":5}`), `Value`(int)

### 4.5 battle_logs.yml
- `CharacterId`, `MonsterId`, `RoundNumber`, `PlayerAction`(dropdown: attack/defend/use_item/flee), `DamageDealt`, `DamageTaken`, `MonsterHpAfter`, `PlayerHpAfter`, `NarrativeText`(textarea, AI 生成), `CreatedAt`

## 5. Tool / 后端能力设计（重点：这里没有捷径）

框架自动注册的 Tool 只有 `query_data` 和 `create_appointment_request`（见 `AiToolRegistryInitializer.cs`），后者字段语义写死在汽车预约场景，**不能**改名复用给"attack"。要让 AI 具备"攻击/使用道具/移动房间"的行动能力，两个可行方案二选一：

**方案 A（推荐，风险低）**：不给 AI Tool Calling 权限，游戏行为通过普通 Controller/API + 前端按钮触发（如 `/api/dungeon/attack`），C# 里做骰子结算写 `battle_logs`，**结算完之后**再调用 AI（`IAntigravityCliService` 或 `ICliChainService`，即 diary-companion 里用的那两个服务）把结构化结果（伤害数字、剩余 HP）转成一段叙事文本存入 `NarrativeText`。AI 只是"文字渲染器"，不做规则判定，安全、可测、成本低。

**方案 B（更"AI 化"，工作量更大）**：仿照 `AiToolRegistryInitializer` 的模式，新写一个 `DungeonToolRegistryInitializer : IHostedService`，向 `IToolRegistry` 注册 `attack` / `use_item` / `move_room` 等 `ToolDefinition`，每个 `ExecuteAsync` 内部直接操作 DB（同样需要 `SqlSafetyGuard` 做防注入）。这样 AI 可以在对话里直接触发游戏动作，体验更沉浸，但：
  - 需要新的速率限制（之前提过的 "AiToolOrchestrator 缺少全局并发限制" 在这里是真风险，AI 若被诱导连续调用 `attack`，可能刷战斗次数）；
  - 战斗数值判定逻辑如果交给 AI 自由发挥会不稳定，建议 Tool 内部仍是确定性 C# 计算，AI 只传参数（如"对哪个怪物用哪个技能"），不让 AI 直接决定伤害数字。

**MVP 阶段建议先做方案 A**，方案 B 作为二期"AI Native 战斗"增强。

## 6. AI 使用场景（诚实版）

真正适合复用 `SlotFillingManager` + `ai/scenarios.yaml` 的，只有**创角流程**（结构化收集几个字段，和"预约试驾"在语义上真的类似）：

```yaml
# ai/scenarios.yaml
allowed_entities:
  - characters
allowed_actions:
  - create
scenarios:
  create_character:
    description: "创建冒险者角色"
    initial_state: "Init"
    required_slots:
      - name: "character_name"
        prompt: "为你的冒险者取个名字吧"
        is_required: true
      - name: "race"
        prompt: "选择种族：人类 / 精灵 / 矮人 / 兽人"
        is_required: true
      - name: "class"
        prompt: "选择职业：战士 / 法师 / 盗贼 / 牧师"
        is_required: true
    tools:
      Init:
        - "query_data"
```

> 注意：不写 `transitions:`（和真实的 auto-dealer-demo 保持一致的用法），意味着状态流转依赖框架默认行为——**这一点需要在实现前专门写单元测试验证 `SlotFillingManager` 在没有 `transitions` 时到底如何推进状态**，不能假设它会像预约场景一样正常工作，因为预约场景的真实运行路径也没有被我在本次审查中完全验证到底。这是本设计书**明确标注的技术风险点**，建议列为 MVP 第一步的技术预研（spike），而不是直接假设可用。

房间探索、战斗对话中的 NPC/怪物台词，走"直接调用 AI 服务 + Prompt 模板"的方式（`IAntigravityCliService`），不经过 SlotFillingManager/FSM。

## 7. Hook 设计（C# 骨架，遵循真实 IEntityHook 接口）

```csharp
namespace NetYamlForge.Projects.DungeonForge.Hooks;

public class InitStartingStatsHook : IEntityHook
{
    public string Name => "init_starting_stats";

    public Task<HookResult> BeforeAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        // Class 决定初始 HP/MP/Attack，写回 ctx.Values
        var cls = ctx.Values.GetValueOrDefault("Class")?.ToString() ?? "warrior";
        var (hp, mp) = cls switch
        {
            "法师" => (60, 100),
            "牧师" => (70, 80),
            "盗贼" => (80, 30),
            _        => (100, 10), // 战士
        };
        ctx.Values["Hp"] = hp; ctx.Values["MaxHp"] = hp;
        ctx.Values["Mp"] = mp; ctx.Values["MaxMp"] = mp;
        ctx.Values["CurrentRoomId"] = 1; // 起始房间
        return Task.FromResult(HookResult.Continue());
    }
}
```

战斗结算（`CombatResolutionHooks.cs`）建议放在独立的 API Controller 而不是 `beforeCreate` hook 里，因为战斗涉及"读怪物状态 -> 算伤害 -> 写日志 -> 判断死亡 -> 掉落物品"多步操作，塞进单个实体 hook 会让职责不清晰；hook 体系更适合"保存前/保存后做一件事"，不适合多实体编排。多实体编排建议用一个普通的 `IDungeonCombatService`，在 Controller 里调用。

## 8. MVP 路线图（现实版本，非之前的"11 天"乐观估计）

| 阶段 | 内容 | 说明 |
|---|---|---|
| 0. 技术预研（1-2 天） | 验证 `SlotFillingManager` 在无 `transitions` 时的真实行为；确认 `IAntigravityCliService`/`ICliChainService` 的调用方式和成本 | 不做这步，后面全是空中楼阁 |
| 1. 数据层（2-3 天） | 5 个 entities YAML + `database/init.sql` + 种子数据（起始房间、几只怪物、几件道具） | |
| 2. 核心玩法（4-6 天） | 房间移动 API、战斗结算 Service（方案 A）、角色创建 Hook | 不涉及 AI，先保证纯规则可玩 |
| 3. AI 叙事层（3-4 天） | 创角对话场景、战斗结果转叙事文本、NPC 对话 Prompt | |
| 4. 界面（3-4 天） | `DungeonMap` / `CharacterSheet` / `BattleLog` 页面 + 自定义 `Explore.cshtml` | |

合计约 13-19 天出可玩 MVP（比此前"11 天"的估计更保守，因为把"技术预研"和"Tool 机制不存在需要新写"这两块之前被忽略的工作量算进去了）。

## 9. 开放问题（需要你确认后才能真正开工）

1. 战斗行为走**方案 A（按钮+API，AI 只叙事）**还是**方案 B（AI Tool Calling 直接触发动作）**？这决定了 4-6 天 vs 更长的后端工作量。
2. 是否要求多人共享同一个地下城（多角色互动），还是单人 Roguelike？这影响 `rooms`/`characters` 是否需要按会话隔离。
3. AI 输出（叙事文本）是否要做 HTML 转义后再渲染到 `views/Dungeon/Explore.cshtml`？（沿用之前提醒：AI 生成内容入库/渲染前必须消毒，防 XSS）

---

*本文档由直接阅读源码验证后编写；如需我现在开始落地阶段 1（写 5 个 entities YAML + database/init.sql），请明确告诉我，我会真正创建文件，而不是仅在对话里描述。*
