// -
// -
// finnonly@outlook.com
// 2026-01-26
// -
// -

namespace Finnonly;

/// <summary>
/// Marks a class as a SQLite table entity.
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public class TableAttribute(string? name = null) : Attribute
{
    public string? Name { get; } = name;
}

/// <summary>
/// Marks a property as a SQLite column.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public class ColumnAttribute(string? name = null) : Attribute
{
    public string? Name { get; } = name;
}

/// <summary>
/// Marks a property as the primary key.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public class PrimaryKeyAttribute(bool autoIncrement = false) : Attribute
{
    public bool AutoIncrement { get; } = autoIncrement;
}

/// <summary>
/// Marks a property to be ignored by the source generator.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public class IgnoreAttribute : Attribute
{
}

/// <summary>
/// Marks a class for migration with a version number.
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public class MigrationAttribute(int version) : Attribute
{
    public int Version { get; } = version;
}
