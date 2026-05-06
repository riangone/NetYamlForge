# biz-docs 项目修复报告

## 修复日期
2026 年 3 月 25 日

## 问题概述

biz-docs 子项目存在以下问题：
1. **缺少 Hooks 目录** - 没有项目特定的验证 Hook
2. **数据库表缺失** - 只有系统表，没有业务实体表
3. **Navigation 菜单不完整** - 与 entity 定义不一致，缺少多个 entity

## 修复内容

### 1. 创建 Hooks 目录和验证 Hook

**文件**: `NetYamlForge/projects/biz-docs/Hooks/BizDocsHooks.cs`

创建了以下验证 Hook：
- `ValidateQuotationStatusHook` - 验证報価単状态（draft/sent/accepted/rejected/expired）
- `ValidateInvoiceStatusHook` - 验证請款書状态（draft/issued/paid/overdue/cancelled）
- `ValidateCustomsDeclarationStatusHook` - 验证報関単状态（draft/submitted/approved/rejected/cleared）
- `ValidateTaxRateHook` - 验证税率范围（0-100%）
- `ValidateAmountPositiveHook` - 验证金额非负
- `ValidatePdfTemplateStatusHook` - 验证 PDF 模板状态（active/inactive/draft）
- `ValidateCurrencyHook` - 验证通货代码（USD/EUR/CNY/JPY）

### 2. 创建数据库初始化脚本

**文件**: `NetYamlForge/projects/biz-docs/database/init.sql`

创建了以下表：
- `Customer` - 取引先マスタ
- `Quotation` - 報価単
- `Invoice` - 請款書
- `CustomsDeclaration` - 報関単
- `PdfTemplate` - PDF テンプレート
- `PdfTemplateCategory` - PDF テンプレートカテゴリ
- `JpEstimate` - 見積書（日本国内用）
- `JpInvoice` - 請求書（日本国内用）
- `JpInvoiceStandard` - 請求書（標準デザイン）
- `JpInvoiceBlue` - 請求書（青いデザイン）
- `JpInvoiceBank` - 請求書（銀行用）
- `JpDelivery` - 納品書
- `JpDeliverySlip` - 送付状
- `JpReceipt` - 領収書
- `JpContract` - 契約書台帳
- `JpResume` - 履歴書
- `FaxCover` - ファックス表紙
- `Meeting` - 会議録

并添加了初始数据：
- PDF テンプレートカテゴリ（3 件）
- 取引先マスタ（4 件サンプル）

### 3. 更新 Navigation 菜单

**文件**: `NetYamlForge/projects/biz-docs/project.yaml`

**添加的 entity**:
- `jp_invoice_standard` - 請求書（標準）
- `jp_invoice_blue` - 請求書（青）
- `jp_invoice_bank` - 請求書（銀行用）
- `jp_delivery_slip` - 送付状
- `jp_receipt` - 領収書
- `jp_resume` - 履歴書
- `fax_cover` - ファックス表紙
- `meeting` - 会議録

**更新的菜单结构**:
```
貿易文書
  - 報価単
  - 請款書
  - 報関単

国内文書
  - 見積書
  - 請求書
  - 請求書（標準）
  - 請求書（青）
  - 請求書（銀行用）
  - 納品書
  - 送付状
  - 領収書
  - 契約書台帳

その他
  - 履歴書
  - ファックス表紙
  - 会議録

テンプレート管理
  - PDF テンプレート
  - テンプレートカテゴリ

マスタ
  - 取引先
```

### 4. 为 Entity 添加 Hooks 配置

**更新的文件**:
- `entities/quotation.yml` - 添加状态、税率、金额、通貨验证
- `entities/invoice.yml` - 添加状态、税率、金额、通貨验证
- `entities/customs_declaration.yml` - 添加状态、金额、通貨验证
- `entities/pdf_template.yml` - 添加状态验证

## 验证结果

### 编译测试
```
Build succeeded.
    0 Warning(s)
    0 Error(s)
```

### 单元测试
```
Passed!  - Failed:     0, Passed:   306, Skipped:     0, Total:   306
```

### 数据库表验证
```
Customer             JpDelivery           PdfTemplate
CustomsDeclaration   JpDeliverySlip       PdfTemplateCategory
FaxCover             JpEstimate           Quotation
Invoice              JpInvoice            JpReceipt
JpContract           JpInvoiceBank        JpResume
JpDelivery           JpInvoiceBlue        Meeting
                     JpInvoiceStandard
```

## 版本更新

**project.yaml**: `1.1.0` → `1.2.0`

## 后续建议

1. **数据迁移** - 如果已有生产数据，需要创建数据迁移脚本
2. **Hook 测试** - 为新增的 Hook 添加单元测试
3. **PDF 模板关联** - 为新增的 entity 配置 PDF 模板
4. **权限设置** - 检查并配置各 entity 的访问权限

## 文件清单

### 新增文件
- `NetYamlForge/projects/biz-docs/Hooks/BizDocsHooks.cs`
- `NetYamlForge/projects/biz-docs/database/init.sql`

### 修改文件
- `NetYamlForge/projects/biz-docs/project.yaml`
- `NetYamlForge/projects/biz-docs/entities/quotation.yml`
- `NetYamlForge/projects/biz-docs/entities/invoice.yml`
- `NetYamlForge/projects/biz-docs/entities/customs_declaration.yml`
- `NetYamlForge/projects/biz-docs/entities/pdf_template.yml`

### 临时文件（不提交）
- `NetYamlForge/projects/biz-docs/database/biz-docs.db.bak` - 备份文件
