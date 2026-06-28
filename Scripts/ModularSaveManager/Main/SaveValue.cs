using System;
using System.Collections;
using System.Collections.Generic;
using LitJson.Extensions;

public interface ISaveValue
{
    void BindDirty(Action markDirty);
    void ClearDirtyBinding();
}

/// <summary>
/// 可存档、可订阅的单值包装。
///
/// 适合 int / float / bool / string / enum 等标量存档字段。
/// T 是引用类型时，只能感知引用本身被替换，不能自动感知引用内部字段变化。
/// </summary>
public sealed class SaveValue<T> : ISaveValue
{
    [JsonIgnore] private Action _markDirty;

    /// <summary>值发生变化时触发，参数为新值。</summary>
    [JsonIgnore] public Action<T> onValueChanged;

    /// <summary>值发生变化时触发，参数为旧值和新值。</summary>
    [JsonIgnore] public Action<T, T> onValueChangedDetail;

    /// <summary>
    /// 当前存档值。
    /// 外部修改请使用 Set，避免绕过通知和 MarkDirty。
    /// private setter 仍可被当前 LitJson 反序列化。
    /// </summary>
    public T Value { get; private set; }

    /// <summary>
    /// LitJson 反序列化需要真正的无参构造函数。
    /// </summary>
    public SaveValue()
    {
        Value = default;
    }

    public SaveValue(T defaultValue)
    {
        Value = defaultValue;
    }

    public override string ToString()
    {
        return Value != null ? Value.ToString() : "Null";
    }

    /// <summary>
    /// 绑定存档脏标记回调。反序列化后需要重新绑定。
    /// </summary>
    public void BindDirty(Action markDirty)
    {
        _markDirty = markDirty;
    }

    /// <summary>
    /// 解除存档脏标记回调。
    /// </summary>
    public void ClearDirtyBinding()
    {
        _markDirty = null;
    }

    /// <summary>
    /// 设置新值。只有新旧值不相等时才通知和标脏。
    /// </summary>
    public void Set(T value)
    {
        if (Equals(Value, value))
        {
            return;
        }

        SetValueWithoutCompare(value);
    }

    /// <summary>
    /// 不做 Equals 比较，直接写入新值并通知监听者。
    /// 适合需要强制刷新同值、同引用对象的场景。
    /// </summary>
    public void SetValueWithoutCompare(T value)
    {
        T oldValue = Value;
        Value = value;
        NotifyValueChanged(oldValue);
    }

    /// <summary>
    /// 当前 Value 本身没有重新赋值，但需要手动通知和标脏时调用。
    /// 引用类型内部字段变化后，可以用它兜底；复杂集合仍建议由服务层统一派发事件。
    /// </summary>
    public void NotifyValueChanged()
    {
        NotifyValueChanged(Value);
    }

    private void NotifyValueChanged(T oldValue)
    {
        _markDirty?.Invoke();
        onValueChanged?.Invoke(Value);
        onValueChangedDetail?.Invoke(oldValue, Value);
    }
}

/// <summary>
/// 可存档、可自动标脏的列表包装。
///
/// LitJson 会把它当作普通 JSON 数组读写。业务层修改集合时请走 Add/Remove/Clear/索引器等方法，
/// 不需要额外手动 MarkDirty。
/// </summary>
public sealed class SaveList<T> : IList<T>, IList, IReadOnlyList<T>, ISaveValue
{
    [JsonIgnore] private Action _markDirty;
    [JsonIgnore] public Action onChanged;
    [JsonIgnore] public Action<T> onItemAdded;
    [JsonIgnore] public Action<T> onItemRemoved;

    private readonly List<T> _items;

    public SaveList()
    {
        _items = new List<T>();
    }

    public SaveList(IEnumerable<T> items)
    {
        _items = items != null ? new List<T>(items) : new List<T>();
    }

    [JsonIgnore] public int Count => _items.Count;
    [JsonIgnore] public bool IsReadOnly => false;
    bool IList.IsFixedSize => false;
    bool ICollection.IsSynchronized => false;
    object ICollection.SyncRoot => this;

    public T this[int index]
    {
        get => _items[index];
        set
        {
            if (Equals(_items[index], value))
            {
                return;
            }

            _items[index] = value;
            NotifyChanged();
        }
    }

    object IList.this[int index]
    {
        get => this[index];
        set => this[index] = CastValue(value);
    }

    public void BindDirty(Action markDirty)
    {
        _markDirty = markDirty;
    }

    public void ClearDirtyBinding()
    {
        _markDirty = null;
    }

    public void Add(T item)
    {
        _items.Add(item);
        NotifyItemAdded(item);
    }

    int IList.Add(object value)
    {
        Add(CastValue(value));
        return _items.Count - 1;
    }

    public void AddRange(IEnumerable<T> items)
    {
        if (items == null)
        {
            return;
        }

        bool changed = false;
        foreach (T item in items)
        {
            _items.Add(item);
            onItemAdded?.Invoke(item);
            changed = true;
        }

        if (changed)
        {
            NotifyChanged();
        }
    }

    public void Clear()
    {
        if (_items.Count == 0)
        {
            return;
        }

        _items.Clear();
        NotifyChanged();
    }

    public bool Contains(T item)
    {
        return _items.Contains(item);
    }

    bool IList.Contains(object value)
    {
        return value is T item && Contains(item);
    }

    public void CopyTo(T[] array, int arrayIndex)
    {
        _items.CopyTo(array, arrayIndex);
    }

    void ICollection.CopyTo(Array array, int index)
    {
        ((ICollection)_items).CopyTo(array, index);
    }

    public IEnumerator<T> GetEnumerator()
    {
        return _items.GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }

    public int IndexOf(T item)
    {
        return _items.IndexOf(item);
    }

    int IList.IndexOf(object value)
    {
        return value is T item ? IndexOf(item) : -1;
    }

    public void Insert(int index, T item)
    {
        _items.Insert(index, item);
        NotifyItemAdded(item);
    }

    void IList.Insert(int index, object value)
    {
        Insert(index, CastValue(value));
    }

    public bool Remove(T item)
    {
        if (!_items.Remove(item))
        {
            return false;
        }

        NotifyItemRemoved(item);
        return true;
    }

    void IList.Remove(object value)
    {
        if (value is T item)
        {
            Remove(item);
        }
    }

    public void RemoveAt(int index)
    {
        T item = _items[index];
        _items.RemoveAt(index);
        NotifyItemRemoved(item);
    }

    public T[] ToArray()
    {
        return _items.ToArray();
    }

    public List<T> ToList()
    {
        return new List<T>(_items);
    }

    private void NotifyItemAdded(T item)
    {
        onItemAdded?.Invoke(item);
        NotifyChanged();
    }

    private void NotifyItemRemoved(T item)
    {
        onItemRemoved?.Invoke(item);
        NotifyChanged();
    }

    private void NotifyChanged()
    {
        _markDirty?.Invoke();
        onChanged?.Invoke();
    }

    private static T CastValue(object value)
    {
        if (value == null)
        {
            return default;
        }

        return (T)value;
    }
}

/// <summary>
/// 可存档、可自动标脏的字典包装。
///
/// LitJson 会把它当作普通 JSON 对象读写。建议 key 使用 string、int、enum 等可从字符串转换的类型。
/// </summary>
public sealed class SaveDictionary<TKey, TValue> : IDictionary<TKey, TValue>, IDictionary, IReadOnlyDictionary<TKey, TValue>, ISaveValue
{
    [JsonIgnore] private Action _markDirty;
    [JsonIgnore] public Action onChanged;
    [JsonIgnore] public Action<TKey, TValue> onItemSet;
    [JsonIgnore] public Action<TKey> onItemRemoved;

    private readonly Dictionary<TKey, TValue> _items;

    public SaveDictionary()
    {
        _items = new Dictionary<TKey, TValue>();
    }

    public SaveDictionary(IDictionary<TKey, TValue> items)
    {
        _items = items != null ? new Dictionary<TKey, TValue>(items) : new Dictionary<TKey, TValue>();
    }

    [JsonIgnore] public int Count => _items.Count;
    [JsonIgnore] public bool IsReadOnly => false;
    bool IDictionary.IsFixedSize => false;
    bool ICollection.IsSynchronized => false;
    object ICollection.SyncRoot => this;
    ICollection<TKey> IDictionary<TKey, TValue>.Keys => _items.Keys;
    ICollection<TValue> IDictionary<TKey, TValue>.Values => _items.Values;
    ICollection IDictionary.Keys => _items.Keys;
    ICollection IDictionary.Values => _items.Values;
    IEnumerable<TKey> IReadOnlyDictionary<TKey, TValue>.Keys => _items.Keys;
    IEnumerable<TValue> IReadOnlyDictionary<TKey, TValue>.Values => _items.Values;

    public TValue this[TKey key]
    {
        get => _items[key];
        set
        {
            if (_items.TryGetValue(key, out TValue existing) && Equals(existing, value))
            {
                return;
            }

            _items[key] = value;
            NotifyItemSet(key, value);
        }
    }

    object IDictionary.this[object key]
    {
        get => this[CastKey(key)];
        set => this[CastKey(key)] = CastValue(value);
    }

    public void BindDirty(Action markDirty)
    {
        _markDirty = markDirty;
    }

    public void ClearDirtyBinding()
    {
        _markDirty = null;
    }

    public void Add(TKey key, TValue value)
    {
        _items.Add(key, value);
        NotifyItemSet(key, value);
    }

    void IDictionary.Add(object key, object value)
    {
        Add(CastKey(key), CastValue(value));
    }

    public void Add(KeyValuePair<TKey, TValue> item)
    {
        Add(item.Key, item.Value);
    }

    public void Clear()
    {
        if (_items.Count == 0)
        {
            return;
        }

        _items.Clear();
        NotifyChanged();
    }

    public bool Contains(KeyValuePair<TKey, TValue> item)
    {
        return ((ICollection<KeyValuePair<TKey, TValue>>)_items).Contains(item);
    }

    bool IDictionary.Contains(object key)
    {
        return key is TKey typedKey && ContainsKey(typedKey);
    }

    public bool ContainsKey(TKey key)
    {
        return _items.ContainsKey(key);
    }

    IDictionaryEnumerator IDictionary.GetEnumerator()
    {
        return ((IDictionary)_items).GetEnumerator();
    }

    public IEnumerator<KeyValuePair<TKey, TValue>> GetEnumerator()
    {
        return _items.GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }

    public bool Remove(TKey key)
    {
        if (!_items.Remove(key))
        {
            return false;
        }

        NotifyItemRemoved(key);
        return true;
    }

    void IDictionary.Remove(object key)
    {
        if (key is TKey typedKey)
        {
            Remove(typedKey);
        }
    }

    public bool Remove(KeyValuePair<TKey, TValue> item)
    {
        if (!Contains(item))
        {
            return false;
        }

        return Remove(item.Key);
    }

    public bool TryGetValue(TKey key, out TValue value)
    {
        return _items.TryGetValue(key, out value);
    }

    public void CopyTo(KeyValuePair<TKey, TValue>[] array, int arrayIndex)
    {
        ((ICollection<KeyValuePair<TKey, TValue>>)_items).CopyTo(array, arrayIndex);
    }

    void ICollection.CopyTo(Array array, int index)
    {
        ((ICollection)_items).CopyTo(array, index);
    }

    public Dictionary<TKey, TValue> ToDictionary()
    {
        return new Dictionary<TKey, TValue>(_items);
    }

    private void NotifyItemSet(TKey key, TValue value)
    {
        onItemSet?.Invoke(key, value);
        NotifyChanged();
    }

    private void NotifyItemRemoved(TKey key)
    {
        onItemRemoved?.Invoke(key);
        NotifyChanged();
    }

    private void NotifyChanged()
    {
        _markDirty?.Invoke();
        onChanged?.Invoke();
    }

    private static TKey CastKey(object key)
    {
        return (TKey)key;
    }

    private static TValue CastValue(object value)
    {
        if (value == null)
        {
            return default;
        }

        return (TValue)value;
    }
}
