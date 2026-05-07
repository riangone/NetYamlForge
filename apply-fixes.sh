#!/bin/bash
# 状态机可视化生成脚本

set -e

echo "=== 1. 生成 AppointmentStateMachine 状态图 ==="

# 为 NetYamlForge.AI 版本生成
if [ -f "NetYamlForge.AI/Services/AppointmentStateMachine.cs" ]; then
    echo "处理 NetYamlForge.AI/Services/AppointmentStateMachine.cs"
    
    # 检查是否已有 UmlDotGraph 方法
    if ! grep -q "GetUmlDotGraph" "NetYamlForge.AI/Services/AppointmentStateMachine.cs"; then
        # 在文件末尾添加方法
        cat >> "NetYamlForge.AI/Services/AppointmentStateMachine.cs" << 'EOF'

    /// <summary>
    /// 获取状态机 UML 图（需要 Stateless.Graph）
    /// </summary>
    public string GetUmlDotGraph()
    {
        return _machine.ToDotGraph();
    }
EOF
        echo "  ✅ 已添加 GetUmlDotGraph() 方法"
    else
        echo "  ⚠️ 已有 GetUmlDotGraph() 方法"
    fi
fi

# 为 NetYamlForge 版本生成
if [ -f "NetYamlForge/Services/AI/AppointmentStateMachine.cs" ]; then
    echo "处理 NetYamlForge/Services/AI/AppointmentStateMachine.cs"
    
    if ! grep -q "GetUmlDotGraph" "NetYamlForge/Services/AI/AppointmentStateMachine.cs"; then
        cat >> "NetYamlForge/Services/AI/AppointmentStateMachine.cs" << 'EOF'

    /// <summary>
    /// 获取状态机 UML 图（需要 Stateless.Graph）
    /// </summary>
    public string GetUmlDotGraph()
    {
        return _machine.ToDotGraph();
    }
EOF
        echo "  ✅ 已添加 GetUmlDotGraph() 方法"
    else
        echo "  ⚠️ 已有 GetUmlDotGraph() 方法"
    fi
fi

echo ""
echo "=== 2. 完善 AiToolOrchestrator 工具执行逻辑 ==="

# 查找 AiToolOrchestrator 文件
find . -name "AiToolOrchestrator.cs" -type f 2>/dev/null | while read file; do
    echo "处理 $file"
    
    # 替换空的 tool 执行逻辑
    if grep -q "// \[4\] 执行 Tool (TODO: 这里需要集成到实际的 Tool 执行逻辑)" "$file"; then
        # 创建临时文件进行替换
        sed -i 's|// \[4\] 执行 Tool (TODO: 这里需要集成到实际的 Tool 执行逻辑)|// [4] 执行 Tool
        if (toolName == "query_data")
        {
            // 执行查询
            var queryResult = await ExecuteQueryToolAsync(toolParams);
            result.Data = queryResult;
        }
        else if (toolName == "send_email")
        {
            // 发送邮件
            await ExecuteSendEmailToolAsync(toolParams);
        }
        else
        {
            // 其他工具
            _logger.LogWarning("未知工具: {ToolName}", toolName);
        }|g' "$file"
        echo "  ✅ 已完善工具执行逻辑"
    fi
    
    # 替换空的数据返回
    if grep -q "result.Data = null; // TODO: 实际的 Tool 执行结果" "$file"; then
        sed -i 's/result.Data = null; \/\/ TODO: 实际的 Tool 执行结果/result.Data = queryResult;/g' "$file"
        echo "  ✅ 已更新数据返回逻辑"
    fi
    
    # 替换空的 LowConfidenceCount
    if grep -q "LowConfidenceCount = 0 // TODO: 从 FSM 获取" "$file"; then
        sed -i 's/LowConfidenceCount = 0 \/\/ TODO: 从 FSM 获取/LowConfidenceCount = _stateMachine?.GetLowConfidenceCount() ?? 0;/g' "$file"
        echo "  ✅ 已更新 LowConfidenceCount"
    fi
done

echo ""
echo "=== 3. 实现批处理作业邮件通知 ==="

# 查找 BatchJobHostedService 文件
find . -name "BatchJobHostedService.cs" -type f 2>/dev/null | while read file; do
    echo "处理 $file"
    
    if grep -q "// TODO: メール通知などの実装" "$file"; then
        # 创建临时文件进行替换
        python3 -c "
import re
with open('$file', 'r') as f:
    content = f.read()

# 替换邮件通知 TODO
todo = '// TODO: メール通知などの実装'
replacement = '''// 邮件通知
if (!string.IsNullOrEmpty(options.NotifyEmail))
{
    try
    {
        var subject = $\"批处理作业完成: {jobName}\";
        var body = $\"作业 {jobName} 已完成，状态: {result.Status}\";
        await emailService.SendEmailAsync(options.NotifyEmail, subject, body);
        _logger.LogInformation(\"已发送完成通知邮件到: {Email}\", options.NotifyEmail);
    }
    catch (Exception ex)
    {
        _logger.LogWarning(ex, \"发送通知邮件失败\");
    }
}\"'''

if todo in content:
    content = content.replace(todo, replacement)
    with open('$file', 'w') as f:
        f.write(content)
    print('  ✅ 已添加邮件通知功能')
else:
    print('  ⚠️ 未找到 TODO 标记')
"
        echo "  ✅ 已添加邮件通知功能"
    fi
done

echo ""
echo "=== 完成 ==="
