using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using LitJson;
using UnityEngine;

/// <summary>
/// 模块化存档管理器。
///
/// 这个类是整套存档系统的唯一高层入口：
/// - 负责注册和查询运行时 SaveModule。
/// - 负责注册模块版本迁移器 ISaveMigration。
/// - 负责按存档槽读取/写入 JSON 文件。
/// - 负责防抖保存、暂停保存、退出保存。
/// - 负责受控 PlayerPrefs 偏好项的读写。
///
/// 推荐启动顺序：
/// 1. 场景里放一个挂有 ModularSaveManager 的 GameObject。
/// 2. 在自己的 Bootstrap 脚本里先 RegisterModule / RegisterMigration。
/// 3. 注册完成后调用 LoadSlot。
/// 4. 游戏运行中修改模块数据后调用 MarkDirty，关键节点调用 Flush。
/// </summary>
public sealed class ModularSaveManager : MonoBehaviour
{
    // PlayerPrefs 的真实存储前缀，避免和项目里其他 PlayerPrefs key 撞名。
    private const string PrefStoragePrefix = "prefs.";

    // 存档文件名由前缀 + 槽位 + 扩展名组成，例如 save_slot_0.json。
    private const string SaveFilePrefix = "save_slot_";
    private const string SaveFileExtension = ".json";

    /// <summary>
    /// 当前存活的管理器实例。
    /// 这套系统允许通过场景挂载使用，也允许业务脚本缓存引用使用。
    /// </summary>
    public static ModularSaveManager Instance { get; private set; }

    /// <summary>存档管理器实例创建完成事件。</summary>
    public static event Action<ModularSaveManager> InstanceReady;

    /// <summary>全部存档模块加载完成事件。</summary>
    public event Action OnAllModulesLoaded;

    [Header("存档文件")] [SerializeField, Min(0), Tooltip("当前读写的存档槽编号。")]
    private int activeSlot = 0;


    [Tooltip("移动端切后台、桌面端退出、对象销毁时是否自动保存。")] [SerializeField]
    private bool autoSaveOnPauseAndQuit = true;

    [Header("加密")] [Tooltip("是否加密 JSON 存档文件。关闭时写入明文 JSON，开启时写入 SMENC1 加密文本。")] [SerializeField]
    private bool encryptSaveFile = false;

    [Tooltip("存档加密密钥。加密后必须使用同一密钥解密，正式项目里不要使用示例默认值。")] [SerializeField]
    private string encryptionKey = "your-game-save-key";

    [Header("写入策略")] [Tooltip("开启后 MarkDirty 不会马上写文件，而是在短时间没有新写入后统一保存。")] [SerializeField]
    private bool useDebouncedSave = true;

    [Tooltip("最后一次 MarkDirty 后等待多少秒再保存。")] [SerializeField, Min(0.1f)]
    private float saveDelaySeconds = 1.0f;

    [Tooltip("即使一直高频 MarkDirty，脏数据最多允许停留多少秒。")] [SerializeField, Min(1.0f)]
    private float maxDirtySeconds = 10.0f;

    // 按模块稳定 key 查找模块。读文件时主要靠这个字典匹配 SaveModuleEntry.key。
    private readonly Dictionary<string, SaveModule> _modulesByKey = new Dictionary<string, SaveModule>(32);

    // 按模块运行时类型查找模块。业务代码 GetModule<T> / TryGetModule<T> 主要靠这个字典。
    private readonly Dictionary<Type, SaveModule> _modulesByType = new Dictionary<Type, SaveModule>(32);

    // 按模块 key 保存迁移链。每个 key 下可以有 v1->v2、v2->v3 等多段迁移器。
    private readonly Dictionary<string, List<ISaveMigration>> _migrationsByKey =
        new Dictionary<string, List<ISaveMigration>>(32);

    // 是否已经完成过一次加载或保存。用于 Flush 在尚未加载时也能写出默认存档。
    private bool _loaded;

    // 是否有尚未落盘的数据。MarkDirty 会置 true，成功保存后会清掉。
    private bool _dirty;

    // 当前这一轮脏数据第一次出现的时间，用来实现 maxDirtySeconds。
    private float _firstDirtyAt;

    // 最近一次 MarkDirty 的时间，用来实现 saveDelaySeconds。
    private float _lastSetAt;

    // 当前正在运行的防抖保存协程。为空表示没有等待中的延迟保存。
    private Coroutine _saveRoutine;

    /// <summary>
    /// 当前正在使用的存档槽。
    /// </summary>
    public int ActiveSlot => activeSlot;

    /// <summary>
    /// 是否已经执行过 LoadSlot 或 SaveNow。
    /// </summary>
    public bool IsLoaded => _loaded;

    /// <summary>
    /// 当前内存模块是否有尚未落盘的数据。
    /// </summary>
    public bool IsDirty => _dirty;

    /// <summary>
    /// 当前是否开启存档文件加密。
    /// </summary>
    public bool EncryptSaveFile => encryptSaveFile;

    /// <summary>
    /// 已注册的存档模块数量。
    /// </summary>
    public int RegisteredModuleCount => _modulesByKey.Count;

    /// <summary>
    /// 已注册的迁移器总数。
    /// </summary>
    public int RegisteredMigrationCount
    {
        get
        {
            int count = 0;
            foreach (List<ISaveMigration> migrations in _migrationsByKey.Values)
            {
                count += migrations.Count;
            }

            return count;
        }
    }

    /// <summary>
    /// 当前槽位对应的存档文件路径。
    /// </summary>
    public string ActiveSlotPath => GetSlotPath(activeSlot);

    private void Awake()
    {
        // 保证运行时只有一个存档管理器，避免多个管理器同时写同一个存档文件。
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
        InstanceReady?.Invoke(this);
    }

    private void OnApplicationPause(bool paused)
    {
        // 移动端切后台后进程可能被系统杀掉，所以暂停时尽量立刻落盘。
        if (paused && autoSaveOnPauseAndQuit)
        {
            Flush();
        }
    }

    private void OnApplicationQuit()
    {
        // 桌面端正常退出时兜底保存。
        if (autoSaveOnPauseAndQuit)
        {
            Flush();
        }
    }

    private void OnDestroy()
    {
        // PlayMode 停止、对象销毁或场景清理时，尽量不丢掉内存里的脏数据。
        if (Instance == this)
        {
            Flush();
            Instance = null;
        }
    }

    /// <summary>
    /// 自动注册所有带标签的存档模块和迁移器。
    ///
    /// 会扫描当前 AppDomain 中已经加载的程序集：
    /// - 带 [SaveModule] 且继承 SaveModule 的非抽象类，会被自动 new 出来并注册。
    /// - 带 [SaveMigration] 且实现 ISaveMigration 的非抽象类，会被自动 new 出来并注册。
    ///
    /// 注意：
    /// 自动注册依赖反射，所以模块和迁移器必须有无参构造函数。
    /// 建议在游戏启动阶段调用一次，然后再 LoadSlot。
    /// </summary>
    public void AutoRegisterAll()
    {
        AutoRegisterModules();
        AutoRegisterMigrations();
    }

    /// <summary>
    /// 自动注册所有带 [SaveModule] 标签的数据模块。
    /// </summary>
    public void AutoRegisterModules()
    {
        Type[] types = RuntimeTypeUtility.GetAllRuntimeTypes(256);
        for (int i = 0; i < types.Length; i++)
        {
            Type type = types[i];
            if (!IsConcreteType(type) || !typeof(SaveModule).IsAssignableFrom(type))
            {
                continue;
            }

            SaveModuleAttribute attribute =
                (SaveModuleAttribute)Attribute.GetCustomAttribute(type, typeof(SaveModuleAttribute));
            if (attribute == null)
            {
                continue;
            }

            if (type.GetConstructor(Type.EmptyTypes) == null)
            {
                Debug.LogError($"自动注册存档模块失败: 类型缺少无参构造函数 type={type.FullName}");
                continue;
            }

            SaveModule module = (SaveModule)Activator.CreateInstance(type);
            RegisterModule(module);
        }
    }

    /// <summary>
    /// 自动注册所有带 [SaveMigration] 标签的迁移器。
    /// </summary>
    public void AutoRegisterMigrations()
    {
        Type[] types = RuntimeTypeUtility.GetAllRuntimeTypes(256);
        for (int i = 0; i < types.Length; i++)
        {
            Type type = types[i];
            if (!IsConcreteType(type) || !typeof(ISaveMigration).IsAssignableFrom(type))
            {
                continue;
            }

            SaveMigrationAttribute attribute =
                (SaveMigrationAttribute)Attribute.GetCustomAttribute(type, typeof(SaveMigrationAttribute));
            if (attribute == null)
            {
                continue;
            }

            if (type.GetConstructor(Type.EmptyTypes) == null)
            {
                Debug.LogError($"自动注册存档迁移器失败: 类型缺少无参构造函数 type={type.FullName}");
                continue;
            }

            ISaveMigration migration = (ISaveMigration)Activator.CreateInstance(type);
            RegisterMigration(migration);
        }
    }

    /// <summary>
    /// 注册一个正式存档模块。
    ///
    /// 模块注册后，SaveManager 才知道：
    /// - 这个模块的稳定 key 是什么。
    /// - 这个模块当前代码版本是多少。
    /// - 这个模块应该反序列化成哪个 C# 类型。
    ///
    /// 同一个 Key 或同一个类型只能注册一次。
    /// </summary>
    public void RegisterModule<T>(T module) where T : SaveModule
    {
        if (module == null)
        {
            Debug.LogError("注册存档模块失败: 模块不能为空");
            return;
        }

        if (!ValidateModule(module))
        {
            return;
        }

        Type type = module.GetType();
        if (_modulesByKey.ContainsKey(module.Key))
        {
            Debug.LogError($"重复存档模块 key: {module.Key}");
            return;
        }

        if (_modulesByType.ContainsKey(type))
        {
            Debug.LogError($"重复存档模块类型: {type.FullName}");
            return;
        }

        Debug.Log($"数据层注册: {type.FullName}");
        module.BindSaveValues(MarkDirty);
        _modulesByKey.Add(module.Key, module);
        _modulesByType.Add(type, module);
    }

    /// <summary>
    /// 注册一个模块版本迁移器。
    ///
    /// 迁移器只负责某个模块的某一段版本升级，例如 player v1 -> v2。
    /// 如果存档里是 v1，当前模块是 v3，管理器会尝试串起 v1 -> v2 -> v3。
    /// </summary>
    public void RegisterMigration(ISaveMigration migration)
    {
        if (migration == null)
        {
            Debug.LogError("注册存档迁移器失败: 迁移器不能为空");
            return;
        }

        if (string.IsNullOrWhiteSpace(migration.Key))
        {
            Debug.LogError("注册存档迁移器失败: 迁移器 key 不能为空");
            return;
        }

        if (migration.ToVersion <= migration.FromVersion)
        {
            Debug.LogError($"注册存档迁移器失败: 版本范围无效 {migration.Key} v{migration.FromVersion} -> v{migration.ToVersion}");
            return;
        }

        if (!_migrationsByKey.TryGetValue(migration.Key, out List<ISaveMigration> migrations))
        {
            // 第一次注册该模块的迁移器时创建迁移列表。
            migrations = new List<ISaveMigration>();
            _migrationsByKey.Add(migration.Key, migrations);
        }

        for (int i = 0; i < migrations.Count; i++)
        {
            ISaveMigration existing = migrations[i];
            if (existing.FromVersion == migration.FromVersion && existing.ToVersion == migration.ToVersion)
            {
                Debug.LogError($"重复存档迁移器: {migration.Key} v{migration.FromVersion} -> v{migration.ToVersion}");
                return;
            }
        }

        migrations.Add(migration);

        // 保持迁移器按 FromVersion 排序，方便后续调试和查看。
        migrations.Sort((a, b) => a.FromVersion.CompareTo(b.FromVersion));
    }

    /// <summary>
    /// 按类型获取已注册模块。
    /// 这是业务代码最常用的读取入口。
    /// </summary>
    public T GetModule<T>() where T : SaveModule
    {
        if (_modulesByType.TryGetValue(typeof(T), out SaveModule module))
        {
            return (T)module;
        }

        Debug.LogError($"未注册的存档模块类型: {typeof(T).FullName}");
        return null;
    }

    /// <summary>
    /// 尝试按类型获取模块，不存在时返回 false。
    /// </summary>
    public bool TryGetModule<T>(out T module) where T : SaveModule
    {
        if (_modulesByType.TryGetValue(typeof(T), out SaveModule saveModule))
        {
            module = (T)saveModule;
            return true;
        }

        module = null;
        return false;
    }

    /// <summary>
    /// 尝试按稳定 key 获取模块。
    /// 适合调试面板、通用工具、或只知道字符串 key 的场景。
    /// </summary>
    public bool TryGetModule(string key, out SaveModule module)
    {
        return _modulesByKey.TryGetValue(key, out module);
    }

    /// <summary>
    /// 获取当前已注册模块的只读快照。
    /// 调试面板或通用工具可以用它显示模块列表，不应该修改返回的模块对象。
    /// </summary>
    public List<SaveModule> GetRegisteredModulesSnapshot()
    {
        return new List<SaveModule>(_modulesByKey.Values);
    }

    /// <summary>
    /// 获取当前已注册迁移器的只读快照。
    /// </summary>
    public List<ISaveMigration> GetRegisteredMigrationsSnapshot()
    {
        List<ISaveMigration> result = new List<ISaveMigration>(RegisteredMigrationCount);
        foreach (List<ISaveMigration> migrations in _migrationsByKey.Values)
        {
            result.AddRange(migrations);
        }

        return result;
    }

    /// <summary>
    /// 读取指定存档槽。
    ///
    /// 读取前会先调用所有模块的 ResetData，确保缺失模块也能回到默认状态。
    /// 读取成功后会调用所有模块的 OnAfterLoad。
    /// </summary>
    public void LoadSlot(int slot = 0)
    {
        if (slot < 0)
        {
            Debug.LogError($"读取存档槽失败: 槽位不能为负数 {slot}");
            return;
        }

        activeSlot = slot;
        ResetAllModules();

        string path = GetSlotPath(slot);
        if (!File.Exists(path))
        {
            TryRestoreBackup(path);
        }

        if (File.Exists(path))
        {
            try
            {
                SaveFile file = JsonMapper.ToObject<SaveFile>(ReadSaveContent(path));
                ApplySaveFile(file);
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"存档文件读取失败，尝试恢复备份: path={path}, error={exception.Message}");
                if (TryRestoreBackup(path))
                {
                    try
                    {
                        SaveFile backupFile = JsonMapper.ToObject<SaveFile>(ReadSaveContent(path));
                        ApplySaveFile(backupFile);
                    }
                    catch (Exception backupException)
                    {
                        Debug.LogWarning($"备份存档文件读取失败: path={path}, error={backupException.Message}");
                    }
                }
            }
        }

        ClearDirtyState();
        _loaded = true;
        if (RepairAndValidateAllModules())
        {
            MarkDirty();
        }

        NotifyAllModulesLoaded();
    }

    /// <summary>
    /// 切换到指定存档槽并立即保存当前内存模块。
    /// 如果只是想切换并读取另一个槽，应该调用 LoadSlot。
    /// </summary>
    public void SaveSlot(int slot)
    {
        if (slot < 0)
        {
            Debug.LogError($"保存存档槽失败: 槽位不能为负数 {slot}");
            return;
        }

        activeSlot = slot;
        SaveNow();
    }

    /// <summary>
    /// 立即把所有已注册模块写入当前槽。
    /// 即使没有 MarkDirty，也会执行一次完整写入。
    /// </summary>
    public void SaveNow()
    {
        if (!_loaded)
        {
            _loaded = true;
        }

        string path = GetSlotPath(activeSlot);
        SaveFile file = BuildSaveFile();

        try
        {
            if (WriteSaveFile(path, file))
            {
                ClearDirtyState();
            }
        }
        catch (Exception exception)
        {
            Debug.LogError($"存档文件写入失败: path={path}, error={exception.Message}");
        }
    }

    /// <summary>
    /// 如果当前有脏数据，立即保存。
    /// 如果还没有加载过，也会写出一次当前模块默认数据。
    /// </summary>
    public void Flush()
    {
        if (_dirty || !_loaded)
        {
            SaveNow();
        }
    }

    /// <summary>
    /// 标记模块数据已经被业务修改。
    /// 开启防抖时不会马上写文件，而是等待一小段时间再保存。
    /// </summary>
    public void MarkDirty()
    {
        _dirty = true;

        float now = Time.unscaledTime;
        _lastSetAt = now;
        if (_firstDirtyAt <= 0f)
        {
            _firstDirtyAt = now;
        }

        if (!useDebouncedSave)
        {
            SaveNow();
            return;
        }

        if (_saveRoutine == null)
        {
            _saveRoutine = StartCoroutine(DebouncedSaveLoop());
        }
    }

    /// <summary>
    /// 删除指定存档槽的正式文件、临时文件和备份文件。
    /// 删除当前槽时，会把内存模块重置成默认状态。
    /// </summary>
    public void DeleteSlot(int slot)
    {
        if (slot < 0)
        {
            Debug.LogError($"删除存档槽失败: 槽位不能为负数 {slot}");
            return;
        }

        string path = GetSlotPath(slot);
        DeleteIfExists(path);
        DeleteIfExists(string.Concat(path, ".tmp"));
        DeleteIfExists(string.Concat(path, ".bak"));

        if (slot == activeSlot)
        {
            ResetAllModules();
            _loaded = true;
            ClearDirtyState();
            NotifyAllModulesLoaded();
        }
    }

    /// <summary>
    /// 获取某个存档槽对应的完整文件路径。
    /// 可用于调试输出，方便找到 save_slot_x.json。
    /// </summary>
    public string GetSlotPath(int slot = 0)
    {
        string fileName = string.Concat(SaveFilePrefix, slot.ToString(CultureInfo.InvariantCulture), SaveFileExtension);
        return Path.Combine(Application.persistentDataPath, fileName);
    }

    /// <summary>
    /// 读取受控 int 偏好项。
    /// 如果 PrefKey 没有注册，或者注册类型不是 int，会打印中文错误并返回默认值。
    /// </summary>
    public int GetIntPref(PrefKey key)
    {
        PrefDefinition definition = Prefs.GetDefinition(key);
        if (!EnsurePrefType(definition, PrefValueType.Int))
        {
            return 0;
        }

        return PlayerPrefs.GetInt(ToPrefStorageKey(key), definition.IntDefault);
    }

    public void SetIntPref(PrefKey key, int value)
    {
        PrefDefinition definition = Prefs.GetDefinition(key);
        if (!EnsurePrefType(definition, PrefValueType.Int))
        {
            return;
        }

        PlayerPrefs.SetInt(ToPrefStorageKey(key), value);
        PlayerPrefs.Save();
    }

    /// <summary>
    /// 读取受控 float 偏好项。
    /// 类型不匹配或未注册时返回 0。
    /// </summary>
    public float GetFloatPref(PrefKey key)
    {
        PrefDefinition definition = Prefs.GetDefinition(key);
        if (!EnsurePrefType(definition, PrefValueType.Float))
        {
            return 0f;
        }

        return PlayerPrefs.GetFloat(ToPrefStorageKey(key), definition.FloatDefault);
    }

    /// <summary>
    /// 写入受控 float 偏好项。
    /// 类型不匹配或未注册时只打印错误，不写入 PlayerPrefs。
    /// </summary>
    public void SetFloatPref(PrefKey key, float value)
    {
        PrefDefinition definition = Prefs.GetDefinition(key);
        if (!EnsurePrefType(definition, PrefValueType.Float))
        {
            return;
        }

        PlayerPrefs.SetFloat(ToPrefStorageKey(key), value);
        PlayerPrefs.Save();
    }

    /// <summary>
    /// 读取受控 bool 偏好项。
    /// PlayerPrefs 没有 bool 类型，所以内部用 0/1 int 表示。
    /// </summary>
    public bool GetBoolPref(PrefKey key)
    {
        PrefDefinition definition = Prefs.GetDefinition(key);
        if (!EnsurePrefType(definition, PrefValueType.Bool))
        {
            return false;
        }

        return PlayerPrefs.GetInt(ToPrefStorageKey(key), definition.BoolDefault ? 1 : 0) != 0;
    }

    /// <summary>
    /// 写入受控 bool 偏好项。
    /// 内部会转成 0/1 int 存入 PlayerPrefs。
    /// </summary>
    public void SetBoolPref(PrefKey key, bool value)
    {
        PrefDefinition definition = Prefs.GetDefinition(key);
        if (!EnsurePrefType(definition, PrefValueType.Bool))
        {
            return;
        }

        PlayerPrefs.SetInt(ToPrefStorageKey(key), value ? 1 : 0);
        PlayerPrefs.Save();
    }

    /// <summary>
    /// 读取受控 string 偏好项。
    /// 类型不匹配或未注册时返回空字符串。
    /// </summary>
    public string GetStringPref(PrefKey key)
    {
        PrefDefinition definition = Prefs.GetDefinition(key);
        if (!EnsurePrefType(definition, PrefValueType.String))
        {
            return string.Empty;
        }

        return PlayerPrefs.GetString(ToPrefStorageKey(key), definition.StringDefault);
    }

    /// <summary>
    /// 写入受控 string 偏好项。
    /// 传入 null 时会按空字符串保存。
    /// </summary>
    public void SetStringPref(PrefKey key, string value)
    {
        PrefDefinition definition = Prefs.GetDefinition(key);
        if (!EnsurePrefType(definition, PrefValueType.String))
        {
            return;
        }

        PlayerPrefs.SetString(ToPrefStorageKey(key), value ?? string.Empty);
        PlayerPrefs.Save();
    }

    /// <summary>
    /// 删除单个受控偏好项。
    /// 下次读取时会回到 Prefs.Definitions 里配置的默认值。
    /// </summary>
    public void ResetPref(PrefKey key)
    {
        if (Prefs.GetDefinition(key) == null)
        {
            return;
        }

        PlayerPrefs.DeleteKey(ToPrefStorageKey(key));
        PlayerPrefs.Save();
    }

    /// <summary>
    /// 删除所有受控偏好项。
    /// 不会调用 PlayerPrefs.DeleteAll，避免误删其他系统的数据。
    /// </summary>
    public void ResetAllPrefs()
    {
        foreach (PrefDefinition definition in Prefs.Definitions)
        {
            PlayerPrefs.DeleteKey(ToPrefStorageKey(definition.Key));
        }

        PlayerPrefs.Save();
    }

    private IEnumerator DebouncedSaveLoop()
    {
        // 只要仍然有脏数据，就持续检查是否满足保存条件。
        while (_dirty)
        {
            float now = Time.unscaledTime;
            bool delayedEnough = now - _lastSetAt >= saveDelaySeconds;
            bool dirtyTooLong = now - _firstDirtyAt >= maxDirtySeconds;

            if (delayedEnough || dirtyTooLong)
            {
                // 达到防抖等待时间，或脏数据停留太久，就立即写入文件。
                SaveNow();
                break;
            }

            yield return null;
        }

        _saveRoutine = null;
    }

    private SaveFile BuildSaveFile()
    {
        // SaveFile 是落盘文件的外壳；每个模块会被打包成一个 SaveModuleEntry。
        SaveFile file = new SaveFile
        {
            saveVersion = 1,
            slotId = activeSlot,
            updatedAtUtc = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        };

        foreach (SaveModule module in _modulesByKey.Values)
        {
            // 给模块一次机会在序列化前同步运行时缓存。
            module.OnBeforeSave();
            Type moduleType = module.GetType();

            file.modules.Add(new SaveModuleEntry
            {
                key = module.Key,
                type = moduleType.FullName,
                version = module.Version,
                json = JsonMapper.ToJson(module)
            });
        }

        return file;
    }

    private void ApplySaveFile(SaveFile file)
    {
        // 空文件或结构异常时直接保持 ResetData 后的默认数据。
        if (file?.modules == null)
        {
            return;
        }

        for (int i = 0; i < file.modules.Count; i++)
        {
            SaveModuleEntry entry = file.modules[i];
            if (entry == null || string.IsNullOrWhiteSpace(entry.key))
            {
                continue;
            }

            if (!_modulesByKey.TryGetValue(entry.key, out SaveModule registeredModule))
            {
                // 文件中存在当前代码不认识的模块，通常是模块被删除或版本回退。
                Debug.LogWarning($"跳过未知存档模块: key={entry.key}");
                continue;
            }

            if (!string.IsNullOrEmpty(entry.type) && entry.type != registeredModule.GetType().FullName)
            {
                Debug.LogWarning(
                    $"存档模块类型发生变化: key={entry.key}, 文件类型={entry.type}, 当前类型={registeredModule.GetType().FullName}");
            }

            string json = entry.json ?? string.Empty;
            int fileVersion = Mathf.Max(0, entry.version);
            int targetVersion = registeredModule.Version;

            if (fileVersion > targetVersion)
            {
                // 文件版本比当前代码更新时不做降级读取，避免用旧代码破坏新数据。
                Debug.LogWarning($"存档模块版本高于当前代码版本，已跳过: key={entry.key}, 文件版本={fileVersion}, 当前版本={targetVersion}");
                continue;
            }

            try
            {
                if (fileVersion < targetVersion)
                {
                    // 文件版本落后时，先跑迁移链，再反序列化成当前模块类型。
                    json = MigrateJson(entry.key, fileVersion, targetVersion, json);
                }

                SaveModule loadedModule = (SaveModule)DeserializeJson(json, registeredModule.GetType());
                if (!ValidateModule(loadedModule))
                {
                    continue;
                }

                loadedModule.BindSaveValues(MarkDirty);
                _modulesByKey[entry.key] = loadedModule;
                _modulesByType[registeredModule.GetType()] = loadedModule;
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"存档模块读取失败: key={entry.key}, error={exception.Message}");
            }
        }
    }

    private bool RepairAndValidateAllModules()
    {
        bool changed = false;
        foreach (SaveModule module in _modulesByKey.Values)
        {
            if (!RepairAndValidateLoadedModule(module))
            {
                changed = true;
            }
        }

        return changed || _dirty;
    }

    private bool RepairAndValidateLoadedModule(SaveModule module)
    {
        if (module == null)
        {
            return false;
        }

        try
        {
            module.ClearSaveValueBindings();
            if (module.RepairAfterLoad())
            {
                _dirty = true;
            }

            module.BindSaveValues(MarkDirty);
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"存档模块修复异常，已重置为默认数据: key={module.Key}, error={exception.Message}");
            module.ResetData();
            module.BindSaveValues(MarkDirty);
            _dirty = true;
            return false;
        }

        try
        {
            if (module.ValidateAfterLoad(out string error))
            {
                return true;
            }

            Debug.LogWarning($"存档模块校验失败，已重置为默认数据: key={module.Key}, error={error}");
            module.ResetData();
            module.BindSaveValues(MarkDirty);
            _dirty = true;
            return false;
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"存档模块校验异常，已重置为默认数据: key={module.Key}, error={exception.Message}");
            module.ResetData();
            module.BindSaveValues(MarkDirty);
            _dirty = true;
            return false;
        }
    }

    private string MigrateJson(string key, int fromVersion, int targetVersion, string json)
    {
        int currentVersion = fromVersion;
        string currentJson = json;

        // 逐段寻找迁移器，例如 v1 -> v2 -> v3。
        while (currentVersion < targetVersion)
        {
            ISaveMigration migration = FindMigration(key, currentVersion);
            if (migration == null)
            {
                Debug.LogError($"缺少存档迁移器: {key} v{currentVersion} -> v{targetVersion}");
                return currentJson;
            }

            currentJson = migration.Migrate(currentJson);
            currentVersion = migration.ToVersion;
        }

        return currentJson;
    }

    private ISaveMigration FindMigration(string key, int fromVersion)
    {
        if (!_migrationsByKey.TryGetValue(key, out List<ISaveMigration> migrations))
        {
            return null;
        }

        for (int i = 0; i < migrations.Count; i++)
        {
            ISaveMigration migration = migrations[i];
            if (migration.FromVersion == fromVersion)
            {
                return migration;
            }
        }

        return null;
    }

    private void ResetAllModules()
    {
        // LoadSlot 前先重置所有模块，保证新模块或缺失模块也能得到默认值。
        foreach (SaveModule module in _modulesByKey.Values)
        {
            module.ResetData();
            module.BindSaveValues(MarkDirty);
        }
    }

    private void NotifyAllModulesLoaded()
    {
        foreach (SaveModule module in _modulesByKey.Values)
        {
            module.OnAfterLoad();
        }

        OnAllModulesLoaded?.Invoke();
    }

    private bool WriteSaveFile(string path, SaveFile file)
    {
        try
        {
            // 确保 persistentDataPath 对应目录存在。
            string directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            string json = JsonMapper.ToJson(file);
            string content = BuildSaveContent(json);
            string tempPath = string.Concat(path, ".tmp");
            string backupPath = string.Concat(path, ".bak");

            // 先写 tmp，避免直接覆盖正式文件时写到一半导致正式档损坏。
            File.WriteAllText(tempPath, content);
            if (File.Exists(path))
            {
                // 写新档前保留上一份正式档作为 bak。
                File.Copy(path, backupPath, true);
            }

            if (File.Exists(path))
            {
                File.Delete(path);
            }

            // 最后把 tmp 移动为正式文件。
            File.Move(tempPath, path);
            return true;
        }
        catch (Exception exception)
        {
            // 写入失败时尝试把 bak 恢复成正式档，至少保住上一份可用数据。
            string backupPath = string.Concat(path, ".bak");
            if (File.Exists(backupPath))
            {
                File.Copy(backupPath, path, true);
            }

            Debug.LogError($"存档文件写入失败: path={path}, error={exception.Message}");
            return false;
        }
    }

    /// <summary>
    /// 读取存档文件内容，并在需要时自动解密。
    ///
    /// 兼容策略：
    /// - 文件以 SMENC1: 开头时，认为它是加密存档，必须使用 encryptionKey 解密。
    /// - 文件不是加密格式时，认为它是旧版明文 JSON，直接返回。
    /// </summary>
    private string ReadSaveContent(string path)
    {
        string content = File.ReadAllText(path);
        if (!SaveCrypto.IsEncryptedText(content))
        {
            return content;
        }

        if (SaveCrypto.TryDecrypt(content, encryptionKey, out string json))
        {
            return json;
        }

        Debug.LogError($"存档文件解密失败: path={path}");
        return string.Empty;
    }

    /// <summary>
    /// 根据当前加密配置，把 JSON 明文转换成最终写入文件的内容。
    ///
    /// encryptSaveFile 关闭时直接写 JSON，方便调试。
    /// encryptSaveFile 开启时写加密文本，玩家打开文件不会直接看到 JSON 字段。
    /// </summary>
    private string BuildSaveContent(string json)
    {
        if (!encryptSaveFile)
        {
            return json;
        }

        string encrypted = SaveCrypto.Encrypt(json, encryptionKey);
        return encrypted;
        //return SaveCrypto.Encrypt(json, encryptionKey);
    }

    private bool TryRestoreBackup(string path)
    {
        // 正式文件不存在或读取失败时，用 .bak 尝试恢复。
        string backupPath = string.Concat(path, ".bak");
        if (!File.Exists(backupPath))
        {
            return false;
        }

        File.Copy(backupPath, path, true);
        return true;
    }

    private void ClearDirtyState()
    {
        // 保存成功或重新加载后，清理防抖状态。
        _dirty = false;
        _firstDirtyAt = 0f;
        _lastSetAt = 0f;

        if (_saveRoutine != null)
        {
            StopCoroutine(_saveRoutine);
            _saveRoutine = null;
        }
    }

    private static bool ValidateModule(SaveModule module)
    {
        // 注册和读取反序列化结果时都走这里，统一检查模块基本合法性。
        if (module == null)
        {
            Debug.LogError("存档模块校验失败: 模块不能为空");
            return false;
        }

        if (string.IsNullOrWhiteSpace(module.Key))
        {
            Debug.LogError("存档模块校验失败: 模块 key 不能为空");
            return false;
        }

        if (module.Version < 0)
        {
            Debug.LogError($"存档模块校验失败: 模块版本不能为负数 key={module.Key}, version={module.Version}");
            return false;
        }

        return true;
    }

    private static string ToPrefStorageKey(PrefKey key)
    {
        // 业务层只暴露 PrefKey，真实 PlayerPrefs 字符串 key 在这里统一生成。
        return string.Concat(PrefStoragePrefix, key.ToString());
    }

    private static bool EnsurePrefType(PrefDefinition definition, PrefValueType expected)
    {
        // 防止同一个 PrefKey 被按错误类型读写，例如把 MusicVolume 当成 int。
        if (definition == null)
        {
            Debug.LogError($"偏好项类型校验失败: 定义不存在, 期望类型={expected}");
            return false;
        }

        if (definition.ValueType != expected)
        {
            Debug.LogError($"偏好项类型错误: {definition.Key} 实际类型={definition.ValueType}, 期望类型={expected}");
            return false;
        }

        return true;
    }

    private static void DeleteIfExists(string path)
    {
        // 删除前先判断存在，避免 File.Delete 因路径不存在产生不必要的不确定性。
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private static bool IsConcreteType(Type type)
    {
        return type != null && type.IsClass && !type.IsAbstract && !type.IsGenericTypeDefinition;
    }

    private static object DeserializeJson(string json, Type type)
    {
        // LitJson 常用泛型 ToObject<T>(string)，这里用反射把运行时 Type 转成泛型调用。
        MethodInfo[] methods = typeof(JsonMapper).GetMethods(BindingFlags.Public | BindingFlags.Static);
        for (int i = 0; i < methods.Length; i++)
        {
            MethodInfo method = methods[i];
            if (method.Name != "ToObject" || !method.IsGenericMethodDefinition)
            {
                continue;
            }

            ParameterInfo[] parameters = method.GetParameters();
            if (parameters.Length == 1 && parameters[0].ParameterType == typeof(string))
            {
                MethodInfo genericMethod = method.MakeGenericMethod(type);
                return genericMethod.Invoke(null, new object[] { json });
            }
        }

        Debug.LogError("LitJson 反序列化失败: 找不到 JsonMapper.ToObject<T>(string)");
        return null;
    }
}
