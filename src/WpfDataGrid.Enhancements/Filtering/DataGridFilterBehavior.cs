using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using Microsoft.Xaml.Behaviors;
using WpfDataGrid.Enhancements.Behaviors;
using WpfDataGrid.Enhancements.Infrastructure;

namespace WpfDataGrid.Enhancements.Filtering;

/// <summary>
/// 过滤主行为：为每个可过滤列生成列头过滤入口（按钮 + <see cref="FilterPanel"/> 弹层），
/// 维护 <see cref="FilterModel"/>，并通过 ICollectionView.Filter 应用多列组合过滤，
/// 保证过滤后 UI 与数据同步（视图刷新即数据刷新）。
/// </summary>
public class DataGridFilterBehavior : DataGridBehaviorBase
{
    /// <summary>当前过滤模型。</summary>
    public static readonly DependencyProperty FilterModelProperty =
        DependencyProperty.Register(
            nameof(FilterModel),
            typeof(FilterModel),
            typeof(DataGridFilterBehavior),
            new PropertyMetadata(null, OnFilterModelChanged));

    /// <summary>当前过滤模型；为 null 时行为自动创建一个默认模型。</summary>
    public FilterModel? FilterModel
    {
        get => (FilterModel?)GetValue(FilterModelProperty);
        set => SetValue(FilterModelProperty, value);
    }

    private readonly Dictionary<DataGridColumn, FilterColumnHeader> _headers = new();
    private DependencyPropertyDescriptor? _itemsSourceDescriptor;
    private Type? _itemType;

    private static void OnFilterModelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var behavior = (DataGridFilterBehavior)d;

        if (e.OldValue is FilterModel oldModel)
        {
            oldModel.Changed -= behavior.OnModelChanged;
        }

        if (e.NewValue is FilterModel newModel)
        {
            newModel.Changed += behavior.OnModelChanged;
            if (behavior.AssociatedObject != null) behavior.ApplyFilter();
        }
    }

    protected override void OnAttached()
    {
        base.OnAttached();

        if (FilterModel == null) FilterModel = new FilterModel();
        FilterModel.Changed += OnModelChanged;

        _itemsSourceDescriptor = DependencyPropertyDescriptor.FromProperty(
            ItemsControl.ItemsSourceProperty,
            typeof(DataGrid));
        _itemsSourceDescriptor.AddValueChanged(AssociatedObject, OnItemsSourceChanged);

        ResolveItemType();
        foreach (var column in AssociatedObject.Columns)
        {
            AttachColumnHeader(column);
        }
        AssociatedObject.Columns.CollectionChanged += OnColumnsCollectionChanged;

        ApplyFilter();
    }

    protected override void OnDetaching()
    {
        if (FilterModel != null) FilterModel.Changed -= OnModelChanged;

        if (_itemsSourceDescriptor != null && AssociatedObject != null)
        {
            _itemsSourceDescriptor.RemoveValueChanged(AssociatedObject, OnItemsSourceChanged);
            _itemsSourceDescriptor = null;
        }

        if (AssociatedObject != null)
        {
            AssociatedObject.Columns.CollectionChanged -= OnColumnsCollectionChanged;
        }

        foreach (var header in _headers.Values)
        {
            header.Detach();
        }
        _headers.Clear();

        RestoreDefaultFilter();
        base.OnDetaching();
    }

    private void OnItemsSourceChanged(object? sender, EventArgs e)
    {
        ResolveItemType();

        // 数据源类型变化后重建列头入口，保证编辑器类型正确。
        foreach (var header in _headers.Values)
        {
            header.Detach();
        }
        _headers.Clear();
        foreach (var column in AssociatedObject.Columns)
        {
            AttachColumnHeader(column);
        }

        ApplyFilter();
    }

    private void OnColumnsCollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Add && e.NewItems != null)
        {
            foreach (var item in e.NewItems)
            {
                if (item is DataGridColumn column) AttachColumnHeader(column);
            }
        }
        else if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Remove && e.OldItems != null)
        {
            foreach (var item in e.OldItems)
            {
                if (item is DataGridColumn column && _headers.TryGetValue(column, out var header))
                {
                    header.Detach();
                    _headers.Remove(column);
                }
            }
        }
        else if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Reset)
        {
            foreach (var header in _headers.Values) header.Detach();
            _headers.Clear();
            foreach (var column in AssociatedObject.Columns) AttachColumnHeader(column);
        }
    }

    private void OnModelChanged(object? sender, EventArgs e) => ApplyFilter();

    private void ResolveItemType()
    {
        _itemType = null;
        var source = AssociatedObject.ItemsSource;
        if (source == null) return;

        var type = source.GetType();
        foreach (var interfaceType in type.GetInterfaces())
        {
            if (interfaceType.IsGenericType && interfaceType.GetGenericTypeDefinition() == typeof(IEnumerable<>))
            {
                _itemType = interfaceType.GetGenericArguments()[0];
                break;
            }
        }

        if (_itemType == null && source is IEnumerable enumerable)
        {
            foreach (var item in enumerable)
            {
                if (item != null)
                {
                    _itemType = item.GetType();
                    break;
                }
            }
        }
    }

    private void AttachColumnHeader(DataGridColumn column)
    {
        if (_headers.ContainsKey(column)) return;

        var path = GetColumnPropertyPath(column);
        if (string.IsNullOrEmpty(path)) return;

        var propertyType = _itemType != null ? PropertyAccessor.GetPropertyType(_itemType, path) : null;
        var header = new FilterColumnHeader(column, this, path, propertyType);
        _headers[column] = header;
        column.Header = header.Root;

        UpdateHeaderIndicator(path, HasFilter(path));
    }

    private static string GetColumnPropertyPath(DataGridColumn column)
    {
        if (column is DataGridBoundColumn boundColumn && boundColumn.Binding is Binding binding &&
            binding.Path != null && !string.IsNullOrEmpty(binding.Path.Path))
        {
            return binding.Path.Path;
        }

        if (!string.IsNullOrEmpty(column.SortMemberPath)) return column.SortMemberPath;

        return column.Header?.ToString() ?? string.Empty;
    }

    /// <summary>应用当前过滤条件到关联 DataGrid 的视图。</summary>
    public void ApplyFilter()
    {
        var dataGrid = AssociatedObject;
        if (dataGrid == null || dataGrid.ItemsSource == null) return;

        if (FilterModel == null || FilterModel.IsEmpty)
        {
            RestoreDefaultFilter();
            return;
        }

        var view = CollectionViewSource.GetDefaultView(dataGrid.ItemsSource);
        view.Filter = item => FilterModel.IsMatch(item);
        view.Refresh();
    }

    private void RestoreDefaultFilter()
    {
        if (AssociatedObject == null || AssociatedObject.ItemsSource == null) return;

        var view = CollectionViewSource.GetDefaultView(AssociatedObject.ItemsSource);
        view.Filter = null;
        view.Refresh();
    }

    private bool HasFilter(string columnName)
        => FilterModel != null &&
           FilterModel.ColumnFilters.TryGetValue(columnName, out var columnFilter) &&
           !columnFilter.IsEmpty;

    /// <summary>更新列头过滤指示（按钮加粗表示该列已有生效条件）。</summary>
    internal void UpdateHeaderIndicator(string columnName, bool hasFilter)
    {
        foreach (var header in _headers.Values)
        {
            if (string.Equals(header.PropertyPath, columnName, StringComparison.Ordinal))
            {
                header.SetFilterActive(hasFilter);
                return;
            }
        }
    }

    /// <summary>查找主题提供的过滤按钮样式；未定义时使用默认样式。</summary>
    internal Style? FindFilterButtonStyle()
        => Application.Current?.TryFindResource("DataGridEnhancements.FilterButton") as Style;

    /// <summary>
    /// 单列列头包装：原列头 + 过滤按钮，点击弹出 <see cref="FilterPanel"/>。
    /// </summary>
    private sealed class FilterColumnHeader
    {
        private readonly DataGridColumn _column;
        private readonly DataGridFilterBehavior _owner;
        private readonly object? _originalHeader;
        private readonly Popup _popup;
        private readonly Button _filterButton;
        private readonly string _propertyPath;

        public FrameworkElement Root { get; }

        public string PropertyPath => _propertyPath;

        public FilterColumnHeader(DataGridColumn column, DataGridFilterBehavior owner, string propertyPath, Type? propertyType)
        {
            _column = column;
            _owner = owner;
            _propertyPath = propertyPath;
            _originalHeader = column.Header;

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            FrameworkElement? headerElement = _originalHeader as FrameworkElement;
            if (headerElement == null)
            {
                headerElement = new TextBlock
                {
                    Text = _originalHeader?.ToString() ?? propertyPath,
                    VerticalAlignment = VerticalAlignment.Center,
                    TextTrimming = TextTrimming.CharacterEllipsis
                };
            }

            Grid.SetColumn(headerElement, 0);
            grid.Children.Add(headerElement);

            _filterButton = new Button
            {
                Content = "▼",
                Width = 20,
                Height = 20,
                Padding = new Thickness(0),
                Margin = new Thickness(4, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                ToolTip = "过滤：" + propertyPath,
                Style = owner.FindFilterButtonStyle()
            };
            Grid.SetColumn(_filterButton, 1);
            grid.Children.Add(_filterButton);

            _filterButton.Click += (_, __) => TogglePopup();

            Root = grid;

            _popup = new Popup
            {
                PlacementTarget = _filterButton,
                Placement = PlacementMode.Bottom,
                StaysOpen = true,
                AllowsTransparency = true
            };

            var panel = new FilterPanel(owner, propertyPath, propertyType, () => _popup.IsOpen = false);
            _popup.Child = panel;
        }

        private void TogglePopup() => _popup.IsOpen = !_popup.IsOpen;

        public void SetFilterActive(bool active)
        {
            _filterButton.FontWeight = active ? FontWeights.Bold : FontWeights.Normal;
            _filterButton.ToolTip = active ? "该列已启用过滤，点击修改" : "过滤：" + _propertyPath;
        }

        public void Detach()
        {
            _filterButton.Click -= (_, __) => TogglePopup();
            _popup.IsOpen = false;
            _column.Header = _originalHeader;
        }
    }
}
