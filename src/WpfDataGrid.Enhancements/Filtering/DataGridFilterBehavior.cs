using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Shapes;
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

    /// <summary>
    /// 过滤即将生效前触发（可取消）。参数含列名与即将生效的谓词。
    /// 设置 <see cref="FilterChangingEventArgs.Cancel"/> 为 true 可阻止本次过滤。
    /// </summary>
    public event EventHandler<FilterChangingEventArgs>? Filtering;

    /// <summary>
    /// 过滤已生效后触发。参数含列名与已生效的谓词（空列表表示该列过滤已被清除）。
    /// </summary>
    public event EventHandler<FilterChangedEventArgs>? Filtered;

    /// <summary>
    /// 自定义过滤面板工厂；为 null 时使用内置 <see cref="FilterPanel"/>。
    /// 委托签名：(owner, columnName, propertyType, closeAction) → IFilterPanel。
    /// 注入的面板必须同时是 <see cref="System.Windows.UIElement"/>（如 UserControl / ContentControl），
    /// 否则在创建弹层时抛出 <see cref="InvalidOperationException"/>。
    /// </summary>
    public Func<DataGridFilterBehavior, string, Type?, Action, IFilterPanel>? FilterPanelFactory { get; set; }

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
    {
        // 优先从关联对象（DataGrid）向上查找（能覆盖窗口/控件级合并主题，且不依赖 Application 是否已预加载）
        if (AssociatedObject?.TryFindResource("DataGridEnhancements.FilterButton") is Style style)
        {
            return style;
        }

        // 兜底 Application 级主题字典（DataGridThemeManager 统一合并到这里）
        return Application.Current?.TryFindResource("DataGridEnhancements.FilterButton") as Style;
    }

    /// <summary>
    /// 以编程方式为指定列应用一组过滤谓词（等价于过滤面板点击"应用"）。
    /// 触发 <see cref="Filtering"/>（可取消）与 <see cref="Filtered"/> 事件。
    /// </summary>
    /// <param name="columnName">列绑定路径 / 列名。</param>
    /// <param name="predicates">要生效的谓词集合；传空集合等价于清除该列过滤。</param>
    /// <returns>true 表示过滤已应用；被事件取消时返回 false。</returns>
    public bool ApplyColumnFilter(string columnName, IEnumerable<IFilterPredicate> predicates)
    {
        if (string.IsNullOrWhiteSpace(columnName)) throw new ArgumentException("列名不能为空。", nameof(columnName));
        if (predicates == null) throw new ArgumentNullException(nameof(predicates));

        var list = new List<IFilterPredicate>(predicates);

        var changing = new FilterChangingEventArgs(columnName, list);
        Filtering?.Invoke(this, changing);
        if (changing.Cancel) return false;

        if (FilterModel != null)
        {
            var column = FilterModel.GetOrAdd(columnName);
            column.ClearPredicates();
            foreach (var predicate in list)
            {
                column.AddPredicate(predicate);
            }
            FilterModel.Invalidate();
        }

        UpdateHeaderIndicator(columnName, list.Count > 0);
        Filtered?.Invoke(this, new FilterChangedEventArgs(columnName, list));
        return true;
    }

    /// <summary>
    /// 清除指定列的全部过滤条件。
    /// 触发 <see cref="Filtering"/>（谓词为空，可取消）与 <see cref="Filtered"/> 事件。
    /// </summary>
    /// <param name="columnName">列绑定路径 / 列名。</param>
    /// <returns>true 表示已清除；被事件取消时返回 false。</returns>
    public bool ClearColumnFilter(string columnName)
    {
        if (string.IsNullOrWhiteSpace(columnName)) throw new ArgumentException("列名不能为空。", nameof(columnName));

        var empty = Array.Empty<IFilterPredicate>();
        var changing = new FilterChangingEventArgs(columnName, empty);
        Filtering?.Invoke(this, changing);
        if (changing.Cancel) return false;

        if (FilterModel != null)
        {
            if (FilterModel.ColumnFilters.TryGetValue(columnName, out var column))
            {
                column.ClearPredicates();
            }
            FilterModel.Invalidate();
        }

        UpdateHeaderIndicator(columnName, false);
        Filtered?.Invoke(this, new FilterChangedEventArgs(columnName, empty));
        return true;
    }

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

            var filterStyle = owner.FindFilterButtonStyle() ?? BuildFallbackFilterButtonStyle();
            _filterButton = new Button
            {
                Width = 24,
                Height = 24,
                Padding = new Thickness(0),
                Margin = new Thickness(10, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                ToolTip = "过滤：" + propertyPath,
                Style = filterStyle
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

            // 默认使用内置 FilterPanel；客户注入 FilterPanelFactory 时用自定义面板（须为 UIElement）。
            var closeAction = new Action(() => _popup.IsOpen = false);
            var panel = owner.FilterPanelFactory != null
                ? owner.FilterPanelFactory(owner, propertyPath, propertyType, closeAction)
                : new FilterPanel(owner, propertyPath, propertyType, closeAction);

            var panelElement = panel as UIElement;
            if (panelElement == null)
            {
                throw new InvalidOperationException(
                    $"FilterPanelFactory 返回的 IFilterPanel 必须同时是 UIElement（实际类型：{panel.GetType().FullName}）。");
            }

            _popup.Child = panelElement;
        }

        private void TogglePopup() => _popup.IsOpen = !_popup.IsOpen;

        public void SetFilterActive(bool active)
        {
            // 活动态：箭头/字符切换为主题强调色（模板内 Path 通过 TemplateBinding Foreground 跟随变化）
            // 注意：非活动态必须 ClearValue 而非赋 null——本地值优先级高于样式 Setter，
            // 赋 null 会覆盖主题样式里的 DynamicResource Foreground，导致模板 Path 的
            // TemplateBinding Foreground 取到 null，漏斗图标不渲染（背景色仍会随主题变化）。
            var activeForeground = Application.Current?.TryFindResource("DataGridEnhancements.FilterButtonActiveForeground") as Brush;
            if (active)
            {
                _filterButton.Foreground = activeForeground;
                _filterButton.FontWeight = FontWeights.Bold;
            }
            else
            {
                _filterButton.ClearValue(Control.ForegroundProperty);
                _filterButton.FontWeight = FontWeights.Normal;
            }
            _filterButton.ToolTip = active ? "该列已启用过滤，点击修改" : "过滤：" + _propertyPath;
        }

        public void Detach()
        {
            _filterButton.Click -= (_, __) => TogglePopup();
            _popup.IsOpen = false;
            _column.Header = _originalHeader;
        }

        /// <summary>兜底样式：主题字典未就绪时，以代码构造等价的漏斗模板，保证图标在任何情况下都正常渲染。</summary>
        private static Style BuildFallbackFilterButtonStyle()
        {
            var style = new Style(typeof(Button));
            style.Setters.Add(new Setter(Control.ForegroundProperty, new DynamicResourceExtension("DataGridEnhancements.FilterButtonForeground")));
            style.Setters.Add(new Setter(Control.BackgroundProperty, new DynamicResourceExtension("DataGridEnhancements.FilterButtonBackground")));
            style.Setters.Add(new Setter(Control.BorderBrushProperty, new DynamicResourceExtension("DataGridEnhancements.FilterButtonBackground")));
            style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0)));
            style.Setters.Add(new Setter(Control.CursorProperty, System.Windows.Input.Cursors.Hand));

            var template = new ControlTemplate(typeof(Button));
            var border = new FrameworkElementFactory(typeof(Border), "Bd");
            border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
            border.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Control.BorderBrushProperty));
            border.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(Control.BorderThicknessProperty));
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(3));

            var path = new FrameworkElementFactory(typeof(Path));
            path.SetValue(Path.DataProperty, Geometry.Parse("M 1.5,1 L 10.5,1 L 7,5.2 L 7,10 L 5,10 L 5,5.2 Z"));
            path.SetValue(Path.FillProperty, new TemplateBindingExtension(Control.ForegroundProperty));
            path.SetValue(Path.WidthProperty, 13.0);
            path.SetValue(Path.HeightProperty, 12.0);
            path.SetValue(Path.StretchProperty, Stretch.Fill);
            path.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            path.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);

            border.AppendChild(path);
            template.VisualTree = border;
            style.Setters.Add(new Setter(Control.TemplateProperty, template));
            return style;
        }
    }
}
