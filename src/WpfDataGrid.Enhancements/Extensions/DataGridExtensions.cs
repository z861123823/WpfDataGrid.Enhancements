using System.Linq;
using System.Windows;
using System.Windows.Controls;
using WpfDataGrid.Enhancements.Theming;

namespace WpfDataGrid.Enhancements;

/// <summary>
/// 静态附加属性入口（公开 API 门面）：
/// 以声明式方式为任意 WPF DataGrid 启用过滤、导出与主题能力。
/// 推荐集成方式：Behavior 为主 + 附加属性为辅（不推荐子类）。
/// </summary>
public static class DataGridExtensions
{
    /// <summary>启用多列组合过滤。</summary>
    public static readonly DependencyProperty EnableFilteringProperty =
        DependencyProperty.RegisterAttached(
            "EnableFiltering",
            typeof(bool),
            typeof(DataGridExtensions),
            new PropertyMetadata(false, OnEnableFilteringChanged));

    /// <summary>启用数据导出。</summary>
    public static readonly DependencyProperty EnableExportingProperty =
        DependencyProperty.RegisterAttached(
            "EnableExporting",
            typeof(bool),
            typeof(DataGridExtensions),
            new PropertyMetadata(false, OnEnableExportingChanged));

    /// <summary>主题模式（Light / Dark / System）。</summary>
    public static readonly DependencyProperty ThemeModeProperty =
        DependencyProperty.RegisterAttached(
            "ThemeMode",
            typeof(ThemeMode),
            typeof(DataGridExtensions),
            new PropertyMetadata(ThemeMode.System, OnThemeModeChanged));

    public static void SetEnableFiltering(DependencyObject element, bool value)
        => element.SetValue(EnableFilteringProperty, value);

    public static bool GetEnableFiltering(DependencyObject element)
        => (bool)element.GetValue(EnableFilteringProperty);

    public static void SetEnableExporting(DependencyObject element, bool value)
        => element.SetValue(EnableExportingProperty, value);

    public static bool GetEnableExporting(DependencyObject element)
        => (bool)element.GetValue(EnableExportingProperty);

    public static void SetThemeMode(DependencyObject element, ThemeMode value)
        => element.SetValue(ThemeModeProperty, value);

    public static ThemeMode GetThemeMode(DependencyObject element)
        => (ThemeMode)element.GetValue(ThemeModeProperty);

    private static void OnEnableFilteringChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (!(d is DataGrid dataGrid)) return;

        var behaviors = Microsoft.Xaml.Behaviors.Interaction.GetBehaviors(dataGrid);
        var existing = behaviors.OfType<Filtering.DataGridFilterBehavior>().FirstOrDefault();

        if ((bool)e.NewValue && existing == null)
        {
            behaviors.Add(new Filtering.DataGridFilterBehavior());
        }
        else if (!(bool)e.NewValue && existing != null)
        {
            behaviors.Remove(existing);
        }
    }

    private static void OnEnableExportingChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (!(d is DataGrid dataGrid)) return;

        var behaviors = Microsoft.Xaml.Behaviors.Interaction.GetBehaviors(dataGrid);
        var existing = behaviors.OfType<Exporting.DataGridExportBehavior>().FirstOrDefault();

        if ((bool)e.NewValue && existing == null)
        {
            behaviors.Add(new Exporting.DataGridExportBehavior());
        }
        else if (!(bool)e.NewValue && existing != null)
        {
            behaviors.Remove(existing);
        }
    }

    private static void OnThemeModeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        // 主题切换由 DataGridThemeManager 统一处理（浅色 / 深色 / 跟随系统）。
        if (e.NewValue is ThemeMode mode)
        {
            DataGridThemeManager.ApplyTheme(mode);
        }
    }
}
