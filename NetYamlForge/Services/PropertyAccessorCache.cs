using System;
using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;

namespace NetYamlForge.Services;

/// <summary>
/// 反射プロパティアクセスのキャッシュ。Expression を用いて高速な Setter をコンパイルし、キャッシュします。
/// </summary>
public static class PropertyAccessorCache
{
    private static readonly ConcurrentDictionary<(Type, string), Action<object, object?>> _setters = new();
    private const int MaxCacheSize = 2000;

    public static void SetValue(object target, string propertyName, object? value)
    {
        if (target == null) throw new ArgumentNullException(nameof(target));
        if (string.IsNullOrEmpty(propertyName)) throw new ArgumentNullException(nameof(propertyName));

        var type = target.GetType();
        var key = (type, propertyName);

        var setter = _setters.GetOrAdd(key, k =>
        {
            var prop = k.Item1.GetProperty(k.Item2, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            if (prop == null || !prop.CanWrite)
            {
                return (t, v) => { };
            }

            var targetParam = Expression.Parameter(typeof(object), "target");
            var valueParam = Expression.Parameter(typeof(object), "value");

            var castTarget = Expression.Convert(targetParam, k.Item1);
            var castValue = Expression.Convert(valueParam, prop.PropertyType);

            var setMethod = prop.GetSetMethod(true);
            if (setMethod == null)
            {
                return (t, v) => prop.SetValue(t, v);
            }

            var body = Expression.Call(castTarget, setMethod, castValue);
            var lambda = Expression.Lambda<Action<object, object?>>(body, targetParam, valueParam);
            return lambda.Compile();
        });

        if (_setters.Count >= MaxCacheSize)
        {
            _setters.Clear();
        }

        setter(target, value);
    }
}
