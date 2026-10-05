using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using Microsoft.Xaml.Behaviors;
using WpfDataGrid.Enhancements;
using WpfDataGrid.Enhancements.Exporting;
using WpfDataGrid.Enhancements.Pro;
using WpfDataGrid.Enhancements.Pro.Exporting;
using WpfDataGrid.Enhancements.Pro.Performance;
using WpfDataGrid.Enhancements.Theming;

namespace WpfDataGrid.Enhancements.Demo;

/// <summary>
/// Demo 主窗口：用 10 万行模拟业务数据演示过滤（列头过滤弹层）、
/// CSV / xlsx / PDF 导出、浅色 / 深色 / 跟随系统主题切换，
/// 以及 Pro 大数据异步虚拟化（按需分页加载 + 滚动加载 + 缓存）。
/// </summary>
public partial class MainWindow : Window
{
    private const int RecordCount = 100000;
    private const int VirtualPageSize = 500;

    private static readonly string[] Regions = { "华东", "华南", "华北", "西南", "华中", "东北", "西北" };
    private static readonly string[] Products = { "笔记本电脑", "智能手机", "显示器", "键盘", "鼠标", "耳机", "打印机", "路由器" };
    private static readonly string[] Categories = { "计算机", "通讯", "外设", "网络" };
    private static readonly string[] Statuses = { "已完成", "处理中", "待发货", "已取消" };
    private static readonly string[] Priorities = { "高", "中", "低" };

    private readonly List<OrderRecord> _records = new List<OrderRecord>(RecordCount);
    private AsyncVirtualizingCollection<OrderRecord>? _virtualCollection;

    public MainWindow()
    {
        InitializeComponent();

        GenerateData();
        MainDataGrid.ItemsSource = _records;
        StatusText.Text = $"共 {_records.Count:N0} 行 · 普通模式（列头 ☰ 可过滤，底部按钮可导出）";
        LicenseText.Text = LicenseManager.IsLicensed
            ? "Pro 授权：已激活"
            : "Pro 授权：未激活（Demo 仍可体验 xlsx/PDF 导出）";
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
        if (ThemeComboBox.SelectedItem is ComboBoxItem item && item.Tag is string mode)
        {
            var value = (ThemeMode)Enum.Parse(typeof(ThemeMode), mode, ignoreCase: true);
            DataGridExtensions.SetThemeMode(MainDataGrid, value);
        }
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
        EnsureProProviders(behavior);

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

    private DataGridExportBehavior GetExportBehavior()
        => Interaction.GetBehaviors(MainDataGrid).OfType<DataGridExportBehavior>().First();

    #endregion

    #region 大数据虚拟化

    private void VirtualModeToggle_Checked(object sender, RoutedEventArgs e)
    {
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
    }

    private void VirtualModeToggle_Unchecked(object sender, RoutedEventArgs e)
    {
        MainDataGrid.ItemsSource = _records;
        StatusText.Text = $"共 {_records.Count:N0} 行 · 普通模式（列头 ☰ 可过滤，底部按钮可导出）";
    }

    #endregion
}
