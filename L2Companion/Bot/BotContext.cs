using L2Companion.Core;
using L2Companion.Proxy;
using L2Companion.World;

namespace L2Companion.Bot;

public readonly record struct BotContext(
    GameWorldState World,
    BotSettings Settings,
    DateTime Now,
    ProxyService Proxy,
    LogService Log);
