// -
// -
// finnonly@outlook.com
// 2026-01-26
// -
// -

using System.Diagnostics;
using Avalonia;
using Avalonia.Data;
using Avalonia.Data.Core;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace Finnonly.Avalonia;

public abstract class EventBindBase(string name)
    : MarkupExtension
{
    private readonly List<object> _args = [];
    private StyledElement? element;

    protected EventBindBase(string name, object arg0) : this(name)
        => AddRange([arg0]);

    protected EventBindBase(string name, object arg0, object arg1) : this(name)
        => AddRange([arg0, arg1]);

    protected EventBindBase(string name, object arg0, object arg1, object arg2) : this(name)
        => AddRange([arg0, arg1, arg2]);

    protected EventBindBase(string name, object arg0, object arg1, object arg2, object arg3) : this(name)
        => AddRange([arg0, arg1, arg2, arg3]);

    protected EventBindBase(string name, object arg0, object arg1, object arg2, object arg3, object arg4) : this(name)
        => AddRange([arg0, arg1, arg2, arg3, arg4]);

    protected EventBindBase(string name, object arg0, object arg1, object arg2, object arg3, object arg4, object arg5) : this(name)
        => AddRange([arg0, arg1, arg2, arg3, arg4, arg5]);

    protected EventBindBase(string name, object arg0, object arg1, object arg2, object arg3, object arg4, object arg5, object arg6) : this(name)
        => AddRange([arg0, arg1, arg2, arg3, arg4, arg5, arg6]);

    protected void AddRange(object[] args)
    {
        var capacity = args.Length;
        _args.Capacity = capacity;

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            _args.Add(arg is IBinding binding ? new BindingHelper(binding) : arg);
        }
    }

    /// <summary>
    /// AOT 兼容的通用 Invoke 方法
    /// 支持所有事件类型，包括不继承 EventArgs 的特殊类型
    /// </summary>
    protected void Invoke(object? sender, object? e)
    {
        object? target;
        object?[] parameters;

        if (_args.Count > 1 && _args[0] is "!static")
        {
            target = _args[1];
            var dataContext = element?.DataContext;
            if (dataContext is null) return;
            parameters = PrepareParameters(_args.GetRange(2, _args.Count - 2), dataContext, sender, e);
        }
        else
        {
            target = element?.DataContext;
            if (target is null) return;
            parameters = PrepareParameters(_args, target, sender, e);
        }

        try
        {
            if (EventBindProvider.InvokeMethod is { } invoke)
                invoke(target, name, parameters);
            else
                Debug.WriteLine($"[EventBind] EventBindProvider.InvokeMethod is not initialized. Did you forget to reference Finnonly.SourceGenerator?");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[EventBind] Method {target}->{name} not found or not marked for dynamic invocation: {ex}");
        }
    }

    protected void Invoke2(object? sender, EventArgs e)
    {
        object? target;
        object?[] parameters;

        if (_args.Count > 1 && _args[0] is "!static")
        {
            target = _args[1];
            var dataContext = element?.DataContext;
            if (dataContext is null) return;
            parameters = PrepareParameters(_args.GetRange(2, _args.Count - 2), dataContext, sender, e);
        }
        else
        {
            target = element?.DataContext;
            if (target is null) return;
            parameters = PrepareParameters(_args, target, sender, e);
        }

        try
        {
            if (EventBindProvider.InvokeMethod is { } invoke)
                invoke(target, name, parameters);
            else
                Debug.WriteLine($"[EventBind] EventBindProvider.InvokeMethod is not initialized. Did you forget to reference Finnonly.SourceGenerator?");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[EventBind] Method {target}->{name} not found or not marked for dynamic invocation: {ex}");
        }
    }

    private static object?[] PrepareParameters(List<object> args, object? dataContext, object? sender, object? e)
    {
        var parameters = new object?[args.Count];

        for (var i = 0; i < args.Count; i++)
        {
            parameters[i] = args[i] switch
            {
                BindingHelper h => h.Evaluate(dataContext),
                "!sender" => sender,
                "!args" => e,
                _ => args[i]
            };
        }

        return parameters;
    }

    protected bool EventHandlerInfo(IServiceProvider serviceProvider)
    {
        if (serviceProvider.GetService(typeof(IProvideValueTarget)) is not IProvideValueTarget { TargetObject: StyledElement element })
            return false;

        this.element = element;
        return true;
    }
}

/// <summary>
/// 路由事件绑定 (EventHandler&lt;RoutedEventArgs&gt;)
/// </summary>
public sealed class RoutedEvent : EventBindBase
{
    public RoutedEvent(string name) : base(name)
    {
    }

    public RoutedEvent(string name, object arg0) : base(name, arg0)
    {
    }

    public RoutedEvent(string name, object arg0, object arg1) : base(name, arg0, arg1)
    {
    }

    public RoutedEvent(string name, object arg0, object arg1, object arg2) : base(name, arg0, arg1, arg2)
    {
    }

    public RoutedEvent(string name, object arg0, object arg1, object arg2, object arg3) : base(name, arg0, arg1, arg2, arg3)
    {
    }

    public RoutedEvent(string name, object arg0, object arg1, object arg2, object arg3, object arg4) : base(name, arg0, arg1, arg2, arg3, arg4)
    {
    }

    public RoutedEvent(string name, object arg0, object arg1, object arg2, object arg3, object arg4, object arg5) : base(name, arg0, arg1, arg2, arg3, arg4, arg5)
    {
    }

    public RoutedEvent(string name, object arg0, object arg1, object arg2, object arg3, object arg4, object arg5, object arg6) : base(name, arg0, arg1, arg2, arg3, arg4, arg5, arg6)
    {
    }

    public override EventHandler<RoutedEventArgs> ProvideValue(IServiceProvider serviceProvider)
    {
        static void EmptyHandler(object? sender, RoutedEventArgs e)
        {
        } 
        return EventHandlerInfo(serviceProvider)
            ? (sender, e) => Invoke(sender, e)
            : EmptyHandler;
    }
}

/// <summary>
/// 原始事件绑定 - 用于非 RoutedEventArgs 的事件 (如 WindowClosingEventArgs)
/// AOT 兼容版本
/// </summary>
public sealed class RawEvent : EventBindBase
{
    public RawEvent(string name) : base(name)
    {
    }

    public RawEvent(string name, object arg0) : base(name, arg0)
    {
    }

    public RawEvent(string name, object arg0, object arg1) : base(name, arg0, arg1)
    {
    }

    public RawEvent(string name, object arg0, object arg1, object arg2) : base(name, arg0, arg1, arg2)
    {
    }

    public RawEvent(string name, object arg0, object arg1, object arg2, object arg3) : base(name, arg0, arg1, arg2, arg3)
    {
    }

    public RawEvent(string name, object arg0, object arg1, object arg2, object arg3, object arg4) : base(name, arg0, arg1, arg2, arg3, arg4)
    {
    }

    public RawEvent(string name, object arg0, object arg1, object arg2, object arg3, object arg4, object arg5) : base(name, arg0, arg1, arg2, arg3, arg4, arg5)
    {
    }

    public RawEvent(string name, object arg0, object arg1, object arg2, object arg3, object arg4, object arg5, object arg6) : base(name, arg0, arg1, arg2, arg3, arg4, arg5, arg6)
    {
    }

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        // 初始化失败时返回空委托
        if (!EventHandlerInfo(serviceProvider))
            return (EventHandler<EventArgs>)((s, e) => { });

        var target = serviceProvider.GetService(typeof(IProvideValueTarget)) as IProvideValueTarget;

        if (target?.TargetProperty is string eventName && target.TargetObject is StyledElement element)
        {
            if (EventBindProvider.GetEventType is not { } getEventType ||
                EventBindProvider.CreateDelegate is not { } createDelegate)
            {
                Debug.WriteLine($"[EventBind] EventBindProvider is not initialized. Did you forget to reference Finnonly.SourceGenerator?");
                return (EventHandler<EventArgs>)((s, e) => { });
            }

            try
            {
                var handlerType = getEventType(element.GetType(), eventName);

                // ✅ AOT 兼容：使用源生成器生成的工厂方法
                // CreateDelegate 接受 Action<object?, object?>
                // 可以处理所有类型，包括不继承 EventArgs 的特殊类型
                return createDelegate(handlerType, Invoke);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[EventBind] Failed to create delegate for {eventName}: {ex.Message}");
            }
        }

        return (EventHandler<EventArgs>)((s, e) => { });
    }
}

public sealed class Event : EventBindBase
{
    public Event(string name) : base(name) { }
    public Event(string name, object arg0) : base(name, arg0) { }
    public Event(string name, object arg0, object arg1) : base(name, arg0, arg1) { }
    public Event(string name, object arg0, object arg1, object arg2) : base(name, arg0, arg1, arg2) { }
    public Event(string name, object arg0, object arg1, object arg2, object arg3) : base(name, arg0, arg1, arg2, arg3) { }
    public Event(string name, object arg0, object arg1, object arg2, object arg3, object arg4) : base(name, arg0, arg1, arg2, arg3, arg4) { }
    public Event(string name, object arg0, object arg1, object arg2, object arg3, object arg4, object arg5) : base(name, arg0, arg1, arg2, arg3, arg4, arg5) { }
    public Event(string name, object arg0, object arg1, object arg2, object arg3, object arg4, object arg5, object arg6) : base(name, arg0, arg1, arg2, arg3, arg4, arg5, arg6) { }
    public Event(string methodName, object[] args) : base(methodName, args) { }

    public override EventHandler ProvideValue(IServiceProvider serviceProvider)
    {
        static void EmptyHandler(object? sender, EventArgs e)
        { }

        return EventHandlerInfo(serviceProvider) ? Invoke : EmptyHandler;
    }
}



internal sealed class BindingHelper : StyledElement
{
    private static readonly StyledProperty<object> ValueProperty =
        AvaloniaProperty.Register<BindingHelper, object>("Value");

    private object? lastDataContext;

    public BindingHelper(IBinding binding)
    {
        UpdateBinding(binding);
    }

    public object Evaluate(object? dataContext)
    {
        if (!ReferenceEquals(dataContext, lastDataContext))
        {
            DataContext = dataContext;
            lastDataContext = dataContext;
        }

        return GetValue(ValueProperty);
    }

    private void UpdateBinding(IBinding binding)
    {
#pragma warning disable CS0618 // Type or member is obsolete
        var ib = binding.Initiate(this, ValueProperty)
                 ?? throw new InvalidOperationException("Unable to create binding");

        BindingOperations.Apply(this, ValueProperty, ib);
#pragma warning restore CS0618
    }
}
