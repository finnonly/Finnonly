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

/// <summary>
/// Configures SQLite PRAGMA settings at the assembly level.
/// Applied once per connection after opening.
/// </summary>
[AttributeUsage(AttributeTargets.Assembly)]
public class SqliteConfigAttribute : Attribute
{
    /// <summary>
    /// Journal mode: DELETE, TRUNCATE, PERSIST, MEMORY, WAL, OFF. Default: WAL.
    /// </summary>
    public string JournalMode { get; set; } = "WAL";

    /// <summary>
    /// Synchronous mode: OFF(0), NORMAL(1), FULL(2), EXTRA(3). Default: NORMAL.
    /// </summary>
    public string Synchronous { get; set; } = "NORMAL";

    /// <summary>
    /// Cache size in pages (positive) or KiB (negative). Default: -8000 (8MB).
    /// </summary>
    public int CacheSize { get; set; } = -8000;

    /// <summary>
    /// Temp store: DEFAULT, FILE, MEMORY. Default: MEMORY.
    /// </summary>
    public string TempStore { get; set; } = "MEMORY";

    /// <summary>
    /// Memory-mapped I/O size in bytes. 0 to disable. Default: 536870912 (512MB).
    /// </summary>
    public long MmapSize { get; set; } = 536870912;

    /// <summary>
    /// Busy timeout in milliseconds for locked database. Default: 5000 (5s).
    /// </summary>
    public int BusyTimeout { get; set; } = 5000;

    /// <summary>
    /// Enable foreign key constraints. Default: true.
    /// </summary>
    public bool ForeignKeys { get; set; } = true;

    /// <summary>
    /// Auto vacuum mode: NONE(0), FULL(1), INCREMENTAL(2). Default: INCREMENTAL.
    /// </summary>
    public string AutoVacuum { get; set; } = "INCREMENTAL";

    /// <summary>
    /// WAL auto-checkpoint interval in pages. 0 to disable. Default: 2000.
    /// </summary>
    public int WalAutoCheckpoint { get; set; } = 2000;

    /// <summary>
    /// Page size in bytes. Must be power of 2 between 512 and 65536. Default: 4096.
    /// </summary>
    public int PageSize { get; set; } = 4096;

    /// <summary>
    /// Securely delete data by overwriting with zeros. Default: false.
    /// </summary>
    public bool SecureDelete { get; set; } = false;
}
