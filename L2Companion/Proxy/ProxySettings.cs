namespace L2Companion.Proxy;

public sealed class ProxySettings
{
    public string LoginHost { get; set; } = "127.0.0.1";
    public int LoginPort { get; set; } = 2106;
    public string GameHost { get; set; } = "127.0.0.1";
    public int GamePort { get; set; } = 7777;

    public int LocalLoginPort { get; set; } = 2106;
    public int LocalGamePort { get; set; } = 7777;

    public ServerProfileMode ServerProfileMode { get; set; } = ServerProfileMode.AutoDetect;
}

public enum ServerProfileMode
{
    AutoDetect,
    TeonLike,
    ClassicL2J
}
