# 复合主键（多主键）配置指南

## 概述

本系统支持复合主键（多主键）的实体定义。通过 YAML 配置中的 `keys` 属性，可以定义由多个列组成的主键。

## YAML 配置语法

### 基本语法

```yaml
entities:
  orderdetail:
    table: OrderDetail
    # 单主键方式（向后兼容）
    # key: OrderDetailId
    
    # 复合主键方式（新）
    keys: ["OrderId", "ProductId"]
    
    # 或者同时指定（keys 优先）
    key: OrderDetailId  # 単一主鍵の場合
    keys: ["OrderId", "ProductId"]  # こちらが優先される
    
    displayName: Order Detail
    displayNameI18n:
      en-US: Order Detail
      zh-CN: 订单明细
      ja-JP: 注文詳細
```

### 完整示例

```yaml
entities:
  orderdetail:
    table: OrderDetail
    keys: ["OrderId", "ProductId"]
    displayName: Order Detail
    softDelete: false
    
    paging:
      pageSize: 20
      mode: numbered
    
    columns:
      OrderId:
        type: int
        label: Order ID
        searchable: false
        sortable: true
        
      ProductId:
        type: int
        label: Product ID
        searchable: false
        sortable: true
        
      Quantity:
        type: int
        label: Quantity
        searchable: false
        sortable: true
        
      UnitPrice:
        type: decimal
        label: Unit Price
        searchable: false
        sortable: true
    
    forms:
      OrderId:
        type: int
        label: Order ID
        editable: false  # 主鍵は編集不可
        required: true
        
      ProductId:
        type: int
        label: Product ID
        editable: false  # 主鍵は編集不可
        required: true
        foreignKey:
          entity: product
          displayColumn: Name
          
      Quantity:
        type: int
        label: Quantity
        editable: true
        required: true
        
      UnitPrice:
        type: decimal
        label: Unit Price
        editable: true
        required: true
    
    filters:
      OrderId:
        type: dropdown
        label: Order
        expression: OrderDetail.OrderId
        
      ProductId:
        type: dropdown
        label: Product
        expression: OrderDetail.ProductId
        foreignKey:
          entity: product
          displayColumn: Name
```

## 主键定义规则

### 1. 单主键（向后兼容）

```yaml
entities:
  customer:
    table: Customer
    key: CustomerId  # 単一主鍵
    # keys: []  # 不要
```

### 2. 复合主键

```yaml
entities:
  orderdetail:
    table: OrderDetail
    keys: ["OrderId", "ProductId"]  # 複合主鍵
```

### 3. 优先级规则

- 如果同时指定了 `key` 和 `keys`，`keys` 优先
- 如果只指定了 `key`，则作为单主键处理
- 如果 `keys` 为空或未指定，则使用 `key`

## 系统行为

### 主键列的自动识别

系统通过以下方法自动识别主键类型：

```csharp
var pkColumns = meta.GetPrimaryKeyColumns();
// 単一主鍵の場合：["CustomerId"]
// 複合主鍵の場合：["OrderId", "ProductId"]

var isComposite = meta.IsCompositeKey;
// true = 複合主鍵、false = 単一主鍵
```

### URL 格式

#### 单主键
```
/chinook/DynamicEntity/EditPage?entity=customer&id=123
```

#### 复合主键
```
/chinook/DynamicEntity/EditPage?entity=orderdetail&id={"OrderId":1001,"ProductId":5}
```

### 内部处理

系统自动处理单主键和复合主键的区别：

1. **GET 请求（EditPage/EditForm）**
   - 复合主键：JSON 形式から主鍵値を解析
   - 単一主鍵：そのまま使用

2. **POST 请求（Edit/Delete）**
   - 复合主键：主鍵値マップを使用して更新・削除
   - 単一主鍵：ID を直接使用

3. **审计日志**
   - 复合主键：JSON 形式で記録
   - 単一主鍵：数値で記録

## 实际示例

### 示例 1：订单明细表

```yaml
entities:
  orderdetail:
    table: OrderDetail
    keys: ["OrderId", "ProductId"]
    displayName: Order Detail
    
    columns:
      OrderId: { type: int, label: Order ID }
      ProductId: { type: int, label: Product ID }
      Quantity: { type: int, label: Quantity }
      UnitPrice: { type: decimal, label: Unit Price }
    
    forms:
      OrderId: { type: int, editable: false, required: true }
      ProductId: { type: int, editable: false, required: true }
      Quantity: { type: int, editable: true, required: true }
      UnitPrice: { type: decimal, editable: true, required: true }
```

### 示例 2：角色权限表

```yaml
entities:
  rolepermission:
    table: RolePermission
    keys: ["RoleId", "PermissionId"]
    displayName: Role Permission
    
    columns:
      RoleId: { type: int, label: Role ID }
      PermissionId: { type: int, label: Permission ID }
      Granted: { type: bool, label: Granted }
    
    forms:
      RoleId: { type: int, editable: false, required: true }
      PermissionId: { type: int, editable: false, required: true }
      Granted: { type: bool, editable: true, required: true }
```

### 示例 3：学生选课表

```yaml
entities:
  studentcourse:
    table: StudentCourse
    keys: ["StudentId", "CourseId"]
    displayName: Student Course
    
    columns:
      StudentId: { type: int, label: Student ID }
      CourseId: { type: int, label: Course ID }
      EnrollmentDate: { type: date, label: Enrollment Date }
      Grade: { type: string, label: Grade }
    
    forms:
      StudentId: { type: int, editable: false, required: true }
      CourseId: { type: int, editable: false, required: true }
      EnrollmentDate: { type: date, editable: true, required: true }
      Grade: { type: string, editable: true }
```

## 注意事项

1. **主键列不可编辑**
   - 复合主键的列通常在表单中设置为 `editable: false`
   - 主键值在创建后不应更改

2. **外键关联**
   - 复合主键的列可以定义外键关联
   - 使用 `foreignKey` 属性指定关联实体

3. **分页和排序**
   - 复合主键实体使用第一个主键列进行分页
   - 可以正常设置 `sortable: true`

4. **审计日志**
   - 复合主键的 ID 以 JSON 形式记录
   - 例：`{"OrderId":1001,"ProductId":5}`

## API 参考

### EntityDefinition 方法

```csharp
// 主鍵列のリストを取得
IReadOnlyList<string> GetPrimaryKeyColumns()

// 複合主鍵かどうか
bool IsCompositeKey { get; }
```

### IDynamicCrudRepository 方法

```csharp
// 単一主鍵用
Task<dynamic?> GetByIdAsync(string entity, object id);
Task<int> UpdateAsync(string entity, object id, IDictionary<string, object?> values, IDbTransaction? tx = null);
Task<int> DeleteAsync(string entity, object id, IDbTransaction? tx = null);

// 複合主鍵用
Task<dynamic?> GetByIdAsync(string entity, IDictionary<string, object?> keyValues);
Task<int> UpdateAsync(string entity, IDictionary<string, object?> keyValues, IDictionary<string, object?> values, IDbTransaction? tx = null);
Task<int> DeleteAsync(string entity, IDictionary<string, object?> keyValues, IDbTransaction? tx = null);
```

## 验证步骤

1. YAML 配置完成后，执行 `dotnet build` 确认无编译错误
2. 访问实体一覧ページ，確認分頁和排序功能
3. 测试新建、编辑、删除操作
4. 確認审计日志中的主键记录格式
