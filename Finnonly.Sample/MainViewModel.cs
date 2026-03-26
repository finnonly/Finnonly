using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Finnonly.Avalonia;

namespace Finnonly.Sample;

public sealed class MainViewModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    private string _message = "点击按钮测试事件绑定";
    public string Message
    {
        get => _message;
        set { _message = value; OnPropertyChanged(); }
    }

    public ObservableCollection<string> Items { get; } =
        new(Enumerable.Range(1, 200).Select(i => $"Item {i}"));

    public string BoundParam => "来自绑定的参数";

    private int _clickCount;

    [EventBind]
    public void OnButtonClick()
    {
        _clickCount++;
        Message = $"按钮被点击了 {_clickCount} 次";
    }

    [EventBind]
    public void OnButtonClickWithArgs(object? sender, object? args)
    {
        _clickCount++;
        Message = $"带参数点击: sender={sender?.GetType().Name}, count={_clickCount}";
    }

    [EventBind]
    public void OnButtonClickWithBinding(string value)
    {
        Message = $"绑定参数传递: {value}";
    }

    [EventBind]
    public void OnWindowClosing()
    {
        Debug.WriteLine("[Sample] Window closing via RawEvent");
    }

    [EventBind]
    public void OnItemSelected(string item)
    {
        Message = $"选中了: {item}";
    }

    public void TestCopyToCompare()
    {
        var a = new SampleModel { Name = "Test", Value = 42 };
        var b = new SampleModel();
        a.CopyTo(b);
        Debug.WriteLine($"[CopyTo] b.Name={b.Name}, b.Value={b.Value}");
        Debug.WriteLine($"[Compare] a==b: {a.Compare(b)}");
        b.Value = 99;
        Debug.WriteLine($"[Compare] a!=b: {!a.Compare(b)}");
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
