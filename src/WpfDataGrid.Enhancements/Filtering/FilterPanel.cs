using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace WpfDataGrid.Enhancements.Filtering;

/// <summary>
/// 列头过滤面板：按列数据类型提供文本 / 数值 / 日期 / 枚举多选过滤编辑界面，
/// 应用结果写入所属列的 <see cref="ColumnFilter"/> 并刷新视图。
/// UI 全部采用代码构建，保证 net462 / net8.0-windows 双目标一致。
/// </summary>
public sealed class FilterPanel : UserControl
{
    private readonly DataGridFilterBehavior _owner;
    private readonly string _columnName;
    private readonly Type? _propertyType;
    private readonly Action _closeAction;

    private StackPanel? _contentRoot;

    private TextBox? _textBox;
    private ComboBox? _textMode;
    private CheckBox? _ignoreCase;
    private DispatcherTimer? _debounceTimer;

    private ComboBox? _numericMode;
    private TextBox? _numericMin;
    private TextBox? _numericMax;

    private DatePicker? _dateStart;
    private DatePicker? _dateEnd;

    private readonly List<CheckBox> _enumChecks = new();

    /// <summary>创建一个过滤面板。</summary>
    /// <param name="owner">所属过滤行为。</param>
    /// <param name="columnName">目标列绑定路径 / 列名。</param>
    /// <param name="propertyType">列属性类型（决定编辑器形态）。</param>
    /// <param name="closeAction">关闭弹层回调。</param>
    public FilterPanel(DataGridFilterBehavior owner, string columnName, Type? propertyType, Action closeAction)
    {
        _owner = owner ?? throw new ArgumentNullException(nameof(owner));
        _columnName = columnName ?? throw new ArgumentNullException(nameof(columnName));
        _propertyType = propertyType;
        _closeAction = closeAction ?? throw new ArgumentNullException(nameof(closeAction));

        BuildUi();
        LoadCurrentState();
    }

    private void BuildUi()
    {
        var root = new StackPanel
        {
            Margin = new Thickness(12),
            MinWidth = 260,
            MaxWidth = 340,
            Background = SystemColors.WindowBrush
        };

        var title = new TextBlock
        {
            Text = "过滤：" + _columnName,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(10, 10, 10, 4)
        };
        root.Children.Add(title);

        _contentRoot = new StackPanel { Margin = new Thickness(10, 0, 10, 0) };
        root.Children.Add(_contentRoot);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(10, 10, 10, 10)
        };

        var apply = new Button { Content = "应用", Width = 72, Margin = new Thickness(0, 0, 8, 0) };
        apply.Click += (_, __) => Apply();

        var clear = new Button { Content = "清除", Width = 72, Margin = new Thickness(0, 0, 8, 0) };
        clear.Click += (_, __) => Clear();

        var close = new Button { Content = "关闭", Width = 72 };
        close.Click += (_, __) => _closeAction();

        buttons.Children.Add(apply);
        buttons.Children.Add(clear);
        buttons.Children.Add(close);
        root.Children.Add(buttons);

        Content = root;
        BuildEditor();
    }

    private void BuildEditor()
    {
        if (_contentRoot == null) return;
        _contentRoot.Children.Clear();

        var targetType = _propertyType ?? typeof(object);

        if (targetType == typeof(string))
        {
            BuildTextEditor();
        }
        else if (targetType == typeof(bool) || targetType.IsEnum)
        {
            BuildEnumEditor(targetType);
        }
        else if (IsNumericType(targetType))
        {
            BuildNumericEditor();
        }
        else if (targetType == typeof(DateTime) || targetType == typeof(DateTimeOffset))
        {
            BuildDateEditor();
        }
        else
        {
            _contentRoot.Children.Add(new TextBlock
            {
                Text = "该列类型暂不支持内置过滤编辑器，请使用自定义谓词（IFilterPredicate）。",
                TextWrapping = TextWrapping.Wrap,
                Foreground = SystemColors.GrayTextBrush
            });
        }
    }

    private void BuildTextEditor()
    {
        if (_contentRoot == null) return;

        _textBox = new TextBox { Margin = new Thickness(0, 0, 0, 6) };
        _textBox.TextChanged += OnTextChangedDebounced;
        _contentRoot.Children.Add(_textBox);

        var hint = new TextBlock
        {
            Text = "输入文字即时过滤（防抖 300ms）",
            FontSize = 11,
            Foreground = SystemColors.GrayTextBrush,
            Margin = new Thickness(0, 0, 0, 6)
        };
        _contentRoot.Children.Add(hint);

        var modeRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
        modeRow.Children.Add(new TextBlock { Text = "模式：", VerticalAlignment = VerticalAlignment.Center });

        _textMode = new ComboBox { Width = 120, VerticalAlignment = VerticalAlignment.Center };
        _textMode.Items.Add(new ComboBoxItem { Content = "包含", Tag = TextMatchMode.Contains });
        _textMode.Items.Add(new ComboBoxItem { Content = "前缀", Tag = TextMatchMode.StartsWith });
        _textMode.Items.Add(new ComboBoxItem { Content = "后缀", Tag = TextMatchMode.EndsWith });
        _textMode.SelectedIndex = 0;
        _textMode.SelectionChanged += (_, __) => ApplyIfHasText();
        modeRow.Children.Add(_textMode);
        _contentRoot.Children.Add(modeRow);

        _ignoreCase = new CheckBox { Content = "忽略大小写", IsChecked = true, Margin = new Thickness(0, 0, 0, 4) };
        _ignoreCase.Checked += (_, __) => ApplyIfHasText();
        _ignoreCase.Unchecked += (_, __) => ApplyIfHasText();
        _contentRoot.Children.Add(_ignoreCase);
    }

    private void BuildNumericEditor()
    {
        if (_contentRoot == null) return;

        var modeRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
        modeRow.Children.Add(new TextBlock { Text = "条件：", VerticalAlignment = VerticalAlignment.Center });

        _numericMode = new ComboBox { Width = 150, VerticalAlignment = VerticalAlignment.Center };
        _numericMode.Items.Add(new ComboBoxItem { Content = "区间 [最小, 最大]", Tag = NumericCompareMode.Between });
        _numericMode.Items.Add(new ComboBoxItem { Content = "等于", Tag = NumericCompareMode.Equals });
        _numericMode.Items.Add(new ComboBoxItem { Content = "大于", Tag = NumericCompareMode.GreaterThan });
        _numericMode.Items.Add(new ComboBoxItem { Content = "小于", Tag = NumericCompareMode.LessThan });
        _numericMode.SelectedIndex = 0;
        _numericMode.SelectionChanged += (_, __) => UpdateNumericMaxVisibility();
        modeRow.Children.Add(_numericMode);
        _contentRoot.Children.Add(modeRow);

        var minRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
        minRow.Children.Add(new TextBlock { Text = "最小 / 目标值：", Width = 100, VerticalAlignment = VerticalAlignment.Center });
        _numericMin = new TextBox { Width = 130 };
        minRow.Children.Add(_numericMin);
        _contentRoot.Children.Add(minRow);

        var maxRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
        maxRow.Children.Add(new TextBlock { Text = "最大：", Width = 100, VerticalAlignment = VerticalAlignment.Center });
        _numericMax = new TextBox { Width = 130 };
        maxRow.Children.Add(_numericMax);
        _contentRoot.Children.Add(maxRow);

        UpdateNumericMaxVisibility();
    }

    private void UpdateNumericMaxVisibility()
    {
        if (_numericMode == null || _numericMax == null) return;
        var mode = NumericCompareMode.Between;
        if (_numericMode.SelectedItem is ComboBoxItem item && item.Tag is NumericCompareMode m) mode = m;
        var visibility = mode == NumericCompareMode.Between ? Visibility.Visible : Visibility.Collapsed;
        _numericMax.Visibility = visibility;
    }

    private void BuildDateEditor()
    {
        if (_contentRoot == null) return;

        var startRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
        startRow.Children.Add(new TextBlock { Text = "起始日期：", Width = 90, VerticalAlignment = VerticalAlignment.Center });
        _dateStart = new DatePicker { Width = 160 };
        startRow.Children.Add(_dateStart);
        _contentRoot.Children.Add(startRow);

        var endRow = new StackPanel { Orientation = Orientation.Horizontal };
        endRow.Children.Add(new TextBlock { Text = "结束日期：", Width = 90, VerticalAlignment = VerticalAlignment.Center });
        _dateEnd = new DatePicker { Width = 160 };
        endRow.Children.Add(_dateEnd);
        _contentRoot.Children.Add(endRow);
    }

    private void BuildEnumEditor(Type type)
    {
        if (_contentRoot == null) return;

        var values = new List<object>();
        if (type == typeof(bool))
        {
            values.Add(true);
            values.Add(false);
        }
        else
        {
            foreach (var value in Enum.GetValues(type)) values.Add(value);
        }

        foreach (var value in values)
        {
            var check = new CheckBox
            {
                Content = Convert.ToString(value, CultureInfo.InvariantCulture),
                Tag = value,
                Margin = new Thickness(0, 2, 0, 2)
            };
            check.Checked += (_, __) => Apply();
            check.Unchecked += (_, __) => Apply();
            _enumChecks.Add(check);
            _contentRoot.Children.Add(check);
        }

        var tools = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
        var all = new Button { Content = "全选", Width = 60, Margin = new Thickness(0, 0, 8, 0) };
        all.Click += (_, __) =>
        {
            foreach (var check in _enumChecks) check.IsChecked = true;
            Apply();
        };
        var none = new Button { Content = "全不选", Width = 60 };
        none.Click += (_, __) =>
        {
            foreach (var check in _enumChecks) check.IsChecked = false;
            Apply();
        };
        tools.Children.Add(all);
        tools.Children.Add(none);
        _contentRoot.Children.Add(tools);
    }

    private void OnTextChangedDebounced(object sender, TextChangedEventArgs e)
    {
        if (_debounceTimer == null)
        {
            _debounceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
            _debounceTimer.Tick += (_, __) =>
            {
                _debounceTimer.Stop();
                Apply();
            };
        }

        _debounceTimer.Stop();
        _debounceTimer.Start();
    }

    private void ApplyIfHasText()
    {
        if (!string.IsNullOrEmpty(_textBox?.Text)) Apply();
    }

    private void LoadCurrentState()
    {
        if (_owner.FilterModel == null) return;

        ColumnFilter? column = null;
        foreach (var pair in _owner.FilterModel.ColumnFilters)
        {
            if (string.Equals(pair.Key, _columnName, StringComparison.Ordinal))
            {
                column = pair.Value;
                break;
            }
        }

        if (column == null || column.IsEmpty) return;

        foreach (var predicate in column.Predicates)
        {
            if (predicate is TextContainsPredicate textPredicate)
            {
                if (_textBox != null) _textBox.Text = textPredicate.SearchText;
                if (_textMode != null)
                {
                    for (int i = 0; i < _textMode.Items.Count; i++)
                    {
                        if (_textMode.Items[i] is ComboBoxItem item && item.Tag is TextMatchMode mode && mode == textPredicate.Mode)
                        {
                            _textMode.SelectedIndex = i;
                            break;
                        }
                    }
                }
                if (_ignoreCase != null) _ignoreCase.IsChecked = textPredicate.IgnoreCase;
            }
            else if (predicate is NumericRangePredicate numericPredicate)
            {
                if (_numericMin != null)
                    _numericMin.Text = numericPredicate.Min.HasValue ? numericPredicate.Min.Value.ToString(CultureInfo.InvariantCulture) : string.Empty;
                if (_numericMax != null)
                    _numericMax.Text = numericPredicate.Max.HasValue ? numericPredicate.Max.Value.ToString(CultureInfo.InvariantCulture) : string.Empty;
                if (_numericMode != null)
                {
                    for (int i = 0; i < _numericMode.Items.Count; i++)
                    {
                        if (_numericMode.Items[i] is ComboBoxItem item && item.Tag is NumericCompareMode mode && mode == numericPredicate.Mode)
                        {
                            _numericMode.SelectedIndex = i;
                            break;
                        }
                    }
                }
                UpdateNumericMaxVisibility();
            }
            else if (predicate is DateRangePredicate datePredicate)
            {
                if (_dateStart != null && datePredicate.Start.HasValue) _dateStart.SelectedDate = datePredicate.Start.Value;
                if (_dateEnd != null && datePredicate.End.HasValue) _dateEnd.SelectedDate = datePredicate.End.Value;
            }
            else if (predicate is EnumMultiSelectPredicate enumPredicate)
            {
                foreach (var check in _enumChecks)
                {
                    check.IsChecked = enumPredicate.Match(check.Tag);
                }
            }
        }
    }

    private IFilterPredicate? BuildPredicate()
    {
        var targetType = _propertyType ?? typeof(object);

        if (targetType == typeof(string))
        {
            var text = _textBox?.Text?.Trim();
            if (string.IsNullOrEmpty(text)) return null;

            var mode = TextMatchMode.Contains;
            if (_textMode?.SelectedItem is ComboBoxItem modeItem && modeItem.Tag is TextMatchMode textMode) mode = textMode;
            var ignoreCase = _ignoreCase?.IsChecked != false;
            return new TextContainsPredicate(text!, ignoreCase, mode);
        }

        if (targetType == typeof(bool) || targetType.IsEnum)
        {
            var selected = new List<object>();
            foreach (var check in _enumChecks)
            {
                if (check.IsChecked == true && check.Tag != null) selected.Add(check.Tag);
            }
            if (selected.Count == 0) return null;
            return new EnumMultiSelectPredicate(selected);
        }

        if (IsNumericType(targetType))
        {
            var mode = NumericCompareMode.Between;
            if (_numericMode?.SelectedItem is ComboBoxItem modeItem2 && modeItem2.Tag is NumericCompareMode numericMode) mode = numericMode;

            var min = ParseNullableDouble(_numericMin?.Text);
            var max = ParseNullableDouble(_numericMax?.Text);

            if (mode != NumericCompareMode.Between && !min.HasValue) return null;
            if (mode == NumericCompareMode.Between && !min.HasValue && !max.HasValue) return null;

            return new NumericRangePredicate(min, max, mode);
        }

        if (targetType == typeof(DateTime) || targetType == typeof(DateTimeOffset))
        {
            var start = _dateStart?.SelectedDate;
            var end = _dateEnd?.SelectedDate;
            if (!start.HasValue && !end.HasValue) return null;

            // 结束日期取当天 23:59:59.999，保证"某日"含当天全部时刻。
            DateTime? endInclusive = end.HasValue ? end.Value.Date.AddDays(1).AddTicks(-1) : (DateTime?)null;
            return new DateRangePredicate(start, endInclusive);
        }

        return null;
    }

    private void Apply()
    {
        var model = _owner.FilterModel;
        if (model == null) return;

        var column = model.GetOrAdd(_columnName);
        column.ClearPredicates();

        var predicate = BuildPredicate();
        if (predicate != null) column.AddPredicate(predicate);

        model.Invalidate();
        _owner.UpdateHeaderIndicator(_columnName, !column.IsEmpty);
    }

    private void Clear()
    {
        var model = _owner.FilterModel;
        if (model == null) return;

        ColumnFilter? column = null;
        foreach (var pair in model.ColumnFilters)
        {
            if (string.Equals(pair.Key, _columnName, StringComparison.Ordinal))
            {
                column = pair.Value;
                break;
            }
        }

        if (column != null) column.ClearPredicates();
        model.Invalidate();
        _owner.UpdateHeaderIndicator(_columnName, false);
        ResetControls();
    }

    private void ResetControls()
    {
        if (_textBox != null) _textBox.Text = string.Empty;
        if (_numericMin != null) _numericMin.Text = string.Empty;
        if (_numericMax != null) _numericMax.Text = string.Empty;
        if (_dateStart != null) _dateStart.SelectedDate = null;
        if (_dateEnd != null) _dateEnd.SelectedDate = null;
        if (_enumChecks.Count > 0)
        {
            foreach (var check in _enumChecks) check.IsChecked = false;
        }
    }

    private static bool IsNumericType(Type type)
    {
        switch (Type.GetTypeCode(type))
        {
            case TypeCode.Byte:
            case TypeCode.SByte:
            case TypeCode.Int16:
            case TypeCode.UInt16:
            case TypeCode.Int32:
            case TypeCode.UInt32:
            case TypeCode.Int64:
            case TypeCode.UInt64:
            case TypeCode.Single:
            case TypeCode.Double:
            case TypeCode.Decimal:
                return true;
            default:
                return false;
        }
    }

    private static double? ParseNullableDouble(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        double value;
        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value)) return value;
        if (double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value)) return value;
        return null;
    }
}
