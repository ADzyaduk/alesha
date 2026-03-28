using Org.BouncyCastle.Crypto.Engines;
using Org.BouncyCastle.Crypto.Parameters;

namespace L2Companion.Protocol;

public sealed class L2Blowfish
{
    private readonly BlowfishEngine _decrypt = new();
    private readonly BlowfishEngine _encrypt = new();

    public L2Blowfish(byte[] key)
    {
        var kp = new KeyParameter(key);
        _decrypt.Init(false, kp);
        _encrypt.Init(true, kp);
    }

    public void DecryptInPlace(byte[] data)
    {
        for (var i = 0; i < data.Length; i += 8)
        {
            SwapWords(data, i);
            _decrypt.ProcessBlock(data, i, data, i);
            SwapWords(data, i);
        }
    }

    public void EncryptInPlace(byte[] data)
    {
        for (var i = 0; i < data.Length; i += 8)
        {
            SwapWords(data, i);
            _encrypt.ProcessBlock(data, i, data, i);
            SwapWords(data, i);
        }
    }

    private static void SwapWords(byte[] data, int offset)
    {
        Array.Reverse(data, offset, 4);
        Array.Reverse(data, offset + 4, 4);
    }
}
