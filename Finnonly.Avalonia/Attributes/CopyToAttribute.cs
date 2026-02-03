// -
// -
// finnonly@outlook.com
// 2026-01-26
// -
// -

namespace Finnonly;

/// <summary>
/// Marks a class for CopyTo method generation.
/// The source generator will create a CopyTo extension method for marked classes.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
public class CopyToAttribute : Attribute
{
}

/// <summary>
/// Marks a class for Compare method generation.
/// The source generator will create comparison methods for marked classes.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
public class CompareAttribute : Attribute
{
}
