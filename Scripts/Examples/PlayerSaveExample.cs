using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public sealed class PlayerItemData
{
    public int itemId;
    public int count;
}

[SaveModule("example_player", 1)]
public sealed class PlayerSaveExampleModule : SaveModule
{
    public SaveValue<int> coins = new SaveValue<int>(0);
    public SaveValue<int> level = new SaveValue<int>(1);

    public SaveList<PlayerItemData> items = new SaveList<PlayerItemData>();
    public SaveDictionary<int, int> itemCounts = new SaveDictionary<int, int>();

    public override void ResetData()
    {
        coins = new SaveValue<int>(0);
        level = new SaveValue<int>(1);
        items = new SaveList<PlayerItemData>();
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

        if (level == null)
        {
            level = new SaveValue<int>(1);
            repaired = true;
        }

        if (items == null)
        {
            items = new SaveList<PlayerItemData>();
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

        if (level.Value < 1)
        {
            level.Set(1);
            repaired = true;
        }

        for (int i = items.Count - 1; i >= 0; i--)
        {
            PlayerItemData item = items[i];
            if (item == null || item.itemId <= 0 || item.count <= 0)
            {
                items.RemoveAt(i);
                repaired = true;
            }
        }

        List<int> invalidItemIds = new List<int>();
        foreach (KeyValuePair<int, int> pair in itemCounts)
        {
            if (pair.Key <= 0 || pair.Value <= 0)
            {
                invalidItemIds.Add(pair.Key);
            }
        }

        for (int i = 0; i < invalidItemIds.Count; i++)
        {
            itemCounts.Remove(invalidItemIds[i]);
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

        if (level == null)
        {
            error = "level 不能为空";
            return false;
        }

        if (items == null)
        {
            error = "items 不能为空";
            return false;
        }

        if (itemCounts == null)
        {
            error = "itemCounts 不能为空";
            return false;
        }

        error = null;
        return true;
    }
}

[ModularServer("example_player", 100)]
public sealed class PlayerSaveExampleManager : SaveBoundManager<PlayerSaveExampleModule>
{
    public event Action<int> OnCoinsChanged;
    public event Action<int> OnLevelChanged;
    public event Action<PlayerItemData> OnItemAdded;

    public int Coins => HasSaveModule ? SaveModule.coins.Value : 0;
    public int Level => HasSaveModule ? SaveModule.level.Value : 1;
    public IReadOnlyList<PlayerItemData> Items => HasSaveModule ? SaveModule.items : Array.Empty<PlayerItemData>();
    public IReadOnlyDictionary<int, int> ItemCounts => HasSaveModule
        ? SaveModule.itemCounts
        : EmptyItemCounts;

    private static readonly IReadOnlyDictionary<int, int> EmptyItemCounts = new Dictionary<int, int>();

    protected override void OnModuleLoaded(PlayerSaveExampleModule module)
    {
        module.coins.onValueChanged -= HandleCoinsChanged;
        module.coins.onValueChanged += HandleCoinsChanged;

        module.level.onValueChanged -= HandleLevelChanged;
        module.level.onValueChanged += HandleLevelChanged;
    }

    protected override void OnManagerShutdown()
    {
        if (!HasSaveModule)
        {
            return;
        }

        SaveModule.coins.onValueChanged -= HandleCoinsChanged;
        SaveModule.level.onValueChanged -= HandleLevelChanged;
    }

    public void AddCoins(int amount)
    {
        if (!HasSaveModule)
        {
            return;
        }

        SaveModule.coins.Set(SaveModule.coins.Value + amount);
    }

    public void SetLevel(int level)
    {
        if (!HasSaveModule)
        {
            return;
        }

        SaveModule.level.Set(level);
    }

    public void AddItem(int itemId, int count)
    {
        if (!HasSaveModule)
        {
            return;
        }

        PlayerItemData item = new PlayerItemData
        {
            itemId = itemId,
            count = count
        };

        SaveModule.items.Add(item);
        if (SaveModule.itemCounts.TryGetValue(itemId, out int oldCount))
        {
            SaveModule.itemCounts[itemId] = oldCount + count;
        }
        else
        {
            SaveModule.itemCounts.Add(itemId, count);
        }

        OnItemAdded?.Invoke(item);
    }

    public void SetItemCount(int itemId, int count)
    {
        if (!HasSaveModule)
        {
            return;
        }

        if (itemId <= 0)
        {
            Debug.LogError($"设置道具数量失败: itemId 无效 {itemId}");
            return;
        }

        if (count <= 0)
        {
            SaveModule.itemCounts.Remove(itemId);
            return;
        }

        SaveModule.itemCounts[itemId] = count;
    }

    private void HandleCoinsChanged(int coins)
    {
        OnCoinsChanged?.Invoke(coins);
    }

    private void HandleLevelChanged(int level)
    {
        OnLevelChanged?.Invoke(level);
    }
}
