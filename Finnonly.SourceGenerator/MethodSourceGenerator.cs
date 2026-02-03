// -
// -
// finnonly@outlook.com
// 2026-01-26
// -
// -

using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Finnonly.SourceGenerator;

[Generator]
public sealed class MethodInvokerGenerator : IIncrementalGenerator
{
    private sealed class MethodInfo
    {
        public string ContainingTypeFullName { get; }
        public string ContainingTypeName { get; }
        public string Name { get; }
        public ImmutableArray<ParameterInfo> Parameters { get; }
        public string ReturnType { get; }
        public bool IsValueType { get; }

        public MethodInfo(
            string containingTypeFullName,
            string containingTypeName,
            string name,
            ImmutableArray<ParameterInfo> parameters,
            string returnType,
            bool isValueType)
        {
            ContainingTypeFullName = containingTypeFullName;
            ContainingTypeName = containingTypeName;
            Name = name;
            Parameters = parameters;
            ReturnType = returnType;
            IsValueType = isValueType;
        }
    }

    private sealed class ParameterInfo
    {
        public string Name { get; }
        public string Type { get; }
        public bool IsValueType { get; }

        public ParameterInfo(string name, string type, bool isValueType)
        {
            Name = name;
            Type = type;
            IsValueType = isValueType;
        }
    }

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
// #if DEBUG
//         if (!Debugger.IsAttached)
//         {
//             Debugger.Launch();
//         }
// #endif

        var methodDeclarations = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (s, _) => s is MethodDeclarationSyntax { AttributeLists.Count: > 0 },
                transform: static (ctx, _) => GetMarkedMethodInfo(ctx))
            .Where(static m => m is not null);

        context.RegisterSourceOutput(methodDeclarations.Collect(),
            static (spc, methods) => GenerateMethodDispatcher(methods!, spc));
    }

    private static MethodInfo? GetMarkedMethodInfo(GeneratorSyntaxContext context)
    {
        var methodDeclaration = (MethodDeclarationSyntax)context.Node;
        var semanticModel = context.SemanticModel;
        var methodSymbol = semanticModel.GetDeclaredSymbol(methodDeclaration);

        if (methodSymbol == null) return null;

        var hasDynamicInvoke = methodSymbol.GetAttributes()
            .Any(ad => ad.AttributeClass?.Name == "EventBindAttribute");

        if (!hasDynamicInvoke) return null;

        var containingType = methodSymbol.ContainingType;
        var parameters = methodSymbol.Parameters
            .Select(p => new ParameterInfo(
                p.Name,
                p.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                p.Type.IsValueType))
            .ToImmutableArray();

        return new MethodInfo(
            containingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            containingType.Name,
            methodSymbol.Name,
            parameters,
            methodSymbol.ReturnType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            methodSymbol.ReturnType.IsValueType);
    }

    private static void GenerateMethodDispatcher(ImmutableArray<MethodInfo> methods, SourceProductionContext context)
    {
        var source = new StringBuilder();
        source.AppendLine("""
                            // Auto-generated at UTC: 2025-02-24 08:26:13
                            // Generator: finnonly
                            #nullable enable

                            using System;
                            using System.Runtime.CompilerServices;
                            using System.Collections.Frozen;
                            using System.Collections.Generic;

                            namespace Finnonly
                            {
                                public static class MethodDispatcher
                                {
                                    private static readonly FrozenDictionary<string, Action<object, object?[]>> _invokers = CreateInvokers();
                                    
                                    [MethodImpl(MethodImplOptions.AggressiveInlining)]
                                    public static void Invoke(object target, string methodName, ReadOnlySpan<object?> args)
                                    {
                                        // 获取完全限定的类型名，添加 global:: 前缀
                                        var key = GetMethodKey($"global::{target.GetType().FullName}", methodName);
                                        if (!_invokers.TryGetValue(key, out var invoker))
                                            throw new MissingMethodException($"Method {methodName} not found or not marked for dynamic invocation");
                                        invoker(target, args.ToArray());
                                    }
                                    
                                    [MethodImpl(MethodImplOptions.AggressiveInlining)]
                                    public static void Invoke(object target, string methodName, object?[] args)
                                        => Invoke(target, methodName, args.AsSpan());
                                    
                                    private static FrozenDictionary<string, Action<object, object?[]>> CreateInvokers()
                                    {
                                        return new Dictionary<string, Action<object, object?[]>>
                                        {
                            """);

        foreach (var method in methods)
        {
            GenerateMethodInvoker(source, method);
        }

        source.AppendLine("""
                                      }.ToFrozenDictionary();
                                  }
                                  
                                  [MethodImpl(MethodImplOptions.AggressiveInlining)]
                                  private static string GetMethodKey(string typeName, string methodName)
                                      => $"{typeName}:{methodName}";
                              }
                          }
                          """);

        context.AddSource("MethodDispatcher.g.cs", source.ToString());
    }

    private static void GenerateMethodInvoker(StringBuilder source, MethodInfo method)
{
    var key = $"{method.ContainingTypeFullName}:{method.Name}";

    source.Append($$"""
                                        { "{{key}}", (target, args) =>
                                        {
                                            var typedTarget = ({{method.ContainingTypeFullName}})target;
                    """);

    if (method.Parameters.Length == 0)
    {
        // 无参方法：直接调用，忽略传入的参数
        source.AppendLine($$"""

                                            typedTarget.{{method.Name}}();
                                        }
                                        },
                            """);
    }
    else
    {
        // 有参方法
        source.AppendLine();
        source.AppendLine($$"""
                                            if (args.Length < {{method.Parameters.Length}})
                                                throw new ArgumentException($"Method {{method.Name}} expects at least {{method.Parameters.Length}} arguments, but got {args.Length}");
                            """);

        // 参数转换
        for (var i = 0; i < method.Parameters.Length; i++)
        {
            var param = method.Parameters[i];
            var isNullableValueType = param.IsValueType && param.Type.EndsWith("?");

            if (param.IsValueType && !isNullableValueType)
            {
                // 非可空值类型：使用模式匹配
                source.AppendLine($$"""
                                                if (args[{{i}}] is not {{param.Type}} arg{{i}})
                                                    throw new ArgumentException($"Argument {{i}} must be of type {{param.Type}}");
                                    """);
            }
            else
            {
                // 引用类型和可空值类型：直接转换
                source.AppendLine($"                            var arg{i} = ({param.Type})args[{i}]!;");
            }
        }

        var arguments = string.Join(", ", Enumerable.Range(0, method.Parameters.Length).Select(i => $"arg{i}"));

        source.AppendLine($$"""
                                            typedTarget.{{method.Name}}({{arguments}});
                                        }
                                        },
                            """);
    }
}
}
