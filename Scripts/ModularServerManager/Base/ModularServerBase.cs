/// <summary>
/// 模块化业务管理器基类。
/// </summary>
public abstract class ModularServerBase : IModularServer
{
    /// <summary>业务管理器稳定 Key。</summary>
    public string Key { get; internal set; }

    /// <summary>初始化优先级，数值越大越早初始化。</summary>
    public int Priority { get; internal set; }

    /// <summary>是否已经初始化。</summary>
    public bool IsInitialized { get; private set; }

    /// <summary>
    /// 初始化业务管理器。
    /// </summary>
    public void Initialize()
    {
        if (IsInitialized)
            return; // 防止重复初始化。

        IsInitialized = true;
        OnInitialize();
    }

    /// <summary>
    /// 关闭业务管理器。
    /// </summary>
    public void Shutdown()
    {
        if (!IsInitialized)
            return; // 未初始化时无需关闭。

        OnShutdown();
        IsInitialized = false;
    }

    /// <summary>
    /// 子类初始化入口。
    /// </summary>
    protected virtual void OnInitialize()
    {
    }

    /// <summary>
    /// 子类关闭入口。
    /// </summary>
    protected virtual void OnShutdown()
    {
    }
}
