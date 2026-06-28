using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 允许写入 PlayerPrefs 的受控 key。
///
/// 业务代码不要直接写字符串 key，也不要绕过 ModularSaveManager 调 PlayerPrefs。
/// 新增偏好项时，需要同时：
/// 1. 在 PrefKey 里加枚举。
/// 2. 在 Prefs.Definitions 里声明类型和默认值。
/// </summary>
public enum PrefKey
{
    MusicVolume,
    SfxVolume,
    VibrationEnabled,
    Language,
    QualityLevel,
    TutorialCompleted,
    SelectedSaveSlot
}

/// <summary>
/// PlayerPrefs 允许保存的标量类型。
///
/// PlayerPrefs 层只允许 int / float / bool / string。
/// List、Dictionary、对象、完整业务数据都应该进入 JSON SaveModule。
/// </summary>
public enum PrefValueType
{
    Int,
    Float,
    Bool,
    String
}

/// <summary>
/// 一个受控偏好项的定义。
/// 包含 key、值类型、默认值。
/// </summary>
public sealed class PrefDefinition
{
    public PrefKey Key;
    public PrefValueType ValueType;
    public int IntDefault;
    public float FloatDefault;
    public bool BoolDefault;
    public string StringDefault;

    public static PrefDefinition Int(PrefKey key, int defaultValue)
    {
        return new PrefDefinition { Key = key, ValueType = PrefValueType.Int, IntDefault = defaultValue };
    }

    public static PrefDefinition Float(PrefKey key, float defaultValue)
    {
        return new PrefDefinition { Key = key, ValueType = PrefValueType.Float, FloatDefault = defaultValue };
    }

    public static PrefDefinition Bool(PrefKey key, bool defaultValue)
    {
        return new PrefDefinition { Key = key, ValueType = PrefValueType.Bool, BoolDefault = defaultValue };
    }

    public static PrefDefinition String(PrefKey key, string defaultValue)
    {
        return new PrefDefinition { Key = key, ValueType = PrefValueType.String, StringDefault = defaultValue ?? string.Empty };
    }
}

/// <summary>
/// PlayerPrefs 白名单定义表。
///
/// 这里就是 PlayerPrefs 层的唯一注册入口。
/// 如果某个 key 不在这个数组里，ModularSaveManager 会拒绝读写它。
/// </summary>
public static class Prefs
{
    public static readonly PrefDefinition[] Definitions =
    {
        PrefDefinition.Float(PrefKey.MusicVolume, 1f),
        PrefDefinition.Float(PrefKey.SfxVolume, 1f),
        PrefDefinition.Bool(PrefKey.VibrationEnabled, true),
        PrefDefinition.String(PrefKey.Language, "zh-CN"),
        PrefDefinition.Int(PrefKey.QualityLevel, 2),
        PrefDefinition.Bool(PrefKey.TutorialCompleted, false),
        PrefDefinition.Int(PrefKey.SelectedSaveSlot, 0)
    };

    private static readonly Dictionary<PrefKey, PrefDefinition> DefinitionsByKey = BuildDefinitionsByKey();

    public static PrefDefinition GetDefinition(PrefKey key)
    {
        if (DefinitionsByKey.TryGetValue(key, out PrefDefinition definition))
        {
            return definition;
        }

        Debug.LogError($"未注册的偏好项 key: {key}");
        return null;
    }

    private static Dictionary<PrefKey, PrefDefinition> BuildDefinitionsByKey()
    {
        Dictionary<PrefKey, PrefDefinition> result = new Dictionary<PrefKey, PrefDefinition>(Definitions.Length);

        for (int i = 0; i < Definitions.Length; i++)
        {
            PrefDefinition definition = Definitions[i];
            if (result.ContainsKey(definition.Key))
            {
                Debug.LogError($"重复偏好项 key: {definition.Key}");
                continue;
            }

            result.Add(definition.Key, definition);
        }

        return result;
    }
}
