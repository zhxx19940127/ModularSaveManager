/// <summary>
/// 模块化业务管理器接口。
/// </summary>
public interface IModularServer
{
    /// <summary>业务管理器稳定 Key。</summary>
    string Key { get; }

    /// <summary>初始化优先级，数值越大越早初始化。</summary>
    int Priority { get; }

    /// <summary>是否已经初始化。</summary>
    bool IsInitialized { get; }

    /// <summary>
    /// 初始化业务管理器。
    /// </summary>
    void Initialize();

    /// <summary>
    /// 关闭业务管理器，释放事件和运行时引用。
    /// </summary>
    void Shutdown();
}
