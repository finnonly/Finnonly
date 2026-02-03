// -
// -
// finnonly@outlook.com
// 2026-01-26
// -
// -

namespace Finnonly;

/// <summary>
/// Marks a method for automatic event binding in Avalonia XAML.
/// Methods with this attribute can be referenced from XAML using {v:RoutedEvent MethodName} syntax.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public class EventBindAttribute : Attribute
{
}
