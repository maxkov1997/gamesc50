namespace Game.Core;

public enum CardType { Creature, Spell, BattleSpell, Secret, Enchantment }

public enum AttachKind { Creature, Field }

/// <summary>Описание карты из content/cards.json (11.1). Неизменяемые данные.</summary>
public sealed class CardDef
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required CardType Type { get; init; }
    public required int Cost { get; init; }
    public required string Text { get; init; }

    public int Attack { get; init; }
    public int Health { get; init; }
    public IReadOnlyList<string> Tags { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Keywords { get; init; } = Array.Empty<string>();
    public bool Token { get; init; }
    public IReadOnlyList<AbilityDef> Abilities { get; init; } = Array.Empty<AbilityDef>();

    public TargetSpec? PlayTarget { get; init; }
    public IReadOnlyList<EffectDef> Effects { get; init; } = Array.Empty<EffectDef>();
    public ConditionDef? Condition { get; init; }
    public AttachKind? Attach { get; init; }
    public GrantsDef? Grants { get; init; }
}

/// <summary>Ability: { trigger, effects?, count?, cost?, target?, aura?, watch? } (11.1).</summary>
public sealed class AbilityDef
{
    public required string Trigger { get; init; }
    public IReadOnlyList<EffectDef> Effects { get; init; } = Array.Empty<EffectDef>();
    public bool HasEffects { get; init; }
    public int? Count { get; init; }
    public int? Cost { get; init; }
    public TargetSpec? Target { get; init; }
    public AuraDef? Aura { get; init; }

    /// <summary>
    /// Только у чар на поле, для триггеров «этого существа»: каким существам своей стороны выдаётся способность.
    /// Пустой фильтр — всем.
    /// </summary>
    public FilterDef? Watch { get; init; }
}

public sealed class AuraDef
{
    public required Selector Target { get; init; }
    public int Attack { get; init; }
    public IReadOnlyList<string> Keywords { get; init; } = Array.Empty<string>();
}

/// <summary>Цель, которую выбирает игрок: { side: "friendly", tag?, not_self? }.</summary>
public sealed class TargetSpec
{
    public required string Side { get; init; }
    public string? Tag { get; init; }
    public bool NotSelf { get; init; }
}

/// <summary>Условие секрета: { event, target?, attacker? } — фильтры участников события event_target и event_attacker.</summary>
public sealed class ConditionDef
{
    public required string Event { get; init; }
    public FilterDef? Target { get; init; }
    public FilterDef? Attacker { get; init; }
}

/// <summary>Фильтр участника события.</summary>
public sealed class FilterDef
{
    public string? Keyword { get; init; }
    public string? NoKeyword { get; init; }
    public string? Tag { get; init; }
}

/// <summary>Что получает существо от чар: { attack, keywords, abilities }.</summary>
public sealed class GrantsDef
{
    public int Attack { get; init; }
    public IReadOnlyList<string> Keywords { get; init; } = Array.Empty<string>();
    public IReadOnlyList<AbilityDef> Abilities { get; init; } = Array.Empty<AbilityDef>();
}

/// <summary>Эффект (11.2). Параметры, которых нет в записи, равны null.</summary>
public sealed class EffectDef
{
    public required string Op { get; init; }
    public Selector? Target { get; init; }
    public int? Attack { get; init; }
    public int? Health { get; init; }
    public string? Duration { get; init; }
    public string? Keyword { get; init; }
    public int? Amount { get; init; }
    public string? Card { get; init; }
    public int? Count { get; init; }
    public string? At { get; init; }

    /// <summary>Задан ли параметр с именем из реестра операций.</summary>
    public bool Has(string param) => param switch
    {
        "target" => Target != null,
        "attack" => Attack != null,
        "health" => Health != null,
        "duration" => Duration != null,
        "keyword" => Keyword != null,
        "amount" => Amount != null,
        "card" => Card != null,
        "count" => Count != null,
        "at" => At != null,
        _ => false,
    };

    /// <summary>Все имена параметров эффектов (для проверки схемы).</summary>
    public static readonly IReadOnlyList<string> ParamNames =
        new[] { "target", "attack", "health", "duration", "keyword", "amount", "card", "count", "at" };
}

/// <summary>
/// Селектор (11.3): либо строка-ссылка (Ref), либо объект с pick.
/// </summary>
public sealed class Selector
{
    public string? Ref { get; init; }
    public string? Pick { get; init; }
    public string? Side { get; init; }
    public int? Count { get; init; }
    public string? Tag { get; init; }
    public string? Keyword { get; init; }
    public string? NoKeyword { get; init; }
    public bool NotSelf { get; init; }

    public bool IsRef => Ref != null;
}

/// <summary>Набор карт с поиском по id. Порядок — порядок в файле.</summary>
public sealed class CardDatabase
{
    private readonly Dictionary<string, CardDef> _byId;

    public IReadOnlyList<CardDef> Cards { get; }

    public CardDatabase(IReadOnlyList<CardDef> cards)
    {
        Cards = cards;
        _byId = new Dictionary<string, CardDef>(StringComparer.Ordinal);
        foreach (var c in cards)
        {
            if (!_byId.TryAdd(c.Id, c))
                throw new ArgumentException($"Повторяющийся id карты «{c.Id}»", nameof(cards));
        }
    }

    public bool TryGet(string id, out CardDef card) => _byId.TryGetValue(id, out card!);

    public CardDef Get(string id) =>
        _byId.TryGetValue(id, out var c) ? c : throw new KeyNotFoundException($"Нет карты с id «{id}»");
}
