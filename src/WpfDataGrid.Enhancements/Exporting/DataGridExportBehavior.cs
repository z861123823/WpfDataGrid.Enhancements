using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using WpfDataGrid.Enhancements.Behaviors;
using WpfDataGrid.Enhancements.Infrastructure;

namespace WpfDataGrid.Enhancements.Exporting;

/// <summary>导出范围：当前视图 / 选中行 / 全量数据。</summary>
public enum ExportScope
{
    /// <summary>当前过滤后的视图行。</summary>
    CurrentView,

    /// <summary>仅选中的行。</summary>
    SelectedRows,

    /// <summary>忽略过滤的全量数据。</summary>
    AllData
}

/// <summary>
/// 导出主行为：按 <see cref="ExportScope"/> 收集 <see cref="TableData"/>，
/// 调用 <see cref="IExportProvider"/> 写出到目标文件。
/// 内置注册 CSV Provider；Pro 版可注入 xlsx / PDF Provider。
/// </summary>
public class DataGridExportBehavior : DataGridBehaviorBase
{
    /// <summary>当前导出策略；未设置时按文件扩展名从 <see cref="Providers"/> 选择。</summary>
    public IExportProvider? ExportProvider { get; set; }

    /// <summary>按扩展名注册的 Provider 集合（内置含 CSV）。</summary>
    public ICollection<IExportProvider> Providers { get; } = new List<IExportProvider>();

    protected override void OnAttached()
    {
        base.OnAttached();
        if (Providers.Count == 0)
        {
            Providers.Add(new CsvExportProvider());
        }
    }

    protected override void OnDetaching()
    {
        Providers.Clear();
        base.OnDetaching();
    }

    /// <summary>执行导出。</summary>
    /// <param name="scope">导出范围。</param>
    /// <param name="filePath">目标文件路径（扩展名决定 Provider 选择）。</param>
    public void Export(ExportScope scope, string filePath)
        => Export(scope, filePath, null);

    /// <summary>执行导出，支持进度回调（0~100）。</summary>
    public void Export(ExportScope scope, string filePath, IProgress<int>? progress)
    {
        if (string.IsNullOrWhiteSpace(filePath)) throw new ArgumentException("目标文件路径不能为空。", nameof(filePath));

        var provider = SelectProvider(filePath);
        if (provider == null)
        {
            throw new InvalidOperationException(
                $"未找到可处理扩展名“{Path.GetExtension(filePath)}”的导出 Provider。请设置 {nameof(ExportProvider)} 或向 {nameof(Providers)} 注册。");
        }

        using (var stream = File.Create(filePath))
        {
            provider.Export(CollectData(scope, progress), stream);
            stream.Flush();
        }

        progress?.Report(100);
    }

    private IExportProvider? SelectProvider(string filePath)
    {
        if (ExportProvider != null) return ExportProvider;

        var extension = Path.GetExtension(filePath);
        return Providers.FirstOrDefault(
            provider => provider.FileExtension.Equals(extension, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>从 DataGrid 收集列头与行值。</summary>
    private TableData CollectData(ExportScope scope, IProgress<int>? progress)
    {
        var dataGrid = AssociatedObject;
        if (dataGrid == null) throw new InvalidOperationException("行为尚未附加到 DataGrid。");

        var columns = new List<string>();
        var paths = new List<string>();
        foreach (var column in dataGrid.Columns)
        {
            if (column.Visibility == Visibility.Collapsed) continue;

            var header = column.Header?.ToString();
            var path = GetBindingPath(column);
            if (string.IsNullOrEmpty(header) && string.IsNullOrEmpty(path)) continue;

            columns.Add((string.IsNullOrEmpty(header) ? path : header) ?? string.Empty);
            paths.Add(path ?? string.Empty);
        }

        var items = new List<object>();
        switch (scope)
        {
            case ExportScope.SelectedRows:
                foreach (var item in dataGrid.SelectedItems)
                {
                    if (item != null) items.Add(item);
                }
                break;

            case ExportScope.AllData:
                if (dataGrid.ItemsSource is IEnumerable source)
                {
                    foreach (var item in source)
                    {
                        if (item != null) items.Add(item);
                    }
                }
                break;

            case ExportScope.CurrentView:
            default:
                if (dataGrid.ItemsSource != null)
                {
                    var view = CollectionViewSource.GetDefaultView(dataGrid.ItemsSource);
                    foreach (var item in view)
                    {
                        if (item != null) items.Add(item);
                    }
                }
                break;
        }

        var rows = new List<object?[]>(items.Count);
        var total = Math.Max(items.Count, 1);
        for (int i = 0; i < items.Count; i++)
        {
            var row = new object?[paths.Count];
            for (int c = 0; c < paths.Count; c++)
            {
                row[c] = string.IsNullOrEmpty(paths[c])
                    ? null
                    : PropertyAccessor.GetValue(items[i], paths[c]);
            }

            rows.Add(row);

            if (progress != null && (i % 100 == 0 || i == items.Count - 1))
            {
                progress.Report((int)(i * 100L / total));
            }
        }

        return new TableData(columns, rows);
    }

    private static string? GetBindingPath(DataGridColumn column)
    {
        if (column is DataGridBoundColumn boundColumn && boundColumn.Binding is Binding binding &&
            binding.Path != null && !string.IsNullOrEmpty(binding.Path.Path))
        {
            return binding.Path.Path;
        }

        return !string.IsNullOrEmpty(column.SortMemberPath) ? column.SortMemberPath : null;
    }
}
