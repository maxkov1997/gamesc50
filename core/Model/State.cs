namespace Game.Core;

public enum Side { A = 0, B = 1 }

public enum PlanningMode { OpponentFirst, Simultaneous }

public enum Outcome { A, B, Draw }

/// <summary>Экземпляр карты колоды: постоянный instanceId на весь матч (4.1).</summary>
public sealed class CardInstance
{
    public int InstanceId { get; set; }
    public string CardId { get; set; } = "";

    /// <summary>Номера способностей, использованных в этом раунде (5.3). Сбрасывается в начале раунда.</summary>
    public List<int> UsedAbilities { get; set; } = new();
}

/// <summary>
/// Существо на столе — сущность. Токен — сущность без экземпляра (InstanceId == null).
/// Здесь хранится только постоянное состояние; урон и временные изменения живут в снимке боя.
/// </summary>
public sealed class Creature
{
    public int EntityId { get; set; }
    public int? InstanceId { get; set; }
    public string CardId { get; set; } = "";

    /// <summary>Постоянные усиления поверх базы карты.</summary>
    public int BonusAttack { get; set; }
    public int BonusHealth { get; set; }

    /// <summary>Ключевые слова, полученные эффектами «навсегда».</summary>
    public List<string> GainedKeywords { get; set; } = new();

    /// <summary>instanceId прикреплённых чар в порядке прикрепления.</summary>
    public List<int> Enchantments { get; set; } = new();

    /// <summary>Флаги способностей у токена (у карты они на экземпляре, 5.3).</summary>
    public List<int> UsedAbilities { get; set; } = new();
}

/// <summary>Состояние стороны (4.1). Списки зон хранят instanceId.</summary>
public sealed class PlayerState
{
    public Side Side { get; set; }
    public int HeroHp { get; set; }

    public int Mana { get; set; }
    public int ManaMax { get; set; }
    public int ManaBonus { get; set; }

    /// <summary>Все экземпляры карт стороны, по возрастанию instanceId.</summary>
    public List<CardInstance> Instances { get; set; } = new();

    /// <summary>Колода; верх колоды — последний элемент.</summary>
    public List<int> DrawPile { get; set; } = new();
    public List<int> Hand { get; set; } = new();

    /// <summary>Стол слева направо.</summary>
    public List<Creature> Board { get; set; } = new();

    /// <summary>Слоты поля чар; null — пусто. Длина = FIELD_SLOTS.</summary>
    public List<int?> Field { get; set; } = new();

    /// <summary>Боевая зона в порядке розыгрыша.</summary>
    public List<int> BattleZone { get; set; } = new();

    public List<int> Discard { get; set; } = new();
    public List<int> Exile { get; set; } = new();

    public bool IsReady { get; set; }

    public CardInstance GetInstance(int instanceId) =>
        Instances.FirstOrDefault(i => i.InstanceId == instanceId)
        ?? throw new KeyNotFoundException($"У стороны {Side} нет экземпляра {instanceId}");
}

/// <summary>Полное состояние матча. Сериализуемые данные; логика — функции над ними (12.1).</summary>
public sealed class MatchState
{
    public ulong Seed { get; set; }
    public PlanningMode Mode { get; set; }
    public GameConfig Config { get; set; } = new();
    public int Round { get; set; }

    /// <summary>Стороны: индекс 0 — A, 1 — B.</summary>
    public List<PlayerState> Players { get; set; } = new();

    /// <summary>Потоки planning[side] (раздел 10), индекс — сторона.</summary>
    public List<Pcg32> PlanningRng { get; set; } = new();

    public int NextInstanceId { get; set; } = 1;
    public int NextEntityId { get; set; } = 1;

    public Outcome? Result { get; set; }

    public List<GameEvent> Log { get; set; } = new();

    public PlayerState Player(Side side) => Players[(int)side];

    public Pcg32 Rng(Side side) => PlanningRng[(int)side];
}
