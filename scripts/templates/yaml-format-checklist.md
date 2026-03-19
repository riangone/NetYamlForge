# NetYamlForge 项目 YAML 格式检查清单

## 实体 YAML (entities/*.yml)

### 必需字段
- [ ] `displayNameKey` - 国际化键名
- [ ] `displayNameI18n` - 多语言显示名称（ja/en/zh）
- [ ] `table` - 数据库表名
- [ ] `key` - 主键字段名

### 字段类型（使用正确的类型名）
- [ ] `int` - 整数（不是 number）
- [ ] `string` - 字符串
- [ ] `text` - 长文本
- [ ] `decimal` - 小数（不是 number）
- [ ] `boolean` - 布尔值（不是 bool）
- [ ] `datetime` / `string` - 日期时间

### 主键字段
- [ ] `identity: true`（不是 isIdentity）

### 钩子格式
- [ ] 使用字符串格式：`- trim:name`
- [ ] 不是对象格式：`- name: trim, field: name` ❌

### 外键关联
- [ ] `foreignKey.entity` - 关联实体名
- [ ] `foreignKey.displayColumn` - 显示字段

---

## 项目 YAML (project.yaml)

### 必需字段
- [ ] `name` - 项目名（小写，连字符）
- [ ] `displayName` - 显示名称
- [ ] `version` - 版本号
- [ ] `database.type` - 数据库类型
- [ ] `database.path` - SQLite 路径

### 布局配置
- [ ] `layout.dashboardTheme` - 仪表板主题
- [ ] `layout.navigation.entities` - 导航实体列表

---

## 仪表板 YAML (dashboard.yml)

### 结构
- [ ] `dashboard.sections` - 区块数组
- [ ] `type` - 区块类型（stats/table/chart）
- [ ] `query` - SQL 查询

---

## 批处理作业 YAML (jobs/*.yml)

### 必需字段
- [ ] `jobs.<job_id>` - 作业 ID
- [ ] `schedule.cron` - Cron 表达式
- [ ] `type` - 作业类型（sql_to_csv/sql_command）
- [ ] `settings.sqlFile` 或 `settings.sqlQuery`
- [ ] `settings.outputFile` - 输出文件（sql_to_csv）

---

## 常见错误

### ❌ 错误：字段类型使用 number
```yaml
columns:
  price:
    type: number  # ❌
```

### ✅ 正确：使用 int 或 decimal
```yaml
columns:
  price:
    type: decimal  # ✅
  count:
    type: int  # ✅
```

---

### ❌ 错误：钩子使用对象格式
```yaml
hooks:
  beforeCreate:
    - name: trim
      field: name  # ❌
```

### ✅ 正确：使用字符串格式
```yaml
hooks:
  beforeCreate:
    - trim:name  # ✅
```

---

### ❌ 错误：缺少国际化字段
```yaml
entities:
  company:
    displayName: 公司  # ❌ 缺少 displayNameKey 和 displayNameI18n
```

### ✅ 正确：包含所有国际化字段
```yaml
entities:
  company:
    displayName: 公司
    displayNameKey: company_display
    displayNameI18n:
      ja: 会社
      en: Company
      zh: 公司
```

---

### ❌ 错误：主键使用 isIdentity
```yaml
columns:
  id:
    type: int
    isIdentity: true  # ❌
```

### ✅ 正确：使用 identity
```yaml
columns:
  id:
    type: int
    identity: true  # ✅
```

---

## 验证步骤

1. [ ] 复制 `scripts/templates/entity-template.yml` 作为起点
2. [ ] 参考现有项目（shop/inventory）的 YAML 格式
3. [ ] 使用 `--scaffold-entities` 生成初始 YAML
4. [ ] 运行应用查看是否有 YAML 解析错误
5. [ ] 检查日志中的错误信息

---

## 有用的命令

```bash
# 从数据库生成 YAML（推荐）
dotnet run --project NetYamlForge -- --scaffold-entities --project=your-project

# 查看启动日志中的 YAML 错误
dotnet run --project NetYamlForge 2>&1 | grep -i "yaml\|error\|schema"
```
