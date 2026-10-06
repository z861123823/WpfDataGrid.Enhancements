using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using Microsoft.Xaml.Behaviors;
using WpfDataGrid.Enhancements;
using WpfDataGrid.Enhancements.Exporting;
using WpfDataGrid.Enhancements.Filtering;
#if PRO
using WpfDataGrid.Enhancements.Pro;
using WpfDataGrid.Enhancements.Pro.Exporting;
using WpfDataGrid.Enhancements.Pro.Performance;
#endif
using WpfDataGrid.Enhancements.Theming;

namespace WpfDataGrid.Enhancements.Demo;

/// <summary>
/// Demo 主窗口：用 10 万行模拟业务数据演示过滤（列头过滤弹层：淡入位移动画 / 阴影 / Esc 关闭 / 自动聚焦）、
/// CSV / xlsx / PDF 导出（Exporting/Exported 事件联动状态栏）、浅色 / 深色 / 跟随系统主题切换、
/// 运行时自定义主题注册（DataGridThemeManager.RegisterTheme + ApplyTheme），
/// 以及 Pro 大数据异步虚拟化（按需分页加载 + 滚动加载 + 缓存）。
/// </summary>
public partial class MainWindow : Window
{
    private const int RecordCount = 100000;
    private const int VirtualPageSize = 500;
    private const string CustomThemeName = "翡翠绿";

    private static readonly string[] Regions = { "华东", "华南", "华北", "西南", "华中", "东北", "西北" };
    private static readonly string[] Products = { "笔记本电脑", "智能手机", "显示器", "键盘", "鼠标", "耳机", "打印机", "路由器" };
    private static readonly string[] Categories = { "计算机", "通讯", "外设", "网络" };
    private static readonly string[] Statuses = { "已完成", "处理中", "待发货", "已取消" };
    private static readonly string[] Priorities = { "高", "中", "低" };

    private readonly List<OrderRecord> _records = new List<OrderRecord>(RecordCount);
#if PRO
    private AsyncVirtualizingCollection<OrderRecord>? _virtualCollection;
#endif
    private static bool _customThemeRegistered;

    public MainWindow()
    {
        // 必须在 InitializeComponent 之前合并主题字典：
        // XAML 解析到 DataGrid 的 ThemeMode="System" 附加属性时，位于其前的窗口元素
        // （Window.Background / 工具栏 / 卡片等）的 DynamicResource 首轮求值已经完成，
        // 若此时才合并主题，启动瞬间会表现为背景黑、样式缺失、表格默认样式。
        // 提前应用可确保窗口 XAML 解析开始时主题资源即已就位，所有 DynamicResource 首轮命中。
        DataGridThemeManager.ApplyTheme(ThemeMode.System);

        InitializeComponent();

        GenerateData();
        MainDataGrid.ItemsSource = _records;
        RowCountChip.Text = $"{_records.Count:N0} 行";
        StatusText.Text = $"共 {_records.Count:N0} 行 · 普通模式（列头漏斗可过滤，工具栏按钮可导出）";
#if PRO
        LicenseText.Text = LicenseManager.IsLicensed
            ? "Pro 授权：已激活"
            : "Pro 授权：未激活（Demo 仍可体验 xlsx/PDF 导出）";
#else
        LicenseText.Text = "Pro 组件未编译（开源演示；构建加 /p:EnablePro=true 可启用 xlsx/PDF/虚拟化）";
#endif
        ThemeStatusText.Text = "主题：跟随系统";

        SubscribeBehaviorEvents();
        ApplyInitialTheme();

        // 启动参数支持：--theme dark|light（真实窗口截图验证时按主题启动）
        var args = Environment.GetCommandLineArgs();
        for (int i = 1; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], "--theme", StringComparison.OrdinalIgnoreCase))
            {
                if (string.Equals(args[i + 1], "dark", StringComparison.OrdinalIgnoreCase))
                {
                    ApplyThemeByTag("Dark");
                    ThemeComboBox.SelectedIndex = 2; // 深色
                }
                else if (string.Equals(args[i + 1], "light", StringComparison.OrdinalIgnoreCase))
                {
                    ApplyThemeByTag("Light");
                    ThemeComboBox.SelectedIndex = 1; // 浅色
                }
            }
        }
    }

    private void ApplyInitialTheme()
    {
        // XAML 中 DataGridExtensions.ThemeMode="System" 在附加行为时已生效；
        // 这里仅同步状态文本。
        var mode = DataGridExtensions.GetThemeMode(MainDataGrid);
        ThemeStatusText.Text = mode == ThemeMode.System
            ? "主题：跟随系统"
            : mode == ThemeMode.Light ? "主题：浅色" : "主题：深色";
    }

    /// <summary>订阅过滤 / 导出行为事件：状态栏实时展示过滤与导出进展。</summary>
    private void SubscribeBehaviorEvents()
    {
        var behaviors = Interaction.GetBehaviors(MainDataGrid);

        var filterBehavior = behaviors.OfType<DataGridFilterBehavior>().FirstOrDefault();
        if (filterBehavior != null)
        {
            filterBehavior.Filtering += (_, e) =>
            {
                // 演示可取消事件：此处不取消，仅展示参数。
                StatusText.Text = e.Predicates.Count == 0
                    ? $"准备清除“{e.ColumnName}”列过滤…"
                    : $"准备过滤“{e.ColumnName}”（{e.Predicates.Count} 个条件）…";
            };
            filterBehavior.Filtered += (_, e) =>
            {
                StatusText.Text = e.Predicates.Count == 0
                    ? $"已清除“{e.ColumnName}”列过滤"
                    : $"已过滤“{e.ColumnName}”：{e.Predicates.Count} 个条件生效";
            };
        }

        var exportBehavior = behaviors.OfType<DataGridExportBehavior>().FirstOrDefault();
        if (exportBehavior != null)
        {
            exportBehavior.Exporting += (_, e) =>
            {
                StatusText.Text = $"正在导出 {e.RowCount:N0} 行 → {e.FilePath}";
            };
            exportBehavior.Exported += (_, e) =>
            {
                StatusText.Text = $"导出完成：{e.FilePath}（{e.RowCount:N0} 行）";
            };
        }
    }

    /// <summary>模拟订单业务数据。</summary>
    public sealed class OrderRecord
    {
        public int OrderId { get; set; }
        public DateTime OrderDate { get; set; }
        public string Customer { get; set; } = string.Empty;
        public string Region { get; set; } = string.Empty;
        public string Product { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public int Quantity { get; set; }
        public decimal UnitPrice { get; set; }
        public decimal Amount => Quantity * UnitPrice;
        public string Status { get; set; } = string.Empty;
        public string Priority { get; set; } = string.Empty;
    }

    private void GenerateData()
    {
        var rnd = new Random(20241005);
        var start = new DateTime(2023, 1, 1);
        var minutesPerYear = 365 * 24 * 60;

        for (var i = 1; i <= RecordCount; i++)
        {
            _records.Add(new OrderRecord
            {
                OrderId = i,
                OrderDate = start.AddMinutes(rnd.Next(0, minutesPerYear)),
                Customer = "客户 " + rnd.Next(1, 5000),
                Region = Regions[rnd.Next(Regions.Length)],
                Product = Products[rnd.Next(Products.Length)],
                Category = Categories[rnd.Next(Categories.Length)],
                Quantity = rnd.Next(1, 50),
                UnitPrice = Math.Round((decimal)(rnd.NextDouble() * 8000 + 99), 2),
                Status = Statuses[rnd.Next(Statuses.Length)],
                Priority = Priorities[rnd.Next(Priorities.Length)]
            });
        }
    }

    #region 主题切换

    private void ThemeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // XAML 解析顺序：工具栏（含 ComboBox）先于 DataGrid 创建，
        // 构造函数 InitializeComponent 期间可能触发本事件，此时 DataGrid 尚未创建。
        if (MainDataGrid == null)
        {
            return;
        }

        if (ThemeComboBox.SelectedItem is ComboBoxItem item && item.Tag is string tag)
        {
            ApplyThemeByTag(tag);
        }
    }

    private void ApplyCustomTheme_Click(object sender, RoutedEventArgs e)
    {
        EnsureCustomThemeRegistered();
        if (DataGridThemeManager.ApplyTheme(CustomThemeName))
        {
            ThemeComboBox.SelectedIndex = ThemeComboBox.Items.IndexOf(
                ThemeComboBox.Items.Cast<ComboBoxItem>().First(i => (i.Tag as string) == "Custom"));
            ThemeStatusText.Text = $"主题：自定义（{CustomThemeName}）";
            StatusText.Text = $"已运行时注册并切换主题：{CustomThemeName}（RegisterTheme + ApplyTheme）";
        }
    }

    private void ApplyThemeByTag(string tag)
    {
        if (tag == "Custom")
        {
            EnsureCustomThemeRegistered();
            if (DataGridThemeManager.ApplyTheme(CustomThemeName))
            {
                ThemeStatusText.Text = $"主题：自定义（{CustomThemeName}）";
            }
            return;
        }

        var mode = (ThemeMode)Enum.Parse(typeof(ThemeMode), tag, ignoreCase: true);
        DataGridExtensions.SetThemeMode(MainDataGrid, mode);
        ThemeStatusText.Text = mode == ThemeMode.System ? "主题：跟随系统" : mode == ThemeMode.Light ? "主题：浅色" : "主题：深色";
    }

    private static void EnsureCustomThemeRegistered()
    {
        if (_customThemeRegistered) return;
        DataGridThemeManager.RegisterTheme(CustomThemeName, BuildGreenTheme());
        _customThemeRegistered = true;
    }

    /// <summary>
    /// 构建“翡翠绿”自定义主题：以内置浅色主题为基底，覆盖 Demo 联动画刷键。
    /// 键与内置主题对齐（DataGridEnhancements.*），因此 DataGrid 与界面整体换肤。
    /// </summary>
    private static ResourceDictionary BuildGreenTheme()
    {
        var dictionary = new ResourceDictionary();
        dictionary.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri(DataGridThemeManager.ThemePackPrefix + DataGridThemeManager.LightThemeFileName, UriKind.Absolute)
        });

        dictionary["DataGridEnhancements.PageBackground"] = new SolidColorBrush(Color.FromRgb(0xED, 0xF6, 0xED));
        dictionary["DataGridEnhancements.CardBackground"] = new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF));
        dictionary["DataGridEnhancements.CardBorderBrush"] = new SolidColorBrush(Color.FromRgb(0xB8, 0xD8, 0xB8));
        dictionary["DataGridEnhancements.TextPrimary"] = new SolidColorBrush(Color.FromRgb(0x14, 0x33, 0x1A));
        dictionary["DataGridEnhancements.TextSecondary"] = new SolidColorBrush(Color.FromRgb(0x4E, 0x6B, 0x54));
        dictionary["DataGridEnhancements.DemoCheckedBrush"] = new SolidColorBrush(Color.FromRgb(0xD8, 0xEE, 0xD8));
        return dictionary;
    }

    #endregion

    #region 导出

    private void ExportCsv_Click(object sender, RoutedEventArgs e)
    {
        Export("CSV 文件|*.csv", "orders.csv", ExportScope.CurrentView);
    }

    private void ExportXlsx_Click(object sender, RoutedEventArgs e)
    {
        Export("Excel 工作簿|*.xlsx", "orders.xlsx", ExportScope.CurrentView);
    }

    private void ExportPdf_Click(object sender, RoutedEventArgs e)
    {
        Export("PDF 文档|*.pdf", "orders.pdf", ExportScope.CurrentView);
    }

    private void Export(string filter, string defaultFileName, ExportScope scope)
    {
        var behavior = GetExportBehavior();
#if PRO
        EnsureProProviders(behavior);
#endif

        var dialog = new SaveFileDialog
        {
            Filter = filter,
            FileName = defaultFileName,
            Title = "导出 DataGrid 数据"
        };

        if (dialog.ShowDialog(this) != true) return;

        try
        {
            behavior.Export(scope, dialog.FileName);
            StatusText.Text = $"已导出：{dialog.FileName}";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "导出失败：" + ex.Message, "WPF DataGrid 增强包", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

#if PRO
    private static void EnsureProProviders(DataGridExportBehavior behavior)
    {
        if (behavior.Providers.All(p => !(p is XlsxExportProvider)))
        {
            behavior.Providers.Add(new XlsxExportProvider());
        }

        if (behavior.Providers.All(p => !(p is PdfExportProvider)))
        {
            behavior.Providers.Add(new PdfExportProvider());
        }
    }
#endif

    private DataGridExportBehavior GetExportBehavior()
        => Interaction.GetBehaviors(MainDataGrid).OfType<DataGridExportBehavior>().First();

    #endregion

    #region 大数据虚拟化

    private void VirtualModeToggle_Checked(object sender, RoutedEventArgs e)
    {
#if PRO
        if (_virtualCollection == null)
        {
            _virtualCollection = new AsyncVirtualizingCollection<OrderRecord>(
                (offset, count) => _records.Skip(offset).Take(count),
                pageSize: VirtualPageSize,
                totalCount: _records.Count,
                marshal: action => Dispatcher.Invoke(action));
        }

        MainDataGrid.ItemsSource = _virtualCollection;
        StatusText.Text = $"共 {_records.Count:N0} 行 · 大数据虚拟化模式（按需分页加载 {VirtualPageSize} 行/页，滚动即加载）";
#else
        VirtualModeToggle.IsChecked = false;
        StatusText.Text = "大数据虚拟化属 Pro 组件，当前为开源编译；构建加 /p:EnablePro=true 后可体验";
#endif
    }

    private void VirtualModeToggle_Unchecked(object sender, RoutedEventArgs e)
    {
        MainDataGrid.ItemsSource = _records;
        StatusText.Text = $"共 {_records.Count:N0} 行 · 普通模式（列头 ☰ 可过滤，底部按钮可导出）";
    }

    #endregion
}
