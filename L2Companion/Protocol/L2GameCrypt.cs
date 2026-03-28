namespace L2Companion.Protocol;

public sealed class L2GameCrypt
{
    private readonly byte[] _key = new byte[16];

    public bool IsInitialized { get; private set; }

    public void Init(byte[] seed)
    {
        Array.Clear(_key);
        var copyLen = Math.Min(_key.Length, seed.Length);
        Buffer.BlockCopy(seed, 0, _key, 0, copyLen);
        IsInitialized = true;
    }

    public void DecryptInPlace(byte[] data)
    {
        byte prevRaw = 0;
        for (var i = 0; i < data.Length; i++)
        {
            var raw = data[i];
            data[i] = (byte)(raw ^ _key[i & 15] ^ prevRaw);
            prevRaw = raw;
        }

        AdvanceKey(data.Length);
    }

    public void EncryptInPlace(byte[] data)
    {
        byte carry = 0;
        for (var i = 0; i < data.Length; i++)
        {
            var enc = (byte)(data[i] ^ _key[i & 15] ^ carry);
            carry = enc;
            data[i] = enc;
        }

        AdvanceKey(data.Length);
    }

    public void AdvanceByLength(int packetLen)
    {
        AdvanceKey(packetLen);
    }

    private void AdvanceKey(int packetLen)
    {
        var counter = BitConverter.ToUInt32(_key, 8);
        counter += (uint)packetLen;
        BitConverter.GetBytes(counter).CopyTo(_key, 8);
    }
}
