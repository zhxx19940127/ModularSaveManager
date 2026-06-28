using System;
using System.Reflection;
using LitJson.Extensions;

/// <summary>
/// 正式存档的数据模块基类。
///
/// 一个模块对应一个相对独立的数据层，例如：
/// - 玩家基础数据：等级、经验、金币。
/// - 背包数据：道具列表、装备列表。
/// - 关卡进度：星级、解锁状态、通关时间。
///
/// 运行时模块由 ModularSaveManager 用 Dictionary 管理；
/// 落盘时每个模块会被序列化成 SaveModuleEntry，并写入同一个存档槽 JSON 文件。
/// </summary>
public abstract class SaveModule
{
    private SaveModuleAttribute _cachedAttribute;
    private FieldInfo[] _cachedSaveValueFields;

    /// <summary>
    /// 模块稳定 ID。
    ///
    /// 这是存档文件里识别模块的主身份，推荐使用短小、稳定、全小写的名字，
    /// 例如 "player"、"inventory"、"level_progress"。
    ///
    /// 重要：上线后不要随便改 Key。类名可以改，Key 尽量不要改。
    /// </summary>
    [JsonIgnore]
    public virtual string Key
    {
        get
        {
            SaveModuleAttribute attribute = GetSaveModuleAttribute();
            return attribute != null ? attribute.Key : string.Empty;
        }
    }

    /// <summary>
    /// 当前模块的数据结构版本。
    ///
    /// 版本由模块开发者自己维护。只有当这个模块的数据结构发生不兼容变化时，
    /// 才需要提升版本并提供对应的 ISaveMigration。
    /// </summary>
    [JsonIgnore]
    public virtual int Version
    {
        get
        {
            SaveModuleAttribute attribute = GetSaveModuleAttribute();
            return attribute != null ? attribute.Version : 0;
        }
    }

    /// <summary>
    /// 重置为新存档默认数据。
    ///
    /// LoadSlot 时会先调用所有模块的 ResetData，然后再用文件里的数据覆盖。
    /// 这样即使旧存档缺少某个新模块，这个新模块也能保持合理默认值。
    /// </summary>
    public virtual void ResetData()
    {
    }

    /// <summary>
    /// 读取存档后修复模块数据。
    ///
    /// 适合修正旧存档或外部篡改导致的非法值，例如金币小于 0、等级小于 1、
    /// 集合字段为 null、背包道具数量无效等。
    ///
    /// 返回 true 表示确实修改了数据，SaveManager 会把当前槽标记为脏数据，
    /// 让修复后的结果在后续自动保存中落盘。
    /// </summary>
    public virtual bool RepairAfterLoad()
    {
        return false;
    }

    /// <summary>
    /// 读取和修复后校验模块数据是否可用。
    ///
    /// 如果返回 false，SaveManager 会把该模块重置为默认数据，避免损坏数据继续进入业务层。
    /// error 用于输出调试原因，可以为空。
    /// </summary>
    public virtual bool ValidateAfterLoad(out string error)
    {
        error = null;
        return true;
    }

    /// <summary>
    /// 保存前回调。
    ///
    /// 适合在落盘前把运行时缓存同步到可序列化字段。
    /// 不建议在这里做昂贵逻辑，也不建议修改其他模块。
    /// </summary>
    public virtual void OnBeforeSave()
    {
    }

    /// <summary>
    /// 读取后回调。
    ///
    /// 适合在反序列化后重建运行时缓存、修正非法值、补齐派生数据。
    /// </summary>
    public virtual void OnAfterLoad()
    {
    }

    /// <summary>
    /// 绑定模块内顶层 SaveValue 字段的脏标记回调。
    /// ModularSaveManager 会在注册、读取和重置后自动调用。
    /// </summary>
    public void BindSaveValues(Action markDirty)
    {
        FieldInfo[] fields = GetSaveValueFields();
        for (int i = 0; i < fields.Length; i++)
        {
            if (fields[i].GetValue(this) is ISaveValue saveValue)
            {
                saveValue.BindDirty(markDirty);
            }
        }
    }

    /// <summary>
    /// 解除模块内顶层 SaveValue 字段的脏标记回调。
    /// 读取后修复数据时会临时解除，避免修复过程提前触发保存。
    /// </summary>
    public void ClearSaveValueBindings()
    {
        FieldInfo[] fields = GetSaveValueFields();
        for (int i = 0; i < fields.Length; i++)
        {
            if (fields[i].GetValue(this) is ISaveValue saveValue)
            {
                saveValue.ClearDirtyBinding();
            }
        }
    }

    private SaveModuleAttribute GetSaveModuleAttribute()
    {
        if (_cachedAttribute == null)
        {
            _cachedAttribute = (SaveModuleAttribute)Attribute.GetCustomAttribute(GetType(), typeof(SaveModuleAttribute));
        }

        return _cachedAttribute;
    }

    private FieldInfo[] GetSaveValueFields()
    {
        if (_cachedSaveValueFields == null)
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public;
            FieldInfo[] fields = GetType().GetFields(flags);
            int count = 0;

            for (int i = 0; i < fields.Length; i++)
            {
                if (typeof(ISaveValue).IsAssignableFrom(fields[i].FieldType))
                {
                    count++;
                }
            }

            _cachedSaveValueFields = new FieldInfo[count];
            int index = 0;
            for (int i = 0; i < fields.Length; i++)
            {
                if (typeof(ISaveValue).IsAssignableFrom(fields[i].FieldType))
                {
                    _cachedSaveValueFields[index] = fields[i];
                    index++;
                }
            }
        }

        return _cachedSaveValueFields;
    }
}
