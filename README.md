# Finnonly.Avalonia

Avalonia extensions for event binding using Source Generators + High Performance VirtualizingWrapPanel.

## Features

- **v:Event** - 简洁的事件绑定标记扩展
- **v:RoutedEvent** - 绑定路由事件到 ViewModel
- **v:RawEvent** - 处理非 EventArgs 事件（如 WindowClosing）
- **Attributes** - EventBind, CopyTo, Compare, Table, Column, PrimaryKey, etc.
- **VirtualizingWrapPanel** - 高性能虚拟化 WrapPanel（.NET 10 / C# 12+ 优化）

## Installation

```xml
<PackageReference Include="Finnonly.Avalonia" Version="1.0.6" />
<PackageReference Include="Finnonly.SourceGenerator" Version="1.0.6" OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
```

---

## v:Event 事件绑定

`v:Event` 是最简洁的事件绑定方式，自动推断事件类型。

### ✨ 特性

- 🎯 **智能推断** - 自动识别路由事件和普通事件
- 📝 **简洁语法** - 一个标记扩展处理所有事件
- 🔗 **直接绑定** - 无需 Command，直接绑定 ViewModel 方法
- ⚡ **源生成器** - 编译时生成代码，零运行时反射开销

### 📦 基本用法

```xml
<Window xmlns:v="clr-namespace:Finnonly.Avalonia;assembly=Finnonly.Avalonia"
        Loaded="{v:RoutedEvent OnLoaded}"
        Closing="{v:RawEvent OnClosing}"
        Closed="{v:Event OnClosed}">
    
    <StackPanel>
        <Button Content="点击" Click="{v:RoutedEvent OnButtonClick}"/>
        <Button Content="点击" Click="{v:RoutedEvent OnButtonClick,'!sender'}"/>
        <Button Content="点击" Click="{v:RoutedEvent OnButtonClick,'!args'}"/>
        <Button Content="点击" Click="{v:RoutedEvent OnButtonClick,'!sender','!args'}"/>
        <TextBox TextChanged="{v:RoutedEvent OnTextChanged}"/>
        <ListBox SelectionChanged="{v:RoutedEvent OnSelectionChanged}"/>
    </StackPanel>
</Window>
```

```csharp
// ViewModel
public partial class MainViewModel : ViewModelBase
{
    [EventBind]
    public void OnLoaded()
    {
        // 窗口加载完成
    }

    [EventBind]
    public void OnClosing()
    {
        // 窗口关闭前
    }

    [EventBind]
    public void OnClosed()
    {
        // 窗口关闭
    }

    [EventBind]
    public void OnButtonClick()
    {
        // 按钮点击
    }

    [EventBind]
    public void OnTextChanged(object sender, TextChangedEventArgs e)
    {
        // 文本变化，可选参数
    }

    [EventBind]
    public void OnSelectionChanged()
    {
        // 选择变化
    }
}
```

### 🎛️ 带参数的事件方法

```csharp
// 无参数 - 最简洁
[EventBind]
public void OnClick() { }

// 只有 sender
[EventBind]
public void OnClick(object sender) { }

// 完整参数
[EventBind]
public void OnClick(object sender, RoutedEventArgs e) { }

// 异步方法
[EventBind]
public async Task OnClickAsync()
{
    await DoSomethingAsync();
}
```

---

## v:RoutedEvent / v:RawEvent

针对特定场景的专用标记扩展。

### v:RoutedEvent - 路由事件

用于明确绑定 Avalonia 路由事件。

```xml
<Window Loaded="{v:RoutedEvent OnLoaded}"
        Unloaded="{v:RoutedEvent OnUnloaded}">
    <Button Click="{v:RoutedEvent OnButtonClick}"/>
</Window>
```

### v:RawEvent - 原始事件

用于处理特殊的非标准事件（如 WindowClosing）。

```xml
<Window Closing="{v:RawEvent OnWindowClosing}">
```

```csharp
[EventBind]
public void OnWindowClosing()
{
    // 可以取消关闭
}
```

---

## VirtualizingWrapPanel

高性能虚拟化 WrapPanel，专为 .NET 10 / C# 12+ 优化，支持大量数据的流畅滚动。

### ✨ 特性

- 🚀 **高性能虚拟化** - 仅渲染可见元素，支持万级数据流畅滚动
- 📐 **固定/自适应尺寸** - 支持固定项目大小或自动测量
- 📏 **自定义间距** - 支持水平和垂直间距配置
- 🔄 **无闪烁刷新** - 集合变化时平滑过渡
- ⚡ **防抖优化** - 尺寸变化时使用防抖减少重绘
- 📜 **加载更多** - 内置滚动到底部加载更多数据支持

### 📦 基本用法

```xml
<Window xmlns:controls="using:YourNamespace.Controls">
    <ScrollViewer HorizontalScrollBarVisibility="Disabled" 
                  VerticalScrollBarVisibility="Auto">
        <ItemsControl ItemsSource="{Binding Items}">
            <ItemsControl.ItemsPanel>
                <ItemsPanelTemplate>
                    <controls:VirtualizingWrapPanel 
                        EstimatedItemWidth="200"
                        EstimatedItemHeight="300"
                        ItemHorizontalSpacing="8"
                        ItemVerticalSpacing="8"
                        UseFixedItemSize="True"
                        FillAvailableSpace="True"/>
                </ItemsPanelTemplate>
            </ItemsControl.ItemsPanel>
            <ItemsControl.ItemTemplate>
                <DataTemplate>
                    <Border Background="LightBlue" CornerRadius="8">
                        <TextBlock Text="{Binding Title}" 
                                   HorizontalAlignment="Center" 
                                   VerticalAlignment="Center"/>
                    </Border>
                </DataTemplate>
            </ItemsControl.ItemTemplate>
        </ItemsControl>
    </ScrollViewer>
</Window>
```

### ⚙️ 属性说明

| 属性 | 类型 | 默认值 | 说明 |
|------|------|--------|------|
| `EstimatedItemWidth` | double | 200 | 预设项目宽度（用于虚拟化计算） |
| `EstimatedItemHeight` | double | 200 | 预设项目高度 |
| `ItemHorizontalSpacing` | double | 0 | 项目水平间距 |
| `ItemVerticalSpacing` | double | 0 | 项目垂直间距 |
| `UseFixedItemSize` | bool | true | 是否使用固定尺寸（忽略实际测量） |
| `FillAvailableSpace` | bool | true | 是否填充满整个可用空间 |

### 📡 事件

| 事件 | 说明 |
|------|------|
| `LoadMoreRequested` | 滚动到底部时触发，用于加载更多数据 |

### 🎛️ 加载更多示例

```csharp
// XAML
<controls:VirtualizingWrapPanel 
    x:Name="VPanel"
    HasMoreItems="True"/>

// Code-behind or ViewModel
VPanel.LoadMoreRequested += async (s, e) =>
{
    await LoadMoreDataAsync();
};
```

### 📋 动态刷新集合

```csharp
// ✅ 推荐：使用 ObservableCollection
public ObservableCollection<ItemModel> Items { get; } = new();

// 刷新数据（自动触发UI更新）
public void RefreshData()
{
    Items.Clear();
    foreach (var item in newData)
    {
        Items.Add(item);
    }
}
```

### 🔧 性能优化建议

1. **使用固定尺寸** - 设置 `UseFixedItemSize="True"` 避免测量开销
2. **合理设置缓冲区** - 控件默认缓冲 5 行元素，减少频繁创建/销毁
3. **简化 ItemTemplate** - 避免复杂嵌套和重型绑定
4. **分批加载** - 利用 `LoadMoreRequested` 实现分页加载

### ⚠️ 注意事项

- 控件必须放在 `ScrollViewer` 内部
- `ScrollViewer` 需要设置 `HorizontalScrollBarVisibility="Disabled"`
- 集合变化时会自动强制刷新，无需手动处理

---

## License

MIT
