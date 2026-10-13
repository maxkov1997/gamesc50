namespace Game.Core;

/// <summary>Типы событий лога (9.4).</summary>
public static class EventTypes
{
    public const string MatchStarted = "match_started";
    public const string RoundStarted = "round_started";
    public const string CardDrawn = "card_drawn";
    public const string DeckReshuffled = "deck_reshuffled";
    public const string CardBurned = "card_burned";
    public const string CardPlayed = "card_played";
    public const string AbilityActivated = "ability_activated";
    public const string CreatureSummoned = "creature_summoned";
    public const string SummonFailed = "summon_failed";
    public const string CreatureMoved = "creature_moved";
    public const string CreatureRecalled = "creature_recalled";
    public const string CreatureDismissed = "creature_dismissed";
    public const string StatsChanged = "stats_changed";
    public const string KeywordChanged = "keyword_changed";
    public const string CombatStarted = "combat_started";
    public const string BattleSpellRevealed = "battle_spell_revealed";
    public const string SecretRevealed = "secret_revealed";
    public const string Trigger = "trigger";
    public const string AttackDeclared = "attack_declared";
    public const string Dodge = "dodge";
    public const string Damage = "damage";
    public const string ShieldBroken = "shield_broken";
    public const string Died = "died";
    public const string Reborn = "reborn";
    public const string SideEmptied = "side_emptied";
    public const string CombatEnded = "combat_ended";
    public const string HeroDamaged = "hero_damaged";
    public const string PermanentApplied = "permanent_applied";
    public const string RoundEnded = "round_ended";
    public const string MatchEnded = "match_ended";
    public const string Warning = "warning";
}

/// <summary>
/// Событие лога. Плоская запись: заполнены только поля, нужные этому типу (9.4).
/// Поля добавляются по мере этапов; null не сериализуется.
/// </summary>
public sealed class GameEvent
{
    public string Type { get; set; } = "";
    public Side? Side { get; set; }
    public int? Round { get; set; }
    public int? InstanceId { get; set; }
    public int? EntityId { get; set; }
    public string? CardId { get; set; }
    public int? Count { get; set; }

    // match_started
    public ulong? Seed { get; set; }
    public PlanningMode? Mode { get; set; }
    public List<List<string>>? Decks { get; set; }

    /// <summary>Для warning.</summary>
    public string? Text { get; set; }
}
