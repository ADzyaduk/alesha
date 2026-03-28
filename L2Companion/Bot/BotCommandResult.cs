namespace L2Companion.Bot;

public enum BotCommandStatus
{
    Sent,
    Rejected,
    Deferred,
    Error
}

public sealed class BotCommandResult
{
    public BotCommandStatus Status { get; init; }
    public string Action { get; init; } = string.Empty;
    public string Reason { get; init; } = string.Empty;
    public byte Opcode { get; init; }

    public static BotCommandResult Sent(string action, byte opcode)
        => new() { Status = BotCommandStatus.Sent, Action = action, Opcode = opcode, Reason = "ok" };

    public static BotCommandResult Rejected(string action, string reason, byte opcode = 0)
        => new() { Status = BotCommandStatus.Rejected, Action = action, Opcode = opcode, Reason = reason };

    public static BotCommandResult Deferred(string action, string reason, byte opcode = 0)
        => new() { Status = BotCommandStatus.Deferred, Action = action, Opcode = opcode, Reason = reason };

    public static BotCommandResult Error(string action, string reason, byte opcode = 0)
        => new() { Status = BotCommandStatus.Error, Action = action, Opcode = opcode, Reason = reason };

    public override string ToString()
        => $"{Status} {Action} op=0x{Opcode:X2} {Reason}";
}
