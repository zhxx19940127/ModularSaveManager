using UnityEngine;

public sealed class PlayerSaveExampleUsage : MonoBehaviour
{
    private PlayerSaveExampleManager _player;

    private void Start()
    {
        ModularSaveManager.Instance.AutoRegisterAll();
        ModularSaveManager.Instance.LoadSlot();
        ModularServerManager.Instance.AutoRegisterAll();
        ModularServerManager.Instance.InitializeAll();

        if (!ModularServerManager.Instance.TryGetServer(out _player))
        {
            Debug.LogError("未找到 PlayerSaveExampleManager，请确认 ModularServerManager 已 AutoRegisterAll 并 InitializeAll。");
            return;
        }

        _player.OnCoinsChanged += HandleCoinsChanged;
        _player.OnLevelChanged += HandleLevelChanged;
        _player.OnItemAdded += HandleItemAdded;

        _player.AddCoins(100);
        _player.SetLevel(2);
        _player.AddItem(101, 1);
        _player.AddItem(102, 5);
        _player.SetItemCount(103, 9);

        foreach (var pair in _player.ItemCounts)
        {
            Debug.Log($"字典道具数量: itemId={pair.Key}, count={pair.Value}");
        }
    }

    private void OnDestroy()
    {
        if (_player == null)
        {
            return;
        }

        _player.OnCoinsChanged -= HandleCoinsChanged;
        _player.OnLevelChanged -= HandleLevelChanged;
        _player.OnItemAdded -= HandleItemAdded;
    }

    private static void HandleCoinsChanged(int coins)
    {
        Debug.Log($"金币变化: {coins}");
    }

    private static void HandleLevelChanged(int level)
    {
        Debug.Log($"等级变化: {level}");
    }

    private static void HandleItemAdded(PlayerItemData item)
    {
        Debug.Log($"获得道具: itemId={item.itemId}, count={item.count}");
    }
}
