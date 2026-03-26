using System.Diagnostics;
using Finnonly.Avalonia;

namespace Finnonly.Sample;

public class StaticService
{
    public static StaticService Instance { get; } = new();

    [EventBind]
    public void OnStaticAction()
    {
        Debug.WriteLine("[StaticService] 静态对象的方法被调用了！");
    }

    [EventBind]
    public void OnStaticActionWithParam(string value)
    {
        Debug.WriteLine($"[StaticService] 静态方法带参数: {value}");
    }
}
