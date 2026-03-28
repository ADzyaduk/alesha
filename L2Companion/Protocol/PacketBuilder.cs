using System.Buffers.Binary;

namespace L2Companion.Protocol;

public static class PacketBuilder
{
    public static byte[] BuildAction(int objectId, int x, int y, int z, byte actionId = 0)
    {
        var payload = new byte[17];
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(0, 4), objectId);
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(4, 4), x);
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(8, 4), y);
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(12, 4), z);
        payload[16] = actionId;
        return Build(0x04, payload);
    }

    public static byte[] BuildAttackRequest(int objectId, int x, int y, int z, byte shift = 0)
    {
        var payload = new byte[17];
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(0, 4), objectId);
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(4, 4), x);
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(8, 4), y);
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(12, 4), z);
        payload[16] = shift;
        return Build(0x0A, payload);
    }

    public static byte[] BuildMoveToLocation(int destX, int destY, int destZ, int origX, int origY, int origZ, int moveMode = 1)
    {
        var payload = new byte[28];
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(0, 4), destX);
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(4, 4), destY);
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(8, 4), destZ);
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(12, 4), origX);
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(16, 4), origY);
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(20, 4), origZ);
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(24, 4), moveMode);
        return Build(0x01, payload);
    }

    public static byte[] BuildForceAttack(int objectId, int x, int y, int z)
    {
        _ = objectId;
        _ = x;
        _ = y;
        _ = z;
        return BuildShortcutSkillUse(16, ctrl: 0, shift: 0);
    }

    public static byte[] BuildShortcutSkillUse(int actionOrSkillId, int ctrl = 0, int shift = 0)
    {
        var payload = new byte[9];
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(0, 4), actionOrSkillId);
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(4, 4), ctrl);
        payload[8] = (byte)(shift != 0 ? 1 : 0);
        return Build(0x2F, payload);
    }

    public static byte[] BuildActionUse(int actionId, int ctrl = 0, int shift = 0)
    {
        var payload = new byte[9];
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(0, 4), actionId);
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(4, 4), ctrl);
        payload[8] = (byte)(shift != 0 ? 1 : 0);
        return Build(0x45, payload);
    }

    public static byte[] BuildGetItem(int x, int y, int z, int objectId)
    {
        var payload = new byte[20];
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(0, 4), x);
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(4, 4), y);
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(8, 4), z);
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(12, 4), objectId);
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(16, 4), 0);
        return Build(0x48, payload);
    }

    public static byte[] BuildMagicSkillUse(int skillId, int ctrl = 0, int shift = 0, string payloadStyle = "ddd")
    {
        payloadStyle = (payloadStyle ?? "ddd").Trim().ToLowerInvariant();

        if (payloadStyle == "dcc")
        {
            var payload = new byte[6];
            BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(0, 4), skillId);
            payload[4] = (byte)(ctrl != 0 ? 1 : 0);
            payload[5] = (byte)(shift != 0 ? 1 : 0);
            return Build(0x39, payload);
        }

        if (payloadStyle == "dcb")
        {
            var payload = new byte[9];
            BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(0, 4), skillId);
            BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(4, 4), ctrl);
            payload[8] = (byte)(shift != 0 ? 1 : 0);
            return Build(0x39, payload);
        }

        var dddPayload = new byte[12];
        BinaryPrimitives.WriteInt32LittleEndian(dddPayload.AsSpan(0, 4), skillId);
        BinaryPrimitives.WriteInt32LittleEndian(dddPayload.AsSpan(4, 4), ctrl);
        BinaryPrimitives.WriteInt32LittleEndian(dddPayload.AsSpan(8, 4), shift);
        return Build(0x39, dddPayload);
    }

    public static byte[] BuildTargetCancel(bool dwordPayload = false)
    {
        return dwordPayload
            ? Build(0x37, [0x00, 0x00, 0x00, 0x00])
            : Build(0x37, [0x00, 0x00]);
    }

    private static byte[] Build(byte opcode, byte[] payload)
    {
        var body = new byte[1 + payload.Length];
        body[0] = opcode;
        Buffer.BlockCopy(payload, 0, body, 1, payload.Length);
        return body;
    }
}