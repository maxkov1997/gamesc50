namespace Game.Core;

/// <summary>
/// Константы матча (раздел 3 GDD). Значения не зашиты в код:
/// они приходят из content/config.json через загрузчик content.
/// </summary>
public sealed class GameConfig
{
    public int HeroHp { get; set; }
    public int BoardSize { get; set; }
    public int FieldSlots { get; set; }
    public int BattleSlots { get; set; }
    public int HandMax { get; set; }
    public int StartHand { get; set; }
    public int DrawPerRound { get; set; }
    public int ManaMax { get; set; }
    public int DeckMin { get; set; }
    public int DeckMax { get; set; }
    public int MaxCopies { get; set; }
    public int DamageBase { get; set; }
    public int DamageCap { get; set; }
    public int DodgeChance { get; set; }
    public int MaxAttacks { get; set; }
    public int MaxTriggers { get; set; }
    public int MaxRounds { get; set; }

    /// <summary>Ключи конфига в порядке раздела 3 и нижняя граница значения.</summary>
    public static readonly IReadOnlyList<ConfigField> Fields = new ConfigField[]
    {
        new("HERO_HP", 1, c => c.HeroHp, (c, v) => c.HeroHp = v),
        new("BOARD_SIZE", 1, c => c.BoardSize, (c, v) => c.BoardSize = v),
        new("FIELD_SLOTS", 1, c => c.FieldSlots, (c, v) => c.FieldSlots = v),
        new("BATTLE_SLOTS", 1, c => c.BattleSlots, (c, v) => c.BattleSlots = v),
        new("HAND_MAX", 1, c => c.HandMax, (c, v) => c.HandMax = v),
        new("START_HAND", 0, c => c.StartHand, (c, v) => c.StartHand = v),
        new("DRAW_PER_ROUND", 0, c => c.DrawPerRound, (c, v) => c.DrawPerRound = v),
        new("MANA_MAX", 1, c => c.ManaMax, (c, v) => c.ManaMax = v),
        new("DECK_MIN", 1, c => c.DeckMin, (c, v) => c.DeckMin = v),
        new("DECK_MAX", 1, c => c.DeckMax, (c, v) => c.DeckMax = v),
        new("MAX_COPIES", 1, c => c.MaxCopies, (c, v) => c.MaxCopies = v),
        new("DAMAGE_BASE", 0, c => c.DamageBase, (c, v) => c.DamageBase = v),
        new("DAMAGE_CAP", 0, c => c.DamageCap, (c, v) => c.DamageCap = v),
        new("DODGE_CHANCE", 0, c => c.DodgeChance, (c, v) => c.DodgeChance = v),
        new("MAX_ATTACKS", 1, c => c.MaxAttacks, (c, v) => c.MaxAttacks = v),
        new("MAX_TRIGGERS", 1, c => c.MaxTriggers, (c, v) => c.MaxTriggers = v),
        new("MAX_ROUNDS", 1, c => c.MaxRounds, (c, v) => c.MaxRounds = v),
    };
}

public sealed record ConfigField(string Key, int Min, Func<GameConfig, int> Get, Action<GameConfig, int> Set);
