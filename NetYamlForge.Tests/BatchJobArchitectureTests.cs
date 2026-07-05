using System;
using System.Linq;
using System.Reflection;
using NetYamlForge.Services.BatchJob;
using NetYamlForge.Services.AI;
using Xunit;

namespace NetYamlForge.Tests;

public class BatchJobArchitectureTests
{
    [Fact]
    public void BatchJobServices_ShouldNot_DirectlyDependOnSpecificCliServices()
    {
        // 1. 获取包含 IBatchStepHandler 的核心程序集
        var coreAssembly = typeof(IBatchStepHandler).Assembly;

        // 2. 获取 NetYamlForge.Services.BatchJob 命名空间下的所有类
        var batchJobTypes = coreAssembly.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract && t.Namespace != null && t.Namespace.StartsWith("NetYamlForge.Services.BatchJob"))
            .ToList();

        // 3. 定义被禁止的具体 CLI 服务接口类型
        var forbiddenTypes = new[]
        {
            typeof(IAntigravityCliService)
        };

        var violations = batchJobTypes
            .SelectMany(t => t.GetConstructors(BindingFlags.Public | BindingFlags.Instance)
                .SelectMany(ctor => ctor.GetParameters())
                .Where(p => forbiddenTypes.Contains(p.ParameterType))
                .Select(p => new { Type = t, Parameter = p }))
            .ToList();

        // 4. 断言没有违规依赖
        if (violations.Any())
        {
            var details = string.Join("\n", violations.Select(v => 
                $"类 '{v.Type.FullName}' 的构造函数参数 '{v.Parameter.Name}' 违规直接依赖了具体 CLI 服务 '{v.Parameter.ParameterType.Name}'。请改用 'ICliChainService'。"));
            
            Assert.Fail($"架构约束违规：批处理服务禁止直接依赖具体 CLI 服务接口。\n{details}");
        }
    }
}
