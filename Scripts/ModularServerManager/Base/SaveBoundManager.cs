using UnityEngine;
// ReSharper disable All

/// <summary>
/// 绑定单个存档模块的业务管理器基类。
/// </summary>
public abstract class SaveBoundManager<TModule> : ModularServerBase where TModule : SaveModule
{
    /// <summary>当前绑定的存档模块。</summary>
    protected TModule SaveModule { get; private set; }

    /// <summary>是否已经成功绑定存档模块。</summary>
    protected bool HasSaveModule => SaveModule != null;

    /// <summary>
    /// 初始化业务管理器，并尝试绑定存档系统。
    /// </summary>
    protected sealed override void OnInitialize()
    {
        OnManagerInitialize(); // 先给子类初始化纯业务事件或缓存。
        BindSaveManager(); // 再绑定存档系统，避免子类状态未准备好。
    }

    /// <summary>
    /// 关闭业务管理器，并解除存档系统绑定。
    /// </summary>
    protected sealed override void OnShutdown()
    {
        OnManagerShutdown(); // 先给子类释放模块事件，此时 SaveModule 仍可访问。
        UnbindSaveManager(); // 再解绑存档系统，避免关闭后继续收到加载回调。
        SaveModule = null; // 最后清掉模块引用，防止跨存档槽误用。
    }

    /// <summary>
    /// 子类业务初始化入口。
    /// </summary>
    protected virtual void OnManagerInitialize()
    {
    }

    /// <summary>
    /// 子类业务关闭入口。
    /// </summary>
    protected virtual void OnManagerShutdown()
    {
    }

    /// <summary>
    /// 成功绑定存档模块后回调。
    /// </summary>
    protected virtual void OnModuleLoaded(TModule module)
    {
    }

    /// <summary>
    /// 存档模块缺失时回调。
    /// </summary>
    protected virtual void OnModuleMissing()
    {
        Debug.LogError($"[{Key}] 未找到存档模块: {typeof(TModule).FullName}");
    }

    /// <summary>
    /// 标记当前存档数据已修改。
    /// </summary>
    protected void MarkSaveDirty()
    {
        ModularSaveManager.Instance?.MarkDirty();
    }

    /// <summary>
    /// 绑定模块化存档系统。
    /// </summary>
    private void BindSaveManager()
    {
        ModularSaveManager.InstanceReady -= HandleSaveManagerReady;
        ModularSaveManager.InstanceReady += HandleSaveManagerReady; // 兼容 Server 早于 SaveManager 初始化的情况。

        if (ModularSaveManager.Instance != null)
            BindSaveManager(ModularSaveManager.Instance);
    }

    /// <summary>
    /// 解除模块化存档系统绑定。
    /// </summary>
    private void UnbindSaveManager()
    {
        ModularSaveManager.InstanceReady -= HandleSaveManagerReady;

        if (ModularSaveManager.Instance != null)
            ModularSaveManager.Instance.OnAllModulesLoaded -= HandleAllModulesLoaded;
    }

    /// <summary>
    /// 存档管理器实例创建完成回调。
    /// </summary>
    private void HandleSaveManagerReady(ModularSaveManager manager)
    {
        BindSaveManager(manager);
    }

    /// <summary>
    /// 绑定指定存档管理器实例。
    /// </summary>
    private void BindSaveManager(ModularSaveManager manager)
    {
        if (manager == null)
            return;

        manager.OnAllModulesLoaded -= HandleAllModulesLoaded;
        manager.OnAllModulesLoaded += HandleAllModulesLoaded;

        if (manager.IsLoaded)
            HandleAllModulesLoaded(); // 存档已加载时立即补一次拉取，避免错过事件。
    }

    /// <summary>
    /// 全部存档模块加载完成回调。
    /// </summary>
    private void HandleAllModulesLoaded()
    {
        ModularSaveManager manager = ModularSaveManager.Instance;
        if (manager == null)
            return;

        if (!manager.TryGetModule(out TModule module))
        {
            SaveModule = null;
            OnModuleMissing();
            return;
        }

        SaveModule = module;
        OnModuleLoaded(module);
    }
}
