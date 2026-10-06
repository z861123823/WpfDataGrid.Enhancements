using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;

namespace WpfDataGrid.Enhancements.Filtering;

/// <summary>
/// 列头过滤面板：按列数据类型提供文本 / 数值 / 日期 / 枚举多选过滤编辑界面，
/// 应用结果写入所属列的 <see cref="ColumnFilter"/> 并刷新视图。
/// UI 全部采用代码构建，保证 net462 / net8.0-windows 双目标一致。
/// </summary>
public sealed class FilterPanel : UserControl, IFilterPanel
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
    private Style? _buttonStyle;
    private Style? _checkStyle;
    private Brush? _inputBackground;
    private Brush? _inputBorder;

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

        // 打开动画初始态：透明 + 轻微上移，Loaded 后播放淡入与位移动画。
        Opacity = 0;
        RenderTransform = new TranslateTransform(0, -10);
        RenderTransformOrigin = new Point(0.5, 0.5);
        Effect = new DropShadowEffect
        {
            BlurRadius = 18,
            ShadowDepth = 4,
            Direction = 270,
            Opacity = 0.35,
            Color = Colors.Black
        };

        BuildUi();
        LoadCurrentState();

        // 打开时聚焦到输入控件；Esc 关闭弹层。
        Loaded += (_, __) =>
        {
            Dispatcher.BeginInvoke(new Action(FocusInput), DispatcherPriority.Input);
            PlayOpenAnimation();
        };
        PreviewKeyDown += OnPreviewKeyDown;
    }

    private void PlayOpenAnimation()
    {
        var duration = TimeSpan.FromMilliseconds(170);
        var easing = new QuadraticEase { EasingMode = EasingMode.EaseOut };

        BeginAnimation(OpacityProperty,
            new DoubleAnimation(0, 1, duration) { EasingFunction = easing });
        if (RenderTransform is TranslateTransform translate)
        {
            translate.BeginAnimation(TranslateTransform.YProperty,
                new DoubleAnimation(-10, 0, duration) { EasingFunction = easing });
        }
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            _closeAction();
        }
    }

    /// <summary>将键盘焦点移到面板最合适的输入控件（文本 / 数值 / 日期 / 枚举）。</summary>
    public void FocusInput()
    {
        Control? target = _textBox ?? (Control?)_numericMin ?? _dateStart;
        if (target == null && _enumChecks.Count > 0) target = _enumChecks[0];
        if (target == null) target = this;

        target.Focus();
        Keyboard.Focus(target);
    }

    /// <summary>关闭弹层（Esc、关闭按钮与客户面板均可调用）。</summary>
    public void Close() => _closeAction();

    private void BuildUi()
    {
        _buttonStyle = FindBrush("DataGridEnhancements.FilterPanelButton") as Style;
        _checkStyle = FindBrush("DataGridEnhancements.FilterPanelCheckBox") as Style;
        var primaryStyle = FindBrush("DataGridEnhancements.FilterPanelPrimaryButton") as Style;
        var cardBrush = FindBrush("DataGridEnhancements.CardBackground") as Brush ?? SystemColors.WindowBrush;
        _inputBackground = FindBrush("DataGridEnhancements.FilterInputBackground") as Brush ?? SystemColors.WindowBrush;
        _inputBorder = FindBrush("DataGridEnhancements.FilterInputBorderBrush") as Brush ?? SystemColors.ControlDarkBrush;
        var textBrush = FindBrush("DataGridEnhancements.TextPrimary") as Brush ?? SystemColors.WindowTextBrush;
        var borderBrush = FindBrush("DataGridEnhancements.FilterPanelBorderBrush") as Brush ?? SystemColors.ControlDarkBrush;
        var separatorBrush = FindBrush("DataGridEnhancements.FilterPanelSeparatorBrush") as Brush ?? SystemColors.ControlLightBrush;

        // UserControl.Foreground 沿属性继承传给子 TextBlock，深色主题下文字颜色同步联动。
        Foreground = textBrush;

        // 外层圆角边框容器：圆角 8 + 1px 主题淡化边框 + 卡片背景；DropShadow 保留在 UserControl 上。
        var shell = new Border
        {
            CornerRadius = new CornerRadius(8),
            BorderBrush = borderBrush,
            BorderThickness = new Thickness(1),
            Background = cardBrush,
            ClipToBounds = true
        };

        var root = new StackPanel
        {
            Margin = new Thickness(14),
            MinWidth = 284,
            MaxWidth = 360
        };

        // 标题栏：标题 + 分隔线
        var title = new TextBlock
        {
            Text = "筛选：" + _columnName,
            FontWeight = FontWeights.SemiBold,
            FontSize = 13,
            Foreground = textBrush,
            Margin = new Thickness(2, 0, 2, 6)
        };
        root.Children.Add(title);

        var separator = new Border
        {
            Height = 1,
            Background = separatorBrush,
            Margin = new Thickness(0, 0, 0, 12)
        };
        root.Children.Add(separator);

        _contentRoot = new StackPanel();
        root.Children.Add(_contentRoot);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 14, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Right
        };

        var apply = new Button { Content = "应用", Width = 78, Height = 30, Margin = new Thickness(0, 0, 8, 0), Style = primaryStyle };
        apply.Click += (_, __) => Apply();

        var clear = new Button { Content = "清除", Width = 78, Height = 30, Margin = new Thickness(0, 0, 8, 0), Style = _buttonStyle };
        clear.Click += (_, __) => Clear();

        var close = new Button { Content = "关闭", Width = 78, Height = 30, Style = _buttonStyle };
        close.Click += (_, __) => _closeAction();

        buttons.Children.Add(apply);
        buttons.Children.Add(clear);
        buttons.Children.Add(close);
        root.Children.Add(buttons);

        shell.Child = root;
        Content = shell;
        BuildEditor();
    }

    /// <summary>从应用资源查找画刷 / 样式等对象；未找到返回 null（TryFindResource 找不到键时返回 null，不抛异常）。</summary>
    private static object? FindBrush(string key)
        => Application.Current?.TryFindResource(key);

    /// <summary>统一输入控件配色：背景 / 前景 / 边框（浅色、深色主题各自定义资源）。</summary>
    private void StyleInput(Control control)
    {
        control.Background = _inputBackground;
        control.Foreground = Foreground;
        control.BorderBrush = _inputBorder;
        control.BorderThickness = new Thickness(1);
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
        var secondaryBrush = FindBrush("DataGridEnhancements.TextSecondary") as Brush ?? SystemColors.GrayTextBrush;

        _textBox = new TextBox { Margin = new Thickness(0, 0, 0, 6), Height = 30, VerticalContentAlignment = VerticalAlignment.Center };
        StyleInput(_textBox);
        _textBox.TextChanged += OnTextChangedDebounced;
        _contentRoot.Children.Add(_textBox);

        var hint = new TextBlock
        {
            Text = "输入文字即时过滤（防抖 300ms）",
            FontSize = 12,
            Foreground = secondaryBrush,
            Margin = new Thickness(0, 0, 0, 8)
        };
        _contentRoot.Children.Add(hint);

        var modeRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
        modeRow.Children.Add(new TextBlock
        {
            Text = "模式：",
            FontSize = 12,
            Foreground = secondaryBrush,
            VerticalAlignment = VerticalAlignment.Center
        });

        _textMode = new ComboBox { Width = 132, Height = 30, VerticalAlignment = VerticalAlignment.Center };
        _textMode.Items.Add(new ComboBoxItem { Content = "包含", Tag = TextMatchMode.Contains });
        _textMode.Items.Add(new ComboBoxItem { Content = "前缀", Tag = TextMatchMode.StartsWith });
        _textMode.Items.Add(new ComboBoxItem { Content = "后缀", Tag = TextMatchMode.EndsWith });
        _textMode.SelectedIndex = 0;
        _textMode.SelectionChanged += (_, __) => ApplyIfHasText();
        modeRow.Children.Add(_textMode);
        _contentRoot.Children.Add(modeRow);

        _ignoreCase = new CheckBox
        {
            Content = "忽略大小写",
            IsChecked = true,
            Margin = new Thickness(0, 0, 0, 4),
            FontSize = 12,
            Style = _checkStyle
        };
        _ignoreCase.Checked += (_, __) => ApplyIfHasText();
        _ignoreCase.Unchecked += (_, __) => ApplyIfHasText();
        _contentRoot.Children.Add(_ignoreCase);
    }

    private void BuildNumericEditor()
    {
        if (_contentRoot == null) return;
        var secondaryBrush = FindBrush("DataGridEnhancements.TextSecondary") as Brush ?? SystemColors.GrayTextBrush;

        var modeRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        modeRow.Children.Add(new TextBlock
        {
            Text = "条件：",
            FontSize = 12,
            Foreground = secondaryBrush,
            VerticalAlignment = VerticalAlignment.Center
        });

        _numericMode = new ComboBox { Width = 150, Height = 30, VerticalAlignment = VerticalAlignment.Center };
        StyleInput(_numericMode);
        _numericMode.Items.Add(new ComboBoxItem { Content = "区间 [最小, 最大]", Tag = NumericCompareMode.Between });
        _numericMode.Items.Add(new ComboBoxItem { Content = "等于", Tag = NumericCompareMode.Equals });
        _numericMode.Items.Add(new ComboBoxItem { Content = "大于", Tag = NumericCompareMode.GreaterThan });
        _numericMode.Items.Add(new ComboBoxItem { Content = "小于", Tag = NumericCompareMode.LessThan });
        _numericMode.SelectedIndex = 0;
        _numericMode.SelectionChanged += (_, __) => UpdateNumericMaxVisibility();
        modeRow.Children.Add(_numericMode);
        _contentRoot.Children.Add(modeRow);

        var minRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        minRow.Children.Add(new TextBlock
        {
            Text = "最小 / 目标值：",
            Width = 100,
            FontSize = 12,
            Foreground = secondaryBrush,
            VerticalAlignment = VerticalAlignment.Center
        });
        _numericMin = new TextBox { Width = 130, Height = 30, VerticalContentAlignment = VerticalAlignment.Center };
        StyleInput(_numericMin);
        minRow.Children.Add(_numericMin);
        _contentRoot.Children.Add(minRow);

        var maxRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        maxRow.Children.Add(new TextBlock
        {
            Text = "最大：",
            Width = 100,
            FontSize = 12,
            Foreground = secondaryBrush,
            VerticalAlignment = VerticalAlignment.Center
        });
        _numericMax = new TextBox { Width = 130, Height = 30, VerticalContentAlignment = VerticalAlignment.Center };
        StyleInput(_numericMax);
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
        var secondaryBrush = FindBrush("DataGridEnhancements.TextSecondary") as Brush ?? SystemColors.GrayTextBrush;

        var startRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        startRow.Children.Add(new TextBlock
        {
            Text = "起始日期：",
            Width = 90,
            FontSize = 12,
            Foreground = secondaryBrush,
            VerticalAlignment = VerticalAlignment.Center
        });
        _dateStart = new DatePicker { Width = 160, Height = 30 };
        StyleInput(_dateStart);
        startRow.Children.Add(_dateStart);
        _contentRoot.Children.Add(startRow);

        var endRow = new StackPanel { Orientation = Orientation.Horizontal };
        endRow.Children.Add(new TextBlock
        {
            Text = "结束日期：",
            Width = 90,
            FontSize = 12,
            Foreground = secondaryBrush,
            VerticalAlignment = VerticalAlignment.Center
        });
        _dateEnd = new DatePicker { Width = 160, Height = 30 };
        StyleInput(_dateEnd);
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

        // 多选区放入滚动容器：枚举项较多时可滚动，避免面板撑爆屏幕。
        var scroll = new ScrollViewer
        {
            MaxHeight = 180,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Margin = new Thickness(0, 0, 0, 6)
        };
        var list = new StackPanel();

        foreach (var value in values)
        {
            var check = new CheckBox
            {
                Content = Convert.ToString(value, CultureInfo.InvariantCulture),
                Tag = value,
                Margin = new Thickness(0, 2, 0, 2),
                FontSize = 12,
                Style = _checkStyle
            };
            check.Checked += (_, __) => Apply();
            check.Unchecked += (_, __) => Apply();
            _enumChecks.Add(check);
            list.Children.Add(check);
        }

        scroll.Content = list;
        _contentRoot.Children.Add(scroll);

        var tools = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0), HorizontalAlignment = HorizontalAlignment.Right };
        var all = new Button { Content = "全选", Width = 68, Height = 30, Margin = new Thickness(0, 0, 8, 0), Style = _buttonStyle };
        all.Click += (_, __) =>
        {
            foreach (var check in _enumChecks) check.IsChecked = true;
            Apply();
        };
        var none = new Button { Content = "全不选", Width = 68, Height = 30, Style = _buttonStyle };
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
        var predicate = BuildPredicate();
        var predicates = predicate != null
            ? new IFilterPredicate[] { predicate }
            : Array.Empty<IFilterPredicate>();
        _owner.ApplyColumnFilter(_columnName, predicates);
    }

    private void Clear()
    {
        _owner.ClearColumnFilter(_columnName);
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
