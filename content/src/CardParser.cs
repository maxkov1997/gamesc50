using System.Text.Json;
using Game.Core;

namespace Game.Content;

/// <summary>
/// Разбор cards.json в CardDef: структура, типы и набор полей по типу карты (11.1).
/// Смысловые проверки (имена из реестров, ссылки, правила 11.6) — в CardValidator.
/// </summary>
internal sealed class CardParser
{
    private static readonly string[] CommonFields = { "id", "name", "type", "cost", "text" };

    private static readonly Dictionary<string, (CardType Type, string[] Fields)> TypeFields = new(StringComparer.Ordinal)
    {
        ["creature"] = (CardType.Creature, new[] { "attack", "health", "tags", "keywords", "token", "abilities" }),
        ["spell"] = (CardType.Spell, new[] { "keywords", "play_target", "effects" }),
        ["battle_spell"] = (CardType.BattleSpell, new[] { "effects" }),
        ["secret"] = (CardType.Secret, new[] { "condition", "effects" }),
        ["enchantment"] = (CardType.Enchantment, new[] { "attach", "play_target", "grants", "abilities" }),
    };

    /// <summary>Все поля карты из 11.1 — чтобы отличать «чужое поле» от опечатки.</summary>
    private static readonly string[] AllCardFields = CommonFields
        .Concat(new[] { "attack", "health", "tags", "keywords", "token", "abilities", "play_target", "effects", "condition", "attach", "grants" })
        .ToArray();

    private static readonly string[] AbilityFields = { "trigger", "effects", "count", "cost", "target", "aura", "watch" };
    private static readonly string[] AuraFields = { "target", "attack", "keywords" };
    private static readonly string[] TargetFields = { "side", "tag", "not_self" };
    private static readonly string[] ConditionFields = { "event", "target", "attacker" };
    private static readonly string[] FilterFields = { "keyword", "no_keyword", "tag" };
    private static readonly string[] GrantsFields = { "attack", "keywords", "abilities" };
    private static readonly string[] SelectorFields = { "pick", "side", "count", "tag", "keyword", "no_keyword", "not_self" };
    private static readonly string[] EffectFields = new[] { "op" }.Concat(EffectDef.ParamNames).ToArray();

    private readonly List<ValidationError> _errors;

    public CardParser(List<ValidationError> errors) => _errors = errors;

    public List<CardDef> ParseAll(JsonElement root, string source)
    {
        var cards = new List<CardDef>();
        if (root.ValueKind != JsonValueKind.Array)
        {
            _errors.Add(new(ErrorCodes.InvalidType, source, "файл карт должен содержать массив карт"));
            return cards;
        }
        int i = 0;
        foreach (var el in root.EnumerateArray())
        {
            string path = $"{source}[{i}]";
            if (el.ValueKind != JsonValueKind.Object)
                _errors.Add(new(ErrorCodes.InvalidType, path, "карта должна быть объектом"));
            else if (ParseCard(el, path) is { } card)
                cards.Add(card);
            i++;
        }
        return cards;
    }

    private CardDef? ParseCard(JsonElement el, string basePath)
    {
        // В пути — id карты, чтобы ошибку было легко найти.
        string path = basePath;
        if (el.TryGetProperty("id", out var idEl) && idEl.ValueKind == JsonValueKind.String)
            path = $"{basePath} ({idEl.GetString()})";
        var r = new ObjectReader(el, path, _errors);
        int before = _errors.Count;

        string? id = r.String("id", true);
        string? name = r.String("name", true);
        string? typeName = r.String("type", true);
        int? cost = r.Int("cost", true, min: 0);
        string? text = r.String("text", true);

        if (typeName == null) return null;
        if (!TypeFields.TryGetValue(typeName, out var typeInfo))
        {
            r.Error(ErrorCodes.UnknownCardType, $"неизвестный тип карты «{typeName}»", "type");
            return null;
        }
        r.CheckFields(CommonFields.Concat(typeInfo.Fields).ToArray(), AllCardFields);

        var type = typeInfo.Type;
        int attack = 0, health = 0;
        if (type == CardType.Creature)
        {
            attack = r.Int("attack", true, min: 0) ?? 0;
            health = r.Int("health", true, min: 1) ?? 0;
        }

        AttachKind? attach = null;
        if (type == CardType.Enchantment)
        {
            string? a = r.String("attach", true);
            if (a == "creature") attach = AttachKind.Creature;
            else if (a == "field") attach = AttachKind.Field;
            else if (a != null) r.Error(ErrorCodes.InvalidValue, $"attach должен быть «creature» или «field», а не «{a}»", "attach");
        }

        bool effectsRequired = type is CardType.Spell or CardType.BattleSpell or CardType.Secret;
        var card = new CardDef
        {
            Id = id ?? "",
            Name = name ?? "",
            Type = type,
            Cost = cost ?? 0,
            Text = text ?? "",
            Attack = attack,
            Health = health,
            Tags = r.StringArray("tags"),
            Keywords = r.StringArray("keywords"),
            Token = r.Bool("token") ?? false,
            Abilities = r.ObjectArray("abilities", false).Select(ParseAbility).ToList(),
            PlayTarget = r.Object("play_target", false) is { } pt ? ParseTarget(pt) : null,
            Effects = r.ObjectArray("effects", effectsRequired).Select(ParseEffect).ToList(),
            Condition = r.Object("condition", type == CardType.Secret) is { } cond ? ParseCondition(cond) : null,
            Attach = attach,
            Grants = r.Object("grants", false) is { } g ? ParseGrants(g) : null,
        };
        return _errors.Count == before ? card : null;
    }

    private AbilityDef ParseAbility(ObjectReader r)
    {
        r.CheckFields(AbilityFields, AbilityFields);
        return new AbilityDef
        {
            Trigger = r.String("trigger", true) ?? "",
            Effects = r.ObjectArray("effects", false).Select(ParseEffect).ToList(),
            HasEffects = r.Has("effects"),
            Count = r.Int("count", false),
            Cost = r.Int("cost", false),
            Target = r.Object("target", false) is { } t ? ParseTarget(t) : null,
            Aura = r.Object("aura", false) is { } a ? ParseAura(a) : null,
            Watch = r.Object("watch", false) is { } w ? ParseFilter(w) : null,
        };
    }

    private AuraDef ParseAura(ObjectReader r)
    {
        r.CheckFields(AuraFields, AuraFields);
        return new AuraDef
        {
            Target = ParseSelector(r, "target", true) ?? new Selector(),
            Attack = r.Int("attack", false) ?? 0,
            Keywords = r.StringArray("keywords"),
        };
    }

    private TargetSpec ParseTarget(ObjectReader r)
    {
        r.CheckFields(TargetFields, TargetFields);
        return new TargetSpec
        {
            Side = r.String("side", true) ?? "",
            Tag = r.String("tag", false),
            NotSelf = r.Bool("not_self") ?? false,
        };
    }

    private ConditionDef ParseCondition(ObjectReader r)
    {
        r.CheckFields(ConditionFields, ConditionFields);
        return new ConditionDef
        {
            Event = r.String("event", true) ?? "",
            Target = r.Object("target", false) is { } t ? ParseFilter(t) : null,
            Attacker = r.Object("attacker", false) is { } a ? ParseFilter(a) : null,
        };
    }

    private static FilterDef ParseFilter(ObjectReader f)
    {
        f.CheckFields(FilterFields, FilterFields);
        return new FilterDef
        {
            Keyword = f.String("keyword", false),
            NoKeyword = f.String("no_keyword", false),
            Tag = f.String("tag", false),
        };
    }

    private GrantsDef ParseGrants(ObjectReader r)
    {
        r.CheckFields(GrantsFields, GrantsFields);
        return new GrantsDef
        {
            Attack = r.Int("attack", false) ?? 0,
            Keywords = r.StringArray("keywords"),
            Abilities = r.ObjectArray("abilities", false).Select(ParseAbility).ToList(),
        };
    }

    private EffectDef ParseEffect(ObjectReader r)
    {
        r.CheckFields(EffectFields, EffectFields);
        return new EffectDef
        {
            Op = r.String("op", true) ?? "",
            Target = ParseSelector(r, "target", false),
            Attack = r.Int("attack", false),
            Health = r.Int("health", false),
            Duration = r.String("duration", false),
            Keyword = r.String("keyword", false),
            Amount = r.Int("amount", false),
            Card = r.String("card", false),
            Count = r.Int("count", false),
            At = r.String("at", false),
        };
    }

    /// <summary>Селектор: строка-ссылка или объект (11.3).</summary>
    private Selector? ParseSelector(ObjectReader parent, string name, bool required)
    {
        if (!parent.TryGetRaw(name, out var v))
        {
            if (required) parent.Error(ErrorCodes.MissingField, $"нет обязательного поля «{name}»", name);
            return null;
        }
        if (v.ValueKind == JsonValueKind.String) return new Selector { Ref = v.GetString() };
        if (v.ValueKind != JsonValueKind.Object)
        {
            parent.Error(ErrorCodes.InvalidType, "селектор должен быть строкой или объектом", name);
            return null;
        }
        var r = parent.Object(name, true)!;
        r.CheckFields(SelectorFields, SelectorFields);
        return new Selector
        {
            Pick = r.String("pick", true),
            Side = r.String("side", false),
            Count = r.Int("count", false),
            Tag = r.String("tag", false),
            Keyword = r.String("keyword", false),
            NoKeyword = r.String("no_keyword", false),
            NotSelf = r.Bool("not_self") ?? false,
        };
    }
}
