using System;
using System.Collections.Generic;

/// <summary>
/// 一个存档槽的完整文件结构。
///
/// 物理存储策略是：一个存档槽一个 JSON 文件。
/// 逻辑数据策略是：文件内部按模块拆成 SaveModuleEntry 列表。
/// </summary>
[Serializable]
public sealed class SaveFile
{
    /// <summary>
    /// 整个存档文件格式版本。
    ///
    /// 目前主要预留给未来改变文件外壳结构时使用。
    /// 普通业务数据迁移应该优先使用 SaveModuleEntry.version。
    /// </summary>
    public int saveVersion = 1;

    /// <summary>
    /// 这个文件所属的存档槽。
    /// </summary>
    public int slotId;

    /// <summary>
    /// 最后保存时间，UTC Unix 秒。
    /// 可用于存档选择界面显示最近保存时间。
    /// </summary>
    public long updatedAtUtc;

    /// <summary>
    /// 所有已注册模块的序列化结果。
    /// 使用 List 而不是 Dictionary，是为了让文件结构更稳定，也便于兼容不同 JSON 库。
    /// </summary>
    public List<SaveModuleEntry> modules = new List<SaveModuleEntry>();
}

/// <summary>
/// 单个模块在存档文件里的记录。
/// </summary>
[Serializable]
public sealed class SaveModuleEntry
{
    /// <summary>
    /// 模块稳定 ID，对应 SaveModule.Key。
    /// 读取时优先按 key 找已注册模块。
    /// </summary>
    public string key;

    /// <summary>
    /// 模块 C# 类型名。
    /// 主要用于调试和发现类名变更，不作为模块主身份。
    /// </summary>
    public string type;

    /// <summary>
    /// 这个模块写入文件时的数据版本，对应 SaveModule.Version。
    /// </summary>
    public int version;

    /// <summary>
    /// 模块自身的 JSON 字符串。
    /// 使用嵌套字符串可以让每个模块独立迁移，SaveManager 不需要理解业务结构。
    /// </summary>
    public string json;
}
