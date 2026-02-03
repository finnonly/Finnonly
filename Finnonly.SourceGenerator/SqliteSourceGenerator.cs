// -
// -
// finnonly@outlook.com
// 2026-01-26
// -
// -

using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Finnonly.SourceGenerator;

[Generator]
public class SqliteSourceGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // 1. 收集候选实体类（带有TableAttribute的类）
        var entityDeclarations = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (s, _) => IsClassWithTableAttribute(s),
                transform: static (ctx, _) => GetClassDeclaration(ctx))
            .Where(static c => c is not null);

        // 2. 获取编译信息
        var compilation = context.CompilationProvider;

        // 3. 组合实体声明和编译信息
        var entities = entityDeclarations
            .Combine(compilation)
            .Select(static (pair, _) => GetEntityInfo(pair.Left, pair.Right))
            .Where(static info => info is not null)
            .Collect();

        // 4. 注册DbContext生成
        context.RegisterSourceOutput(entities, GenerateDbContext);

        // 5. 注册迁移管理器生成
        context.RegisterSourceOutput(entities, GenerateMigrationManager);
    }

    #region 语法分析辅助方法

    private static bool IsClassWithTableAttribute(SyntaxNode node)
    {
        return node is ClassDeclarationSyntax classDecl &&
               classDecl.AttributeLists.Count > 0;
    }

    private static ClassDeclarationSyntax? GetClassDeclaration(GeneratorSyntaxContext context)
    {
        var classDecl = (ClassDeclarationSyntax)context.Node;

        // 检查是否有TableAttribute
        var symbol = context.SemanticModel.GetDeclaredSymbol(classDecl);
        if (symbol?.GetAttributes().Any(a => a.AttributeClass?.Name == "TableAttribute") == true)
        {
            return classDecl;
        }

        return null;
    }

    private static EntityInfo? GetEntityInfo(
        ClassDeclarationSyntax? classDecl,
        Compilation compilation)
    {
        if (classDecl == null) return null;

        var semanticModel = compilation.GetSemanticModel(classDecl.SyntaxTree);
        var classSymbol = semanticModel.GetDeclaredSymbol(classDecl) as INamedTypeSymbol;
        if (classSymbol == null) return null;

        // 获取表名
        var tableAttr = classSymbol.GetAttributes()
            .FirstOrDefault(a => a.AttributeClass?.Name == "TableAttribute");
        string tableName = (tableAttr?.ConstructorArguments.FirstOrDefault().Value as string) ?? classSymbol.Name;

        // 获取迁移版本
        var migrationAttr = classSymbol.GetAttributes()
            .FirstOrDefault(a => a.AttributeClass?.Name == "MigrationAttribute");
        int migrationVersion = (migrationAttr?.ConstructorArguments.FirstOrDefault().Value as int?) ?? 1;

        // 获取列信息
        var properties = classSymbol.GetMembers().OfType<IPropertySymbol>();
        var columns = new List<ColumnInfo>();
        ColumnInfo? primaryKey = null;

        foreach (var prop in properties)
        {
            // 忽略标记为Ignore的属性
            if (prop.GetAttributes().Any(a => a.AttributeClass?.Name == "IgnoreAttribute"))
                continue;

            // 获取列名
            var colAttr = prop.GetAttributes()
                .FirstOrDefault(a => a.AttributeClass?.Name == "ColumnAttribute");
            string columnName = (colAttr?.ConstructorArguments.FirstOrDefault().Value as string) ?? prop.Name;

            // 检查是否主键
            var pkAttr = prop.GetAttributes()
                .FirstOrDefault(a => a.AttributeClass?.Name == "PrimaryKeyAttribute");
            bool isPrimaryKey = pkAttr != null;
            bool autoIncrement = isPrimaryKey &&
                                 (bool)(pkAttr?.ConstructorArguments.FirstOrDefault().Value ?? false);

            // 增强的类型分析
            var typeInfo = AnalyzePropertyType(prop.Type);

            var columnInfo = new ColumnInfo
            {
                PropertyName = prop.Name,
                ColumnName = columnName,
                Type = prop.Type.ToDisplayString(),
                TypeInfo = typeInfo,
                IsPrimaryKey = isPrimaryKey,
                AutoIncrement = autoIncrement,
                IsNullable = typeInfo.IsNullable
            };

            columns.Add(columnInfo);

            if (isPrimaryKey)
            {
                primaryKey = columnInfo;
            }
        }

        return new EntityInfo
        {
            ClassName = classSymbol.Name,
            TableName = tableName,
            Columns = columns,
            PrimaryKey = primaryKey,
            MigrationVersion = migrationVersion,
            Namespace = classSymbol.ContainingNamespace.ToDisplayString()
        };
    }

    // 新增：分析属性类型的详细信息
    private static PropertyTypeInfo AnalyzePropertyType(ITypeSymbol typeSymbol)
    {
        var typeInfo = new PropertyTypeInfo();

        // 检查是否为可空类型
        if (typeSymbol.CanBeReferencedByName && typeSymbol.Name == "Nullable" && typeSymbol is INamedTypeSymbol namedType)
        {
            typeInfo.IsNullable = true;
            typeInfo.UnderlyingType = namedType.TypeArguments[0].ToDisplayString();
            typeInfo.SqliteType = GetSqliteTypeFromDotNetType(typeInfo.UnderlyingType);
        }
        else if (typeSymbol.IsReferenceType)
        {
            typeInfo.IsNullable = typeSymbol.NullableAnnotation == NullableAnnotation.Annotated;
            typeInfo.UnderlyingType = typeSymbol.ToDisplayString();
            typeInfo.SqliteType = GetSqliteTypeFromDotNetType(typeInfo.UnderlyingType);
        }
        else
        {
            typeInfo.IsNullable = false;
            typeInfo.UnderlyingType = typeSymbol.ToDisplayString();
            typeInfo.SqliteType = GetSqliteTypeFromDotNetType(typeInfo.UnderlyingType);
        }

        return typeInfo;
    }

    // 增强的类型映射方法
    private static string GetSqliteTypeFromDotNetType(string dotnetType)
    {
        return dotnetType.Replace("?", "").Trim() switch
        {
            // 整数类型
            "byte" or "System.Byte" => "INTEGER",
            "sbyte" or "System.SByte" => "INTEGER",
            "short" or "System.Int16" => "INTEGER",
            "ushort" or "System.UInt16" => "INTEGER",
            "int" or "System.Int32" => "INTEGER",
            "uint" or "System.UInt32" => "INTEGER",
            "long" or "System.Int64" => "INTEGER",
            "ulong" or "System.UInt64" => "INTEGER",

            // 布尔类型
            "bool" or "System.Boolean" => "INTEGER",

            // 浮点数类型
            "float" or "System.Single" => "REAL",
            "double" or "System.Double" => "REAL",

            // 字符串类型
            "string" or "System.String" => "TEXT",
            "char" or "System.Char" => "TEXT",

            // 日期时间类型
            "System.DateTime" => "TEXT",
            "System.DateTimeOffset" => "TEXT",
            "System.DateOnly" => "TEXT",
            "System.TimeOnly" => "TEXT",
            "System.TimeSpan" => "TEXT",

            // 精确数值类型
            "decimal" or "System.Decimal" => "TEXT", // SQLite 推荐使用TEXT存储decimal

            // 二进制数据类型
            "byte[]" or "System.Byte[]" => "BLOB",

            // GUID类型
            "System.Guid" => "TEXT",

            // 枚举类型（需要特殊处理）
            var enumType when enumType.Contains("Enum") => "INTEGER",

            // JSON类型支持
            var jsonType when jsonType.Contains("JsonDocument") || jsonType.Contains("JsonElement") => "TEXT",

            // 默认情况
            _ => "TEXT"
        };
    }

    #endregion

    #region 代码生成方法

    private void GenerateDbContext(SourceProductionContext context, ImmutableArray<EntityInfo?> entities)
    {
        if (entities.IsDefaultOrEmpty) return;

        var validEntities = entities.Where(e => e != null).Cast<EntityInfo>().ToList();

        var source = BuildDbContextCode(validEntities);
        context.AddSource("SqliteDbContext.g.cs", SourceText.From(source, Encoding.UTF8));
    }

    private void GenerateMigrationManager(SourceProductionContext context, ImmutableArray<EntityInfo?> entities)
    {
        if (entities.IsDefaultOrEmpty) return;

        var validEntities = entities.Where(e => e != null).Cast<EntityInfo>().ToList();

        var source = BuildMigrationManagerCode(validEntities);
        context.AddSource("SqliteMigrationManager.g.cs", SourceText.From(source, Encoding.UTF8));
    }

    private string BuildDbContextCode(List<EntityInfo> entities)
    {
        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated />");
        sb.AppendLine("#nullable enable");
        sb.AppendLine("using Microsoft.Data.Sqlite;");
        sb.AppendLine("using System;");
        sb.AppendLine("using System.Collections.Generic;");
        sb.AppendLine("using System.Threading.Tasks;");
        sb.AppendLine("using System.Text.Json;");
        sb.AppendLine("using System.Linq;");
        sb.AppendLine();

        sb.AppendLine($"namespace Finnonly;");
        sb.AppendLine();

        sb.AppendLine("public sealed class SqliteDbContext : IDisposable");
        sb.AppendLine("{");
        sb.AppendLine("    private readonly SqliteConnection _connection;");
        sb.AppendLine("    private SqliteTransaction? _transaction;");
        sb.AppendLine();
        sb.AppendLine("    public SqliteDbContext(string connectionString)");
        sb.AppendLine("    {");
        sb.AppendLine("        _connection = new SqliteConnection(connectionString);");
        sb.AppendLine("        _connection.Open();");
        sb.AppendLine("        // 自动应用迁移");
        sb.AppendLine("        SqliteMigrationManager.MigrateAsync(_connection).GetAwaiter().GetResult();");
        sb.AppendLine("    }");
        sb.AppendLine();
        sb.AppendLine("    public void BeginTransaction()");
        sb.AppendLine("    {");
        sb.AppendLine("        _transaction = _connection.BeginTransaction();");
        sb.AppendLine("    }");
        sb.AppendLine();
        sb.AppendLine("    public void CommitTransaction()");
        sb.AppendLine("    {");
        sb.AppendLine("        _transaction?.Commit();");
        sb.AppendLine("        _transaction = null;");
        sb.AppendLine("    }");
        sb.AppendLine();
        sb.AppendLine("    public void RollbackTransaction()");
        sb.AppendLine("    {");
        sb.AppendLine("        _transaction?.Rollback();");
        sb.AppendLine("        _transaction = null;");
        sb.AppendLine("    }");
        sb.AppendLine();
        sb.AppendLine("    public void Dispose()");
        sb.AppendLine("    {");
        sb.AppendLine("        _transaction?.Dispose();");
        sb.AppendLine("        _connection?.Dispose();");
        sb.AppendLine("    }");
        sb.AppendLine();

        // 为每个实体生成CRUD方法
        foreach (var entity in entities)
        {
            GenerateEntityCrudMethods(sb, entity);
        }

        sb.AppendLine("}");
        return sb.ToString();
    }

    private void GenerateEntityCrudMethods(StringBuilder sb, EntityInfo entity)
    {
        // 插入方法
        sb.AppendLine($"    public async Task<int> Insert{entity.ClassName}Async({entity.ClassName} entity)");
        sb.AppendLine("    {");

        // 过滤掉自增主键
        var insertColumns = entity.Columns
            .Where(c => !c.IsPrimaryKey || !c.AutoIncrement)
            .ToList();

        sb.AppendLine($"        var sql = @\"INSERT INTO {entity.TableName} (");
        sb.AppendLine($"            {string.Join(", ", insertColumns.Select(c => c.ColumnName))}");
        sb.AppendLine("        ) VALUES (");
        sb.AppendLine($"            {string.Join(", ", insertColumns.Select(c => $"@{c.PropertyName}"))}");
        sb.AppendLine("        );");

        if (entity.PrimaryKey?.AutoIncrement == true)
        {
            sb.AppendLine("        SELECT last_insert_rowid();\";");
        }
        else
        {
            sb.AppendLine("\";");
        }

        sb.AppendLine();
        sb.AppendLine("        using var command = new SqliteCommand(sql, _connection, _transaction);");

        foreach (var column in insertColumns)
        {
            GenerateParameterAssignment(sb, column);
        }

        sb.AppendLine();

        if (entity.PrimaryKey?.AutoIncrement == true)
        {
            sb.AppendLine("        var result = await command.ExecuteScalarAsync();");
            sb.AppendLine("        return Convert.ToInt32(result);");
        }
        else
        {
            sb.AppendLine("        return await command.ExecuteNonQueryAsync();");
        }

        sb.AppendLine("    }");
        sb.AppendLine();

        // 查询方法
        sb.AppendLine($"    public async Task<List<{entity.ClassName}>> GetAll{entity.ClassName}Async()");
        sb.AppendLine("    {");
        sb.AppendLine($"        var sql = @\"SELECT * FROM {entity.TableName};\";");
        sb.AppendLine("        using var command = new SqliteCommand(sql, _connection, _transaction);");
        sb.AppendLine($"        return await ExecuteQuery{entity.ClassName}Async(command);");
        sb.AppendLine("    }");
        sb.AppendLine();

        if (entity.PrimaryKey != null)
        {
            sb.AppendLine($"    public async Task<{entity.ClassName}?> Get{entity.ClassName}ByIdAsync({entity.PrimaryKey.TypeInfo.UnderlyingType} id)");
            sb.AppendLine("    {");
            sb.AppendLine($"        var sql = @\"SELECT * FROM {entity.TableName} WHERE {entity.PrimaryKey.ColumnName} = @id;\";");
            sb.AppendLine("        using var command = new SqliteCommand(sql, _connection, _transaction);");
            sb.AppendLine("        command.Parameters.AddWithValue(\"@id\", id);");
            sb.AppendLine($"        var results = await ExecuteQuery{entity.ClassName}Async(command);");
            sb.AppendLine("        return results.FirstOrDefault();");
            sb.AppendLine("    }");
            sb.AppendLine();
        }

        // 更新方法
        sb.AppendLine($"    public async Task<int> Update{entity.ClassName}Async({entity.ClassName} entity)");
        sb.AppendLine("    {");
        if (entity.PrimaryKey == null)
        {
            sb.AppendLine("        throw new InvalidOperationException(\"Cannot update entity without primary key\");");
        }
        else
        {
            sb.AppendLine($"        var sql = @\"UPDATE {entity.TableName} SET");

            var updateColumns = entity.Columns
                .Where(c => !c.IsPrimaryKey)
                .ToList();

            bool firstUpdate = true;
            foreach (var column in updateColumns)
            {
                if (!firstUpdate) sb.AppendLine(",");
                sb.Append($"            {column.ColumnName} = @{column.PropertyName}");
                firstUpdate = false;
            }

            sb.AppendLine();
            sb.AppendLine($"        WHERE {entity.PrimaryKey.ColumnName} = @{entity.PrimaryKey.PropertyName};\";");
            sb.AppendLine();
            sb.AppendLine("        using var command = new SqliteCommand(sql, _connection, _transaction);");

            foreach (var column in entity.Columns)
            {
                GenerateParameterAssignment(sb, column);
            }

            sb.AppendLine();
            sb.AppendLine("        return await command.ExecuteNonQueryAsync();");
        }

        sb.AppendLine("    }");
        sb.AppendLine();

        // 删除方法
        if (entity.PrimaryKey != null)
        {
            sb.AppendLine($"    public async Task<int> Delete{entity.ClassName}Async({entity.PrimaryKey.TypeInfo.UnderlyingType} id)");
            sb.AppendLine("    {");
            sb.AppendLine($"        var sql = @\"DELETE FROM {entity.TableName} WHERE {entity.PrimaryKey.ColumnName} = @id;\";");
            sb.AppendLine("        using var command = new SqliteCommand(sql, _connection, _transaction);");
            sb.AppendLine("        command.Parameters.AddWithValue(\"@id\", id);");
            sb.AppendLine("        return await command.ExecuteNonQueryAsync();");
            sb.AppendLine("    }");
            sb.AppendLine();
        }

        // 查询执行方法
        sb.AppendLine($"    private async Task<List<{entity.ClassName}>> ExecuteQuery{entity.ClassName}Async(SqliteCommand command)");
        sb.AppendLine("    {");
        sb.AppendLine($"        var results = new List<{entity.ClassName}>();");
        sb.AppendLine("        using var reader = await command.ExecuteReaderAsync();");
        sb.AppendLine("        while (await reader.ReadAsync())");
        sb.AppendLine("        {");
        sb.AppendLine($"            var entity = new {entity.ClassName}();");

        foreach (var column in entity.Columns)
        {
            GeneratePropertyAssignment(sb, column);
        }

        sb.AppendLine("            results.Add(entity);");
        sb.AppendLine("        }");
        sb.AppendLine("        return results;");
        sb.AppendLine("    }");
        sb.AppendLine();


        // 新增：InsertOrUpdate方法（Upsert）
        sb.AppendLine($"    public async Task<int> InsertOrUpdate{entity.ClassName}Async({entity.ClassName} entity)");
        sb.AppendLine("    {");

        if (entity.PrimaryKey == null)
        {
            sb.AppendLine("        // 没有主键，只能插入");
            sb.AppendLine($"        return await Insert{entity.ClassName}Async(entity);");
        }
        else
        {
            sb.AppendLine("        // 使用 INSERT OR REPLACE 语法实现 upsert");

            // 包含所有列（包括主键）
            var allColumns = entity.Columns.ToList();

            sb.AppendLine($"        var sql = @\"INSERT OR REPLACE INTO {entity.TableName} (");
            sb.AppendLine($"            {string.Join(", ", allColumns.Select(c => c.ColumnName))}");
            sb.AppendLine("        ) VALUES (");
            sb.AppendLine($"            {string.Join(", ", allColumns.Select(c => $"@{c.PropertyName}"))}");
            sb.AppendLine("        );\";");
            sb.AppendLine();
            sb.AppendLine("        using var command = new SqliteCommand(sql, _connection, _transaction);");

            foreach (var column in allColumns)
            {
                GenerateParameterAssignment(sb, column);
            }

            sb.AppendLine();
            sb.AppendLine("        return await command.ExecuteNonQueryAsync();");
        }

        sb.AppendLine("    }");
        sb.AppendLine();

        // 新增：条件性InsertOrUpdate方法
        if (entity.PrimaryKey != null)
        {
            sb.AppendLine($"    public async Task<int> InsertOrUpdate{entity.ClassName}ByIdAsync({entity.ClassName} entity)");
            sb.AppendLine("    {");
            sb.AppendLine("        // 先检查记录是否存在");
            sb.AppendLine($"        var existingEntity = await Get{entity.ClassName}ByIdAsync(entity.{entity.PrimaryKey.PropertyName});");
            sb.AppendLine("        ");
            sb.AppendLine("        if (existingEntity != null)");
            sb.AppendLine("        {");
            sb.AppendLine("            // 存在则更新");
            sb.AppendLine($"            return await Update{entity.ClassName}Async(entity);");
            sb.AppendLine("        }");
            sb.AppendLine("        else");
            sb.AppendLine("        {");
            sb.AppendLine("            // 不存在则插入");

            if (entity.PrimaryKey.AutoIncrement)
            {
                sb.AppendLine($"            var newId = await Insert{entity.ClassName}Async(entity);");
                sb.AppendLine($"            entity.{entity.PrimaryKey.PropertyName} = ({entity.PrimaryKey.TypeInfo.UnderlyingType})newId;");
                sb.AppendLine("            return newId > 0 ? 1 : 0;");
            }
            else
            {
                sb.AppendLine($"            return await Insert{entity.ClassName}Async(entity);");
            }

            sb.AppendLine("        }");
            sb.AppendLine("    }");
            sb.AppendLine();
        }

        // 新增：批量InsertOrUpdate方法
        sb.AppendLine($"    public async Task<int> BulkInsertOrUpdate{entity.ClassName}Async(IEnumerable<{entity.ClassName}> entities)");
        sb.AppendLine("    {");
        sb.AppendLine("        if (!entities.Any()) return 0;");
        sb.AppendLine();
        sb.AppendLine("        var totalAffected = 0;");
        sb.AppendLine("        var wasInTransaction = _transaction != null;");
        sb.AppendLine();
        sb.AppendLine("        try");
        sb.AppendLine("        {");
        sb.AppendLine("            if (!wasInTransaction)");
        sb.AppendLine("            {");
        sb.AppendLine("                BeginTransaction();");
        sb.AppendLine("            }");
        sb.AppendLine();
        sb.AppendLine("            foreach (var entity in entities)");
        sb.AppendLine("            {");
        sb.AppendLine($"                var affected = await InsertOrUpdate{entity.ClassName}Async(entity);");
        sb.AppendLine("                totalAffected += affected;");
        sb.AppendLine("            }");
        sb.AppendLine();
        sb.AppendLine("            if (!wasInTransaction)");
        sb.AppendLine("            {");
        sb.AppendLine("                CommitTransaction();");
        sb.AppendLine("            }");
        sb.AppendLine();
        sb.AppendLine("            return totalAffected;");
        sb.AppendLine("        }");
        sb.AppendLine("        catch");
        sb.AppendLine("        {");
        sb.AppendLine("            if (!wasInTransaction)");
        sb.AppendLine("            {");
        sb.AppendLine("                RollbackTransaction();");
        sb.AppendLine("            }");
        sb.AppendLine("            throw;");
        sb.AppendLine("        }");
        sb.AppendLine("    }");
        sb.AppendLine();

        // 新增：根据自定义条件查询单个对象
        sb.AppendLine($"    public async Task<{entity.ClassName}?> Get{entity.ClassName}ByConditionAsync(string whereClause, SqliteParameter[] parameters)");
        sb.AppendLine("    {");
        sb.AppendLine($"        var sql = @\"SELECT * FROM {entity.TableName} WHERE \" + whereClause + \" LIMIT 1;\";");
        sb.AppendLine("        using var command = new SqliteCommand(sql, _connection, _transaction);");
        sb.AppendLine("        if (parameters != null)");
        sb.AppendLine("        {");
        sb.AppendLine("            command.Parameters.AddRange(parameters);");
        sb.AppendLine("        }");
        sb.AppendLine($"        var results = await ExecuteQuery{entity.ClassName}Async(command);");
        sb.AppendLine("        return results.FirstOrDefault();");
        sb.AppendLine("    }");
        sb.AppendLine();

        // 新增：根据自定义条件查询列表对象
        sb.AppendLine($"    public async Task<List<{entity.ClassName}>> Get{entity.ClassName}ListByConditionAsync(string whereClause, SqliteParameter[] parameters)");
        sb.AppendLine("    {");
        sb.AppendLine($"        var sql = @\"SELECT * FROM {entity.TableName} WHERE \" + whereClause + \";\";");
        sb.AppendLine("        using var command = new SqliteCommand(sql, _connection, _transaction);");
        sb.AppendLine("        if (parameters != null)");
        sb.AppendLine("        {");
        sb.AppendLine("            command.Parameters.AddRange(parameters);");
        sb.AppendLine("        }");
        sb.AppendLine($"        return await ExecuteQuery{entity.ClassName}Async(command);");
        sb.AppendLine("    }");
        sb.AppendLine();

        // 新增：根据自定义条件查询列表对象（带排序和分页）
        sb.AppendLine($"    public async Task<List<{entity.ClassName}>> Get{entity.ClassName}ListByConditionAsync(");
        sb.AppendLine("        string whereClause, ");
        sb.AppendLine("        SqliteParameter[] parameters,");
        sb.AppendLine("        string? orderBy = null,");
        sb.AppendLine("        int? limit = null,");
        sb.AppendLine("        int? offset = null)");
        sb.AppendLine("    {");
        sb.AppendLine($"        var sql = @\"SELECT * FROM {entity.TableName} WHERE \" + whereClause;");
        sb.AppendLine();
        sb.AppendLine("        if (!string.IsNullOrWhiteSpace(orderBy))");
        sb.AppendLine("        {");
        sb.AppendLine("            sql += \" ORDER BY \" + orderBy;");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine("        if (limit.HasValue)");
        sb.AppendLine("        {");
        sb.AppendLine("            sql += \" LIMIT \" + limit.Value;");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine("        if (offset.HasValue)");
        sb.AppendLine("        {");
        sb.AppendLine("            sql += \" OFFSET \" + offset.Value;");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine("        using var command = new SqliteCommand(sql, _connection, _transaction);");
        sb.AppendLine("        if (parameters != null)");
        sb.AppendLine("        {");
        sb.AppendLine("            command.Parameters.AddRange(parameters);");
        sb.AppendLine("        }");
        sb.AppendLine($"        return await ExecuteQuery{entity.ClassName}Async(command);");
        sb.AppendLine("    }");
        sb.AppendLine();

        // 新增：根据自定义条件获取数量
        sb.AppendLine($"    public async Task<int> Get{entity.ClassName}CountByConditionAsync(string whereClause, SqliteParameter[] parameters)");
        sb.AppendLine("    {");
        sb.AppendLine($"        var sql = @\"SELECT COUNT(*) FROM {entity.TableName} WHERE \" + whereClause + \";\";");
        sb.AppendLine("        using var command = new SqliteCommand(sql, _connection, _transaction);");
        sb.AppendLine("        if (parameters != null)");
        sb.AppendLine("        {");
        sb.AppendLine("            command.Parameters.AddRange(parameters);");
        sb.AppendLine("        }");
        sb.AppendLine("        var result = await command.ExecuteScalarAsync();");
        sb.AppendLine("        return Convert.ToInt32(result);");
        sb.AppendLine("    }");
        sb.AppendLine();

        // 新增：根据自定义条件删除
        sb.AppendLine($"    public async Task<int> Delete{entity.ClassName}ByConditionAsync(string whereClause, SqliteParameter[] parameters)");
        sb.AppendLine("    {");
        sb.AppendLine($"        var sql = @\"DELETE FROM {entity.TableName} WHERE \" + whereClause + \";\";");
        sb.AppendLine("        using var command = new SqliteCommand(sql, _connection, _transaction);");
        sb.AppendLine("        if (parameters != null)");
        sb.AppendLine("        {");
        sb.AppendLine("            command.Parameters.AddRange(parameters);");
        sb.AppendLine("        }");
        sb.AppendLine("        return await command.ExecuteNonQueryAsync();");
        sb.AppendLine("    }");
        sb.AppendLine();
    }

    // 新增：生成参数赋值代码
    private void GenerateParameterAssignment(StringBuilder sb, ColumnInfo column)
    {
        if (column.IsNullable)
        {
            sb.AppendLine($"        command.Parameters.AddWithValue(\"@{column.PropertyName}\", entity.{column.PropertyName} ?? (object)DBNull.Value);");
        }
        else
        {
            // 处理特殊类型的转换
            var conversion = GetParameterConversion(column);
            sb.AppendLine($"        command.Parameters.AddWithValue(\"@{column.PropertyName}\", {conversion});");
        }
    }

    // 新增：获取参数转换代码
    private string GetParameterConversion(ColumnInfo column)
    {
        var propertyAccess = $"entity.{column.PropertyName}";

        return column.TypeInfo.UnderlyingType.Replace("?", "").Trim() switch
        {
            "System.DateTime" => $"{propertyAccess}.ToString(\"yyyy-MM-dd HH:mm:ss\")",
            "System.DateTimeOffset" => $"{propertyAccess}.ToString(\"yyyy-MM-dd HH:mm:ss zzz\")",
            "System.DateOnly" => $"{propertyAccess}.ToString(\"yyyy-MM-dd\")",
            "System.TimeOnly" => $"{propertyAccess}.ToString(\"HH:mm:ss\")",
            "System.TimeSpan" => $"{propertyAccess}.ToString()",
            "System.Guid" => $"{propertyAccess}.ToString()",
            "decimal" or "System.Decimal" => $"{propertyAccess}.ToString(System.Globalization.CultureInfo.InvariantCulture)",
            var enumType when enumType.Contains("Enum") => $"(int){propertyAccess}",
            var jsonType when jsonType.Contains("JsonDocument") || jsonType.Contains("JsonElement") => $"JsonSerializer.Serialize({propertyAccess})",
            _ => propertyAccess
        };
    }

    // 新增：生成属性赋值代码
    private void GeneratePropertyAssignment(StringBuilder sb, ColumnInfo column)
    {
        sb.AppendLine($"            if (!await reader.IsDBNullAsync(reader.GetOrdinal(\"{column.ColumnName}\")))");
        sb.AppendLine($"            {{");

        var conversion = GetReaderConversion(column);
        sb.AppendLine($"                entity.{column.PropertyName} = {conversion};");
        sb.AppendLine($"            }}");

        if (column.IsNullable)
        {
            sb.AppendLine($"            else");
            sb.AppendLine($"            {{");
            sb.AppendLine($"                entity.{column.PropertyName} = null;");
            sb.AppendLine($"            }}");
        }
    }

    // 增强的读取器转换方法
    private string GetReaderConversion(ColumnInfo column)
    {
        var ordinalAccess = $"reader.GetOrdinal(\"{column.ColumnName}\")";

        return column.TypeInfo.UnderlyingType.Replace("?", "").Trim() switch
        {
            // 基本数值类型
            "byte" or "System.Byte" => $"(byte)reader.GetInt32({ordinalAccess})",
            "sbyte" or "System.SByte" => $"(sbyte)reader.GetInt32({ordinalAccess})",
            "short" or "System.Int16" => $"reader.GetInt16({ordinalAccess})",
            "ushort" or "System.UInt16" => $"(ushort)reader.GetInt32({ordinalAccess})",
            "int" or "System.Int32" => $"reader.GetInt32({ordinalAccess})",
            "uint" or "System.UInt32" => $"(uint)reader.GetInt64({ordinalAccess})",
            "long" or "System.Int64" => $"reader.GetInt64({ordinalAccess})",
            "ulong" or "System.UInt64" => $"(ulong)reader.GetInt64({ordinalAccess})",
            "bool" or "System.Boolean" => $"reader.GetBoolean({ordinalAccess})",
            "float" or "System.Single" => $"reader.GetFloat({ordinalAccess})",
            "double" or "System.Double" => $"reader.GetDouble({ordinalAccess})",

            // 字符串类型
            "string" or "System.String" => $"reader.GetString({ordinalAccess})",
            "char" or "System.Char" => $"reader.GetString({ordinalAccess})[0]",

            // 日期时间类型
            "System.DateTime" => $"DateTime.Parse(reader.GetString({ordinalAccess}))",
            "System.DateTimeOffset" => $"DateTimeOffset.Parse(reader.GetString({ordinalAccess}))",
            "System.DateOnly" => $"DateOnly.Parse(reader.GetString({ordinalAccess}))",
            "System.TimeOnly" => $"TimeOnly.Parse(reader.GetString({ordinalAccess}))",
            "System.TimeSpan" => $"TimeSpan.Parse(reader.GetString({ordinalAccess}))",

            // 特殊类型
            "System.Guid" => $"Guid.Parse(reader.GetString({ordinalAccess}))",
            "decimal" or "System.Decimal" => $"decimal.Parse(reader.GetString({ordinalAccess}), System.Globalization.CultureInfo.InvariantCulture)",
            "byte[]" or "System.Byte[]" => $"reader.GetFieldValue<byte[]>({ordinalAccess})",

            // 枚举类型
            var enumType when enumType.Contains("Enum") => $"({column.TypeInfo.UnderlyingType})reader.GetInt32({ordinalAccess})",

            // JSON类型
            var jsonType when jsonType.Contains("JsonDocument") => $"JsonDocument.Parse(reader.GetString({ordinalAccess}))",
            var jsonType when jsonType.Contains("JsonElement") => $"JsonSerializer.Deserialize<{column.TypeInfo.UnderlyingType}>(reader.GetString({ordinalAccess}))",

            // 默认情况
            _ => $"({column.TypeInfo.UnderlyingType})reader.GetValue({ordinalAccess})"
        };
    }

    private string BuildMigrationManagerCode(List<EntityInfo> entities)
    {
        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated />");
        sb.AppendLine("#nullable enable");
        sb.AppendLine("using Microsoft.Data.Sqlite;");
        sb.AppendLine("using System;");
        sb.AppendLine("using System.Collections.Generic;");
        sb.AppendLine("using System.Linq;");
        sb.AppendLine("using System.Threading.Tasks;");
        sb.AppendLine();

        sb.AppendLine($"namespace Finnonly;");
        sb.AppendLine();

        sb.AppendLine("public static class SqliteMigrationManager");
        sb.AppendLine("{");
        sb.AppendLine("    private const string MIGRATION_TABLE = \"__EFMigrationsHistory\";");
        sb.AppendLine();

        // 为每个实体生成列类型字典
        foreach (var entity in entities)
        {
            sb.AppendLine($"    // {entity.ClassName} 列类型映射");
            sb.AppendLine($"    private static readonly Dictionary<string, string> {entity.ClassName}ColumnTypes = new()");
            sb.AppendLine("    {");
            foreach (var column in entity.Columns)
            {
                sb.AppendLine($"        {{\"{column.ColumnName}\", \"{column.TypeInfo.SqliteType}\"}},");
            }

            sb.AppendLine("    };");
            sb.AppendLine();
        }

        sb.AppendLine("    public static async Task MigrateAsync(SqliteConnection connection)");
        sb.AppendLine("    {");
        sb.AppendLine("        await CreateMigrationTableAsync(connection);");
        sb.AppendLine();

        // 为每个实体生成迁移调用
        foreach (var entity in entities)
        {
            sb.AppendLine($"        await Migrate{entity.ClassName}Async(connection, {entity.MigrationVersion});");
        }

        sb.AppendLine("    }");
        sb.AppendLine();
        sb.AppendLine("    private static async Task CreateMigrationTableAsync(SqliteConnection connection)");
        sb.AppendLine("    {");
        sb.AppendLine("        var sql = @$\"CREATE TABLE IF NOT EXISTS {MIGRATION_TABLE} (");
        sb.AppendLine("            MigrationId TEXT PRIMARY KEY,");
        sb.AppendLine("            Version INTEGER NOT NULL");
        sb.AppendLine("        )\";");
        sb.AppendLine("        using var command = new SqliteCommand(sql, connection);");
        sb.AppendLine("        await command.ExecuteNonQueryAsync();");
        sb.AppendLine("    }");
        sb.AppendLine();

        // 为每个实体生成迁移方法
        foreach (var entity in entities)
        {
            GenerateEntityMigrationMethod(sb, entity);
        }

        // 生成迁移辅助方法
        sb.AppendLine("    private static async Task<bool> NeedsMigrationAsync(");
        sb.AppendLine("        SqliteConnection connection, ");
        sb.AppendLine("        string tableName, ");
        sb.AppendLine("        int requiredVersion)");
        sb.AppendLine("    {");
        sb.AppendLine("        var sql = @$\"SELECT Version FROM {MIGRATION_TABLE} ");
        sb.AppendLine("                  WHERE MigrationId = @tableName\";");
        sb.AppendLine("        using var command = new SqliteCommand(sql, connection);");
        sb.AppendLine("        command.Parameters.AddWithValue(\"@tableName\", tableName);");
        sb.AppendLine("        var result = await command.ExecuteScalarAsync();");
        sb.AppendLine();
        sb.AppendLine("        if (result == null || result == DBNull.Value) return true;");
        sb.AppendLine("        var currentVersion = Convert.ToInt32(result);");
        sb.AppendLine("        return currentVersion < requiredVersion;");
        sb.AppendLine("    }");
        sb.AppendLine();
        sb.AppendLine("    private static async Task UpdateMigrationVersionAsync(");
        sb.AppendLine("        SqliteConnection connection, ");
        sb.AppendLine("        string tableName, ");
        sb.AppendLine("        int version)");
        sb.AppendLine("    {");
        sb.AppendLine("        var sql = @$\"INSERT OR REPLACE INTO {MIGRATION_TABLE} ");
        sb.AppendLine("                    (MigrationId, Version) VALUES (@tableName, @version)\";");
        sb.AppendLine("        using var command = new SqliteCommand(sql, connection);");
        sb.AppendLine("        command.Parameters.AddWithValue(\"@tableName\", tableName);");
        sb.AppendLine("        command.Parameters.AddWithValue(\"@version\", version);");
        sb.AppendLine("        await command.ExecuteNonQueryAsync();");
        sb.AppendLine("    }");
        sb.AppendLine();
        sb.AppendLine("    private static async Task<List<string>> GetTableColumnsAsync(");
        sb.AppendLine("        SqliteConnection connection, ");
        sb.AppendLine("        string tableName)");
        sb.AppendLine("    {");
        sb.AppendLine("        var columns = new List<string>();");
        sb.AppendLine("        var sql = @$\"PRAGMA table_info('{tableName}')\";");
        sb.AppendLine("        using var command = new SqliteCommand(sql, connection);");
        sb.AppendLine("        using var reader = await command.ExecuteReaderAsync();");
        sb.AppendLine("        while (await reader.ReadAsync())");
        sb.AppendLine("        {");
        sb.AppendLine("            columns.Add(reader.GetString(1)); // name is at index 1");
        sb.AppendLine("        }");
        sb.AppendLine("        return columns;");
        sb.AppendLine("    }");
        sb.AppendLine("}");

        return sb.ToString();
    }

    private void GenerateEntityMigrationMethod(StringBuilder sb, EntityInfo entity)
    {
        sb.AppendLine($"    private static async Task Migrate{entity.ClassName}Async(");
        sb.AppendLine($"        SqliteConnection connection, int requiredVersion)");
        sb.AppendLine("    {");
        sb.AppendLine($"        if (!await NeedsMigrationAsync(connection, \"{entity.TableName}\", requiredVersion))");
        sb.AppendLine("            return;");
        sb.AppendLine();
        sb.AppendLine("        // 检查表是否存在");
        sb.AppendLine("        var tableExistsSql = @\"SELECT count(*) FROM sqlite_master ");
        sb.AppendLine("                           WHERE type = 'table' AND name = @tableName\";");
        sb.AppendLine("        using var existsCmd = new SqliteCommand(tableExistsSql, connection);");
        sb.AppendLine($"        existsCmd.Parameters.AddWithValue(\"@tableName\", \"{entity.TableName}\");");
        sb.AppendLine("        var exists = (long)await existsCmd.ExecuteScalarAsync() > 0;");
        sb.AppendLine();
        sb.AppendLine("        if (!exists)");
        sb.AppendLine("        {");
        sb.AppendLine("            // 创建新表");
        sb.AppendLine($"            await Create{entity.ClassName}TableAsync(connection);");
        sb.AppendLine("        }");
        sb.AppendLine("        else");
        sb.AppendLine("        {");
        sb.AppendLine("            // 迁移现有表");
        sb.AppendLine($"            var currentColumns = await GetTableColumnsAsync(connection, \"{entity.TableName}\");");
        sb.AppendLine();
        sb.AppendLine("            // 添加缺失的列");
        sb.AppendLine($"            foreach (var expectedColumn in {entity.ClassName}ColumnTypes.Keys)");
        sb.AppendLine("            {");
        sb.AppendLine("                if (!currentColumns.Contains(expectedColumn))");
        sb.AppendLine("                {");
        sb.AppendLine($"                    var sqliteType = {entity.ClassName}ColumnTypes[expectedColumn];");
        sb.AppendLine($"                    var addSql = @$\"ALTER TABLE {entity.TableName} ");
        sb.AppendLine("                                  ADD COLUMN {expectedColumn} {sqliteType}\";");
        sb.AppendLine("                    using var addCmd = new SqliteCommand(addSql, connection);");
        sb.AppendLine("                    await addCmd.ExecuteNonQueryAsync();");
        sb.AppendLine("                }");
        sb.AppendLine("            }");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine($"        await UpdateMigrationVersionAsync(connection, \"{entity.TableName}\", requiredVersion);");
        sb.AppendLine("    }");
        sb.AppendLine();

        // 生成创建表的方法
        sb.AppendLine($"    private static async Task Create{entity.ClassName}TableAsync(SqliteConnection connection)");
        sb.AppendLine("    {");
        sb.AppendLine($"        var sql = @\"CREATE TABLE {entity.TableName} (");

        bool firstColumn = true;
        foreach (var column in entity.Columns)
        {
            if (!firstColumn) sb.AppendLine(",");

            sb.Append($"            {column.ColumnName} {column.TypeInfo.SqliteType}");

            if (column.IsPrimaryKey)
            {
                sb.Append(" PRIMARY KEY");
                if (column.AutoIncrement) sb.Append(" AUTOINCREMENT");
            }

            if (!column.IsNullable && !column.IsPrimaryKey)
            {
                sb.Append(" NOT NULL");
            }

            firstColumn = false;
        }

        sb.AppendLine("\n        )\";");
        sb.AppendLine("        using var command = new SqliteCommand(sql, connection);");
        sb.AppendLine("        await command.ExecuteNonQueryAsync();");
        sb.AppendLine("    }");
        sb.AppendLine();
    }

    #endregion

    #region 数据结构

    private class EntityInfo
    {
        public string ClassName { get; set; } = string.Empty;
        public string TableName { get; set; } = string.Empty;
        public List<ColumnInfo> Columns { get; set; } = new List<ColumnInfo>();
        public ColumnInfo? PrimaryKey { get; set; }
        public int MigrationVersion { get; set; } = 1;
        public string Namespace { get; set; } = string.Empty;
    }

    private class ColumnInfo
    {
        public string PropertyName { get; set; } = string.Empty;
        public string ColumnName { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public PropertyTypeInfo TypeInfo { get; set; } = new PropertyTypeInfo();
        public bool IsPrimaryKey { get; set; }
        public bool AutoIncrement { get; set; }
        public bool IsNullable { get; set; }
    }

    // 新增：属性类型信息类
    private class PropertyTypeInfo
    {
        public string UnderlyingType { get; set; } = string.Empty;
        public string SqliteType { get; set; } = string.Empty;
        public bool IsNullable { get; set; }
    }

    #endregion
}
