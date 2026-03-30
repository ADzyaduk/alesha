using L2Companion.Bot;

namespace L2Companion.Bot.Roles;

public enum TickResult
{
    Continue,
    Yielded,
    Blocked
}

public interface IBotRole
{
    BotRole RoleType { get; }
    TickResult Tick(BotContext ctx);
    void Reset();
}
