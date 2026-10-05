using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Threading.Tasks;
using WpfDataGrid.Enhancements.Infrastructure;

namespace WpfDataGrid.Enhancements.Pro.Performance;

/// <summary>
/// 大数据虚拟化集合（Pro）：按需分页加载 + 滚动窗口化 + 内存缓存，面向 10 万+ 行场景。
/// 集合对外表现为固定 <see cref="TotalCount"/> 的只读 IList&lt;T&gt;：
/// - 索引器访问到未加载行时，立即触发所在页的后台异步加载（滚动加载）；
/// - 页加载完成后通过 <see cref="INotifyCollectionChanged"/> 的 Reset 通知刷新视图；
/// - 已加载行缓存在内部数组，重复访问零开销。
/// 注意：本集合为"虚拟视图"只读集合，写操作（Add/Remove/Clear/索引赋值）均不支持。
/// </summary>
public class AsyncVirtualizingCollection<T> : IList<T>, IList, INotifyCollectionChanged, INotifyPropertyChanged
{
    private readonly object _lock = new object();
    private readonly PageLoader _pageLoader;
    private readonly int _pageSize;
    private readonly Action<Action> _marshal;
    private readonly HashSet<int> _loadingPages = new HashSet<int>();
    private readonly bool _hasComparableValueType;

    private T[] _items;
    private bool[] _loaded;
    private int _totalCount;
    private bool _isLoadingAll;

    /// <summary>异步加载一页数据。</summary>
    public delegate IEnumerable<T> PageLoader(int offset, int count);

    /// <summary>创建虚拟化集合。</summary>
    /// <param name="pageLoader">分页数据源：参数为 (offset, count)，返回该区间数据。</param>
    /// <param name="pageSize">分页大小（默认 100）。</param>
    /// <param name="totalCount">总行数（虚拟长度）。</param>
    /// <param name="marshal">跨线程回调调度器；为空时回调在加载线程直接执行（仅适合测试）。</param>
    public AsyncVirtualizingCollection(
        PageLoader pageLoader,
        int pageSize = 100,
        int totalCount = 0,
        Action<Action>? marshal = null)
    {
        _pageLoader = pageLoader ?? throw new ArgumentNullException(nameof(pageLoader));
        _pageSize = Math.Max(1, pageSize);
        _totalCount = Math.Max(0, totalCount);
        _items = new T[_totalCount];
        _loaded = new bool[_totalCount];
        _marshal = marshal ?? (action => action());
        _hasComparableValueType = typeof(IComparable).IsAssignableFrom(typeof(T)) && !typeof(T).IsClass;
    }

    /// <summary>总行数（虚拟长度）。</summary>
    public int TotalCount => _totalCount;

    /// <summary>分页大小。</summary>
    public int PageSize => _pageSize;

    /// <summary>当前已缓存（已加载）行数。</summary>
    public int LoadedCount
    {
        get
        {
            lock (_lock)
            {
                var count = 0;
                for (var i = 0; i < _loaded.Length; i++)
                {
                    if (_loaded[i]) count++;
                }

                return count;
            }
        }
    }

    /// <summary>动态调整虚拟总行数（保留已加载缓存）。</summary>
    public void SetTotalCount(int totalCount)
    {
        if (totalCount < 0) totalCount = 0;
        lock (_lock)
        {
            if (totalCount == _totalCount) return;
            var newItems = new T[totalCount];
            var newLoaded = new bool[totalCount];
            var copy = Math.Min(_totalCount, totalCount);
            Array.Copy(_items, 0, newItems, 0, copy);
            Array.Copy(_loaded, 0, newLoaded, 0, copy);
            _items = newItems;
            _loaded = newLoaded;
            _totalCount = totalCount;
        }

        RaisePropertyChanged(nameof(TotalCount));
        RaiseReset();
    }

    /// <summary>异步加载第 pageIndex 页（页码从 0 开始）；已在加载中则忽略。</summary>
    public void LoadPage(int pageIndex)
    {
        if (pageIndex < 0) return;
        lock (_lock)
        {
            if (_totalCount == 0) return;
            var pageCount = (_totalCount + _pageSize - 1) / _pageSize;
            if (pageIndex >= pageCount) return;
            if (_loadingPages.Contains(pageIndex)) return;
            _loadingPages.Add(pageIndex);
        }

        LoadPageCore(pageIndex);
    }

    private void LoadPageCore(int pageIndex)
    {
        var offset = pageIndex * _pageSize;
        int count;
        lock (_lock)
        {
            count = Math.Min(_pageSize, _totalCount - offset);
        }

        Task.Run(() =>
        {
            List<T>? snapshot = null;
            try
            {
                var loaded = _pageLoader(offset, count);
                if (loaded != null)
                {
                    snapshot = new List<T>(loaded);
                }
            }
            catch (Exception)
            {
                snapshot = null;
            }

            var result = snapshot;
            _marshal(() =>
            {
                if (result == null)
                {
                    lock (_lock) _loadingPages.Remove(pageIndex);
                    return;
                }

                lock (_lock)
                {
                    for (var i = 0; i < result.Count && offset + i < _totalCount; i++)
                    {
                        _items[offset + i] = result[i];
                        _loaded[offset + i] = true;
                    }

                    _loadingPages.Remove(pageIndex);
                }

                RaisePropertyChanged(nameof(LoadedCount));
                RaiseReset();
            });
        });
    }

    /// <summary>同步加载全部数据（排序等全量操作使用；大数据场景慎用）。</summary>
    public void LoadAll()
    {
        lock (_lock)
        {
            if (_isLoadingAll) return;
            _isLoadingAll = true;
        }

        try
        {
            var result = _pageLoader(0, _totalCount);
            if (result != null)
            {
                var snapshot = new List<T>(result);
                lock (_lock)
                {
                    for (var i = 0; i < snapshot.Count && i < _totalCount; i++)
                    {
                        _items[i] = snapshot[i];
                        _loaded[i] = true;
                    }
                }

                RaisePropertyChanged(nameof(LoadedCount));
                RaiseReset();
            }
        }
        finally
        {
            lock (_lock) _isLoadingAll = false;
        }
    }

    /// <summary>异步增量排序（Pro）：排序前会同步加载全部数据；大数据场景建议服务端排序后重建集合。</summary>
    public void ApplySort(string propertyName, bool ascending)
    {
        if (string.IsNullOrWhiteSpace(propertyName)) return;

        lock (_lock)
        {
            if (_totalCount > 0 && !_isLoadingAll)
            {
                LoadAll();
            }
        }

        lock (_lock)
        {
            if (_totalCount == 0) return;

            var indexes = new int[_totalCount];
            for (var i = 0; i < indexes.Length; i++) indexes[i] = i;

            Array.Sort(indexes, (x, y) =>
            {
                var a = _items[x];
                var b = _items[y];
                var result = CompareValues(a, b, propertyName);
                return ascending ? result : -result;
            });

            var sortedItems = new T[_totalCount];
            var sortedLoaded = new bool[_totalCount];
            for (var i = 0; i < indexes.Length; i++)
            {
                sortedItems[i] = _items[indexes[i]];
                sortedLoaded[i] = _loaded[indexes[i]];
            }

            _items = sortedItems;
            _loaded = sortedLoaded;
        }

        RaiseReset();
    }

    private int CompareValues(T a, T b, string propertyName)
    {
        var va = PropertyAccessor.GetValue(a!, propertyName);
        var vb = PropertyAccessor.GetValue(b!, propertyName);
        if (va is IComparable ca && vb is IComparable cb)
        {
            return Comparer<object?>.Default.Compare(va, vb);
        }

        return string.CompareOrdinal(PropertyAccessor.ToDisplayString(va), PropertyAccessor.ToDisplayString(vb));
    }

    #region IList<T> / IList 实现

    /// <inheritdoc />
    public int Count => _totalCount;

    /// <inheritdoc />
    public bool IsReadOnly => true;

    /// <inheritdoc />
    public T this[int index]
    {
        get
        {
            if (index < 0 || index >= _totalCount) throw new ArgumentOutOfRangeException(nameof(index));
            if (_loaded[index]) return _items[index];

            LoadPage(index / _pageSize);
            return default!;
        }
        set => throw new NotSupportedException("AsyncVirtualizingCollection 为只读虚拟集合，不支持索引赋值。");
    }

    object? IList.this[int index]
    {
        get => this[index];
        set => throw new NotSupportedException("AsyncVirtualizingCollection 为只读虚拟集合，不支持索引赋值。");
    }

    /// <inheritdoc />
    public bool IsFixedSize => false;

    object ICollection.SyncRoot => this;

    bool ICollection.IsSynchronized => false;

    /// <inheritdoc />
    public int IndexOf(T item)
    {
        lock (_lock)
        {
            for (var i = 0; i < _loaded.Length; i++)
            {
                if (_loaded[i] && EqualityComparer<T>.Default.Equals(_items[i], item)) return i;
            }
        }

        return -1;
    }

    /// <inheritdoc />
    public bool Contains(T item) => IndexOf(item) >= 0;

    /// <inheritdoc />
    public void CopyTo(T[] array, int arrayIndex)
    {
        if (array is null) throw new ArgumentNullException(nameof(array));
        lock (_lock)
        {
            for (var i = 0; i < _totalCount; i++)
            {
                array[arrayIndex + i] = _loaded[i] ? _items[i] : default!;
            }
        }
    }

    /// <inheritdoc />
    public IEnumerator<T> GetEnumerator()
    {
        for (var i = 0; i < _totalCount; i++)
        {
            yield return this[i];
        }
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    int IList.Add(object? value) => throw new NotSupportedException("AsyncVirtualizingCollection 为只读虚拟集合，不支持 Add。");

    void ICollection<T>.Add(T item) => throw new NotSupportedException("AsyncVirtualizingCollection 为只读虚拟集合，不支持 Add。");

    bool ICollection<T>.Remove(T item) => throw new NotSupportedException("AsyncVirtualizingCollection 为只读虚拟集合，不支持 Remove。");

    void IList.Remove(object? value) => throw new NotSupportedException("AsyncVirtualizingCollection 为只读虚拟集合，不支持 Remove。");

    void IList.RemoveAt(int index) => throw new NotSupportedException("AsyncVirtualizingCollection 为只读虚拟集合，不支持 RemoveAt。");

    void IList<T>.RemoveAt(int index) => throw new NotSupportedException("AsyncVirtualizingCollection 为只读虚拟集合，不支持 RemoveAt。");

    void IList.Insert(int index, object? value) => throw new NotSupportedException("AsyncVirtualizingCollection 为只读虚拟集合，不支持 Insert。");

    void IList<T>.Insert(int index, T item) => throw new NotSupportedException("AsyncVirtualizingCollection 为只读虚拟集合，不支持 Insert。");

    void IList.Clear() => throw new NotSupportedException("AsyncVirtualizingCollection 为只读虚拟集合，不支持 Clear。");

    void ICollection<T>.Clear() => throw new NotSupportedException("AsyncVirtualizingCollection 为只读虚拟集合，不支持 Clear。");

    int IList.IndexOf(object? value)
    {
        if (value is T t) return IndexOf(t);
        return -1;
    }

    bool IList.Contains(object? value)
    {
        if (value is T t) return Contains(t);
        return false;
    }

    void ICollection.CopyTo(Array array, int index)
    {
        if (array is null) throw new ArgumentNullException(nameof(array));
        lock (_lock)
        {
            for (var i = 0; i < _totalCount; i++)
            {
                array.SetValue(_loaded[i] ? _items[i] : default!, index + i);
            }
        }
    }

    #endregion

    #region 通知

    /// <inheritdoc />
    public event NotifyCollectionChangedEventHandler? CollectionChanged;

    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    private void RaiseReset()
    {
        CollectionChanged?.Invoke(this, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }

    private void RaisePropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    #endregion
}
