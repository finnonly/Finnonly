using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Threading;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace Finnonly.Avalonia;

/// <summary>
/// 高性能虚拟化WrapPanel - 针对 .NET 10 / C# 12+ 优化
/// 🚀 丝滑版：尺寸变化时不清空元素，直接更新位置
/// </summary>
public sealed class VirtualizingWrapPanel : VirtualizingPanel, IScrollSnapPointsInfo
{
    public bool AreHorizontalSnapPointsRegular { get; set; }
    public bool AreVerticalSnapPointsRegular { get; set; }
    public bool HasMoreItems { get; set; } = true;
    public bool IsLoadingMore { get; private set; }
    public EventHandler? LoadMoreRequested;

    #region 依赖属性

    public static readonly StyledProperty<double> EstimatedItemWidthProperty =
        AvaloniaProperty.Register<VirtualizingWrapPanel, double>(nameof(EstimatedItemWidth), 200.0, coerce: CoercePositive);

    public static readonly StyledProperty<double> EstimatedItemHeightProperty =
        AvaloniaProperty.Register<VirtualizingWrapPanel, double>(nameof(EstimatedItemHeight), 200.0, coerce: CoercePositive);

    public static readonly StyledProperty<double> ItemHorizontalSpacingProperty =
        AvaloniaProperty.Register<VirtualizingWrapPanel, double>(nameof(ItemHorizontalSpacing), 0.0, coerce: CoerceNonNegative);

    public static readonly StyledProperty<double> ItemVerticalSpacingProperty =
        AvaloniaProperty.Register<VirtualizingWrapPanel, double>(nameof(ItemVerticalSpacing), 0.0, coerce: CoerceNonNegative);

    public static readonly StyledProperty<bool> UseFixedItemSizeProperty =
        AvaloniaProperty.Register<VirtualizingWrapPanel, bool>(nameof(UseFixedItemSize), true);

    public static readonly StyledProperty<bool> FillAvailableSpaceProperty =
        AvaloniaProperty.Register<VirtualizingWrapPanel, bool>(nameof(FillAvailableSpace), true);

    public double EstimatedItemWidth { get => GetValue(EstimatedItemWidthProperty); set => SetValue(EstimatedItemWidthProperty, value); }
    public double EstimatedItemHeight { get => GetValue(EstimatedItemHeightProperty); set => SetValue(EstimatedItemHeightProperty, value); }
    public double ItemHorizontalSpacing { get => GetValue(ItemHorizontalSpacingProperty); set => SetValue(ItemHorizontalSpacingProperty, value); }
    public double ItemVerticalSpacing { get => GetValue(ItemVerticalSpacingProperty); set => SetValue(ItemVerticalSpacingProperty, value); }
    public bool UseFixedItemSize { get => GetValue(UseFixedItemSizeProperty); set => SetValue(UseFixedItemSizeProperty, value); }
    public bool FillAvailableSpace { get => GetValue(FillAvailableSpaceProperty); set => SetValue(FillAvailableSpaceProperty, value); }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static double CoercePositive(AvaloniaObject _, double v) => Math.Max(1.0, v);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static double CoerceNonNegative(AvaloniaObject _, double v) => Math.Max(0.0, v);

    static VirtualizingWrapPanel()
    {
        // 🚀 尺寸属性变化 - 不清空，只重新布局
        EstimatedItemWidthProperty.Changed.AddClassHandler<VirtualizingWrapPanel>(OnSizePropertyChanged);
        EstimatedItemHeightProperty.Changed.AddClassHandler<VirtualizingWrapPanel>(OnSizePropertyChanged);
        ItemHorizontalSpacingProperty.Changed.AddClassHandler<VirtualizingWrapPanel>(OnLayoutPropertyChanged);
        ItemVerticalSpacingProperty.Changed.AddClassHandler<VirtualizingWrapPanel>(OnLayoutPropertyChanged);
        UseFixedItemSizeProperty.Changed.AddClassHandler<VirtualizingWrapPanel>(OnLayoutPropertyChanged);
        FillAvailableSpaceProperty.Changed.AddClassHandler<VirtualizingWrapPanel>(OnLayoutPropertyChanged);
    }

    /// <summary>
    /// 🚀 丝滑优化：尺寸变化时不清空元素，使用防抖延迟处理
    /// </summary>
    private static void OnSizePropertyChanged(VirtualizingWrapPanel panel, AvaloniaPropertyChangedEventArgs _)
    {
        panel._layoutCacheValid = false;
        panel._maximumItemWidth = panel.EstimatedItemWidth;
        panel._maximumItemHeight = panel.EstimatedItemHeight;

        // 🚀 关键：不清空元素！使用防抖延迟更新
        panel.ScheduleSizeChangeUpdate();
    }

    private static void OnLayoutPropertyChanged(VirtualizingWrapPanel panel, AvaloniaPropertyChangedEventArgs _)
    {
        panel._layoutCacheValid = false;
        panel.ScheduleUpdate();
    }

    #endregion

    #region 私有字段

    private struct ElementData
    {
        public Control? Control;
        public double Left, Top, Width, Height;
    }

    private readonly Dictionary<int, ElementData> _elements = new(256);
    private RowInfo[] _rowCache = [];
    private int _rowCount;

    private readonly record struct RowInfo(double Top, int StartIndex, int EndIndex);

    // 布局状态
    private Rect _effectiveViewport = new(0, -1, 0, 0);
    private Size _panelSize;
    private double _maximumItemWidth;
    private double _maximumItemHeight;
    private int _itemsPerRow = 1;
    private int _lastItemCount;
    private double _lastViewportWidth;
    private bool _layoutCacheValid;

    // 节流控制
    private DispatcherTimer? _updateTimer;
    private Rect _pendingViewport;
    private bool _hasPendingUpdate;
    private bool _isUpdating;
    private long _lastUpdateTicks;
    private const long UPDATE_THROTTLE_TICKS = 160_000; // ~16ms (60fps)

    // 🚀 尺寸变化防抖
    private DispatcherTimer? _sizeDebounceTimer;
    private const int SIZE_DEBOUNCE_MS = 50; // 50ms 防抖

    #endregion

    public VirtualizingWrapPanel()
    {
        EffectiveViewportChanged += OnEffectiveViewportChanged;
        _updateTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _updateTimer.Tick += OnUpdateTimerTick;

        // 🚀 尺寸变化防抖定时器
        _sizeDebounceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(SIZE_DEBOUNCE_MS) };
        _sizeDebounceTimer.Tick += OnSizeDebounceTimerTick;

        _maximumItemWidth = EstimatedItemWidth;
        _maximumItemHeight = EstimatedItemHeight;
    }

    #region 防抖处理

    /// <summary>
    /// 🚀 尺寸变化防抖调度
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ScheduleSizeChangeUpdate()
    {
        _sizeDebounceTimer?.Stop();
        _sizeDebounceTimer?.Start();
    }

    /// <summary>
    /// 🚀 防抖结束后执行丝滑更新
    /// </summary>
    private void OnSizeDebounceTimerTick(object? sender, EventArgs e)
    {
        _sizeDebounceTimer?.Stop();

        // 🚀 关键：不清空元素，直接更新所有已存在元素的布局
        UpdateAllElementsLayout();
    }

    /// <summary>
    /// 🚀 丝滑更新：直接更新所有元素的尺寸和位置，不销毁重建
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private void UpdateAllElementsLayout()
    {
        if (_isUpdating)
            return;
        _isUpdating = true;

        try
        {
            var viewportWidth = _effectiveViewport.Width > 0 ? _effectiveViewport.Width : Bounds.Width;
            if (viewportWidth <= 0)
                viewportWidth = 800; // fallback

            var itemsCount = Items.Count;
            RecalculateLayout(itemsCount, viewportWidth);

            var itemTotalWidth = GetItemTotalWidth();
            var itemTotalHeight = GetItemTotalHeight();
            var itemWidth = EstimatedItemWidth;
            var itemHeight = EstimatedItemHeight;

            // 🚀 直接更新所有已存在元素的位置和尺寸
            foreach (var index in _elements.Keys)
            {
                ref var data = ref CollectionsMarshal.GetValueRefOrNullRef(_elements, index);
                if (Unsafe.IsNullRef(ref data) || data.Control is null)
                    continue;

                // 更新尺寸
                data.Width = itemWidth;
                data.Height = itemHeight;

                // 重新计算位置
                var (row, col) = Math.DivRem(index, _itemsPerRow);
                data.Left = col * itemTotalWidth;
                data.Top = row * itemTotalHeight;
            }

            // 🔧 关键修复：变小后需要渲染新的可见项
            var viewportTop = _effectiveViewport.Top;
            var viewportHeight = _effectiveViewport.Height > 0 ? _effectiveViewport.Height : Bounds.Height;
            var bufferHeight = itemTotalHeight * 2;

            var startIndex = FindFirstVisibleIndex(viewportTop - bufferHeight, itemTotalHeight);
            var endIndex = FindLastVisibleIndex(viewportTop + viewportHeight + bufferHeight, itemsCount, itemTotalHeight);

            // 渲染新的可见元素
            RenderVisibleElements(startIndex, endIndex);

            // 触发重新排列
            InvalidateMeasure();
            InvalidateArrange();
        }
        finally
        {
            _isUpdating = false;
        }
    }

    #endregion

    #region 视口处理

    private void OnEffectiveViewportChanged(object? sender, EffectiveViewportChangedEventArgs e)
    {
        if (e.EffectiveViewport.Top < 0 || _isUpdating)
            return;

        var viewport = e.EffectiveViewport;
        var isWidthChanged = Math.Abs(viewport.Width - _effectiveViewport.Width) > 1.0;

        if (!isWidthChanged)
        {
            var now = Environment.TickCount64;
            if (now - _lastUpdateTicks < UPDATE_THROTTLE_TICKS)
            {
                _pendingViewport = viewport;
                _hasPendingUpdate = true;
                if (!_updateTimer!.IsEnabled)
                    _updateTimer.Start();
                return;
            }
        }

        ProcessViewportChange(viewport, isWidthChanged);
    }

    private void OnUpdateTimerTick(object? sender, EventArgs e)
    {
        _updateTimer!.Stop();
        if (_hasPendingUpdate && !_isUpdating)
        {
            ProcessViewportChange(_pendingViewport, false);
            _hasPendingUpdate = false;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ScheduleUpdate()
    {
        if (!_isUpdating)
        {
            Dispatcher.UIThread.InvokeAsync(() => ProcessViewportChange(_effectiveViewport, false), DispatcherPriority.Background);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private void ProcessViewportChange(Rect viewport, bool isWidthChanged)
    {
        if (_isUpdating)
            return;
        _isUpdating = true;
        _lastUpdateTicks = Environment.TickCount64;

        try
        {
            _effectiveViewport = viewport;

            var itemsCount = Items.Count;
            if (!_layoutCacheValid || itemsCount != _lastItemCount || isWidthChanged)
            {
                RecalculateLayout(itemsCount, viewport.Width);

                // 🚀 关键修复：宽度变化或强制刷新时，回收所有元素让它们重新创建
                if (isWidthChanged)
                {
                    RecycleAllElements();
                }
            }

            var itemTotalHeight = GetItemTotalHeight();
            var bufferHeight = itemTotalHeight * 2;
            var startIndex = FindFirstVisibleIndex(viewport.Top - bufferHeight, itemTotalHeight);
            var endIndex = FindLastVisibleIndex(viewport.Top + viewport.Height + bufferHeight, itemsCount, itemTotalHeight);

            // 渲染可见元素
            RenderVisibleElements(startIndex, endIndex);

            // 回收不可见元素（非强制刷新时）
            if (!isWidthChanged)
            {
                RecycleInvisibleElements(startIndex, endIndex);
            }

            InvalidateMeasure();
            InvalidateArrange();

            if (viewport.Top + viewport.Height >= _panelSize.Height - 300)
                LoadMoreAsync();
        }
        finally
        {
            _isUpdating = false;
        }
    }

    #endregion

    #region 布局计算

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private double GetEffectiveItemWidth() => UseFixedItemSize ? EstimatedItemWidth : Math.Max(_maximumItemWidth, EstimatedItemWidth);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private double GetEffectiveItemHeight() => UseFixedItemSize ? EstimatedItemHeight : Math.Max(_maximumItemHeight, EstimatedItemHeight);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private double GetItemTotalWidth() => GetEffectiveItemWidth() + ItemHorizontalSpacing;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private double GetItemTotalHeight() => GetEffectiveItemHeight() + ItemVerticalSpacing;

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private void RecalculateLayout(int itemsCount, double viewportWidth)
    {
        var itemTotalWidth = GetItemTotalWidth();
        var itemTotalHeight = GetItemTotalHeight();

        _itemsPerRow = Math.Max(1, (int)Math.Floor((viewportWidth + ItemHorizontalSpacing) / itemTotalWidth));
        var totalRows = (itemsCount + _itemsPerRow - 1) / _itemsPerRow;

        if (_rowCache.Length < totalRows)
        {
            _rowCache = new RowInfo[Math.Max(totalRows, _rowCache.Length * 2)];
        }
        _rowCount = totalRows;

        for (int row = 0; row < totalRows; row++)
        {
            var startIdx = row * _itemsPerRow;
            var endIdx = Math.Min(startIdx + _itemsPerRow - 1, itemsCount - 1);
            _rowCache[row] = new RowInfo(row * itemTotalHeight, startIdx, endIdx);
        }

        var panelHeight = Math.Max(0, totalRows * itemTotalHeight);
        _panelSize = new Size(FillAvailableSpace ? viewportWidth : CalculateContentWidth(itemsCount), panelHeight);
        _lastItemCount = itemsCount;
        _lastViewportWidth = viewportWidth;
        _layoutCacheValid = true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private double CalculateContentWidth(int itemsCount)
    {
        if (itemsCount == 0)
            return 0;
        var effectiveWidth = GetEffectiveItemWidth();
        var actualItemsInRow = Math.Min(_itemsPerRow, itemsCount);
        return actualItemsInRow * effectiveWidth + Math.Max(0, actualItemsInRow - 1) * ItemHorizontalSpacing;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int FindFirstVisibleIndex(double top, double itemTotalHeight)
    {
        if (_rowCount == 0)
            return 0;
        var estimatedRow = Math.Clamp((int)(top / itemTotalHeight), 0, _rowCount - 1);
        return _rowCache[estimatedRow].StartIndex;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int FindLastVisibleIndex(double bottom, int itemsCount, double itemTotalHeight)
    {
        if (_rowCount == 0)
            return itemsCount - 1;
        var estimatedRow = Math.Clamp((int)(bottom / itemTotalHeight), 0, _rowCount - 1);
        return Math.Min(_rowCache[estimatedRow].EndIndex, itemsCount - 1);
    }

    #endregion

    #region 元素渲染

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private void RenderVisibleElements(int startIndex, int endIndex)
    {
        var items = Items;
        var itemTotalWidth = GetItemTotalWidth();
        var itemTotalHeight = GetItemTotalHeight();
        var itemWidth = UseFixedItemSize ? EstimatedItemWidth : GetEffectiveItemWidth();
        var itemHeight = UseFixedItemSize ? EstimatedItemHeight : GetEffectiveItemHeight();

        for (int i = startIndex; i <= endIndex && i < items.Count; i++)
        {
            var item = items[i];
            if (item is null)
                continue;

            ref var data = ref CollectionsMarshal.GetValueRefOrAddDefault(_elements, i, out var exists);

            if (!exists || data.Control is null)
            {
                // 🚀 创建新元素
                data.Control = CreateVirtualizingElement(item, i);
                data.Width = itemWidth;
                data.Height = itemHeight;
            }
            else
            {
                // 🚀 更新已存在元素的尺寸（数据绑定已在 RecycleAllElements 后重建）
                data.Width = itemWidth;
                data.Height = itemHeight;
            }

            var (row, col) = Math.DivRem(i, _itemsPerRow);
            data.Left = col * itemTotalWidth;
            data.Top = row * itemTotalHeight;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private Control CreateVirtualizingElement(object item, int index)
    {
        var generator = ItemContainerGenerator!;
        var container = generator.CreateContainer(item, index, index.ToString());
        generator.PrepareItemContainer(container, item, index);
        AddInternalChild(container);
        generator.ItemContainerPrepared(container, item, index);
        container.Measure(UseFixedItemSize ? new Size(EstimatedItemWidth, EstimatedItemHeight) : Size.Infinity);
        return container;
    }

    #endregion

    #region 元素回收

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private void RecycleInvisibleElements(int visibleStart, int visibleEnd)
    {
        // 🚀 增大缓冲区，减少频繁创建/销毁
        var bufferSize = _itemsPerRow * 5; // 增加到5行缓冲
        var keepStart = Math.Max(0, visibleStart - bufferSize);
        var keepEnd = Math.Min(Items.Count - 1, visibleEnd + bufferSize);

        var keysBuffer = ArrayPool<int>.Shared.Rent(_elements.Count);
        var recycleCount = 0;

        foreach (var key in _elements.Keys)
        {
            if (key < keepStart || key > keepEnd)
            {
                keysBuffer[recycleCount++] = key;
                if (recycleCount >= 30)
                    break; // 限制每帧回收数量
            }
        }

        for (int i = 0; i < recycleCount; i++)
        {
            var key = keysBuffer[i];
            if (_elements.TryGetValue(key, out var data) && data.Control is { } ctrl)
            {
                RemoveInternalChild(ctrl);
                ItemContainerGenerator?.ClearItemContainer(ctrl);
            }
            _elements.Remove(key);
        }

        ArrayPool<int>.Shared.Return(keysBuffer);
    }

    /// <summary>
    /// 🚀 回收所有元素（用于集合变化或强制刷新时）
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private void RecycleAllElements()
    {
        var generator = ItemContainerGenerator;
        if (generator == null)
            return;

        foreach (var (_, data) in _elements)
        {
            if (data.Control is { } ctrl)
            {
                RemoveInternalChild(ctrl);
                generator.ClearItemContainer(ctrl);
            }
        }

        _elements.Clear();
    }

    #endregion

    #region 测量与排列

    protected override Size MeasureOverride(Size availableSize)
    {
        if (_panelSize.Width == 0 && Items.Count > 0 && availableSize.Width > 0 && !double.IsInfinity(availableSize.Width))
        {
            RecalculateLayout(Items.Count, availableSize.Width);
        }

        var measureSize = UseFixedItemSize ? new Size(EstimatedItemWidth, EstimatedItemHeight) : Size.Infinity;
        foreach (var (_, data) in _elements)
        {
            data.Control?.Measure(measureSize);
        }

        return FillAvailableSpace
            ? new Size(availableSize.Width > 0 && !double.IsInfinity(availableSize.Width) ? availableSize.Width : _panelSize.Width, _panelSize.Height)
            : new Size(CalculateContentWidth(Items.Count), _panelSize.Height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var itemWidth = UseFixedItemSize ? EstimatedItemWidth : GetEffectiveItemWidth();
        var itemHeight = UseFixedItemSize ? EstimatedItemHeight : GetEffectiveItemHeight();

        foreach (var (_, data) in _elements)
        {
            if (data.Control is { } ctrl)
            {
                // 🚀 使用最新的尺寸
                ctrl.Arrange(new Rect(data.Left, data.Top, itemWidth, itemHeight));
            }
        }
        return finalSize;
    }

    #endregion

    #region 其他接口实现

    private void LoadMoreAsync()
    {
        if (IsLoadingMore || !HasMoreItems)
            return;
        IsLoadingMore = true;
        Dispatcher.UIThread.InvokeAsync(() =>
        {
            try
            { LoadMoreRequested?.Invoke(this, EventArgs.Empty); }
            finally { IsLoadingMore = false; }
        }, DispatcherPriority.Background);
    }

    protected override void OnItemsChanged(IReadOnlyList<object?> items, NotifyCollectionChangedEventArgs e)
    {
        base.OnItemsChanged(items, e);
        _layoutCacheValid = false;

        // 🚀 关键修复：集合变化时，使用 ForceRefresh 策略
        Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (!_isUpdating)
            {
                ForceRefresh();
            }
        }, DispatcherPriority.Background);
    }

    /// <summary>
    /// 🚀 强制刷新（参考旧版的成功策略）
    /// </summary>
    private void ForceRefresh()
    {
        _updateTimer?.Stop();
        _sizeDebounceTimer?.Stop();
        _hasPendingUpdate = false;

        Dispatcher.UIThread.InvokeAsync(() =>
        {
            try
            {
                InvalidateMeasure();
                InvalidateArrange();
                UpdateLayout();

                if (IsEffectivelyVisible && Bounds.Width > 0 && Bounds.Height > 0)
                {
                    var itemsCount = Items.Count;

                    // 🔥 关键修复：空集合时也要清空所有元素
                    if (itemsCount == 0)
                    {
                        RecycleAllElements();
                        _panelSize = new Size(0, 0);
                        InvalidateMeasure();
                        InvalidateArrange();
                        UpdateLayout();
                        return;
                    }

                    RecalculateLayout(itemsCount, Bounds.Width);

                    var currentViewport = _effectiveViewport;
                    if (currentViewport.Width <= 0 || currentViewport.Height <= 0)
                    {
                        currentViewport = new Rect(0, 0, Bounds.Width, Bounds.Height);
                    }

                    // 🔥 关键：重置视口状态，触发 isWidthChanged=true
                    // 这会导致 RecycleAllElements 被调用，从而重建所有元素
                    _effectiveViewport = new Rect(0, -1, 0, 0);

                    ProcessViewportChange(currentViewport, true);  // 强制作为宽度变化处理
                    UpdateLayout();
                }
            }
            catch { }
        }, DispatcherPriority.Render);
    }

    public event EventHandler<RoutedEventArgs>? HorizontalSnapPointsChanged;
    public event EventHandler<RoutedEventArgs>? VerticalSnapPointsChanged;

    protected override Control? ScrollIntoView(int index) =>
        _elements.TryGetValue(index, out var data) ? data.Control : null;

    protected override Control? ContainerFromIndex(int index) =>
        _elements.TryGetValue(index, out var data) ? data.Control : null;

    protected override int IndexFromContainer(Control container)
    {
        foreach (var (key, data) in _elements)
            if (ReferenceEquals(data.Control, container))
                return key;
        return -1;
    }

    protected override IEnumerable<Control>? GetRealizedContainers()
    {
        var result = new List<Control>(_elements.Count);
        foreach (var (_, data) in _elements)
            if (data.Control is { } ctrl)
                result.Add(ctrl);
        return result;
    }

    protected override IInputElement? GetControl(NavigationDirection direction, IInputElement? from, bool wrap)
    {
        var count = Items.Count;
        if (count == 0)
            return null;

        var fromIndex = from is Control ctrl ? IndexFromContainer(ctrl) : -1;
        var toIndex = direction switch
        {
            NavigationDirection.First => 0,
            NavigationDirection.Last => count - 1,
            NavigationDirection.Next or NavigationDirection.Right => fromIndex + 1,
            NavigationDirection.Previous or NavigationDirection.Left => fromIndex - 1,
            NavigationDirection.Up => fromIndex - _itemsPerRow,
            NavigationDirection.Down => fromIndex + _itemsPerRow,
            _ => fromIndex
        };

        if (toIndex == fromIndex)
            return from;
        if (wrap)
            toIndex = (toIndex % count + count) % count;
        else if (toIndex < 0 || toIndex >= count)
            return null;

        return ScrollIntoView(toIndex);
    }

    public IReadOnlyList<double> GetIrregularSnapPoints(Orientation orientation, SnapPointsAlignment snapPointsAlignment) => [];
    public double GetRegularSnapPoints(Orientation orientation, SnapPointsAlignment snapPointsAlignment, out double offset) => throw new NotImplementedException();

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _updateTimer?.Stop();
        _sizeDebounceTimer?.Stop();
        _layoutCacheValid = false;
        _rowCount = 0;
    }

    #endregion
}



