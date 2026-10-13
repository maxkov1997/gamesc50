namespace Game.Core;

/// <summary>Фаза, в которой может сработать триггер (таблица 7.2).</summary>
public enum Phase { Planning, Combat, Both }

/// <param name="Static">Статичное ключевое слово: может даваться аурой и чарами (7.4).</param>
/// <param name="ForCreatures">Допустимо в поле keywords существа.</param>
/// <param name="ForSpells">Допустимо в поле keywords заклинания.</param>
public sealed record KeywordSpec(string Id, bool Static, bool ForCreatures, bool ForSpells);

/// <param name="SelfCreature">Источник триггера — само существо («это существо…»); у чар на поле такого триггера быть не может.</param>
/// <param name="AllowsTarget">Игрок выбирает цель (поле target у способности).</param>
/// <param name="NeedsCount">Нужен параметр count (Возмездие).</param>
/// <param name="NeedsCost">Нужен параметр cost (Способность).</param>
/// <param name="IsAura">Вместо effects — объект aura.</param>
/// <param name="IsPlayTrigger">«При розыгрыше» — на него действуют правила 11.6 про цикл отзыва.</param>
/// <param name="EventRefs">Какие ссылки на участников события доступны эффектам (9.3).</param>
public sealed record TriggerSpec(
    string Id,
    Phase Phase,
    bool SelfCreature,
    bool AllowsTarget = false,
    bool NeedsCount = false,
    bool NeedsCost = false,
    bool IsAura = false,
    bool IsPlayTrigger = false,
    IReadOnlyList<string>? EventRefs = null)
{
    public IReadOnlyList<string> EventRefs { get; } = EventRefs ?? Array.Empty<string>();
}

/// <summary>Условие секрета (7.2).</summary>
public sealed record ConditionSpec(string Id, IReadOnlyList<string> EventRefs);

public enum ParamKind { Selector, Int, PositiveInt, Keyword, Duration, Card, Placement }

public sealed record OpParam(string Name, ParamKind Kind, bool Required);

/// <param name="CombatOnly">Операция допустима только в эффектах, которые срабатывают в бою (11.6: урон вне боя невозможен).</param>
/// <param name="GrantsMana">Операция даёт ману — запрещена в «При розыгрыше» (11.6).</param>
/// <param name="RequiredCondition">Операция имеет смысл только в секрете с этим условием.</param>
public sealed record OpSpec(
    string Id,
    IReadOnlyList<OpParam> Params,
    bool CombatOnly = false,
    bool GrantsMana = false,
    string? RequiredCondition = null);

/// <summary>
/// Реестры ключевых слов (7.1), триггеров (7.2), условий секретов, операций (11.2) и селекторов (11.3).
/// Новая механика добавляется сюда, а не проверкой id карты в правилах.
/// Списки упорядочены; поиск — по id.
/// </summary>
public static class Registry
{
    public static class Keywords
    {
        public const string Taunt = "taunt";
        public const string Shield = "shield";
        public const string Poison = "poison";
        public const string DoubleAttack = "double_attack";
        public const string Cleave = "cleave";
        public const string Reborn = "reborn";
        public const string Flying = "flying";
        public const string Exhaust = "exhaust";
    }

    public static class Triggers
    {
        public const string OnPlay = "on_play";
        public const string OnDeath = "on_death";
        public const string StartOfCombat = "start_of_combat";
        public const string OnAttack = "on_attack";
        public const string AfterDamaged = "after_damaged";
        public const string FriendlyDied = "friendly_died";
        public const string Avenge = "avenge";
        public const string RoundStart = "round_start";
        public const string RoundEnd = "round_end";
        public const string Activated = "activated";
        public const string Aura = "aura";
    }

    public static class Conditions
    {
        public const string EnemyAttacks = "enemy_attacks";
        public const string FriendlyDied = "friendly_died";
        public const string SideEmptied = "side_emptied";
        public const string HeroWouldTakeDamage = "hero_would_take_damage";
    }

    public static class Ops
    {
        public const string Buff = "buff";
        public const string GiveKeyword = "give_keyword";
        public const string Damage = "damage";
        public const string Destroy = "destroy";
        public const string Summon = "summon";
        public const string Draw = "draw";
        public const string ManaNextRound = "mana_next_round";
        public const string Consume = "consume";
        public const string ReduceDamage = "reduce_damage";
    }

    public static class Refs
    {
        public const string Self = "self";
        public const string Chosen = "chosen";
        public const string EventTarget = "event_target";
        public const string EventAttacker = "event_attacker";
    }

    public static class Picks
    {
        public const string All = "all";
        public const string Random = "random";
        public const string Leftmost = "leftmost";
        public const string Rightmost = "rightmost";
        public const string Adjacent = "adjacent";
    }

    public static class Sides
    {
        public const string Friendly = "friendly";
        public const string Enemy = "enemy";
    }

    public static class Durations
    {
        public const string Permanent = "permanent";
        public const string Combat = "combat";
    }

    public static class Placements
    {
        public const string DeathPosition = "death_position";
        public const string RightOfSelf = "right_of_self";
        public const string Rightmost = "rightmost";
    }

    public static readonly IReadOnlyList<KeywordSpec> KeywordList = new KeywordSpec[]
    {
        new(Keywords.Taunt, Static: true, ForCreatures: true, ForSpells: false),
        new(Keywords.Shield, Static: false, ForCreatures: true, ForSpells: false),
        new(Keywords.Poison, Static: true, ForCreatures: true, ForSpells: false),
        new(Keywords.DoubleAttack, Static: true, ForCreatures: true, ForSpells: false),
        new(Keywords.Cleave, Static: true, ForCreatures: true, ForSpells: false),
        new(Keywords.Reborn, Static: false, ForCreatures: true, ForSpells: false),
        new(Keywords.Flying, Static: true, ForCreatures: true, ForSpells: false),
        new(Keywords.Exhaust, Static: false, ForCreatures: false, ForSpells: true),
    };

    public static readonly IReadOnlyList<TriggerSpec> TriggerList = new TriggerSpec[]
    {
        new(Triggers.OnPlay, Phase.Planning, SelfCreature: true, AllowsTarget: true, IsPlayTrigger: true),
        new(Triggers.OnDeath, Phase.Both, SelfCreature: true),
        new(Triggers.StartOfCombat, Phase.Combat, SelfCreature: false),
        // GDD-QUESTION: какие ссылки на участников события доступны триггерам (см. docs/QUESTIONS.md, Q2).
        new(Triggers.OnAttack, Phase.Combat, SelfCreature: true, EventRefs: new[] { Refs.EventTarget }),
        new(Triggers.AfterDamaged, Phase.Combat, SelfCreature: true),
        new(Triggers.FriendlyDied, Phase.Both, SelfCreature: false, EventRefs: new[] { Refs.EventTarget }),
        // GDD-QUESTION: Возмездие считает смерти «других союзных существ» — допускаем только у существ (Q3).
        new(Triggers.Avenge, Phase.Combat, SelfCreature: true, NeedsCount: true),
        new(Triggers.RoundStart, Phase.Planning, SelfCreature: false),
        new(Triggers.RoundEnd, Phase.Planning, SelfCreature: false),
        new(Triggers.Activated, Phase.Planning, SelfCreature: true, AllowsTarget: true, NeedsCost: true),
        new(Triggers.Aura, Phase.Both, SelfCreature: false, IsAura: true),
    };

    public static readonly IReadOnlyList<ConditionSpec> ConditionList = new ConditionSpec[]
    {
        new(Conditions.EnemyAttacks, new[] { Refs.EventTarget, Refs.EventAttacker }),
        new(Conditions.FriendlyDied, new[] { Refs.EventTarget }),
        new(Conditions.SideEmptied, Array.Empty<string>()),
        new(Conditions.HeroWouldTakeDamage, Array.Empty<string>()),
    };

    public static readonly IReadOnlyList<OpSpec> OpList = new OpSpec[]
    {
        new(Ops.Buff, new OpParam[]
        {
            new("target", ParamKind.Selector, true),
            new("attack", ParamKind.Int, false),
            new("health", ParamKind.Int, false),
            new("duration", ParamKind.Duration, false),
        }),
        new(Ops.GiveKeyword, new OpParam[]
        {
            new("target", ParamKind.Selector, true),
            new("keyword", ParamKind.Keyword, true),
            new("duration", ParamKind.Duration, false),
        }),
        new(Ops.Damage, new OpParam[]
        {
            new("target", ParamKind.Selector, true),
            new("amount", ParamKind.PositiveInt, true),
        }, CombatOnly: true),
        new(Ops.Destroy, new OpParam[]
        {
            new("target", ParamKind.Selector, true),
        }),
        new(Ops.Summon, new OpParam[]
        {
            new("card", ParamKind.Card, true),
            new("count", ParamKind.PositiveInt, false),
            new("at", ParamKind.Placement, true),
        }),
        new(Ops.Draw, new OpParam[]
        {
            new("count", ParamKind.PositiveInt, true),
        }),
        new(Ops.ManaNextRound, new OpParam[]
        {
            new("amount", ParamKind.PositiveInt, true),
        }, GrantsMana: true),
        new(Ops.Consume, new OpParam[]
        {
            new("target", ParamKind.Selector, true),
        }),
        new(Ops.ReduceDamage, new OpParam[]
        {
            new("amount", ParamKind.PositiveInt, true),
        }, RequiredCondition: Conditions.HeroWouldTakeDamage),
    };

    public static readonly IReadOnlyList<string> RefList = new[] { Refs.Self, Refs.Chosen, Refs.EventTarget, Refs.EventAttacker };
    public static readonly IReadOnlyList<string> PickList = new[] { Picks.All, Picks.Random, Picks.Leftmost, Picks.Rightmost, Picks.Adjacent };
    public static readonly IReadOnlyList<string> SideList = new[] { Sides.Friendly, Sides.Enemy };
    public static readonly IReadOnlyList<string> DurationList = new[] { Durations.Permanent, Durations.Combat };
    public static readonly IReadOnlyList<string> PlacementList = new[] { Placements.DeathPosition, Placements.RightOfSelf, Placements.Rightmost };
    public static readonly IReadOnlyList<string> TagList = new[] { "beast", "undead", "mech", "spirit" };

    /// <summary>Префикс id токенов (11.1).</summary>
    public const string TokenPrefix = "tok_";

    public static KeywordSpec? FindKeyword(string id) => KeywordList.FirstOrDefault(k => k.Id == id);
    public static TriggerSpec? FindTrigger(string id) => TriggerList.FirstOrDefault(t => t.Id == id);
    public static ConditionSpec? FindCondition(string id) => ConditionList.FirstOrDefault(c => c.Id == id);
    public static OpSpec? FindOp(string id) => OpList.FirstOrDefault(o => o.Id == id);
}
