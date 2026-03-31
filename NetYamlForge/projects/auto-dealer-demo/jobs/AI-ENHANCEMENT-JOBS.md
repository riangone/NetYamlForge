# 汽车销售 AI 增强功能 - 批处理作业示例

## 1. 线索培育任务生成器 (lead_nurturing_generator.yml)

```yaml
name: lead_nurturing_generator
displayName: "线索培育任务生成器"
description: "每日扫描客户行为数据，AI 自动生成跟进任务"
schedule: "0 9 * * *"  # 每天上午 9 点执行
timeout: 300
enabled: true

tasks:
  # ─────────────────────────────────────────────────────
  # 任务 1: 浏览多次未咨询的客户
  # ─────────────────────────────────────────────────────
  - name: high_interest_no_inquiry
    type: script
    script: |
      -- 查找 7 天内浏览同一车型 3 次以上但未咨询的客户
      INSERT INTO lead_nurturing_tasks 
        (task_id, lead_id, customer_id, task_type, trigger_reason, priority_score, ai_recommendation)
      SELECT 
        'TASK-' || strftime('%Y%m%d%H%M%S', 'now') || '-' || substr(hex(randomblob(4)), 1, 6),
        NULL,
        la.customer_id,
        'send_info',
        '7 天内浏览 ' || v.maker || ' ' || v.model || ' 共 ' || COUNT(*) || ' 次，未主动咨询',
        60 + MIN(COUNT(*) * 5, 30),  -- 浏览次数越多优先级越高
        '您好，我是 XX 车行的 AI 助手。注意到您最近多次浏览了 ' || v.maker || ' ' || v.model || '，
        这款车目前有特别优惠活动。请问您有什么想了解的吗？我可以为您安排试驾。'
      FROM lead_activities la
      JOIN vehicles v ON la.vehicle_id = v.vehicle_id
      LEFT JOIN sales_leads sl ON la.customer_id = sl.customer_id 
        AND sl.vehicle_interest LIKE '%' || v.model || '%'
      WHERE la.activity_type = 'vehicle_view'
        AND la.created_at >= datetime('now', '-7 days')
        AND sl.lead_id IS NULL  -- 还没有销售线索
      GROUP BY la.customer_id, v.vehicle_id
      HAVING COUNT(*) >= 3
      AND NOT EXISTS (
        SELECT 1 FROM lead_nurturing_tasks lnt 
        WHERE lnt.customer_id = la.customer_id 
          AND lnt.task_type = 'send_info'
          AND lnt.status = 'pending'
      );

  # ─────────────────────────────────────────────────────
  # 任务 2: 试驾后未跟进的客户
  # ─────────────────────────────────────────────────────
  - name: post_testdrive_followup
    type: script
    script: |
      -- 试驾完成后 3 天未联系的客戶
      INSERT INTO lead_nurturing_tasks 
        (task_id, lead_id, customer_id, task_type, trigger_reason, priority_score, ai_recommendation)
      SELECT 
        'TASK-' || strftime('%Y%m%d%H%M%S', 'now') || '-' || substr(hex(randomblob(4)), 1, 6),
        sl.lead_id,
        sa.customer_id,
        'followup_call',
        '试驾 ' || v.model || ' 已完成 ' || 
          CAST(julianday('now') - julianday(sa.completed_at) AS INT) || ' 天，尚未跟进',
        80,  -- 试驾后跟进优先级高
        '您好，上次试驾的 ' || v.model || ' 感觉如何？
        有没有什么想了解的地方？
        这周签约的话可以享受特别优惠哦！'
      FROM service_appointments sa
      JOIN vehicles v ON sa.vehicle_id = v.vehicle_id
      LEFT JOIN sales_leads sl ON sa.customer_id = sl.customer_id
      WHERE sa.appointment_type = 'test_drive'
        AND sa.status = 'completed'
        AND sa.completed_at < datetime('now', '-3 days')
        AND (sl.status IS NULL OR sl.status IN ('new', 'contacted'))
        AND NOT EXISTS (
          SELECT 1 FROM lead_nurturing_tasks lnt 
          WHERE lnt.customer_id = sa.customer_id 
            AND lnt.task_type = 'followup_call'
            AND lnt.status = 'pending'
      );

  # ─────────────────────────────────────────────────────
  # 任务 3: 高评分线索长时间未联系
  # ─────────────────────────────────────────────────────
  - name: high_score_no_contact
    type: script
    script: |
      -- 线索评分>=70 但 5 天未联系
      INSERT INTO lead_nurturing_tasks 
        (task_id, lead_id, customer_id, task_type, trigger_reason, priority_score, ai_recommendation)
      SELECT 
        'TASK-' || strftime('%Y%m%d%H%M%S', 'now') || '-' || substr(hex(randomblob(4)), 1, 6),
        sl.lead_id,
        sl.customer_id,
        'followup_call',
        '高评分线索（' || sl.lead_score || '分）已 ' || 
          CAST(julianday('now') - julianday(sl.last_contact_at) AS INT) || ' 天未联系',
        sl.lead_score,
        '您好，我是 XX 车行的 ' || COALESCE(sr.name, 'AI 助手') || '。
        上次跟您聊过的 ' || COALESCE(sl.vehicle_interest, '车型') || '，
        最近有客户刚订了同款，评价很不错。
        您考虑得怎么样了？有什么我可以帮忙的吗？'
      FROM sales_leads sl
      LEFT JOIN staff sr ON sl.assigned_to_user_id = sr.staff_id
      WHERE sl.status IN ('new', 'contacted', 'qualified')
        AND sl.lead_score >= 70
        AND (sl.last_contact_at IS NULL OR sl.last_contact_at < datetime('now', '-5 days'))
        AND NOT EXISTS (
          SELECT 1 FROM lead_nurturing_tasks lnt 
          WHERE lnt.lead_id = sl.lead_id 
            AND lnt.status = 'pending'
      );

  # ─────────────────────────────────────────────────────
  # 任务 4: 价格咨询未成交
  # ─────────────────────────────────────────────────────
  - name: price_inquiry_no_close
    type: script
    script
    script: |
      -- 咨询价格后 7 天未成交
      INSERT INTO lead_nurturing_tasks 
        (task_id, lead_id, customer_id, task_type, trigger_reason, priority_score, ai_recommendation)
      SELECT 
        'TASK-' || strftime('%Y%m%d%H%M%S', 'now') || '-' || substr(hex(randomblob(4)), 1, 6),
        sl.lead_id,
        sl.customer_id,
        'special_offer',
        '价格咨询后 ' || 
          CAST(julianday('now') - julianday(sl.created_at) AS INT) || ' 天未成交',
        70,
        '您好！告诉您一个好消息，您之前咨询的车型本月有特别优惠：
        ・首付 10% 即可提车
        ・3 年免息贷款
        ・置换补贴最高 20 万
        活动截止到本月底，要不要来店里详细聊聊？'
      FROM sales_leads sl
      WHERE sl.last_intent IN ('price_inquiry', 'quote_request')
        AND sl.status NOT IN ('won', 'lost')
        AND sl.created_at < datetime('now', '-7 days')
        AND NOT EXISTS (
          SELECT 1 FROM lead_nurturing_tasks lnt 
          WHERE lnt.lead_id = sl.lead_id 
            AND lnt.task_type = 'special_offer'
            AND lnt.status = 'pending'
      );

  # ─────────────────────────────────────────────────────
  # 任务 5: 客户生日祝福
  # ─────────────────────────────────────────────────────
  - name: birthday_greeting
    type: script
    script: |
      -- 今天生日的客户（从 customers 表的 birthday 字段）
      INSERT INTO lead_nurturing_tasks 
        (task_id, lead_id, customer_id, task_type, trigger_reason, priority_score, ai_recommendation)
      SELECT 
        'TASK-' || strftime('%Y%m%d%H%M%S', 'now') || '-' || substr(hex(randomblob(4)), 1, 6),
        sl.lead_id,
        c.customer_id,
        'birthday_greeting',
        c.name || ' 先生的生日',
        90,  -- 生日祝福优先级高
        c.name || '先生，生日快乐！🎂
        感谢您一直以来对 XX 车行的支持。
        为庆祝您的生日，我们为您准备了特别优惠：
        ・购车立减 5 万日元
        ・免费升级导航系统
        活动仅限本周，欢迎随时来店！'
      FROM customers c
      LEFT JOIN sales_leads sl ON c.customer_id = sl.customer_id AND sl.status != 'lost'
      WHERE strftime('%m-%d', c.birthday) = strftime('%m-%d', 'now')
        AND NOT EXISTS (
          SELECT 1 FROM lead_nurturing_tasks lnt 
          WHERE lnt.customer_id = c.customer_id 
            AND lnt.task_type = 'birthday_greeting'
            AND lnt.status = 'pending'
            AND DATE(lnt.created_at) = DATE('now')
      );

  # ─────────────────────────────────────────────────────
  # 任务 6: 库存车龄过长促销
  # ─────────────────────────────────────────────────────
  - name: aging_inventory_promotion
    type: script
    script: |
      -- 库存超过 90 天的车辆，生成促销任务
      INSERT INTO lead_nurturing_tasks 
        (task_id, lead_id, customer_id, task_type, trigger_reason, priority_score, ai_recommendation, context_data)
      SELECT 
        'TASK-' || strftime('%Y%m%d%H%M%S', 'now') || '-' || substr(hex(randomblob(4)), 1, 6),
        NULL,
        la.customer_id,
        'special_offer',
        v.maker || ' ' || v.model || ' 库存 ' || 
          CAST(julianday('now') - julianday(v.arrival_date) AS INT) || ' 天，特别促销',
        65,
        '您好！您之前关注的 ' || v.maker || ' ' || v.model || '，
        现在店头有特别优惠活动：
        ・现金优惠 15 万日元
        ・免费赠送 5 年保养
        ・旧车置换补贴 +10 万
        库存有限，先到先得！'
        -- context_data 包含车辆信息
        '{"vehicle_id": "' || v.vehicle_id || '", "original_price": ' || v.price || ', "discount": 150000}',
      FROM vehicles v
      CROSS JOIN (
        -- 查找对这类车感兴趣的客户
        SELECT DISTINCT customer_id 
        FROM lead_activities 
        WHERE vehicle_id IN (SELECT vehicle_id FROM vehicles WHERE maker = v.maker AND model = v.model)
      ) la
      WHERE v.status = 'available'
        AND v.arrival_date < datetime('now', '-90 days')
        AND NOT EXISTS (
          SELECT 1 FROM lead_nurturing_tasks lnt 
          WHERE lnt.task_type = 'special_offer'
            AND lnt.context_data LIKE '%"vehicle_id": "' || v.vehicle_id || '"%'
            AND lnt.status = 'pending'
      );

  # ─────────────────────────────────────────────────────
  # 任务 7: 竞品对比客户
  # ─────────────────────────────────────────────────────
  - name: competitor_comparison
    type: script
    script: |
      -- 浏览记录显示在对比竞品的客户
      INSERT INTO lead_nurturing_tasks 
        (task_id, lead_id, customer_id, task_type, trigger_reason, priority_score, ai_recommendation)
      SELECT 
        'TASK-' || strftime('%Y%m%d%H%M%S', 'now') || '-' || substr(hex(randomblob(4)), 1, 6),
        NULL,
        la1.customer_id,
        'competitor_counter',
        '正在对比竞品：' || v1.model || ' vs ' || v2.model,
        75,
        '您好！听说您在 ' || v1.model || ' 和 ' || v2.model || ' 之间犹豫？
        从专业角度看，' || v1.model || ' 有 3 个明显优势：
        1. 保值率高出 5%（5 年后多卖 10 万）
        2. 油耗低 15%（每年省 2 万油费）
        3. 安全配置更全面（标配 L2 自动驾驶）
        要不要来店里实际对比一下？'
      FROM lead_activities la1
      JOIN vehicles v1 ON la1.vehicle_id = v1.vehicle_id
      JOIN lead_activities la2 ON la1.customer_id = la2.customer_id
      JOIN vehicles v2 ON la2.vehicle_id = v2.vehicle_id
      WHERE la1.created_at >= datetime('now', '-7 days')
        AND la2.created_at >= datetime('now', '-7 days')
        AND v1.maker != v2.maker  -- 不同品牌
        AND v1.status = 'available'
        AND NOT EXISTS (
          SELECT 1 FROM lead_nurturing_tasks lnt 
          WHERE lnt.customer_id = la1.customer_id 
            AND lnt.task_type = 'competitor_counter'
            AND lnt.status = 'pending'
      )
      GROUP BY la1.customer_id, v1.vehicle_id, v2.vehicle_id;

  # ─────────────────────────────────────────────────────
  # 任务 8: 保养提醒（售后）
  # ─────────────────────────────────────────────────────
  - name: maintenance_reminder
    type: script
    script: |
      -- 距离上次保养超过 6 个月的客户
      INSERT INTO lead_nurturing_tasks 
        (task_id, lead_id, customer_id, task_type, trigger_reason, priority_score, ai_recommendation)
      SELECT 
        'TASK-' || strftime('%Y%m%d%H%M%S', 'now') || '-' || substr(hex(randomblob(4)), 1, 6),
        NULL,
        c.customer_id,
        'maintenance_reminder',
        '距离上次保养已 ' || 
          CAST(julianday('now') - julianday(MAX(sa.completed_at)) AS INT) || ' 天',
        70,
        c.name || '先生，您的爱车该保养了。
        长时间不保养会影响性能和油耗哦。
        现在预约保养可享受：
        ・工时费 8 折优惠
        ・免费全车检查
        ・洗车服务
        点击这里立即预约：[预约链接]'
      FROM customers c
      JOIN service_appointments sa ON c.customer_id = sa.customer_id
      WHERE sa.appointment_type = 'service'
        AND sa.status = 'completed'
      GROUP BY c.customer_id
      HAVING julianday('now') - julianday(MAX(sa.completed_at)) >= 180
        AND NOT EXISTS (
          SELECT 1 FROM lead_nurturing_tasks lnt 
          WHERE lnt.customer_id = c.customer_id 
            AND lnt.task_type = 'maintenance_reminder'
            AND lnt.status = 'pending'
      );

# ─────────────────────────────────────────────────────────
# 执行统计
# ─────────────────────────────────────────────────────────
summary:
  - name: count_generated_tasks
    type: script
    script: |
      SELECT 
        task_type,
        COUNT(*) as count
      FROM lead_nurturing_tasks
      WHERE DATE(created_at) = DATE('now')
      GROUP BY task_type
      ORDER BY count DESC;
```

---

## 2. 客户行为评分计算器 (customer_behavior_scorer.yml)

```yaml
name: customer_behavior_scorer
displayName: "客户行为评分计算器"
description: "基于客户行为数据，AI 计算多维度评分"
schedule: "0 2 * * *"  # 每天凌晨 2 点执行
timeout: 600
enabled: true

tasks:
  # ─────────────────────────────────────────────────────
  # 计算所有活跃客户的各项评分
  # ─────────────────────────────────────────────────────
  - name: calculate_scores
    type: script
    script: |
      -- 插入或更新客户行为评分
      INSERT OR REPLACE INTO customer_behavior_scores (
        score_id, customer_id, engagement_score, purchase_intent_score,
        price_sensitivity_score, brand_loyalty_score, churn_risk_score,
        preferred_channel, preferred_contact_time, vehicle_interests,
        browsing_patterns, last_calculated_at, created_at, updated_at
      )
      SELECT 
        'SCORE-' || c.customer_id,
        c.customer_id,
        
        -- 参与度评分 (0-100)
        -- 基于：浏览次数、咨询次数、试驾次数
        MIN(100, 
          (SELECT COUNT(*) FROM lead_activities WHERE customer_id = c.customer_id 
           AND created_at >= datetime('now', '-30 days')) * 5 +
          (SELECT COUNT(*) FROM ai_messages WHERE customer_id = c.customer_id 
           AND created_at >= datetime('now', '-30 days')) * 10 +
          (SELECT COUNT(*) FROM service_appointments WHERE customer_id = c.customer_id 
           AND appointment_type = 'test_drive'
           AND created_at >= datetime('now', '-90 days')) * 20
        ) as engagement_score,
        
        -- 购买意向评分 (0-100)
        -- 基于：询价、试驾、对比行为
        MIN(100,
          (SELECT COUNT(*) FROM lead_activities la
           JOIN vehicles v ON la.vehicle_id = v.vehicle_id
           WHERE la.customer_id = c.customer_id 
             AND la.activity_type IN ('price_inquiry', 'quote_request')
             AND la.created_at >= datetime('now', '-14 days')) * 15 +
          (SELECT CASE WHEN COUNT(*) > 0 THEN 30 ELSE 0 END
           FROM service_appointments 
           WHERE customer_id = c.customer_id 
             AND appointment_type = 'test_drive'
             AND status = 'completed'
             AND completed_at >= datetime('now', '-30 days')) +
          (SELECT COUNT(DISTINCT vehicle_id) * 10
           FROM lead_activities 
           WHERE customer_id = c.customer_id 
             AND created_at >= datetime('now', '-7 days'))
        ) as purchase_intent_score,
        
        -- 价格敏感度 (0-100)
        -- 基于：价格相关行为占比
        COALESCE((
          SELECT CAST(
            (SELECT COUNT(*) FROM lead_activities 
             WHERE customer_id = c.customer_id 
               AND activity_type IN ('price_inquiry', 'discount_inquiry')
               AND created_at >= datetime('now', '-30 days')) * 100.0 /
            NULLIF((SELECT COUNT(*) FROM lead_activities 
             WHERE customer_id = c.customer_id 
               AND created_at >= datetime('now', '-30 days')), 0)
          AS INT)
        ), 50) as price_sensitivity_score,
        
        -- 品牌忠诚度 (0-100)
        -- 基于：是否只看一个品牌
        COALESCE((
          SELECT CASE 
            WHEN COUNT(DISTINCT v.maker) = 1 THEN 90
            WHEN COUNT(DISTINCT v.maker) = 2 THEN 60
            ELSE 30
          END
          FROM lead_activities la
          JOIN vehicles v ON la.vehicle_id = v.vehicle_id
          WHERE la.customer_id = c.customer_id
            AND la.created_at >= datetime('now', '-30 days')
        ), 50) as brand_loyalty_score,
        
        -- 流失风险 (0-100)
        -- 基于：未联系天数、线索状态
        MIN(100,
          CASE 
            WHEN sl.status = 'lost' THEN 90
            WHEN sl.status IS NULL THEN 50
            WHEN julianday('now') - julianday(sl.last_contact_at) >= 30 THEN 80
            WHEN julianday('now') - julianday(sl.last_contact_at) >= 14 THEN 60
            WHEN julianday('now') - julianday(sl.last_contact_at) >= 7 THEN 40
            ELSE 20
          END +
          -- 参与度低增加风险
          CASE WHEN engagement_score < 30 THEN 20 ELSE 0 END
        ) as churn_risk_score,
        
        -- 偏好渠道（从历史互动推断）
        (
          SELECT 
            CASE 
              WHEN COUNT(CASE WHEN channel = 'line' THEN 1 END) > 
                   COUNT(CASE WHEN channel = 'web' THEN 1 END) THEN 'line'
              WHEN COUNT(CASE WHEN channel = 'email' THEN 1 END) > 0 THEN 'email'
              ELSE 'phone'
            END
          FROM ai_conversations 
          WHERE customer_id = c.customer_id
        ) as preferred_channel,
        
        -- 偏好联系时间（从活跃时间推断）
        (
          SELECT 
            CASE 
              WHEN AVG(strftime('%H', created_at)) BETWEEN 9 AND 12 THEN '上午 9:00-12:00'
              WHEN AVG(strftime('%H', created_at)) BETWEEN 13 AND 17 THEN '下午 13:00-17:00'
              ELSE '随时'
            END
          FROM lead_activities 
          WHERE customer_id = c.customer_id
        ) as preferred_contact_time,
        
        -- 关注车型（JSON 数组）
        (
          SELECT '[' || GROUP_CONCAT('"' || v.vehicle_id || '"') || ']'
          FROM (
            SELECT la.vehicle_id, COUNT(*) as cnt
            FROM lead_activities la
            WHERE la.customer_id = c.customer_id
              AND la.created_at >= datetime('now', '-30 days')
            GROUP BY la.vehicle_id
            ORDER BY cnt DESC
            LIMIT 5
          ) sub
          JOIN vehicles v ON sub.vehicle_id = v.vehicle_id
        ) as vehicle_interests,
        
        -- 浏览模式（JSON 对象）
        (
          SELECT '{' || 
            '"total_views": ' || COALESCE((SELECT COUNT(*) FROM lead_activities WHERE customer_id = c.customer_id), 0) || ',' ||
            '"avg_session_minutes": ' || COALESCE((SELECT AVG(duration) FROM (SELECT 1 as duration)), 0) || ',' ||
            '"peak_day": "' || COALESCE((SELECT 
              CASE CAST(strftime('%w', MAX(created_at)) AS INT)
                WHEN 0 THEN 'Sunday'
                WHEN 1 THEN 'Monday'
                WHEN 2 THEN 'Tuesday'
                WHEN 3 THEN 'Wednesday'
                WHEN 4 THEN 'Thursday'
                WHEN 5 THEN 'Friday'
                WHEN 6 THEN 'Saturday'
              END
              FROM lead_activities WHERE customer_id = c.customer_id), 'Unknown') || '"' ||
            '}'
        ) as browsing_patterns,
        
        datetime('now') as last_calculated_at,
        datetime('now') as created_at,
        datetime('now') as updated_at
        
      FROM customers c
      LEFT JOIN sales_leads sl ON c.customer_id = sl.customer_id 
        AND sl.status IN ('new', 'contacted', 'qualified')
      WHERE EXISTS (
        SELECT 1 FROM lead_activities WHERE customer_id = c.customer_id
        AND created_at >= datetime('now', '-90 days')
      );

  # ─────────────────────────────────────────────────────
  # 生成高流失风险客户报告
  # ─────────────────────────────────────────────────────
  - name: generate_churn_report
    type: script
    script: |
      -- 输出高流失风险客户列表（用于日志或后续处理）
      SELECT 
        c.name,
        c.phone,
        cbs.churn_risk_score,
        cbs.purchase_intent_score,
        sl.status,
        julianday('now') - julianday(sl.last_contact_at) as days_no_contact
      FROM customer_behavior_scores cbs
      JOIN customers c ON cbs.customer_id = c.customer_id
      LEFT JOIN sales_leads sl ON c.customer_id = sl.customer_id
      WHERE cbs.churn_risk_score >= 70
      ORDER BY cbs.churn_risk_score DESC
      LIMIT 20;
```

---

## 3. 销售预测生成器 (sales_forecast_generator.yml)

```yaml
name: sales_forecast_generator
displayName: "销售预测生成器"
description: "基于历史数据和当前 pipeline 预测月度销售"
schedule: "0 6 * * 1"  # 每周一早上 6 点执行
timeout: 180
enabled: true

tasks:
  - name: generate_forecast
    type: script
    script: |
      -- 计算各销售人员的预测
      SELECT 
        sr.staff_id,
        sr.name,
        COUNT(CASE WHEN sl.status = 'won' THEN 1 END) as closed_deals,
        COUNT(CASE WHEN sl.status = 'proposal' THEN 1 END) as in_proposal,
        COUNT(CASE WHEN sl.status = 'qualified' THEN 1 END) as qualified,
        COUNT(CASE WHEN sl.status IN ('new', 'contacted') THEN 1 END) as early_stage,
        
        -- 基于转化率的预测
        COUNT(CASE WHEN sl.status = 'won' THEN 1 END) +
        ROUND(COUNT(CASE WHEN sl.status = 'proposal' THEN 1 END) * 0.6) +
        ROUND(COUNT(CASE WHEN sl.status = 'qualified' THEN 1 END) * 0.3) +
        ROUND(COUNT(CASE WHEN sl.status IN ('new', 'contacted') THEN 1 END) * 0.1) as predicted_total,
        
        -- 目标
        12 as monthly_target,
        
        -- 达成率
        ROUND(
          (COUNT(CASE WHEN sl.status = 'won' THEN 1 END) * 100.0 / 12),
          1
        ) as achievement_rate
        
      FROM staff sr
      LEFT JOIN sales_leads sl ON sr.staff_id = sl.assigned_to_user_id
        AND strftime('%Y-%m', sl.created_at) = strftime('%Y-%m', 'now')
      WHERE sr.role IN ('sales_rep', 'sales_manager')
      GROUP BY sr.staff_id, sr.name
      ORDER BY predicted_total DESC;
```

---

## 使用说明

### 1. 创建作业文件

将上述 YAML 保存为：

```
NetYamlForge/projects/auto-dealer-demo/jobs/
├── lead_nurturing_generator.yml
├── customer_behavior_scorer.yml
└── sales_forecast_generator.yml
```

### 2. 测试作业

```bash
# 手动执行作业测试
dotnet run --project NetYamlForge -- --execute-job --project=auto-dealer-demo --job=lead_nurturing_generator
```

### 3. 查看执行日志

```bash
# 查看作业执行历史
dotnet run --project NetYamlForge -- --list-jobs --project=auto-dealer-demo
```

### 4. 监控效果

```sql
-- 查看生成的任务统计
SELECT 
  task_type,
  COUNT(*) as total,
  SUM(CASE WHEN status = 'completed' THEN 1 ELSE 0 END) as completed,
  SUM(CASE WHEN status = 'pending' THEN 1 ELSE 0 END) as pending
FROM lead_nurturing_tasks
GROUP BY task_type;

-- 查看任务转化效果
SELECT 
  lnt.task_type,
  COUNT(*) as task_count,
  SUM(CASE WHEN sl.status = 'won' THEN 1 ELSE 0 END) as won_count,
  ROUND(SUM(CASE WHEN sl.status = 'won' THEN 1 ELSE 0 END) * 100.0 / COUNT(*), 2) as conversion_rate
FROM lead_nurturing_tasks lnt
LEFT JOIN sales_leads sl ON lnt.lead_id = sl.lead_id
WHERE lnt.created_at >= datetime('now', '-30 days')
GROUP BY lnt.task_type;
```
