using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace NetYamlForge.Services.BatchJob;

/// <summary>
/// 批处理作业步骤间传递输入输出的上下文管道。
/// 用于 DAG 多步骤流转中，在步骤之间传递数据。
/// </summary>
public class BatchJobPipeContext
{
    private readonly ConcurrentDictionary<string, object?> _data = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 设置管道参数
    /// </summary>
    public void Set(string key, object? value)
    {
        _data[key] = value;
    }

    /// <summary>
    /// 获取管道参数
    /// </summary>
    public object? Get(string key)
    {
        _data.TryGetValue(key, out var value);
        return value;
    }

    /// <summary>
    /// 获取强类型的管道参数
    /// </summary>
    public T? Get<T>(string key)
    {
        if (_data.TryGetValue(key, out var value) && value is T typedValue)
        {
            return typedValue;
        }
        return default;
    }

    /// <summary>
    /// 是否包含指定的键
    /// </summary>
    public bool ContainsKey(string key)
    {
        return _data.ContainsKey(key);
    }

    /// <summary>
    /// 移除键
    /// </summary>
    public bool Remove(string key)
    {
        return _data.TryRemove(key, out _);
    }

    /// <summary>
    /// 清除所有上下文数据
    /// </summary>
    public void Clear()
    {
        _data.Clear();
    }

    /// <summary>
    /// 获取全部数据的只读字典快照
    /// </summary>
    public IReadOnlyDictionary<string, object?> GetAll()
    {
        return _data;
    }
}
