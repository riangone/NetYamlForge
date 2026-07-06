using System.Reflection;
using BenchmarkDotNet.Attributes;
using NetYamlForge.Services;

namespace NetYamlForge.Benchmarks;

[MemoryDiagnoser]
public class EntityAccessorBench
{
    public class DummyEntity
    {
        public List<string>? Hooks { get; set; }
    }

    private readonly DummyEntity _target = new();
    private readonly List<string> _value = new() { "Hook1", "Hook2" };
    private PropertyInfo? _propInfo;

    [GlobalSetup]
    public void Setup()
    {
        _propInfo = typeof(DummyEntity).GetProperty(nameof(DummyEntity.Hooks));
    }

    [Benchmark]
    public void ReflectionSetValue()
    {
        _propInfo?.SetValue(_target, _value);
    }

    [Benchmark]
    public void CachedExpressionSetValue()
    {
        PropertyAccessorCache.SetValue(_target, nameof(DummyEntity.Hooks), _value);
    }
}
