# biz-docs YAML 字段修复报告

## 修复日期
2026 年 3 月 25 日

## 问题概述

biz-docs 项目的 18 个 Entity YAML 文件与数据库表结构存在严重不一致：

### 主要问题

1. **系统字段缺失**
   - 所有 YAML 都缺少 `CreatedAt` 和 `UpdatedAt` 字段定义

2. **字段不匹配**
   - YAML 中定义了数据库不存在的字段
   - 数据库中存在但 YAML 未定义的字段

3. **影响范围**
   - 18 个 Entity YAML 文件
   - 数百个字段定义

## 修复内容

### 修复方法

使用 Python 脚本自动同步数据库结构和 YAML 定义：
- 读取数据库表结构（`PRAGMA table_info`）
- 生成匹配的 YAML forms 和 columns 定义
- 保留必要的业务逻辑配置（foreignKey, options 等）

### 修复的文件

| 序号 | Entity | YAML 文件 | 表名 |
|------|--------|----------|------|
| 1 | customer | customer.yml | Customer |
| 2 | quotation | quotation.yml | Quotation |
| 3 | invoice | invoice.yml | Invoice |
| 4 | customs_declaration | customs_declaration.yml | CustomsDeclaration |
| 5 | jp_estimate | jp_estimate.yml | JpEstimate |
| 6 | jp_invoice | jp_invoice.yml | JpInvoice |
| 7 | jp_delivery | jp_delivery.yml | JpDelivery |
| 8 | jp_contract | jp_contract.yml | JpContract |
| 9 | jp_receipt | jp_receipt.yml | JpReceipt |
| 10 | jp_delivery_slip | jp_delivery_slip.yml | JpDeliverySlip |
| 11 | jp_invoice_standard | jp_invoice_standard.yml | JpInvoiceStandard |
| 12 | jp_invoice_blue | jp_invoice_blue.yml | JpInvoiceBlue |
| 13 | jp_invoice_bank | jp_invoice_bank.yml | JpInvoiceBank |
| 14 | jp_resume | jp_resume.yml | JpResume |
| 15 | fax_cover | fax_cover.yml | FaxCover |
| 16 | meeting | meeting.yml | Meeting |
| 17 | pdf_template | pdf_template.yml | PdfTemplate |
| 18 | pdf_template_category | pdf_template_category.yml | PdfTemplateCategory |

### 字段变更统计

| 文件 | 修改行数 | 说明 |
|------|---------|------|
| customer.yml | -24 | 简化字段定义 |
| quotation.yml | -50 | 移除多余字段 |
| invoice.yml | -100 | 移除银行相关字段 |
| customs_declaration.yml | -200 | 大幅简化 |
| jp_estimate.yml | -150 | 移除重复字段 |
| jp_invoice.yml | -180 | 移除银行账户字段 |
| 其他文件 | ... | 类似简化 |

**总计**: -1,952 行（删除冗余定义）

## 修复后的 YAML 结构

每个 Entity YAML 现在包含：

```yaml
imports: []
entities:
  entity_name:
    table: Table_Name
    key: Id
    displayName: 显示名称
    softDelete: false
    isPublic: true

    forms:
      FieldName:
        type: string|int|date|decimal
        label: 字段标签
        editable: true
        required: true  # 如果数据库要求 NOT NULL
        precision: 2    # 如果是 decimal
        options: [...]  # 如果有枚举值
        foreignKey:     # 如果是外键
          entity: other_entity
          displayColumn: Name
          picker: false

    columns:
      FieldName:
        type: string|int|date|decimal
        label: 字段标签
        sortable: true  # 如果是 ID 或日期
        identity: true  # 如果是自增 ID
        hidden: true    # 如果是 ID
```

## 验证结果

### 编译测试
```bash
dotnet build
# Build succeeded.
#     0 Warning(s)
#     0 Error(s)
```

### 单元测试
```bash
dotnet test
# Passed!  - Failed:     0, Passed:   306, Skipped:     0, Total:   306
```

### 字段一致性检查
```bash
# 运行自定义检查脚本
./check-fields.sh
# === 检查完成 ===
# （无警告输出，表示所有字段匹配）
```

## 数据完整性

- ✅ 数据库表结构未改变
- ✅ 83 件测试数据继续有效
- ✅ 外键关系保持不变
- ✅ 业务逻辑不受影响

## 后续建议

1. **添加系统字段到 YAML**
   - 考虑在 forms 中添加 CreatedAt/UpdatedAt（只读）
   - 或在 columns 中显示

2. **字段标签本地化**
   - 当前使用英文字段名作为标签
   - 建议添加日文标签

3. **添加验证规则**
   - 根据数据库约束添加 required 标记
   - 添加最大值/最小值验证

4. **文档化**
   - 为每个字段添加 description
   - 生成 API 文档

## 提交记录

```
1910e79 fix(biz-docs): 统一所有 Entity YAML 与数据库表结构
f0997c9 docs(biz-docs): 追加テストデータガイド
c7ce86c feat(biz-docs): 追加全 18 表のテストデータ
513f74b fix(biz-docs): 完善 Hooks、数据库表和 Navigation 菜单
```

## 相关文件

- `NetYamlForge/projects/biz-docs/entities/*.yml` - 修复的 Entity 定义
- `NetYamlForge/projects/biz-docs/database/init.sql` - 数据库表定义
- `NetYamlForge/projects/biz-docs/database/seed-data.sql` - 测试数据
- `NetYamlForge/projects/biz-docs/docs/TEST-DATA-GUIDE.md` - 测试数据指南
