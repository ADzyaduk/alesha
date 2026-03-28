using System.Buffers.Binary;
using System.Text;

namespace L2Companion.Protocol;

public sealed class PacketReader
{
    private readonly byte[] _data;
    private int _offset;

    public PacketReader(ReadOnlySpan<byte> data)
    {
        _data = data.ToArray();
        _offset = 0;
    }

    public int Remaining => _data.Length - _offset;

    public byte ReadByte()
    {
        var v = _data[_offset];
        _offset += 1;
        return v;
    }

    public short ReadInt16()
    {
        var v = BinaryPrimitives.ReadInt16LittleEndian(_data.AsSpan(_offset, 2));
        _offset += 2;
        return v;
    }

    public int ReadInt32()
    {
        var v = BinaryPrimitives.ReadInt32LittleEndian(_data.AsSpan(_offset, 4));
        _offset += 4;
        return v;
    }

    public long ReadInt64()
    {
        var v = BinaryPrimitives.ReadInt64LittleEndian(_data.AsSpan(_offset, 8));
        _offset += 8;
        return v;
    }

    public ReadOnlySpan<byte> ReadBytes(int len)
    {
        var span = _data.AsSpan(_offset, len);
        _offset += len;
        return span;
    }

    public void Skip(int len) => _offset = Math.Min(_offset + len, _data.Length);

    public string ReadUnicodeString()
    {
        var start = _offset;
        while (_offset + 1 < _data.Length)
        {
            if (_data[_offset] == 0 && _data[_offset + 1] == 0)
            {
                var len = _offset - start;
                var s = Encoding.Unicode.GetString(_data.AsSpan(start, len));
                _offset += 2;
                return s;
            }

            _offset += 2;
        }

        return string.Empty;
    }
}
