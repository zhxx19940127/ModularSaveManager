using System;

/// <summary>
/// 业务管理器自动注册标记。
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class ModularServerAttribute : Attribute
{
    /// <summary>业务管理器稳定 Key，用于注册表、查重、日志和调试定位。</summary>
    public string Key { get; }

    /// <summary>初始化优先级，数值越大越早初始化。</summary>
    public int Priority { get; }

    /// <summary>
    /// 创建业务管理器注册标记。
    /// </summary>
    public ModularServerAttribute(string key, int priority = 0)
    {
        Key = key;
        Priority = priority;
    }
}
