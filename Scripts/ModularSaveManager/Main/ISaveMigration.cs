/// <summary>
/// 单个模块的一段版本迁移器。
///
/// 迁移器的职责非常窄：只把某个模块从 FromVersion 的 JSON 转成 ToVersion 的 JSON。
/// ModularSaveManager 不理解业务结构，只负责按版本顺序串起多个迁移器。
///
/// 推荐写法：
/// - 每次只迁移一个版本段，例如 v1 -> v2、v2 -> v3。
/// - 旧版本数据类型可以只写在迁移器文件里。
/// - 返回值必须是目标版本的数据 JSON。
/// </summary>
public interface ISaveMigration
{
    /// <summary>
    /// 迁移器对应的模块 Key，必须和 SaveModule.Key 完全一致。
    /// </summary>
    string Key { get; }

    /// <summary>
    /// 能处理的旧数据版本。
    /// </summary>
    int FromVersion { get; }

    /// <summary>
    /// 迁移后的新数据版本。
    /// </summary>
    int ToVersion { get; }

    /// <summary>
    /// 执行迁移。
    ///
    /// oldJson 是 FromVersion 对应的模块 JSON。
    /// 返回值必须是 ToVersion 对应的模块 JSON。
    /// </summary>
    string Migrate(string oldJson);
}

/// <summary>
/// 迁移器基类。
///
/// 如果迁移器继承这个类，并打上 [SaveMigration] 标签，就不用手写 Key / FromVersion / ToVersion。
/// 只需要实现 Migrate。
/// </summary>
public abstract class SaveMigration : ISaveMigration
{
    private SaveMigrationAttribute _cachedAttribute;

    public string Key
    {
        get
        {
            SaveMigrationAttribute attribute = GetSaveMigrationAttribute();
            return attribute != null ? attribute.Key : string.Empty;
        }
    }

    public int FromVersion
    {
        get
        {
            SaveMigrationAttribute attribute = GetSaveMigrationAttribute();
            return attribute != null ? attribute.FromVersion : 0;
        }
    }

    public int ToVersion
    {
        get
        {
            SaveMigrationAttribute attribute = GetSaveMigrationAttribute();
            return attribute != null ? attribute.ToVersion : 0;
        }
    }

    public abstract string Migrate(string oldJson);

    private SaveMigrationAttribute GetSaveMigrationAttribute()
    {
        if (_cachedAttribute == null)
        {
            _cachedAttribute = (SaveMigrationAttribute)System.Attribute.GetCustomAttribute(GetType(), typeof(SaveMigrationAttribute));
        }

        return _cachedAttribute;
    }
}
