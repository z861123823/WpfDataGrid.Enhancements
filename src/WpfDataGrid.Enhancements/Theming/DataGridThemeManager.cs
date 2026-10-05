using System;
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
/// 支持浅色 / 深色 / 跟随系统（监听系统主题变更自动切换）。
/// </summary>
public static class DataGridThemeManager
{
    /// <summary>内置主题资源字典的包 URI 前缀。</summary>
    public const string ThemePackPrefix = "pack://application:,,,/WpfDataGrid.Enhancements;component/Theming/Themes/";

    /// <summary>浅色主题文件名。</summary>
    public const string LightThemeFileName = "Enhancements.Light.xaml";

    /// <summary>深色主题文件名。</summary>
    public const string DarkThemeFileName = "Enhancements.Dark.xaml";

    private static ResourceDictionary? _appliedDictionary;
    private static ThemeMode? _currentMode;
    private static bool _systemListenerAttached;

    /// <summary>将指定主题资源合并到 Application.Resources（重复键由新主题覆盖）。</summary>
    public static void ApplyTheme(ThemeMode mode)
    {
        _currentMode = mode;

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
    }

    private static void ApplyDictionary(ThemeMode mode)
    {
        if (Application.Current == null) return;

        RemoveAppliedDictionary();

        var fileName = mode == ThemeMode.Dark ? DarkThemeFileName : LightThemeFileName;
        var dictionary = new ResourceDictionary
        {
            Source = new Uri(ThemePackPrefix + fileName, UriKind.Absolute)
        };

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
