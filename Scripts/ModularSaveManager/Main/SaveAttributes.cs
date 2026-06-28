using System;

/// <summary>
/// 标记一个类是可自动注册的存档模块。
///
/// 推荐写法：
/// [SaveModule("player", 2)]
/// public sealed class PlayerSaveModule : SaveModule
/// {
///     public int coins;
/// }
///
/// key 是模块稳定 ID，version 是当前模块数据结构版本。
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class SaveModuleAttribute : Attribute
{
    public string Key { get; }
    public int Version { get; }

    public SaveModuleAttribute(string key, int version)
    {
        Key = key;
        Version = version;
    }
}

/// <summary>
/// 标记一个类是可自动注册的存档迁移器。
///
/// 推荐写法：
/// [SaveMigration("player", 1, 2)]
/// public sealed class PlayerV1ToV2Migration : SaveMigration
/// {
///     public override string Migrate(string oldJson) { ... }
/// }
///
/// 自动注册时会扫描所有带这个标签、并实现 ISaveMigration 的类型。
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class SaveMigrationAttribute : Attribute
{
    public string Key { get; }
    public int FromVersion { get; }
    public int ToVersion { get; }

    public SaveMigrationAttribute(string key, int fromVersion, int toVersion)
    {
        Key = key;
        FromVersion = fromVersion;
        ToVersion = toVersion;
    }
}
