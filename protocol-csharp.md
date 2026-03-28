# L2 Interlude — Protocol Reference (for C# implementation)

Документация по протоколу Lineage 2 Interlude на основе реверса MITM-прокси.
Цель: достаточно информации чтобы написать аналогичный прокси/бот на C#.

---

## Содержание

1. [Схема подключения](#схема-подключения)
2. [Формат пакетов](#формат-пакетов)
3. [Шифрование](#шифрование)
4. [Порядок инициализации соединения](#порядок-инициализации-соединения)
5. [Login-пакеты](#login-пакеты)
6. [Game-пакеты — Server → Client](#game-пакеты--server--client)
7. [Game-пакеты — Client → Server](#game-пакеты--client--server)
8. [Структуры пакетов](#структуры-пакетов)
9. [Детектирование XOR-ключа (Teon/Elmorelab)](#детектирование-xor-ключа-teonelmorelab)
10. [Состояние мира](#состояние-мира)
11. [Реализация на C#](#реализация-на-c)

---

## Схема подключения

```
L2.exe ──TCP──► localhost:2106  [LoginProxy]  ──TCP──► Реальный Login :2106
L2.exe ──TCP──► localhost:7777  [GameProxy]   ──TCP──► Реальный Game  :7777
```

**LoginProxy** перехватывает пакет `ServerList` и заменяет в нём адрес игрового
сервера на `127.0.0.1:7777`, чтобы клиент пошёл через GameProxy.

**GameProxy** — прозрачный релей. Байты пересылаются as-is. Параллельно ведётся
**теневое дешифрование** (shadow decrypt) копии потока для чтения состояния мира.

---

## Формат пакетов

Одинаков для Login и Game:

```
[uint16 LE: total_length] [зашифрованное тело]
```

`total_length` включает сами 2 байта заголовка.

После расшифровки тело выглядит так:

```
[byte: opcode] [payload...] [uint32: XOR-checksum]  ← S→C
[byte: opcode] [payload...]                          ← C→S (без checksum)
```

Тело всегда выровнено до кратного **8 байтам** (padding нулями перед шифрованием).

### Checksum (S→C, перед шифрованием)

```
Последние 4 байта тела = XOR всех предыдущих DWORD (uint32 LE)
```

```csharp
static void AppendChecksum(byte[] buf, int dataLen) {
    // dataLen = длина данных без 4 байт checksum
    uint cs = 0;
    for (int i = 0; i < dataLen; i += 4)
        cs ^= BitConverter.ToUInt32(buf, i);
    BitConverter.GetBytes(cs).CopyTo(buf, dataLen);
}
```

---

## Шифрование

### Слой 1 — Blowfish ECB (всегда, и Login и Game)

**Нестандартность L2:** LE порядок слов внутри каждого 8-байтного блока.
Каждое 4-байтное слово нужно развернуть до и после операции шифра.

```csharp
// NuGet: BouncyCastle.NetCore
using Org.BouncyCastle.Crypto.Engines;
using Org.BouncyCastle.Crypto.Parameters;

class L2Blowfish {
    readonly BlowfishEngine _engine = new();

    public L2Blowfish(byte[] key) =>
        _engine.Init(false, new KeyParameter(key));

    public void Decrypt(byte[] data) {
        for (int i = 0; i < data.Length; i += 8) {
            SwapWords(data, i);
            _engine.ProcessBlock(data, i, data, i);
            SwapWords(data, i);
        }
    }

    public void Encrypt(byte[] data) {
        var enc = new BlowfishEngine();
        enc.Init(true, _engine /* переиспользовать параметры */);
        for (int i = 0; i < data.Length; i += 8) {
            SwapWords(data, i);
            enc.ProcessBlock(data, i, data, i);
            SwapWords(data, i);
        }
    }

    static void SwapWords(byte[] b, int off) {
        Array.Reverse(b, off,     4);
        Array.Reverse(b, off + 4, 4);
    }
}
```

**Ключи:**

| Контекст | Источник ключа |
|----------|----------------|
| Login (до Init) | не используется — первый пакет plaintext |
| Login (после Init) | 8 байт из пакета `Init (0x00)` |
| Game (до BlowfishInit) | дефолтный ключ Interlude (см. ниже) |
| Game (после BlowfishInit) | dynamic_key(8B) + статичный суффикс(8B) |

**Дефолтный ключ Interlude (16 байт):**
```
6B 60 CB 5B 82 CE 90 B1  CC 2B 6C 55 6C 6C 6C 6C
```

**Ключ из BlowfishInit:**
```
Пакет BlowfishInit:
  [1B  revision]
  [8B  dynamic_key]   ← берём это
  [4B  enc_flag]
  [4B  server_id]

Полный ключ (16B) = dynamic_key(8B) + статичный суффикс(8B):
  C8 27 93 01  A1 6C 31 97
```

---

### Слой 2 — L2GameCrypt XOR (только Game-сервер, поверх Blowfish)

Потоковый XOR с эволюционирующим счётчиком. Применяется **к расшифрованным Blowfish данным**.

```csharp
class L2GameCrypt {
    readonly byte[] _key = new byte[16];
    byte _prevRaw;

    public void Init(byte[] seed) => seed.CopyTo(_key, 0);

    public void Decrypt(byte[] data) {
        for (int i = 0; i < data.Length; i++) {
            byte raw = data[i];
            data[i] = (byte)(raw ^ _key[i & 15] ^ _prevRaw);
            _prevRaw = raw;
        }
        AdvanceKey(data.Length);
    }

    public void Encrypt(byte[] data) {
        byte carry = 0;
        for (int i = 0; i < data.Length; i++) {
            byte enc = (byte)(data[i] ^ _key[i & 15] ^ carry);
            carry = enc;
            data[i] = enc;
        }
        AdvanceKey(data.Length);
    }

    void AdvanceKey(int packetLen) {
        uint counter = BitConverter.ToUInt32(_key, 8);
        counter += (uint)packetLen;
        BitConverter.GetBytes(counter).CopyTo(_key, 8);
    }
}
```

**Когда инициализируется:** при получении `BlowfishInit (0x00)` — берётся `dynamic_key` как seed.

> **Важно:** для инжекта пакетов нужны **отдельные экземпляры** шифров для наблюдения
> и для инжекта, иначе счётчики разъедутся и сессия сломается.

---

## Порядок инициализации соединения

```
1.  Client → LoginProxy  TCP connect :2106
2.  LoginProxy → Server  TCP connect real:2106
3.  Server → Client:     Init(0x00)  [plaintext] — содержит 8-байтный Blowfish key
4.  LoginProxy:          инициализирует Blowfish этим ключом
5.  Client → Server:     AuthLogin(0x00) [Blowfish]
6.  Server → Client:     ServerList(0x04) — LoginProxy патчит IP → 127.0.0.1:7777
7.  Client → Server:     RequestPlay(0x02)
8.  Server → Client:     PlayOk(0x07) — сохраняем токены login_ok1/2, play_ok1/2

--- клиент подключается к 127.0.0.1:7777 ---

9.  Client → GameProxy   TCP connect :7777
10. GameProxy → Server   TCP connect real:7777
11. Server → Client:     BlowfishInit(0x00) [plaintext] — новый ключ для игры
12. GameProxy:           инициализирует Blowfish + L2GameCrypt
13. Client → Server:     AuthLogin(0x08) [Blowfish+XOR] — play_ok токены
14. Client → Server:     CharSelected(0x09) — выбор персонажа
15. Server → Client:     поток пакетов: UserInfo, NpcInfo, SkillList, ...
```

---

## Login-пакеты

### Server → Client

| Opcode | Имя | Шифрование | Ключевые поля |
|--------|-----|-----------|---------------|
| `0x00` | Init | plaintext | `[4B session_id][4B protocol_rev][128B RSA_modulus][8B blowfish_key][4B unk]` |
| `0x04` | ServerList | Blowfish | список серверов: `[2B count][per server: 1B id, IP string, 2B port, ...]` — **патчим IP/port** |
| `0x07` | PlayOk | Blowfish | `[4B login_ok1][4B login_ok2][4B play_ok1][4B play_ok2]` |
| `0x01` | LoginFail | Blowfish | `[4B reason_code]` |

### Client → Server

| Opcode | Имя | Ключевые поля |
|--------|-----|---------------|
| `0x00` | AuthLogin | логин/пароль, RSA-зашифрованные |
| `0x02` | RequestPlay | `[1B server_id][4B play_ok1][4B play_ok2]` |

---

## Game-пакеты — Server → Client

### Стандартные opcodes (Interlude / L2J base)

> На **Teon/Elmorelab** все opcodes scrambled через XOR с сессионным ключом.
> Детектирование — см. раздел [ниже](#детектирование-xor-ключа-teonelmorelab).

| Base opcode | Имя | Размер payload | Назначение |
|-------------|-----|---------------|------------|
| `0x00` | BlowfishInit | 17B | `[1B rev][8B dyn_key][4B enc_flag][4B server_id]` — первый пакет, plaintext |
| `0x04` | UserInfo | ~468B | Полные данные своего персонажа (HP/MP/CP/pos/stats) |
| `0x16` | NpcInfo | **187B** | Моб/NPC появился в зоне видимости — **anchor для детектирования** |
| `0x01` | MoveToPoint | 24–28B | Юнит начал движение: `objectId + destXYZ + origXY[Z]` |
| `0x0E` | StatusUpdate | 8B+ | Изменение атрибутов любого объекта: `objectId + count + [attrId+value]×N` |
| `0x14` | ValidatePosition | 20B | Сервер корректирует позицию |
| `0x25` | ChangeWaitType | 8B | Сел/встал: `objectId + type(0=stand,1=sit)` |
| `0x12` | Die | 4B | Смерть объекта: `objectId` |
| `0x24` | TargetSelected | 16B | Подтверждение таргета: `objectId + targetId + origXYZ` |
| `0x0C` | SpawnItem | 32B | Предмет на земле: `objectId + itemId + count + xyz + unk` |
| `0x0B` | DeleteObject | 4B | Объект ушёл из зоны видимости: `objectId` |
| `0x58` | SkillList | 559B+ | Список известных скиллов |
| `0x1B` | ItemList | 4B+36B×N | Снимок инвентаря |
| `0x60` | Attack | 20B | Удар: `attackerId + targetId + damage + unk` |
| `0x48` | MagicSkillLaunched | 32B | Каст скилла: `casterId + targetId + skillId + level + timings` |
| `0x6A` | SkillCoolTime | 4B+12B×N | Кулдауны: `[skillId + remaining + total]×N` |
| `0x7F` | AbnormalStatusUpdate | 6B+ | Бафы/дебафы: `oid + count + [skillId+level+duration]×N` |
| `0x2D` | StopMove | 4B | Объект остановился |
| `0x4E` | PartySmallWindowAll | 6B+N×row | Все члены пати |
| `0x4F` | PartySmallWindowAdd | row | Добавление в пати |
| `0x50` | PartySmallWindowDelete | 4B | Выход из пати: `objectId` |
| `0x52` | PartySmallWindowUpdate | 4B+ | Обновление HP члена пати |

### StatusUpdate — attrId константы

```
0x01 = LEVEL
0x09 = CUR_HP
0x0A = MAX_HP
0x0B = CUR_MP
0x0C = MAX_MP
0x0D = CUR_EXP
0x0F = SP
0x21 = CUR_CP
0x22 = MAX_CP
```

---

## Game-пакеты — Client → Server

| Opcode | Имя | Payload | Назначение |
|--------|-----|---------|------------|
| `0x08` | AuthLogin | переменный | Авторизация: play_ok токены (из LoginProxy) |
| `0x09` | CharSelected | `[4B char_slot]` | Выбор персонажа |
| `0x5C` | RequestEnterWorld | 0B | Войти в мир (после CharSelected) |
| `0x01` | MoveBackwardToLocation | 28B: `destXYZ(3×4B) + origXYZ(3×4B) + moveMode(4B)` | Движение |
| `0x04` | Action | 17B: `objectId(4B) + origXYZ(3×4B) + actionId(1B)` | Таргет / взаимодействие |
| `0x0A` | AttackRequest | 17B: `objectId(4B) + origXYZ(3×4B) + shiftFlag(1B)` | Атака |
| `0x39` | RequestMagicSkillUse | см. ниже | Каст скилла |
| `0x37` | RequestTargetCancel | `2B(H: 0)` или `4B(D: 0)` | Снять таргет |
| `0x45` | RequestActionUse | 9B: `actionId(4B) + ctrl(4B) + shift(1B)` | Сесть/встать (`actionId=0`) |
| `0x48` | RequestGetItem | 20B: `xyz(3×4B) + objectId(4B) + pad(4B)` | Подобрать предмет |
| `0x19` | RequestItemUse | 8B: `objectId(4B) + ctrl(4B)` | Использовать предмет из инвентаря |
| `0x03` | RequestCharSelect | 0B | Вернуться к выбору персонажа |
| `0x9D` | RequestPing | 0B | Keepalive |

### RequestMagicSkillUse — три варианта payload (сервер-зависимо)

```
"dcb" = skillId(int32) + ctrl(int32) + shift(byte)  =  9 байт  ← стандарт L2J
"ddd" = skillId(int32) + ctrl(int32) + shift(int32)  = 12 байт  ← большинство приватов
"dcc" = skillId(int32) + ctrl(byte)  + shift(byte)   =  6 байт  ← редко
```

Определяется опытным путём: отправить `ddd`, если сессия рвётся — попробовать `dcb`.

---

## Структуры пакетов

Все int — `int32 LE`, short — `int16 LE`, long — `int64 LE`, строки — `UTF-16LE null-terminated`.

### NpcInfo (187 байт, base opcode `0x16`)

```
[0:4]    objectId (int)
[4:8]    npcTypeId (int)  = npcId + 1_000_000
[8:12]   isAttackable (int bool)
[12:16]  x  (int)
[16:20]  y  (int)
[20:24]  z  (int)
[24:28]  heading (int)
[28:32]  unk (int)
[32:36]  mAtk     (int)
[36:40]  atkSpeed (int)
[40:44]  pAtk     (int)
[44:48]  runSpeed (int)
[48:52]  walkSpeed (int)
[52:56]  swimRunSpeed  (int)
[56:60]  swimWalkSpeed (int)
[60:64]  flyRunSpeed   (int)
[64:68]  flyWalkSpeed  (int)
[68:72]  flyRunSpeed2  (int)
[72:76]  flyWalkSpeed2 (int)
[76:84]  movMult       (double)
[84:92]  atkSpeedMult  (double)
[92:100] colRadius     (double)
[100:108]colHeight     (double)
[108:112]rHandId (int)
[112:116]chest   (int)
[116:120]lHandId (int)
[120:121]isFlying (byte)
[121:122]team     (byte)
[122:130]colRadius2 (double)
[130:138]colHeight2 (double)
[138:142]enchantEffect (int)
[142:146]... (int)
[146:...] name  (UTF-16LE null-term)
[...    ] title (UTF-16LE null-term)
[+0:+4]  pvpFlag      (int)
[+4:+8]  karma        (int)
[+8:+12] abnormalEffect (int)
[+12:+16]clanId       (int)
[+16:+20]clanCrestId  (int)
[+20:+24]allyId       (int)
[+24:+28]allyCrestId  (int)
[+28:+29]isWalking    (byte)
[+29:+30]isInCombat   (byte)
[+30:+34]hpPercent    (int, 0-100)
```

Итого ровно **187 байт** — именно поэтому используется как anchor.

### UserInfo (~468 байт, base opcode `0x04`)

```
[0:4]    x  (int)
[4:8]    y  (int)
[8:12]   z  (int)
[12:16]  heading (int)
[16:20]  objectId (int)
[20:...] name  (UTF-16LE null-term)
[...]    race  (int)
[...]    sex   (int)
[...]    classId (int)
[...]    level   (int)
[...]    exp  (int64)
[...]    str, dex, con, int, wit, men  (int × 6)
[...]    maxHp, curHp (int × 2)
[...]    maxMp, curMp (int × 2)
[...]    sp   (int)
[...]    curLoad, maxLoad  (int × 2)
[...]    pakketid (int)
[...]    inventory_limit (int)
... [много полей с экипировкой, статами, флагами]
[...]    curCp, maxCp (int × 2)
```

### StatusUpdate (base opcode `0x0E`)

```
[0:4]  objectId (int)
[4:8]  count    (int)
Per attribute:
  [+0:+4]  attrId (int)
  [+4:+8]  value  (int)
```

### ChangeWaitType (base opcode `0x25`)

```
[0:4]  objectId (int)
[4:8]  type     (int)   0 = stand, 1 = sit, 2 = fake_death
```

### AbnormalStatusUpdate (base opcode `0x7F`) — несколько вариантов

```
Вариант A (основной, Teon):
  [0:4]  objectId (int)
  [4:6]  count    (short)
  Per effect:
    [+0:+4]  skillId  (int)
    [+4:+6]  level    (short)
    [+6:+10] duration (int, ms)

Вариант B (без objectId, S→C для себя):
  [0:4]  count (int)
  Per effect: как выше

Детектировать по размеру: если первые 4 байта <= 1000 и == кол-ву эффектов — вариант B.
```

### SpawnItem (base opcode `0x0C`)

```
[0:4]   objectId (int)
[4:8]   itemId   (int)
[8:12]  x  (int)
[12:16] y  (int)
[16:20] z  (int)
[20:24] stackable (int bool)
[24:32] count (int64)
[32:36] unk   (int)
```

---

## Детектирование XOR-ключа (Teon/Elmorelab)

На Teon сервере все opcodes S→C scrambled: `wire_opcode = base_opcode XOR session_key`.
`session_key` — 1 байт, разный для каждой сессии.

**Алгоритм:**

```csharp
// NpcInfo ВСЕГДА имеет payload ровно 187 байт
// Накапливаем кандидатов: wire_opcode → сколько раз пришёл с payload 187 байт
var candidates = new Dictionary<byte, int>();

void OnPacket(byte wireOpcode, int payloadLen) {
    if (payloadLen == 187)
        candidates[wireOpcode] = candidates.GetValueOrDefault(wireOpcode) + 1;

    if (candidates.Count > 0 && candidates.Values.Max() >= 5) {
        byte npcInfoWireOpcode = candidates.MaxBy(kv => kv.Value).Key;
        byte xorKey = (byte)(npcInfoWireOpcode ^ 0x16); // 0x16 = base NpcInfo opcode
        // Теперь для любого пакета: base_opcode = wire_opcode ^ xorKey
        OnOpcodeDetected(xorKey);
    }
}
```

После детектирования строим таблицу:
```csharp
byte Resolve(byte baseOpcode) => (byte)(baseOpcode ^ _xorKey);

var handlers = new Dictionary<byte, Action<byte[]>> {
    [Resolve(0x04)] = OnUserInfo,
    [Resolve(0x16)] = OnNpcInfo,
    [Resolve(0x0E)] = OnStatusUpdate,
    // ...
};
```

До детектирования буферизуем все пакеты и проигрываем их после.

---

## Состояние мира

Минимальный набор для бота:

```csharp
class GameWorld {
    public MyCharacter Me { get; set; }
    public Dictionary<int, Npc>         Npcs    = new();
    public HashSet<int>                 DeadIds = new();
    public Dictionary<int, GroundItem>  Items   = new();
    public Dictionary<int, int>         Skills  = new(); // skillId → level
    public Dictionary<int, PartyMember> Party   = new();
}

class MyCharacter {
    public int    ObjectId, X, Y, Z, Heading;
    public int    CurHp, MaxHp, CurMp, MaxMp, CurCp, MaxCp;
    public int    TargetId;
    public bool   IsSitting;
    public string Name = "";

    public float HpPct => MaxHp > 0 ? CurHp * 100f / MaxHp : 0;
    public float MpPct => MaxMp > 0 ? CurMp * 100f / MaxMp : 0;
}

class Npc {
    public int    ObjectId, NpcTypeId; // NpcTypeId = npcId + 1_000_000
    public int    X, Y, Z;
    public bool   IsAttackable, IsDead;
    public float  HpPct;        // 0–100, из NpcInfo или StatusUpdate
    public string Name = "";
}

class GroundItem {
    public int ObjectId, ItemId;
    public int X, Y, Z;
    public long Count;
}
```

---

## Реализация на C#

### Скелет прокси

```csharp
class GameProxy {
    readonly TcpListener _listener = new(IPAddress.Loopback, 7777);

    public async Task RunAsync() {
        _listener.Start();
        while (true) {
            var client = await _listener.AcceptTcpClientAsync();
            _ = HandleClientAsync(client);
        }
    }

    async Task HandleClientAsync(TcpClient client) {
        using var server = new TcpClient(realGameHost, realGamePort);

        var cStream = client.GetStream();
        var sStream = server.GetStream();

        // Два независимых relay
        var t1 = RelayAsync(sStream, cStream, isS2C: true);
        var t2 = RelayAsync(cStream, sStream, isS2C: false);
        await Task.WhenAny(t1, t2);
    }

    async Task RelayAsync(NetworkStream from, NetworkStream to, bool isS2C) {
        while (true) {
            // Заголовок
            var header = new byte[2];
            await ReadExactAsync(from, header);
            int totalLen = BitConverter.ToUInt16(header, 0);
            int bodyLen  = totalLen - 2;

            // Тело
            var body = new byte[bodyLen];
            await ReadExactAsync(from, body);

            // Пересылаем оригинал
            await to.WriteAsync(header);
            await to.WriteAsync(body);

            // Теневое дешифрование для анализа
            if (isS2C) {
                var copy = (byte[])body.Clone();
                _shadowBlowfish.Decrypt(copy);   // Blowfish
                _shadowXor.Decrypt(copy);         // L2GameCrypt (если инициализирован)
                OnServerPacket(copy[0], copy.AsSpan(1));
            }
        }
    }

    static async Task ReadExactAsync(NetworkStream stream, byte[] buf) {
        int read = 0;
        while (read < buf.Length)
            read += await stream.ReadAsync(buf, read, buf.Length - read);
    }
}
```

### Инжект пакета к серверу

```csharp
// Отдельные экземпляры шифров для инжекта (не путать с shadow decrypt)
async Task InjectToServer(byte opcode, byte[] payload) {
    // 1. Собрать тело: opcode + payload
    // 2. Padding до кратного 8
    // 3. XOR-зашифровать (_injectXor.Encrypt)
    // 4. Blowfish-зашифровать (_injectBlowfish.Encrypt)
    // 5. Добавить uint16 заголовок длины
    // 6. Отправить в серверный поток
}
```

### NuGet зависимости

```xml
<PackageReference Include="BouncyCastle.NetCore" Version="2.2.1" />
```

Всё остальное — `System.Net.Sockets`, `System.Text`, `System.IO` из BCL.

---

*Основано на реверсе Python-реализации против Teon/Elmorelab L2 Interlude.*
