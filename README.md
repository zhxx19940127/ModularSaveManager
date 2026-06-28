# Modular Save Manager and Server Manager

## 项目简介

这套脚本提供了一套 Unity 用的模块化存档系统和服务管理系统。

核心思路是把数据和业务分开：

- `SaveModule` 负责保存可序列化数据。
- `ModularSaveManager` 负责注册模块、读档、写档、迁移、加密和 PlayerPrefs 白名单。
- `ModularServerManager` 负责注册和初始化业务服务。
- `SaveBoundManager<TModule>` 负责把某个业务服务绑定到对应的存档模块。
- `SaveValue<T>` 用于需要订阅变化、并自动标记存档脏数据的基础值。
- `SaveList<T>` / `SaveDictionary<TKey, TValue>` 用于集合增删改时自动标记存档脏数据。
- Editor 调试面板用于查看运行状态、模块列表、槽位文件和 JSON 内容。

推荐职责划分：

```text
SaveModule               只放可存储数据
Server / Manager          提供业务 API、修改规则、事件派发
SaveValue<T>              用于基础类型的自动订阅和自动 MarkDirty
SaveList / SaveDictionary  用于集合结构变化时自动 MarkDirty
普通字段 / class           用服务层方法修改，并手动 MarkSaveDirty
```

## 目录结构

```text
Scripts
├── ModularSaveManager
│   ├── Main
│   │   ├── ModularSaveManager.cs
│   │   ├── SaveModule.cs
│   │   ├── SaveValue.cs
│   │   ├── SaveAttributes.cs
│   │   ├── ISaveMigration.cs
│   │   ├── SaveFileModels.cs
│   │   ├── SavePrefs.cs
│   │   ├── SaveCrypto.cs
│   │   └── RuntimeTypeUtility.cs
│   ├── Editor
│   │   ├── ModularSaveManagerDebugWindow.cs
│   │   └── ModularSaveManagerInspector.cs
│   └── LitJson
├── ModularServerManager
│   └── Base
│       ├── ModularServerManager.cs
│       ├── ModularServerBase.cs
│       ├── SaveBoundManager.cs
│       ├── IModularServer.cs
│       └── ModularServerAttribute.cs
└── Examples
```

## 功能要点

### 模块化存档

每个业务数据块都可以写成一个 `SaveModule`：

```csharp
[SaveModule("player", 1)]
public sealed class PlayerSaveModule : SaveModule
{
    public int exp;
    public SaveValue<int> coins = new SaveValue<int>(0);
}
```

存档文件按槽位保存，一个槽位对应一个 JSON 文件：

```text
save_slot_0.json
save_slot_1.json
```

文件路径由 `Application.persistentDataPath` 和槽位号决定。

### 自动注册

存档模块使用 `[SaveModule]` 标记后，可以通过反射自动注册：

```csharp
ModularSaveManager.Instance.AutoRegisterAll();
```

业务服务使用 `[ModularServer]` 标记后，可以通过反射自动注册：

```csharp
ModularServerManager.Instance.AutoRegisterAll();
```

自动注册依赖无参构造函数。

### 读写策略

修改数据后调用：

```csharp
ModularSaveManager.Instance.MarkDirty();
```

`MarkDirty()` 不一定立刻写文件。默认开启防抖保存：

- `saveDelaySeconds`：最后一次修改后等待多久保存。
- `maxDirtySeconds`：脏数据最多停留多久必须保存。

关键节点可以调用：

```csharp
ModularSaveManager.Instance.Flush();
```

`Flush()` 会在存在脏数据时立刻保存。

### SaveValue<T>

`SaveValue<T>` 是可存档、可订阅、可自动 `MarkDirty` 的单值包装。

适合：

- `int`
- `float`
- `bool`
- `string`
- `enum`
- 其他需要整体替换并订阅变化的值

示例：

```csharp
public SaveValue<int> coins = new SaveValue<int>(0);
```

修改：

```csharp
SaveModule.coins.Set(SaveModule.coins.Value + 100);
```

订阅：

```csharp
module.coins.onValueChanged += HandleCoinsChanged;
```

注意：`SaveValue<T>` 的 JSON 结构是对象：

```json
"coins": {
  "Value": 100
}
```

如果希望 JSON 直接是：

```json
"coins": 100
```

可以继续使用普通字段：

```csharp
public int coins;
```

普通字段不会自动订阅，也不会自动 `MarkDirty`，需要服务层手动处理。

### SaveList<T> 和 SaveDictionary<TKey, TValue>

`SaveList<T>` 和 `SaveDictionary<TKey, TValue>` 是可存档、可自动 `MarkDirty` 的集合包装。

适合：

- 背包道具列表
- 已解锁关卡列表
- 道具 ID 到数量的映射
- 配置 ID 到运行时状态的映射

示例：

```csharp
public SaveList<ItemData> items = new SaveList<ItemData>();
public SaveDictionary<int, int> itemCounts = new SaveDictionary<int, int>();
```

修改集合结构时会自动标脏：

```csharp
items.Add(item);
items.RemoveAt(0);
items.Clear();

itemCounts[101] = 5;
itemCounts.Remove(101);
itemCounts.Clear();
```

JSON 结构保持接近普通集合：

```json
"items": [
  {
    "itemId": 101,
    "count": 1
  }
],
"itemCounts": {
  "101": 1,
  "102": 5
}
```

字典 key 推荐使用基础类型，例如 `string`、`int`、`long`、`enum`。JSON 对象的 key 本质是字符串，读取时会按 `TKey` 尝试转换回来。

value 可以是引用类型：

```csharp
public sealed class ItemState
{
    public int count;
    public int level;
}

public SaveDictionary<int, ItemState> itemStates = new SaveDictionary<int, ItemState>();
```

注意：如果 value 是引用类型，修改 value 内部字段不会被字典自动感知：

```csharp
itemStates[101].count += 1; // 不会自动 MarkDirty
```

这种情况建议由服务层统一封装并手动标脏：

```csharp
ItemState state = SaveModule.itemStates[101];
state.count += 1;
MarkSaveDirty();
```

### 服务绑定存档

业务服务可以继承：

```csharp
SaveBoundManager<TModule>
```

它会在存档加载完成后自动获取对应的 `SaveModule`：

```csharp
[ModularServer("player", 100)]
public sealed class PlayerManager : SaveBoundManager<PlayerSaveModule>
{
    protected override void OnModuleLoaded(PlayerSaveModule module)
    {
        module.coins.onValueChanged += HandleCoinsChanged;
    }

    public void AddCoins(int amount)
    {
        if (!HasSaveModule)
            return;

        SaveModule.coins.Set(SaveModule.coins.Value + amount);
    }
}
```

### 复杂数据修改

集合结构变化优先使用 `SaveList<T>` / `SaveDictionary<TKey, TValue>`：

```csharp
public sealed class PlayerSaveModule : SaveModule
{
    public SaveList<ItemData> items = new SaveList<ItemData>();
    public SaveDictionary<int, int> itemCounts = new SaveDictionary<int, int>();
}
```

服务层修改：

```csharp
public void AddItem(ItemData item)
{
    if (!HasSaveModule)
        return;

    SaveModule.items.Add(item); // 自动 MarkDirty
    OnItemAdded?.Invoke(item);
}
```

如果使用普通 `List`、普通 `Dictionary` 或直接修改引用对象内部字段，仍然需要服务层手动调用 `MarkSaveDirty()`。

不要让外部绕过业务服务直接改存档数据。服务层应该集中处理事件派发、合法性检查和必要的手动标脏。

### 读取后修复和校验

`SaveModule` 支持读取后修复和校验：

```csharp
public override bool RepairAfterLoad()
{
    bool repaired = false;

    if (coins == null)
    {
        coins = new SaveValue<int>(0);
        repaired = true;
    }

    if (coins.Value < 0)
    {
        coins.Set(0);
        repaired = true;
    }

    return repaired;
}

public override bool ValidateAfterLoad(out string error)
{
    if (coins == null)
    {
        error = "coins 不能为空";
        return false;
    }

    error = null;
    return true;
}
```

`RepairAfterLoad()` 返回 `true` 时，`ModularSaveManager` 会把当前槽标记为脏数据，让修复后的结果在后续自动保存中落盘。

如果 `ValidateAfterLoad()` 返回 `false`，该模块会被重置为默认数据，避免损坏数据继续进入业务层。

### 存档迁移

模块数据结构变更时，提升 `[SaveModule]` 版本号，并添加迁移器。

```csharp
[SaveMigration("player", 1, 2)]
public sealed class PlayerV1ToV2Migration : SaveMigration
{
    public override string Migrate(string oldJson)
    {
        // 将 v1 JSON 转成 v2 JSON
        return oldJson;
    }
}
```

迁移器只负责一个版本段，例如：

```text
v1 -> v2
v2 -> v3
```

不要写一个迁移器同时跨很多版本，后续维护会更困难。

### 受控 PlayerPrefs

`SavePrefs.cs` 提供了 PlayerPrefs 白名单。

新增偏好项需要两步：

1. 在 `PrefKey` 中添加枚举。
2. 在 `Prefs.Definitions` 中声明类型和默认值。

示例：

```csharp
public enum PrefKey
{
    MusicVolume,
    SfxVolume
}
```

```csharp
PrefDefinition.Float(PrefKey.MusicVolume, 1f)
```

读写：

```csharp
float volume = ModularSaveManager.Instance.GetFloatPref(PrefKey.MusicVolume);
ModularSaveManager.Instance.SetFloatPref(PrefKey.MusicVolume, 0.8f);
```

### Editor 调试面板

项目提供了单独的 Editor 调试工具，不会进入运行时打包逻辑。

菜单入口：

```text
Tools/存档管理器/调试面板
```

调试面板可以查看：

- 当前槽位、是否已加载、是否有脏数据。
- 当前槽位文件路径。
- 已注册模块列表。
- 已注册迁移器列表。
- 指定槽位 JSON 预览。

调试面板可以执行：

- 读取槽位。
- 保存到槽位。
- 立即保存当前槽。
- 删除槽位。
- 打开存档目录。

`ModularSaveManager` 组件自身也有中文 Inspector，会显示运行时重要参数，并提供常用调试按钮。

## 重要 API

### ModularSaveManager

`AutoRegisterAll()`

自动注册所有 `[SaveModule]` 和 `[SaveMigration]`。

`RegisterModule<T>(T module)`

手动注册存档模块。

`RegisterMigration(ISaveMigration migration)`

手动注册迁移器。

`LoadSlot(int slot = 0)`

读取指定槽位。读取前会重置所有模块，读取后会触发 `OnAfterLoad()` 和 `OnAllModulesLoaded`。

`SaveSlot(int slot)`

切换到指定槽位并立即保存当前内存数据。

`SaveNow()`

立即写入当前槽位，不管是否 dirty。

`MarkDirty()`

标记当前存档数据发生变化。开启防抖时不会立刻写文件。

`Flush()`

如果当前有脏数据，立刻保存。

`DeleteSlot(int slot)`

删除指定槽位的正式文件、临时文件和备份文件。如果删除的是当前槽，会重置内存模块并通知服务层重新绑定。

`GetSlotPath(int slot = 0)`

获取槽位文件路径。

`GetModule<T>() / TryGetModule<T>()`

按类型获取已注册存档模块。

`GetRegisteredModulesSnapshot()`

获取当前已注册模块的快照。主要用于调试面板或通用工具展示模块列表。

`GetRegisteredMigrationsSnapshot()`

获取当前已注册迁移器的快照。

`RegisteredModuleCount / RegisteredMigrationCount`

当前已注册模块数量和迁移器数量。

`ActiveSlotPath`

当前槽位对应的存档文件路径。

`OnAllModulesLoaded`

全部模块加载完成后触发。`SaveBoundManager<TModule>` 内部依赖它绑定存档模块。

### SaveModule

`ResetData()`

重置为新档默认值。`LoadSlot()` 和删除当前槽后会调用。

`RepairAfterLoad()`

读取后修复模块数据。适合修正 null 字段、非法数值、无效集合元素等。返回 `true` 表示修复过数据。

`ValidateAfterLoad(out string error)`

读取和修复后校验模块是否可用。返回 `false` 时模块会被重置为默认数据。

`OnBeforeSave()`

保存前回调。适合同步运行时缓存到可序列化字段。

`OnAfterLoad()`

读取后回调。适合修复非法数据、重建缓存、补齐派生数据。

`Key`

模块稳定 ID，来自 `[SaveModule("key", version)]`。

`Version`

模块数据版本，来自 `[SaveModule("key", version)]`。

### SaveValue<T>

`Value`

当前值。外部只能读，不能直接写。

`Set(T value)`

设置新值。新旧值不相等时自动 `MarkDirty` 并派发事件。

`SetValueWithoutCompare(T value)`

强制设置并派发事件，不做 `Equals` 比较。

`NotifyValueChanged()`

当前引用内部变化后，手动通知并标脏。复杂引用类型仍建议走服务层方法。

`onValueChanged`

新值事件。

`onValueChangedDetail`

旧值、新值事件。

### SaveList<T>

`Add(T item)`

添加元素并自动标脏。

`Remove(T item) / RemoveAt(int index)`

删除元素并自动标脏。

`Clear()`

清空列表并自动标脏。

`this[int index]`

替换指定位置元素。新旧值不相等时自动标脏。

`onChanged`

集合变化事件。

`onItemAdded / onItemRemoved`

元素增删事件。

### SaveDictionary<TKey, TValue>

`Add(TKey key, TValue value)`

添加键值并自动标脏。

`Remove(TKey key)`

删除键值并自动标脏。

`Clear()`

清空字典并自动标脏。

`this[TKey key]`

设置键值。新旧值不相等时自动标脏。

`TryGetValue(TKey key, out TValue value)`

尝试读取键值。

`onChanged`

字典变化事件。

`onItemSet / onItemRemoved`

键值设置和删除事件。

### ModularServerManager

`AutoRegisterAll()`

自动注册所有 `[ModularServer]` 服务。

`RegisterServer(IModularServer server, string key, int priority)`

手动注册服务。

`InitializeAll()`

按优先级初始化所有服务。优先级越大越早初始化。

`ShutdownAll()`

按初始化的反向顺序关闭服务。

`TryGetServer<TServer>(out TServer server)`

按类型获取服务。

### SaveBoundManager<TModule>

`SaveModule`

当前绑定的存档模块。

`HasSaveModule`

是否已绑定模块。

`OnManagerInitialize()`

服务初始化时调用，早于存档模块绑定。

`OnModuleLoaded(TModule module)`

存档模块绑定成功后调用。适合订阅 `SaveValue<T>` 事件。

`OnManagerShutdown()`

服务关闭时调用。此时 `SaveModule` 仍可访问，适合解绑事件。

`MarkSaveDirty()`

服务层手动标记存档脏数据。

## 如何使用

### 1. 场景中放置管理器

在场景中创建两个 GameObject，分别挂载：

```text
ModularSaveManager
ModularServerManager
```

二者都会 `DontDestroyOnLoad`。

### 2. 创建存档模块

```csharp
[SaveModule("player", 1)]
public sealed class PlayerSaveModule : SaveModule
{
    public SaveValue<int> coins = new SaveValue<int>(0);
    public int exp;
    public SaveList<int> unlockedLevels = new SaveList<int>();
    public SaveDictionary<int, int> itemCounts = new SaveDictionary<int, int>();

    public override void ResetData()
    {
        coins = new SaveValue<int>(0);
        exp = 0;
        unlockedLevels = new SaveList<int>();
        itemCounts = new SaveDictionary<int, int>();
    }

    public override bool RepairAfterLoad()
    {
        bool repaired = false;

        if (coins == null)
        {
            coins = new SaveValue<int>(0);
            repaired = true;
        }

        if (unlockedLevels == null)
        {
            unlockedLevels = new SaveList<int>();
            repaired = true;
        }

        if (itemCounts == null)
        {
            itemCounts = new SaveDictionary<int, int>();
            repaired = true;
        }

        if (coins.Value < 0)
        {
            coins.Set(0);
            repaired = true;
        }

        return repaired;
    }
}
```

### 3. 创建业务服务

```csharp
using System;

[ModularServer("player", 100)]
public sealed class PlayerManager : SaveBoundManager<PlayerSaveModule>
{
    public event Action<int> OnCoinsChanged;
    public event Action<int> OnLevelUnlocked;

    protected override void OnModuleLoaded(PlayerSaveModule module)
    {
        module.coins.onValueChanged -= HandleCoinsChanged;
        module.coins.onValueChanged += HandleCoinsChanged;
    }

    protected override void OnManagerShutdown()
    {
        if (!HasSaveModule)
            return;

        SaveModule.coins.onValueChanged -= HandleCoinsChanged;
    }

    public void AddCoins(int amount)
    {
        if (!HasSaveModule)
            return;

        SaveModule.coins.Set(SaveModule.coins.Value + amount);
    }

    public void UnlockLevel(int level)
    {
        if (!HasSaveModule)
            return;

        if (SaveModule.unlockedLevels.Contains(level))
            return;

        SaveModule.unlockedLevels.Add(level); // SaveList 自动 MarkDirty
        OnLevelUnlocked?.Invoke(level);
    }

    private void HandleCoinsChanged(int coins)
    {
        OnCoinsChanged?.Invoke(coins);
    }
}
```

### 4. 启动注册和加载

建议写一个 Bootstrap，并确保它在业务逻辑使用服务前执行：

```csharp
using UnityEngine;

public sealed class GameBootstrap : MonoBehaviour
{
    private void Awake()
    {
        ModularSaveManager.Instance.AutoRegisterAll();
        ModularSaveManager.Instance.LoadSlot(0);

        ModularServerManager.Instance.AutoRegisterAll();
        ModularServerManager.Instance.InitializeAll();
    }
}
```

推荐顺序：

```text
1. SaveManager AutoRegisterAll
2. SaveManager LoadSlot
3. ServerManager AutoRegisterAll
4. ServerManager InitializeAll
```

这样服务初始化时可以立即绑定已加载的存档模块。

### 5. 使用服务

```csharp
if (ModularServerManager.Instance.TryGetServer(out PlayerManager player))
{
    player.OnCoinsChanged += coins =>
    {
        Debug.Log($"Coins: {coins}");
    };

    player.AddCoins(100);
    player.UnlockLevel(3);
}
```

## 存档文件结构

外层是一个 `SaveFile`：

```json
{
  "saveVersion": 1,
  "slotId": 0,
  "updatedAtUtc": 1782486086,
  "modules": [
    {
      "key": "player",
      "type": "PlayerSaveModule",
      "version": 1,
      "json": "{ ... module json ... }"
    }
  ]
}
```

每个模块自己的 JSON 被放在 `SaveModuleEntry.json` 字符串中。这样每个模块可以独立迁移，`ModularSaveManager` 不需要理解业务字段。

模块内部 JSON 示例：

```json
{
  "coins": {
    "Value": 100
  },
  "level": {
    "Value": 2
  },
  "items": [
    {
      "itemId": 101,
      "count": 1
    }
  ],
  "itemCounts": {
    "101": 1,
    "102": 5
  }
}
```

其中：

- `SaveValue<int>` 会保存为 `{ "Value": 100 }`。
- `SaveList<T>` 会保存为普通 JSON 数组。
- `SaveDictionary<int, int>` 会保存为 JSON 对象，key 在 JSON 中显示为字符串，读取时会转回 `int`。

## 注意事项

### 启动顺序

`InitializeAll()` 必须在 `AutoRegisterAll()` 之后调用。

错误顺序：

```csharp
ModularServerManager.Instance.InitializeAll();
ModularServerManager.Instance.AutoRegisterAll();
```

这样服务列表为空，服务不会初始化，也不会绑定存档模块。

### 示例脚本会参与自动注册

`Assets/Scripts/Examples/PlayerSaveExample.cs` 中的示例带有 `[SaveModule]` 和 `[ModularServer]`。只要调用 `AutoRegisterAll()`，示例也会被注册。

正式项目中可以删除示例、移到测试程序集，或用条件编译隔离。

### SaveValue<T> 适用边界

`SaveValue<T>` 可以用于引用类型，但它只能判断引用本身是否变化，不能自动捕获引用内部字段变化。

例如：

```csharp
module.info.Set(newInfo);      // 可以触发
module.info.Value.level = 10;  // 不会自动触发
```

引用类型内部变化可以调用：

```csharp
module.info.NotifyValueChanged();
```

但更推荐复杂对象和集合由服务层提供明确修改方法。

### 不要直接修改复杂集合

不推荐外部这样写：

```csharp
module.items.Add(item);
```

推荐：

```csharp
playerManager.AddItem(item);
```

这样事件派发和 `MarkSaveDirty()` 都在一个地方完成。

### public 字段和属性

当前 `SaveModule.BindSaveValues()` 会自动绑定顶层 public 字段中的 `ISaveValue`，包括 `SaveValue<T>`、`SaveList<T>` 和 `SaveDictionary<TKey, TValue>`。

推荐存档字段写成：

```csharp
public SaveValue<int> coins = new SaveValue<int>(0);
public SaveList<int> unlockedLevels = new SaveList<int>();
public int exp;
```

不要把需要自动绑定的 `ISaveValue` 写成属性，否则可以被 LitJson 序列化，但不会自动绑定 dirty 回调。

### Key 上线后不要随便改

`[SaveModule("player", 1)]` 中的 `"player"` 是存档文件识别模块的主身份。

类名可以改，Key 尽量不要改。Key 改了以后，旧存档会被视为未知模块。

### 版本升级要写迁移器

如果模块数据结构发生不兼容变化，需要：

1. 提升 `[SaveModule]` 版本号。
2. 添加对应 `[SaveMigration]`。

如果缺少迁移器，旧数据可能无法正确读取。

### PlayerPrefs 只放轻量偏好

PlayerPrefs 层只建议放：

- 音量
- 语言
- 画质
- 当前选择槽位
- 教程完成状态

完整游戏数据、背包、进度等应放入 `SaveModule`。

### 加密不是绝对安全

`SaveCrypto` 可以让存档不再是明文 JSON，并使用 HMAC 检查篡改。

但本地加密不能提供服务端级别安全，因为客户端代码和密钥最终都在玩家设备上。

## 推荐实践

- 基础值需要订阅：用 `SaveValue<T>`。
- 基础值只想简单存储：用普通字段。
- 集合结构变化：优先用 `SaveList<T>` / `SaveDictionary<TKey, TValue>`。
- 引用对象内部字段变化：由服务层方法修改，并手动 `MarkSaveDirty()`。
- 服务层统一派发事件，不让外部直接改存档数据。
- 启动时固定顺序：先存档注册和读档，再服务注册和初始化。
- 切槽、删档后依赖 `OnAllModulesLoaded` 重新绑定服务。
- 上线后保持 SaveModule Key 稳定。
- 数据结构变更时写迁移器。
