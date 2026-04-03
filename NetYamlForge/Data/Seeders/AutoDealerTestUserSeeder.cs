// ファイル概要: auto-dealer-demo プロジェクト用の包括的なテストユーザーシーダーです。
// 従業員（全ロール）、顧客、第三者ユーザーを作成します。
// 既に存在するユーザーはスキップします。パスワードはすべて Test@123! です。

using System.Data;
using Dapper;
using NetYamlForge.Models.Auth;
using Microsoft.AspNetCore.Identity;

namespace NetYamlForge.Data.Seeders;

/// <summary>
/// auto-dealer-demo プロジェクト用テストユーザーシード。
// 销售、服务、管理、客户等各类角色的测试账户。
/// </summary>
public class AutoDealerTestUserSeeder
{
    private readonly PasswordHasher<AppUser> _hasher = new();

    /// <summary>
    /// auto-dealer-demo 用テストユーザーを作成します（存在しない場合のみ）。
    /// </summary>
    public async Task EnsureAutoDealerTestUsersAsync(IDbConnection conn, ILogger logger)
    {
        var now = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
        var createdUserCount = 0;
        var createdEmployeeCount = 0;
        var createdCustomerCount = 0;

        // 确保员工表存在
        if (!await TableExistsAsync(conn, "employees"))
        {
            logger.LogWarning("employees テーブルが存在しないため、従業員データの作成をスキップします");
        }
        else
        {
            // 创建各类员工用户（覆盖所有业务角色）
            var employees = new[]
            {
                // 销售部门
                ("EMP-SALES-001", "sales_rep1", "张伟销售", "sales", "staff", "sales_rep", "张伟", "zhangwei@autodealer.com", "13800138001"),
                ("EMP-SALES-002", "sales_rep2", "李娜销售", "sales", "senior_staff", "sales_rep", "李娜", "lina@autodealer.com", "13800138002"),
                ("EMP-SALES-003", "sales_rep3", "王强销售", "sales", "staff", "sales_rep", "王强", "wangqiang@autodealer.com", "13800138003"),
                
                // 销售经理
                ("EMP-SALES-MGR-001", "sales_manager1", "刘洋经理", "sales", "manager", "sales_manager", "刘洋", "liuyang@autodealer.com", "13800138010"),
                ("EMP-SALES-MGR-002", "sales_manager2", "陈静经理", "sales", "manager", "sales_manager", "陈静", "chenjing@autodealer.com", "13800138011"),
                
                // 服务部门
                ("EMP-SVC-001", "service_staff1", "赵明服务", "service", "staff", "service_staff", "赵明", "zhaoming@autodealer.com", "13800138020"),
                ("EMP-SVC-002", "service_staff2", "孙丽服务", "service", "senior_staff", "service_staff", "孙丽", "sunli@autodealer.com", "13800138021"),
                
                // 服务经理
                ("EMP-SVC-MGR-001", "service_manager1", "周华服务经理", "service", "manager", "service_staff", "周华", "zhouhua@autodealer.com", "13800138030"),
                
                // 客服/操作员
                ("EMP-OPS-001", "operator1", "吴芳客服", "administration", "staff", "operator", "吴芳", "wufang@autodealer.com", "13800138040"),
                ("EMP-OPS-002", "operator2", "郑军客服", "administration", "staff", "operator", "郑军", "zhengjun@autodealer.com", "13800138041"),
                ("EMP-OPS-003", "operator3", "钱丽客服", "administration", "senior_staff", "operator", "钱丽", "qianli@autodealer.com", "13800138042"),
                
                // AI 管理员
                ("EMP-AI-ADMIN-001", "ai_admin1", "冯雪AI管理", "management", "manager", "ai_admin", "冯雪", "fengxue@autodealer.com", "13800138050"),
                
                // 高管
                ("EMP-EXEC-001", "executive1", "蒋总", "management", "general_manager", "executive", "蒋总", "jiangzong@autodealer.com", "13800138060"),
                ("EMP-EXEC-002", "executive2", "沈总", "management", "executive", "executive", "沈总", "shenzong@autodealer.com", "13800138061"),
                
                // 零部件部门
                ("EMP-PARTS-001", "parts_staff1", "韩磊零部件", "parts", "staff", "operator", "韩磊", "hanlei@autodealer.com", "13800138070"),
                
                // 实习销售
                ("EMP-SALES-INT-001", "sales_intern1", "杨实习", "sales", "intern", "sales_rep", "杨实习", "yangshi@autodealer.com", "13800138080"),
            };

            foreach (var (empId, userName, displayName, dept, position, role, name, email, mobile) in employees)
            {
                // 创建 AppUser
                if (await CreateUserIfNotExists(conn, userName, displayName, false, now, logger))
                {
                    createdUserCount++;
                }

                // 分配角色
                await EnsureUserRole(conn, userName, role, now, logger);

                // 创建员工记录
                if (await CreateEmployeeIfNotExists(conn, empId, userName, name, dept, position, role, email, mobile, now, logger))
                {
                    createdEmployeeCount++;
                }
            }
        }

        // 确保客户表存在
        if (!await TableExistsAsync(conn, "customers"))
        {
            logger.LogWarning("customers テーブルが存在しないため、顧客データの作成をスキップします");
        }
        else
        {
            // 创建各类客户用户
            var customers = new[]
            {
                // 普通客户
                ("CUST-001", "customer1", "客户张三", "individual", "张三", "", "male", "13900139001", "zhangsan@email.com", "regular"),
                ("CUST-002", "customer2", "客户李四", "individual", "李四", "", "female", "13900139002", "lisi@email.com", "regular"),
                ("CUST-003", "customer3", "客户王五", "individual", "王五", "", "male", "13900139003", "wangwu@email.com", "regular"),
                
                // 银卡客户
                ("CUST-SILVER-001", "customer_silver1", "银卡客户赵六", "individual", "赵六", "リウ", "male", "13900139010", "zhaoliu@email.com", "silver"),
                
                // 金卡客户
                ("CUST-GOLD-001", "customer_gold1", "金卡客户孙七", "individual", "孙七", "チー", "female", "13900139020", "sunqi@email.com", "gold"),
                ("CUST-GOLD-002", "customer_gold2", "金卡客户周八", "individual", "周八", "パー", "male", "13900139021", "zhouba@email.com", "gold"),
                
                // VIP 客户
                ("CUST-VIP-001", "customer_vip1", "VIP客户吴九", "individual", "吴九", "ウー", "male", "13900139030", "wujiu@email.com", "vip"),
                ("CUST-VIP-002", "customer_vip2", "VIP客户郑十", "individual", "郑十", "シーン", "female", "13900139031", "zhengshi@email.com", "vip"),
                
                // 铂金客户
                ("CUST-PLAT-001", "customer_platinum1", "铂金客户冯总", "individual", "冯总", "フォン", "male", "13900139040", "fengzong@email.com", "platinum"),
                
                // 企业客户
                ("CUST-CORP-001", "corp_customer1", "企业客户ABC公司", "corporate", "ABC公司", "エービーシー", "", "13900139050", "contact@abc-corp.com", "gold"),
                ("CUST-CORP-002", "corp_customer2", "企业客户XYZ集团", "corporate", "XYZ集团", "エックスワイゼット", "", "13900139051", "info@xyz-group.com", "platinum"),
            };

            foreach (var (customerId, userName, displayName, custType, name, nameKana, gender, mobile, email, tier) in customers)
            {
                // 创建 AppUser
                if (await CreateUserIfNotExists(conn, userName, displayName, false, now, logger))
                {
                    createdUserCount++;
                }

                // 分配客户角色
                await EnsureUserRole(conn, userName, "customer", now, logger);

                // 创建客户记录
                if (await CreateCustomerIfNotExists(conn, customerId, userName, custType, name, nameKana, gender, mobile, email, tier, now, logger))
                {
                    createdCustomerCount++;
                }
            }
        }

        // 确保第三方用户表存在
        if (!await TableExistsAsync(conn, "third_party_users"))
        {
            logger.LogWarning("third_party_users テーブルが存在しないため、第三者データの作成をスキップします");
        }
        else
        {
            // 创建第三方用户
            var thirdPartyUsers = new[]
            {
                ("TP-001", "vendor1", "供应商A公司", "vendor", "供应商联系人", "vendor_a@example.com", "active"),
                ("TP-002", "vendor2", "供应商B公司", "vendor", "供应商联系人", "vendor_b@example.com", "active"),
                ("TP-003", "logistics1", "物流公司X", "logistics", "物流联系人", "logistics_x@example.com", "active"),
                ("TP-004", "finance1", "金融机构Y", "finance", "金融联系人", "finance_y@example.com", "active"),
                ("TP-005", "insurance1", "保险公司Z", "insurance", "保险联系人", "insurance_z@example.com", "active"),
            };

            foreach (var (tpId, userName, companyName, serviceType, contactPerson, email, status) in thirdPartyUsers)
            {
                // 创建 AppUser
                if (await CreateUserIfNotExists(conn, userName, companyName, false, now, logger))
                {
                    createdUserCount++;
                }

                // 创建第三方用户记录
                if (await CreateThirdPartyUserIfNotExists(conn, tpId, userName, companyName, serviceType, contactPerson, email, status, now, logger))
                {
                    logger.LogInformation("第三者ユーザー '{UserName}' を作成しました", userName);
                }
            }
        }

        logger.LogInformation("auto-dealer-demo テストユーザー設定完了: AppUser={UserCount}, 従業員={EmpCount}, 顧客={CustCount}", 
            createdUserCount, createdEmployeeCount, createdCustomerCount);
    }

    /// <summary>
    /// テーブルの存在チェックを行います。
    /// </summary>
    private async Task<bool> TableExistsAsync(IDbConnection conn, string tableName)
    {
        try
        {
            // DCS001 抑制理由：tableName はハードコード値（ユーザー入力なし）
#pragma warning disable DCS001
            var result = await conn.QueryFirstOrDefaultAsync<int?>(
                $"SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='{tableName}'");
#pragma warning restore DCS001
            return result.HasValue && result.Value > 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// ユーザーが存在しない場合のみ作成します。
    /// </summary>
    private async Task<bool> CreateUserIfNotExists(
        IDbConnection conn,
        string userName,
        string displayName,
        bool isAdmin,
        string createdAt,
        ILogger logger)
    {
        var existingId = await conn.QueryFirstOrDefaultAsync<int?>(
            "SELECT Id FROM AppUser WHERE UserName = @UserName",
            new { UserName = userName });

        if (existingId.HasValue)
        {
            return false;
        }

        var user = new AppUser
        {
            UserName = userName,
            DisplayName = displayName,
            PreferredLanguage = "zh-CN",
            IsAdmin = isAdmin,
            IsActive = true,
            CreatedAt = createdAt
        };

        // 统一密码：Test@123!
        user.PasswordHash = _hasher.HashPassword(user, "Test@123!");

        await conn.ExecuteAsync(@"
INSERT INTO AppUser (UserName, PasswordHash, DisplayName, PreferredLanguage, IsAdmin, IsActive, CreatedAt)
VALUES (@UserName, @PasswordHash, @DisplayName, @PreferredLanguage, @IsAdmin, @IsActive, @CreatedAt)", user);

        logger.LogDebug("AppUser '{UserName}' を作成しました", userName);
        return true;
    }

    /// <summary>
    /// ユーザーロールを確保します（存在しない場合のみ）。
    /// </summary>
    private async Task EnsureUserRole(
        IDbConnection conn,
        string userName,
        string roleName,
        string createdAt,
        ILogger logger)
    {
        var existingRole = await conn.QueryFirstOrDefaultAsync<int?>(
            "SELECT COUNT(*) FROM AppUserRole WHERE UserName = @UserName AND RoleName = @RoleName",
            new { UserName = userName, RoleName = roleName });

        if (!existingRole.HasValue || existingRole.Value == 0)
        {
            await conn.ExecuteAsync(
                @"INSERT OR IGNORE INTO AppUserRole (UserName, RoleName, CreatedAt) VALUES (@UserName, @RoleName, @CreatedAt)",
                new { UserName = userName, RoleName = roleName, CreatedAt = createdAt });
        }
    }

    /// <summary>
    /// 従業員レコードが存在しない場合のみ作成します。
    /// </summary>
    private async Task<bool> CreateEmployeeIfNotExists(
        IDbConnection conn,
        string employeeId,
        string userName,
        string name,
        string department,
        string position,
        string role,
        string email,
        string mobile,
        string now,
        ILogger logger)
    {
        var existing = await conn.QueryFirstOrDefaultAsync<string>(
            "SELECT employee_id FROM employees WHERE employee_id = @EmployeeId",
            new { EmployeeId = employeeId });

        if (!string.IsNullOrEmpty(existing))
        {
            return false;
        }

        // 获取 AppUser ID
        var appUserId = await conn.QueryFirstOrDefaultAsync<int?>(
            "SELECT Id FROM AppUser WHERE UserName = @UserName",
            new { UserName = userName });

        if (!appUserId.HasValue)
        {
            logger.LogWarning("従業員 '{EmployeeId}' の AppUser が見つかりません", employeeId);
            return false;
        }

        await conn.ExecuteAsync(@"
INSERT INTO employees (
    employee_id, app_user_id, user_name, employee_number, name, name_kana,
    gender, email, mobile, department, position, role, hire_date, 
    employment_type, status, created_at, updated_at
) VALUES (
    @EmployeeId, @AppUserId, @UserName, @EmpNumber, @Name, '',
    '', @Email, @Mobile, @Department, @Position, @Role, date('now'),
    'full_time', 'active', @Now, @Now
)", new
        {
            EmployeeId = employeeId,
            AppUserId = appUserId.Value,
            UserName = userName,
            EmpNumber = employeeId,
            Name = name,
            Email = email,
            Mobile = mobile,
            Department = department,
            Position = position,
            Role = role,
            Now = now
        });

        logger.LogDebug("従業員 '{EmployeeId}' ({Name}) を作成しました", employeeId, name);
        return true;
    }

    /// <summary>
    /// 顧客レコードが存在しない場合のみ作成します。
    /// </summary>
    private async Task<bool> CreateCustomerIfNotExists(
        IDbConnection conn,
        string customerId,
        string userName,
        string customerType,
        string name,
        string nameKana,
        string gender,
        string mobile,
        string email,
        string tierLevel,
        string now,
        ILogger logger)
    {
        var existing = await conn.QueryFirstOrDefaultAsync<string>(
            "SELECT customer_id FROM customers WHERE customer_id = @CustomerId",
            new { CustomerId = customerId });

        if (!string.IsNullOrEmpty(existing))
        {
            return false;
        }

        // 获取 AppUser ID
        var appUserId = await conn.QueryFirstOrDefaultAsync<int?>(
            "SELECT Id FROM AppUser WHERE UserName = @UserName",
            new { UserName = userName });

        await conn.ExecuteAsync(@"
INSERT INTO customers (
    customer_id, app_user_id, user_name, customer_type, name, name_kana,
    gender, phone, mobile, email, tier_level, purchase_count, 
    total_purchase_amount, preferred_contact, created_at, updated_at
) VALUES (
    @CustomerId, @AppUserId, @UserName, @CustomerType, @Name, @NameKana,
    @Gender, @Phone, @Mobile, @Email, @TierLevel, 0,
    0, 'phone', @Now, @Now
)", new
        {
            CustomerId = customerId,
            AppUserId = appUserId,
            UserName = userName,
            CustomerType = customerType,
            Name = name,
            NameKana = nameKana,
            Gender = gender,
            Phone = mobile,
            Mobile = mobile,
            Email = email,
            TierLevel = tierLevel,
            Now = now
        });

        logger.LogDebug("顧客 '{CustomerId}' ({Name}) を作成しました", customerId, name);
        return true;
    }

    /// <summary>
    /// 第三者ユーザーが存在しない場合のみ作成します。
    /// </summary>
    private async Task<bool> CreateThirdPartyUserIfNotExists(
        IDbConnection conn,
        string thirdPartyId,
        string userName,
        string companyName,
        string serviceType,
        string contactPerson,
        string email,
        string status,
        string now,
        ILogger logger)
    {
        var existing = await conn.QueryFirstOrDefaultAsync<string>(
            "SELECT third_party_id FROM third_party_users WHERE third_party_id = @ThirdPartyId",
            new { ThirdPartyId = thirdPartyId });

        if (!string.IsNullOrEmpty(existing))
        {
            return false;
        }

        // 获取 AppUser ID
        var appUserId = await conn.QueryFirstOrDefaultAsync<int?>(
            "SELECT Id FROM AppUser WHERE UserName = @UserName",
            new { UserName = userName });

        if (!appUserId.HasValue)
        {
            logger.LogWarning("第三者 '{ThirdPartyId}' の AppUser が見つかりません", thirdPartyId);
            return false;
        }

        await conn.ExecuteAsync(@"
INSERT INTO third_party_users (
    third_party_id, app_user_id, company_name, service_type, 
    contact_person, contact_email, status, rating, created_at, updated_at
) VALUES (
    @ThirdPartyId, @AppUserId, @CompanyName, @ServiceType,
    @ContactPerson, @Email, @Status, 5, @Now, @Now
)", new
        {
            ThirdPartyId = thirdPartyId,
            AppUserId = appUserId.Value,
            CompanyName = companyName,
            ServiceType = serviceType,
            ContactPerson = contactPerson,
            Email = email,
            Status = status,
            Now = now
        });

        return true;
    }
}
