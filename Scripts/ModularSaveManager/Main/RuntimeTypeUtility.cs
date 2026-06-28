using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

/// <summary>
/// 运行时类型扫描工具。
/// </summary>
public static class RuntimeTypeUtility
{
    /// <summary>
    /// 获取当前 AppDomain 中所有可读取的运行时类型。
    /// </summary>
    public static Type[] GetAllRuntimeTypes(int capacity = 512)
    {
        List<Type> result = new List<Type>(capacity);
        Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();

        for (int i = 0; i < assemblies.Length; i++)
        {
            Assembly assembly = assemblies[i];
            Type[] types;

            try
            {
                types = assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException exception)
            {
                types = exception.Types;
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"自动注册扫描程序集失败: assembly={assembly.FullName}, error={exception.Message}");
                continue;
            }

            if (types == null)
            {
                continue;
            }

            for (int j = 0; j < types.Length; j++)
            {
                if (types[j] != null)
                {
                    result.Add(types[j]);
                }
            }
        }

        return result.ToArray();
    }
}
