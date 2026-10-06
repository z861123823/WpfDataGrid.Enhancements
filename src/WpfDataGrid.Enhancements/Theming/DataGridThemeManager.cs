using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using Microsoft.Win32;

namespace WpfDataGrid.Enhancements.Theming;

/// <summary>主题模式。</summary>
public enum ThemeMode
{
    /// <summary>跟随系统。</summary>
    System = 0,

    /// <summary>浅色。</summary>
    Light = 1,

    /// <summary>深色。</summary>
    Dark = 2
}

/// <summary>
/// 主题资源管理与运行时切换。
/// 主题资源为纯 XAML ResourceDictionary（Enhancements.Light / Enhancements.Dark），无外部依赖。
/// 支持浅色 / 深色 / 跟随系统（监听系统主题变更自动切换），
/// 并支持运行时注册自定义主题（<see cref="RegisterTheme"/> + <see cref="ApplyTheme(string)"/>）。
/// </summary>
public static class DataGridThemeManager
{
    /// <summary>内置主题资源字典的包 URI 前缀。</summary>
    public const string ThemePackPrefix = "pack://application:,,,/WpfDataGrid.Enhancements;component/Theming/Themes/";

    /// <summary>浅色主题文件名。</summary>
    public const string LightThemeFileName = "Enhancements.Light.xaml";

    /// <summary>深色主题文件名。</summary>
    public const string DarkThemeFileName = "Enhancements.Dark.xaml";

    private static readonly Dictionary<string, ResourceDictionary> _customThemes =
        new Dictionary<string, ResourceDictionary>(StringComparer.OrdinalIgnoreCase);

    private static ResourceDictionary? _appliedDictionary;
    private static ThemeMode? _currentMode;
    private static string? _appliedCustomTheme;
    private static bool _systemListenerAttached;

    /// <summary>已注册的自定义主题名称（只读视图；名称不区分大小写）。</summary>
    public static IReadOnlyCollection<string> RegisteredThemeNames => _customThemes.Keys;

    /// <summary>当前是否正在应用某个自定义主题。</summary>
    public static bool IsCustomThemeApplied => _appliedCustomTheme != null;

    /// <summary>当前生效的自定义主题名称；内置主题时返回 null。</summary>
    public static string? AppliedCustomThemeName => _appliedCustomTheme;

    /// <summary>
    /// 注册一个可运行时切换的自定义主题。
    /// 同名主题重复注册时替换旧字典（若该主题正在生效，会立即重新合并以反映最新内容）。
    /// </summary>
    /// <param name="name">主题名称（不区分大小写，不可为空）。</param>
    /// <param name="dictionary">主题资源字典，键与内置主题对齐（DataGridEnhancements.*）时可完整覆盖配色。</param>
    public static void RegisterTheme(string name, ResourceDictionary dictionary)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("主题名称不能为空。", nameof(name));
        if (dictionary == null) throw new ArgumentNullException(nameof(dictionary));

        _customThemes[name] = dictionary;

        if (string.Equals(_appliedCustomTheme, name, StringComparison.OrdinalIgnoreCase))
        {
            // 当前正在生效的主题被重新注册：立即用新字典替换，避免界面停留在旧资源上。
            RemoveAppliedDictionary();
            ApplyDictionary(dictionary);
        }
    }

    /// <summary>
    /// 切换到已注册的自定义主题（见 <see cref="RegisterTheme"/>）。
    /// 主题资源覆盖内置键后，DataGrid 与 Demo 界面会同步换肤。
    /// </summary>
    /// <param name="name">已注册的主题名称。</param>
    /// <returns>主题是否存在且切换成功；未注册时返回 false 且不改变当前主题。</returns>
    public static bool ApplyTheme(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        if (!_customThemes.TryGetValue(name, out var dictionary)) return false;

        _currentMode = null;
        _appliedCustomTheme = name;
        DetachSystemListener();
        ApplyDictionary(dictionary);
        return true;
    }

    /// <summary>获取已注册的自定义主题字典；未注册时返回 null。</summary>
    public static ResourceDictionary? TryGetRegisteredTheme(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        return _customThemes.TryGetValue(name, out var dictionary) ? dictionary : null;
    }

    /// <summary>将指定主题资源合并到 Application.Resources（重复键由新主题覆盖）。</summary>
    public static void ApplyTheme(ThemeMode mode)
    {
        _currentMode = mode;
        _appliedCustomTheme = null;

        if (mode == ThemeMode.System)
        {
            AttachSystemListener();
            ApplyDictionary(ResolveSystemTheme());
        }
        else
        {
            DetachSystemListener();
            ApplyDictionary(mode);
        }
    }

    /// <summary>从当前 Application.Resources 移除已合并的增强主题字典。</summary>
    public static void ClearTheme()
    {
        DetachSystemListener();
        RemoveAppliedDictionary();
        _currentMode = null;
        _appliedCustomTheme = null;
    }

    private static void ApplyDictionary(ThemeMode mode)
    {
        if (Application.Current == null) return;

        var fileName = mode == ThemeMode.Dark ? DarkThemeFileName : LightThemeFileName;
        var dictionary = new ResourceDictionary
        {
            Source = new Uri(ThemePackPrefix + fileName, UriKind.Absolute)
        };

        ApplyDictionary(dictionary);
    }

    private static void ApplyDictionary(ResourceDictionary dictionary)
    {
        if (Application.Current == null) return;

        RemoveAppliedDictionary();

        Application.Current.Resources.MergedDictionaries.Add(dictionary);
        _appliedDictionary = dictionary;
    }

    private static void RemoveAppliedDictionary()
    {
        if (_appliedDictionary == null || Application.Current == null) return;

        Application.Current.Resources.MergedDictionaries.Remove(_appliedDictionary);
        _appliedDictionary = null;
    }

    /// <summary>读取系统"应用使用浅色主题"设置；读取失败时回退浅色。</summary>
    private static ThemeMode ResolveSystemTheme()
    {
        try
        {
            using (var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
            {
                var value = key?.GetValue("AppsUseLightTheme");
                if (value is int intValue && intValue == 0)
                {
                    return ThemeMode.Dark;
                }
            }
        }
        catch (Exception)
        {
            // 注册表不可读时按浅色处理。
        }

        return ThemeMode.Light;
    }

    private static void AttachSystemListener()
    {
        if (_systemListenerAttached) return;
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
        _systemListenerAttached = true;
    }

    private static void DetachSystemListener()
    {
        if (!_systemListenerAttached) return;
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        _systemListenerAttached = false;
    }

    private static void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category != UserPreferenceCategory.General) return;
        if (_currentMode != ThemeMode.System || Application.Current == null) return;

        Application.Current.Dispatcher.BeginInvoke((Action)(() => ApplyDictionary(ResolveSystemTheme())));
    }
}
