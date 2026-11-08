namespace PhiDos.Core.Kernel;

/// <summary>
/// 环境变量区。保持插入顺序（SET 列出时按定义顺序），名字统一大写。
/// 总大小按 DOS 的存储方式计算："NAME=VALUE\0" 逐个相加，再加结尾的一个 \0。
/// </summary>
internal sealed class DosEnvironment
{
    private readonly List<KeyValuePair<string, string>> _vars = [];

    public DosEnvironment(int capacity) => Capacity = capacity;

    public int Capacity { get; }

    public IReadOnlyList<KeyValuePair<string, string>> Entries => _vars;

    public string? Get(string name)
    {
        string key = name.ToUpperInvariant();
        foreach (var kv in _vars)
            if (kv.Key == key) return kv.Value;
        return null;
    }

    /// <summary>设置变量，value 为 null 或空表示删除。空间不足时返回 false 且不做修改。</summary>
    public bool Set(string name, string? value)
    {
        string key = name.ToUpperInvariant();
        int index = _vars.FindIndex(kv => kv.Key == key);
        if (string.IsNullOrEmpty(value))
        {
            if (index >= 0) _vars.RemoveAt(index);
            return true;
        }

        int size = UsedBytes();
        if (index >= 0) size -= EntrySize(_vars[index].Key, _vars[index].Value);
        size += EntrySize(key, value);
        if (size > Capacity) return false;

        if (index >= 0) _vars[index] = new(key, value);
        else _vars.Add(new(key, value));
        return true;
    }

    /// <summary>开机时初始化用，允许空值（例如 DOS 2.0 默认就有一行 "PATH="）。</summary>
    public void Init(string name, string value) => _vars.Add(new(name.ToUpperInvariant(), value));

    public DosEnvironment Clone()
    {
        var copy = new DosEnvironment(Capacity);
        copy._vars.AddRange(_vars);
        return copy;
    }

    private int UsedBytes()
    {
        int n = 1;
        foreach (var kv in _vars) n += EntrySize(kv.Key, kv.Value);
        return n;
    }

    private static int EntrySize(string key, string value)
        => System.Text.Encoding.UTF8.GetByteCount(key) + 1 + System.Text.Encoding.UTF8.GetByteCount(value) + 1;
}
