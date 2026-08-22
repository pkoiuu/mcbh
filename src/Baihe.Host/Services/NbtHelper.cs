// NBT 工具 — Minecraft NBT (Named Binary Tag) 格式的通用读取/写入
// 用于读写 servers.dat（服务器列表）
// 格式要点: 大端字节序；String = u16 长度 + UTF-8；List = i8 元素类型 + i32 数量；
//          Compound = 一系列 (i8 类型 + u16 名称长度 + 名称 + 载荷) 直到 TAG_End(0)
// 读取时保留所有 tag 类型与顺序，写入时原样序列化，避免破坏未知字段
// 注意: 必须使用大端读取（BinaryPrimitives.*BigEndian），BinaryReader 默认小端会导致错位

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace Baihe.Host.Services;

/// <summary>
/// NBT 通用工具 — 解析/序列化 Minecraft NBT 数据
/// </summary>
public static class NbtHelper
{
    // NBT tag 类型
    private const byte TagEnd = 0;
    private const byte TagByte = 1;
    private const byte TagShort = 2;
    private const byte TagInt = 3;
    private const byte TagLong = 4;
    private const byte TagFloat = 5;
    private const byte TagDouble = 6;
    private const byte TagByteArray = 7;
    private const byte TagString = 8;
    private const byte TagList = 9;
    private const byte TagCompound = 10;
    private const byte TagIntArray = 11;
    private const byte TagLongArray = 12;

    /// <summary>NBT 节点（递归结构，保留原始顺序）</summary>
    public abstract class NbtTag
    {
        public abstract byte Type { get; }
    }

    public sealed class NbtEnd : NbtTag
    {
        public override byte Type => TagEnd;
    }

    public sealed class NbtByte : NbtTag
    {
        public sbyte Value;
        public override byte Type => TagByte;
    }

    public sealed class NbtShort : NbtTag
    {
        public short Value;
        public override byte Type => TagShort;
    }

    public sealed class NbtInt : NbtTag
    {
        public int Value;
        public override byte Type => TagInt;
    }

    public sealed class NbtLong : NbtTag
    {
        public long Value;
        public override byte Type => TagLong;
    }

    public sealed class NbtFloat : NbtTag
    {
        public float Value;
        public override byte Type => TagFloat;
    }

    public sealed class NbtDouble : NbtTag
    {
        public double Value;
        public override byte Type => TagDouble;
    }

    public sealed class NbtByteArray : NbtTag
    {
        public byte[] Value = Array.Empty<byte>();
        public override byte Type => TagByteArray;
    }

    public sealed class NbtString : NbtTag
    {
        public string Value = "";
        public override byte Type => TagString;
    }

    public sealed class NbtList : NbtTag
    {
        public byte ElementType;
        public List<NbtTag> Items = new();
        public override byte Type => TagList;
    }

    public sealed class NbtCompound : NbtTag
    {
        /// <summary>tag 名称（根节点通常为空字符串）</summary>
        public string Name = "";
        public List<(string Name, NbtTag Tag)> Entries = new();
        public override byte Type => TagCompound;

        public NbtTag? Get(string name)
        {
            foreach (var (n, t) in Entries)
                if (n == name) return t;
            return null;
        }

        public string? GetString(string name)
        {
            var t = Get(name);
            return t is NbtString s ? s.Value : null;
        }

        public byte? GetByte(string name)
        {
            var t = Get(name);
            return t is NbtByte b ? (byte)b.Value : null;
        }

        public void Set(string name, NbtTag tag)
        {
            for (var i = 0; i < Entries.Count; i++)
            {
                if (Entries[i].Name == name)
                {
                    Entries[i] = (name, tag);
                    return;
                }
            }
            Entries.Add((name, tag));
        }
    }

    public sealed class NbtIntArray : NbtTag
    {
        public int[] Value = Array.Empty<int>();
        public override byte Type => TagIntArray;
    }

    public sealed class NbtLongArray : NbtTag
    {
        public long[] Value = Array.Empty<long>();
        public override byte Type => TagLongArray;
    }

    // =========================================================================
    // 大端读取辅助（NBT 为大端序）
    // =========================================================================

    private sealed class NbtReader
    {
        private readonly byte[] _data;
        private int _pos;

        public NbtReader(byte[] data) { _data = data; }

        public byte ReadByte()
        {
            if (_pos >= _data.Length) throw new EndOfStreamException();
            return _data[_pos++];
        }

        public short ReadShort()
        {
            if (_pos + 2 > _data.Length) throw new EndOfStreamException();
            var v = BinaryPrimitives.ReadInt16BigEndian(_data.AsSpan(_pos, 2));
            _pos += 2;
            return v;
        }

        public ushort ReadUShort()
        {
            if (_pos + 2 > _data.Length) throw new EndOfStreamException();
            var v = BinaryPrimitives.ReadUInt16BigEndian(_data.AsSpan(_pos, 2));
            _pos += 2;
            return v;
        }

        public int ReadInt()
        {
            if (_pos + 4 > _data.Length) throw new EndOfStreamException();
            var v = BinaryPrimitives.ReadInt32BigEndian(_data.AsSpan(_pos, 4));
            _pos += 4;
            return v;
        }

        public long ReadLong()
        {
            if (_pos + 8 > _data.Length) throw new EndOfStreamException();
            var v = BinaryPrimitives.ReadInt64BigEndian(_data.AsSpan(_pos, 8));
            _pos += 8;
            return v;
        }

        public float ReadFloat()
        {
            var bits = ReadInt();
            return BitConverter.Int32BitsToSingle(bits);
        }

        public double ReadDouble()
        {
            var bits = ReadLong();
            return BitConverter.Int64BitsToDouble(bits);
        }

        public byte[] ReadBytes(int len)
        {
            if (_pos + len > _data.Length) throw new EndOfStreamException();
            var arr = new byte[len];
            Array.Copy(_data, _pos, arr, 0, len);
            _pos += len;
            return arr;
        }

        public string ReadString()
        {
            var len = ReadUShort();
            return Encoding.UTF8.GetString(ReadBytes(len));
        }

        public int Position => _pos;
    }

    // =========================================================================
    // 读取
    // =========================================================================

    /// <summary>
    /// 从文件读取 NBT 根节点 — 自动检测 gzip；根必须为 TAG_Compound
    /// </summary>
    public static NbtCompound? ReadFile(string path)
    {
        var bytes = File.ReadAllBytes(path);
        return Read(bytes) as NbtCompound;
    }

    /// <summary>从字节数组读取 NBT（含 gzip 检测），返回根节点</summary>
    public static NbtTag? Read(byte[] data)
    {
        byte[] payload;
        if (data.Length >= 2 && data[0] == 0x1F && data[1] == 0x8B)
        {
            using var ms = new MemoryStream(data);
            using var gz = new GZipStream(ms, CompressionMode.Decompress);
            using var outMs = new MemoryStream();
            gz.CopyTo(outMs);
            payload = outMs.ToArray();
        }
        else
        {
            payload = data;
        }

        var reader = new NbtReader(payload);
        var type = reader.ReadByte();
        var name = reader.ReadString();
        var root = ReadPayload(reader, type);
        if (root is NbtCompound rc)
            rc.Name = name;
        return root;
    }

    // =========================================================================
    // 写入
    // =========================================================================

    /// <summary>
    /// 将 NBT 根节点写为未压缩格式的字节数组（servers.dat 使用未压缩 NBT）
    /// </summary>
    public static byte[] WriteUncompressed(NbtCompound root)
    {
        using var ms = new MemoryStream();
        WriteNamedTag(ms, root);
        return ms.ToArray();
    }

    /// <summary>
    /// 写文件 — 保持与源文件一致的压缩方式（gzip 或未压缩），文件不存在时用未压缩
    /// </summary>
    public static void WriteFile(string path, NbtCompound root, bool gzip = false)
    {
        var dir = Path.GetDirectoryName(path);
        if (dir != null) Directory.CreateDirectory(dir);

        using var fs = new FileStream(path, FileMode.Create, FileAccess.Write);
        Stream stream = fs;
        if (gzip)
            stream = new GZipStream(fs, CompressionLevel.Optimal);

        using (var ms = new MemoryStream())
        {
            WriteNamedTag(ms, root);
            ms.Position = 0;
            ms.CopyTo(stream);
        }
    }

    /// <summary>写入带名称的根 tag（type + name + payload）</summary>
    private static void WriteNamedTag(MemoryStream ms, NbtCompound root)
    {
        ms.WriteByte(root.Type);
        WriteString(ms, root.Name);
        WritePayload(ms, root);
    }

    // =========================================================================
    // 内部实现
    // =========================================================================

    private static NbtTag ReadPayload(NbtReader r, byte type)
    {
        switch (type)
        {
            case TagEnd: return new NbtEnd();
            case TagByte: return new NbtByte { Value = unchecked((sbyte)r.ReadByte()) };
            case TagShort: return new NbtShort { Value = r.ReadShort() };
            case TagInt: return new NbtInt { Value = r.ReadInt() };
            case TagLong: return new NbtLong { Value = r.ReadLong() };
            case TagFloat: return new NbtFloat { Value = r.ReadFloat() };
            case TagDouble: return new NbtDouble { Value = r.ReadDouble() };
            case TagByteArray: return new NbtByteArray { Value = r.ReadBytes(r.ReadInt()) };
            case TagString: return new NbtString { Value = r.ReadString() };
            case TagList:
            {
                var elemType = r.ReadByte();
                var count = r.ReadInt();
                var list = new NbtList { ElementType = elemType };
                for (var i = 0; i < count; i++)
                    list.Items.Add(ReadPayload(r, elemType));
                return list;
            }
            case TagCompound:
            {
                var compound = new NbtCompound();
                while (true)
                {
                    var childType = r.ReadByte();
                    if (childType == TagEnd)
                        break;
                    var childName = r.ReadString();
                    var child = ReadPayload(r, childType);
                    compound.Entries.Add((childName, child));
                }
                return compound;
            }
            case TagIntArray:
            {
                var len = r.ReadInt();
                var arr = new int[len];
                for (var i = 0; i < len; i++) arr[i] = r.ReadInt();
                return new NbtIntArray { Value = arr };
            }
            case TagLongArray:
            {
                var len = r.ReadInt();
                var arr = new long[len];
                for (var i = 0; i < len; i++) arr[i] = r.ReadLong();
                return new NbtLongArray { Value = arr };
            }
            default:
                throw new InvalidDataException($"未知 NBT tag 类型: {type}");
        }
    }

    private static void WriteString(MemoryStream ms, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        Span<byte> lenBuf = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(lenBuf, (ushort)bytes.Length);
        ms.Write(lenBuf);
        ms.Write(bytes);
    }

    private static void WritePayload(MemoryStream ms, NbtTag tag)
    {
        switch (tag)
        {
            case NbtByte t: ms.WriteByte(unchecked((byte)t.Value)); break;
            case NbtShort t:
            {
                Span<byte> buf = stackalloc byte[2];
                BinaryPrimitives.WriteInt16BigEndian(buf, t.Value);
                ms.Write(buf);
                break;
            }
            case NbtInt t:
            {
                Span<byte> buf = stackalloc byte[4];
                BinaryPrimitives.WriteInt32BigEndian(buf, t.Value);
                ms.Write(buf);
                break;
            }
            case NbtLong t:
            {
                Span<byte> buf = stackalloc byte[8];
                BinaryPrimitives.WriteInt64BigEndian(buf, t.Value);
                ms.Write(buf);
                break;
            }
            case NbtFloat t:
            {
                Span<byte> buf = stackalloc byte[4];
                BinaryPrimitives.WriteInt32BigEndian(buf, BitConverter.SingleToInt32Bits(t.Value));
                ms.Write(buf);
                break;
            }
            case NbtDouble t:
            {
                Span<byte> buf = stackalloc byte[8];
                BinaryPrimitives.WriteInt64BigEndian(buf, BitConverter.DoubleToInt64Bits(t.Value));
                ms.Write(buf);
                break;
            }
            case NbtByteArray t:
            {
                Span<byte> lenBuf = stackalloc byte[4];
                BinaryPrimitives.WriteInt32BigEndian(lenBuf, t.Value.Length);
                ms.Write(lenBuf);
                ms.Write(t.Value);
                break;
            }
            case NbtString t:
                WriteString(ms, t.Value);
                break;
            case NbtList t:
            {
                ms.WriteByte(t.ElementType);
                Span<byte> lenBuf = stackalloc byte[4];
                BinaryPrimitives.WriteInt32BigEndian(lenBuf, t.Items.Count);
                ms.Write(lenBuf);
                foreach (var item in t.Items)
                    WritePayload(ms, item);
                break;
            }
            case NbtCompound t:
            {
                foreach (var (name, child) in t.Entries)
                {
                    ms.WriteByte(child.Type);
                    WriteString(ms, name);
                    WritePayload(ms, child);
                }
                ms.WriteByte(TagEnd);
                break;
            }
            case NbtIntArray t:
            {
                Span<byte> lenBuf = stackalloc byte[4];
                BinaryPrimitives.WriteInt32BigEndian(lenBuf, t.Value.Length);
                ms.Write(lenBuf);
                Span<byte> buf = stackalloc byte[4];
                foreach (var v in t.Value)
                {
                    BinaryPrimitives.WriteInt32BigEndian(buf, v);
                    ms.Write(buf);
                }
                break;
            }
            case NbtLongArray t:
            {
                Span<byte> lenBuf = stackalloc byte[4];
                BinaryPrimitives.WriteInt32BigEndian(lenBuf, t.Value.Length);
                ms.Write(lenBuf);
                Span<byte> buf = stackalloc byte[8];
                foreach (var v in t.Value)
                {
                    BinaryPrimitives.WriteInt64BigEndian(buf, v);
                    ms.Write(buf);
                }
                break;
            }
            case NbtEnd: break;
        }
    }
}
