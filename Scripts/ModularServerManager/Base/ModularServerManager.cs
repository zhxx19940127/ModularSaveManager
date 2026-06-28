using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

/// <summary>
/// 模块化业务管理器总控制器。
/// </summary>
public sealed class ModularServerManager : MonoBehaviour
{
    /// <summary>当前存活的业务管理器总控制器。</summary>
    public static ModularServerManager Instance { get; private set; }

    private readonly List<IModularServer> _servers = new List<IModularServer>(32);
    private readonly Dictionary<string, IModularServer> _serversByKey = new Dictionary<string, IModularServer>(32);
    private readonly Dictionary<Type, IModularServer> _serversByType = new Dictionary<Type, IModularServer>(32);

    /// <summary>已注册业务管理器数量。</summary>
    public int ServerCount => _servers.Count;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            ShutdownAll();
            Instance = null;
        }
    }

    /// <summary>
    /// 自动注册所有带 [ModularServer] 的业务管理器。
    /// </summary>
    public void AutoRegisterAll()
    {
        Type[] types = RuntimeTypeUtility.GetAllRuntimeTypes();
        for (int i = 0; i < types.Length; i++)
        {
            Type type = types[i];
            if (!IsConcreteType(type) || !typeof(IModularServer).IsAssignableFrom(type))
                continue;

            ModularServerAttribute attribute =
                (ModularServerAttribute)Attribute.GetCustomAttribute(type, typeof(ModularServerAttribute));
            if (attribute == null)
                continue;

            if (_serversByKey.ContainsKey(attribute.Key) || _serversByType.ContainsKey(type))
                continue;

            IModularServer server = CreateServer(type);
            if (server == null)
                continue;

            Debug.Log($"服务注册   {attribute.Key}");
            RegisterServer(server, attribute.Key, attribute.Priority);
        }

        SortServers();
    }

    /// <summary>
    /// 注册业务管理器。
    /// </summary>
    public void RegisterServer(IModularServer server, string key, int priority)
    {
        if (server == null)
        {
            Debug.LogError("注册业务管理器失败: server 不能为空");
            return;
        }

        if (string.IsNullOrWhiteSpace(key))
        {
            Debug.LogError($"注册业务管理器失败: key 不能为空 type={server.GetType().FullName}");
            return;
        }

        Type type = server.GetType();
        if (_serversByKey.ContainsKey(key))
        {
            Debug.LogError($"重复业务管理器 key: {key}");
            return;
        }

        if (_serversByType.ContainsKey(type))
        {
            Debug.LogError($"重复业务管理器类型: {type.FullName}");
            return;
        }

        if (server is ModularServerBase baseServer)
        {
            baseServer.Key = key; // key 只用于业务管理器注册表，不参与存档。
            baseServer.Priority = priority; // priority 数值越大越早初始化。
        }

        _servers.Add(server);
        _serversByKey.Add(key, server);
        _serversByType.Add(type, server);
        SortServers();
    }

    /// <summary>
    /// 初始化全部业务管理器。
    /// </summary>
    public void InitializeAll()
    {
        SortServers();
        for (int i = 0; i < _servers.Count; i++)
        {
            _servers[i].Initialize();
        }
    }

    /// <summary>
    /// 关闭全部业务管理器。
    /// </summary>
    public void ShutdownAll()
    {
        for (int i = _servers.Count - 1; i >= 0; i--)
        {
            _servers[i].Shutdown();
        }
    }

    /// <summary>
    /// 尝试按类型获取业务管理器。
    /// </summary>
    public bool TryGetServer<TServer>(out TServer server) where TServer : class, IModularServer
    {
        if (_serversByType.TryGetValue(typeof(TServer), out IModularServer rawServer))
        {
            server = rawServer as TServer;
            return server != null;
        }

        server = null;
        return false;
    }

    /// <summary>
    /// 尝试按 key 获取业务管理器。
    /// </summary>
    public bool TryGetServer(string key, out IModularServer server)
    {
        return _serversByKey.TryGetValue(key, out server);
    }

    /// <summary>
    /// 创建业务管理器实例。
    /// </summary>
    private static IModularServer CreateServer(Type type)
    {
        IModularServer singleton = GetStaticInstance(type);
        if (singleton != null)
            return singleton; // 优先使用子类自己的单例 Instance。

        try
        {
            return Activator.CreateInstance(type, true) as IModularServer; // 允许 private 无参构造，方便普通 C# 单例。
        }
        catch (Exception exception)
        {
            Debug.LogError($"创建业务管理器失败: type={type.FullName}, error={exception.Message}");
            return null;
        }
    }

    /// <summary>
    /// 获取类型上的静态 Instance 属性或字段。
    /// </summary>
    private static IModularServer GetStaticInstance(Type type)
    {
        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;

        PropertyInfo property = type.GetProperty("Instance", flags);
        if (property != null && typeof(IModularServer).IsAssignableFrom(property.PropertyType))
            return property.GetValue(null, null) as IModularServer;

        FieldInfo field = type.GetField("Instance", flags);
        if (field != null && typeof(IModularServer).IsAssignableFrom(field.FieldType))
            return field.GetValue(null) as IModularServer;

        return null;
    }

    /// <summary>
    /// 按优先级排序业务管理器。
    /// </summary>
    private void SortServers()
    {
        _servers.Sort((a, b) =>
        {
            int priorityCompare = b.Priority.CompareTo(a.Priority);
            return priorityCompare != 0 ? priorityCompare : string.CompareOrdinal(a.Key, b.Key);
        });
    }

    /// <summary>
    /// 是否为可实例化类型。
    /// </summary>
    private static bool IsConcreteType(Type type)
    {
        return type != null && type.IsClass && !type.IsAbstract && !type.IsGenericTypeDefinition;
    }
}
