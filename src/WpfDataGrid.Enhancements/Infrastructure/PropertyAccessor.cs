using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.Reflection;

namespace WpfDataGrid.Enhancements.Infrastructure;

/// <summary>
/// 基于反射的属性访问器（带缓存）：供过滤、导出统一读取数据行属性值，
/// 避免每次取值都走完整反射链路（10 万行场景下性能关键）。
/// </summary>
public static class PropertyAccessor
{
    private sealed class PropertyEntry
    {
        public Func<object, object?>? Getter;
        public Type? PropertyType;
    }

    private static readonly ConcurrentDictionary<Type, ConcurrentDictionary<string, PropertyEntry>> TypeCache =
        new ConcurrentDictionary<Type, ConcurrentDictionary<string, PropertyEntry>>();

    private static ConcurrentDictionary<string, PropertyEntry> GetEntries(Type type)
        => TypeCache.GetOrAdd(type, _ => new ConcurrentDictionary<string, PropertyEntry>(StringComparer.Ordinal));

    /// <summary>尝试获取指定属性的读取委托；属性不存在或不可读时返回 null。</summary>
    public static Func<object, object?>? GetGetter(Type itemType, string propertyName)
    {
        if (itemType == null || string.IsNullOrEmpty(propertyName)) return null;

        var entries = GetEntries(itemType);
        if (entries.TryGetValue(propertyName, out var cached))
        {
            return cached.Getter;
        }

        var property = itemType.GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
        if (property == null || !property.CanRead)
        {
            entries.TryAdd(propertyName, new PropertyEntry());
            return null;
        }

        var method = property.GetGetMethod();
        Func<object, object?> getter = method != null
            ? (item => { try { return method.Invoke(item, null); } catch (Exception) { return null; } })
            : (_ => null);

        entries[propertyName] = new PropertyEntry { Getter = getter, PropertyType = property.PropertyType };
        return getter;
    }

    /// <summary>读取属性值；属性不存在、不可读或读取失败时返回 null。</summary>
    public static object? GetValue(object item, string propertyName)
    {
        if (item == null || string.IsNullOrEmpty(propertyName)) return null;
        var getter = GetGetter(item.GetType(), propertyName);
        return getter?.Invoke(item);
    }

    /// <summary>获取属性类型；属性不存在或不可读时返回 null。</summary>
    public static Type? GetPropertyType(Type itemType, string propertyName)
    {
        if (itemType == null || string.IsNullOrEmpty(propertyName)) return null;

        var entries = GetEntries(itemType);
        if (entries.TryGetValue(propertyName, out var cached))
        {
            return cached.PropertyType;
        }

        var property = itemType.GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
        if (property == null || !property.CanRead)
        {
            entries.TryAdd(propertyName, new PropertyEntry());
            return null;
        }

        entries[propertyName] = new PropertyEntry { Getter = item => property.GetValue(item, null), PropertyType = property.PropertyType };
        return property.PropertyType;
    }

    /// <summary>把任意值规范化为显示字符串（导出 / 过滤共用）。</summary>
    public static string ToDisplayString(object? value)
        => value == null ? string.Empty : Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
}
