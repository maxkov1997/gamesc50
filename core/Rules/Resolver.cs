namespace Game.Core;

/// <summary>Триггер или эффект в очереди (9.1).</summary>
internal sealed class PendingTrigger
{
    public required Side Side { get; init; }
    public required string SourceCardId { get; init; }

    /// <summary>Источник-существо (может уже стать надгробием).</summary>
    public Creature? SourceCreature { get; init; }

    /// <summary>Источник-карта: заклинание, боевое заклинание, секрет или чары на поле.</summary>
    public int? SourceInstanceId { get; init; }

    /// <summary>null — эффект карты (заклинание, секрет), а не способность.</summary>
    public AbilityRef? Ability { get; init; }

    public IReadOnlyList<EffectDef> Effects { get; init; } = Array.Empty<EffectDef>();

    /// <summary>Триггер срабатывает и после ухода источника со стола: on_death и Перерождение (9.1).</summary>
    public bool SurvivesSource { get; init; }

    /// <summary>Перерождение источника (8.8, шаг 2).</summary>
    public bool IsReborn { get; init; }

    /// <summary>Срабатывание секрета: при разрешении раскрывается (secret_revealed).</summary>
    public bool IsSecret { get; init; }

    public int? Chosen { get; init; }
    public int? EventTarget { get; init; }
    public int? EventAttacker { get; init; }
}

/// <summary>Изменение «навсегда», сделанное в бою: переносится на оригинал по entityId (8.9).</summary>
public sealed record PermanentDelta(Side Side, int EntityId, int Attack, int Health, string? Keyword);

/// <summary>
/// Очередь триггеров и исполнение эффектов (9.1, 8.7, 8.8, 11.2, 11.3).
/// В подготовке работает на одной стороне — другую не видит вовсе (5.6). В бою — на копиях обеих сторон.
/// </summary>
internal sealed class Resolver
{
    private readonly CardDatabase _db;
    private readonly GameConfig _config;
    private readonly PlayerState?[] _sides;
    private readonly Func<Side, IRandom> _rng;
    private readonly Queue<PendingTrigger> _queue = new();
    private readonly int[] _combatSeq = { 1, 1 };
    private int _resolved;

    public Phase Phase { get; }
    public List<GameEvent> Events { get; }

    /// <summary>Активная сторона (глоссарий): её смерти и триггеры идут первыми.</summary>
    public Side ActiveSide { get; set; }

    /// <summary>В бою превышен MAX_TRIGGERS — бой заканчивается ничьей limit (8.10).</summary>
    public bool LimitReached { get; private set; }

    public List<PermanentDelta> PermanentDeltas { get; } = new();

    /// <summary>Сработавшие в бою боевые заклинания и секреты по сторонам — после боя в сброс (8.9).</summary>
    public List<int>[] SpentCards { get; } = { new(), new() };

    /// <summary>Урон герою, который вот-вот будет нанесён (hero_would_take_damage); его меняет reduce_damage.</summary>
    public int? PendingHeroDamage { get; set; }

    private delegate void OpHandler(Resolver r, PendingTrigger t, EffectDef e);

    /// <summary>Реализации операций 11.2. Новая механика — новая запись здесь и в Registry.</summary>
    private static readonly Dictionary<string, OpHandler> Handlers = new(StringComparer.Ordinal)
    {
        [Registry.Ops.Buff] = (r, t, e) => r.OpBuff(t, e),
        [Registry.Ops.GiveKeyword] = (r, t, e) => r.OpGiveKeyword(t, e),
        [Registry.Ops.Damage] = (r, t, e) => r.OpDamage(t, e),
        [Registry.Ops.Destroy] = (r, t, e) => r.OpDestroy(t, e),
        [Registry.Ops.Summon] = (r, t, e) => r.OpSummon(t, e),
        [Registry.Ops.Draw] = (r, t, e) => r.OpDraw(t, e),
        [Registry.Ops.ManaNextRound] = (r, t, e) => r.OpManaNextRound(t, e),
        [Registry.Ops.Consume] = (r, t, e) => r.OpConsume(t, e),
        [Registry.Ops.ReduceDamage] = (r, t, e) => r.OpReduceDamage(t, e),
    };

    public Resolver(CardDatabase db, GameConfig config, Phase phase, PlayerState?[] sides, Func<Side, IRandom> rng, List<GameEvent> events)
    {
        _db = db;
        _config = config;
        Phase = phase;
        _sides = sides;
        _rng = rng;
        Events = events;
    }

    /// <summary>Резолвер подготовки одной стороны: противник недоступен.</summary>
    public static Resolver ForPlanning(CardDatabase db, MatchState state, Side side, List<GameEvent> events)
    {
        var sides = new PlayerState?[2];
        sides[(int)side] = state.Player(side);
        var rng = state.Rng(side);
        return new Resolver(db, state.Config, Phase.Planning, sides, _ => rng, events) { ActiveSide = side };
    }

    public PlayerState Side(Side side) =>
        _sides[(int)side] ?? throw new InvalidOperationException($"Сторона {side} недоступна в подготовке противника (5.6)");

    public static Side Other(Side s) => s == Core.Side.A ? Core.Side.B : Core.Side.A;

    /// <summary>Стороны в порядке приоритета: активная, затем другая (9.2).</summary>
    private IEnumerable<Side> SidesInOrder()
    {
        foreach (var s in new[] { ActiveSide, Other(ActiveSide) })
            if (_sides[(int)s] != null) yield return s;
    }

    public void Enqueue(PendingTrigger t) => _queue.Enqueue(t);

    /// <summary>
    /// Поставить в очередь способности с триггером trigger в порядке 9.2:
    /// поле чар по слотам → существа слева направо (свои способности, затем от чар).
    /// </summary>
    public void EnqueueSideTriggers(Side side, string trigger, int? eventTarget = null)
    {
        var p = Side(side);
        foreach (var slot in p.Field)
        {
            if (slot is not int ench) continue;
            var def = _db.Get(p.GetInstance(ench).CardId);
            for (int i = 0; i < def.Abilities.Count; i++)
            {
                var a = def.Abilities[i];
                // Способности «этого существа» с watch выданы существам и срабатывают у них (Q5).
                if (a.Trigger != trigger || a.Watch != null) continue;
                Enqueue(new PendingTrigger
                {
                    Side = side, SourceCardId = def.Id, SourceInstanceId = ench,
                    Ability = new AbilityRef(a, i, AbilitySource.Field, ench, i), Effects = a.Effects, EventTarget = eventTarget,
                });
            }
        }
        foreach (var c in p.Alive.ToList())
            EnqueueCreatureTriggers(side, c, trigger, eventTarget: eventTarget);
    }

    /// <summary>Боевые заклинания стороны в порядке розыгрыша (8.4). Срабатывают всегда — сразу помечаются потраченными.</summary>
    public void EnqueueBattleSpells(Side side)
    {
        var p = Side(side);
        foreach (int inst in p.BattleZone)
        {
            var def = _db.Get(p.GetInstance(inst).CardId);
            if (def.Type != CardType.BattleSpell) continue;
            SpentCards[(int)side].Add(inst);
            Enqueue(new PendingTrigger { Side = side, SourceCardId = def.Id, SourceInstanceId = inst, Effects = def.Effects });
        }
    }

    /// <summary>
    /// Секреты стороны с условием condition, в порядке розыгрыша (9.2). Только в бою.
    /// Секрет помечается сработавшим, когда встаёт в очередь, и больше в очередь не ставится (9.1).
    /// Фильтры условия: target — участник event_target, attacker — участник event_attacker.
    /// </summary>
    public void EnqueueSecrets(Side side, string condition, int? eventTarget = null, int? eventAttacker = null)
    {
        if (Phase != Phase.Combat) return;
        var p = Side(side);
        foreach (int inst in p.BattleZone)
        {
            var def = _db.Get(p.GetInstance(inst).CardId);
            if (def.Type != CardType.Secret || def.Condition!.Event != condition) continue;
            if (SpentCards[(int)side].Contains(inst)) continue;
            if (def.Condition.Target is { } byTarget && !FilterMatches(byTarget, eventTarget)) continue;
            if (def.Condition.Attacker is { } byAttacker && !FilterMatches(byAttacker, eventAttacker)) continue;
            SpentCards[(int)side].Add(inst);
            Enqueue(new PendingTrigger
            {
                Side = side, SourceCardId = def.Id, SourceInstanceId = inst, Effects = def.Effects,
                EventTarget = eventTarget, EventAttacker = eventAttacker, IsSecret = true,
            });
        }
    }

    /// <summary>Участника нет (или он исчез) — фильтр не пройден.</summary>
    private bool FilterMatches(FilterDef filter, int? participant)
    {
        if (participant is not int id || FindAliveAnywhere(id) is not { } c) return false;
        var p = OwnerOf(c);
        var keywords = Stats.Keywords(_db, p, c);
        if (filter.Keyword != null && !keywords.Contains(filter.Keyword)) return false;
        if (filter.NoKeyword != null && keywords.Contains(filter.NoKeyword)) return false;
        if (filter.Tag != null && !_db.Get(c.CardId).Tags.Contains(filter.Tag)) return false;
        return true;
    }

    public void EnqueueCreatureTriggers(Side side, Creature c, string trigger, int? chosen = null, int? eventTarget = null, bool survives = false)
    {
        foreach (var a in Stats.Abilities(_db, Side(side), c))
        {
            if (a.Ability.Trigger != trigger) continue;
            Enqueue(new PendingTrigger
            {
                Side = side, SourceCardId = c.CardId, SourceCreature = c, Ability = a, Effects = a.Ability.Effects,
                Chosen = a.Ability.Target != null ? chosen : null, EventTarget = eventTarget, SurvivesSource = survives,
            });
        }
    }

    /// <summary>
    /// разрешить_очередь() (9.1). Предел MAX_TRIGGERS: в подготовке — на одно действие (лишние отбрасываются),
    /// в бою — на весь бой (бой кончается ничьей limit).
    /// </summary>
    public void Run()
    {
        if (Phase == Phase.Planning) _resolved = 0;
        ProcessDeaths();
        while (_queue.Count > 0 && !LimitReached)
        {
            var t = _queue.Dequeue();
            if (SourceLeft(t)) continue;
            Resolve(t);
            ProcessDeaths();
            _resolved++;
            if (_resolved > _config.MaxTriggers)
            {
                _queue.Clear();
                if (Phase == Phase.Combat) LimitReached = true;
                string where = Phase == Phase.Combat ? "за бой" : "за одно действие";
                Events.Add(new GameEvent { Type = EventTypes.Warning, Text = $"превышен предел MAX_TRIGGERS ({_config.MaxTriggers}) {where}" });
            }
        }
        foreach (var p in _sides)
            p?.Board.RemoveAll(c => c.Tombstone);
    }

    private bool SourceLeft(PendingTrigger t)
    {
        if (t.SourceCreature == null || t.SurvivesSource) return false;
        return !t.SourceCreature.IsAlive || !Side(t.Side).Board.Contains(t.SourceCreature);
    }

    private void Resolve(PendingTrigger t)
    {
        if (t.IsReborn)
        {
            ResolveReborn(t);
            return;
        }
        if (t.IsSecret)
            Events.Add(new GameEvent { Type = EventTypes.SecretRevealed, Side = t.Side, CardId = t.SourceCardId, InstanceId = t.SourceInstanceId });
        if (t.Ability != null)
        {
            Events.Add(new GameEvent
            {
                Type = EventTypes.Trigger, Side = t.Side, CardId = t.SourceCardId,
                SourceEntityId = t.SourceCreature?.EntityId, SourceInstanceId = t.SourceInstanceId,
                AbilityIndex = t.Ability.Index, Trigger = t.Ability.Ability.Trigger,
            });
        }
        foreach (var e in t.Effects)
        {
            if (!Handlers.TryGetValue(e.Op, out var handler))
                throw new InvalidOperationException($"Операция «{e.Op}» не реализована");
            handler(this, t, e);
        }
    }

    // ---------- Смерть (8.8) ----------

    private bool IsDying(Creature c) =>
        c.MarkedForDestroy || (Phase == Phase.Combat && Stats.Health(_db, c) <= 0);

    private void ProcessDeaths()
    {
        while (true)
        {
            // Порядок: активная сторона, затем другая; внутри стороны слева направо.
            var dead = new List<(Side Side, Creature C, List<AbilityRef> Abilities, bool Reborn)>();
            foreach (var side in SidesInOrder())
            {
                var p = Side(side);
                foreach (var c in p.Alive)
                    if (IsDying(c))
                        dead.Add((side, c, Stats.Abilities(_db, p, c), Stats.HasKeyword(_db, p, c, Registry.Keywords.Reborn)));
            }
            if (dead.Count == 0) return;

            var aliveBefore = new[] { _sides[0]?.AliveCount ?? 0, _sides[1]?.AliveCount ?? 0 };
            foreach (var (side, c, _, _) in dead)
            {
                var p = Side(side);
                int position = AliveIndex(p, c);
                c.MarkedForDestroy = false;
                c.Tombstone = true;
                Events.Add(new GameEvent { Type = EventTypes.Died, Side = side, EntityId = c.EntityId, CardId = c.CardId, Position = position });
            }

            foreach (var (side, c, abilities, reborn) in dead)
            {
                // 1. «Посмертно», включая полученные от чар — их список снят до ухода чар в сброс.
                foreach (var a in abilities)
                {
                    if (a.Ability.Trigger != Registry.Triggers.OnDeath) continue;
                    Enqueue(new PendingTrigger
                    {
                        Side = side, SourceCardId = c.CardId, SourceCreature = c, Ability = a,
                        Effects = a.Ability.Effects, SurvivesSource = true,
                    });
                }
                // 2. Перерождение — только в бою.
                if (Phase == Phase.Combat && reborn)
                    Enqueue(new PendingTrigger { Side = side, SourceCardId = c.CardId, SourceCreature = c, SurvivesSource = true, IsReborn = true });

                // 3. «Когда умирает союзник»: секреты (только в бою), чары на поле, живые союзники (9.2).
                EnqueueSecrets(side, Registry.Conditions.FriendlyDied, eventTarget: c.EntityId);
                EnqueueSideTriggers(side, Registry.Triggers.FriendlyDied, eventTarget: c.EntityId);
                //    +1 к счётчику Возмездия каждого живого союзника; достигшие N — в очередь, счётчик = 0.
                if (Phase == Phase.Combat) CountAvenge(side);

                // В подготовке погибшее существо уходит в сброс (токен исчезает), чары с него — в сброс.
                // В бою погибает только копия.
                if (Phase == Phase.Planning) Zones.DiscardCreature(Side(side), c);
            }

            // Сторона, где живых стало 0, а было больше 0 — секреты side_emptied.
            foreach (var side in SidesInOrder())
                if (aliveBefore[(int)side] > 0 && Side(side).AliveCount == 0)
                    EnqueueSecrets(side, Registry.Conditions.SideEmptied);
        }
    }

    private void CountAvenge(Side side)
    {
        var p = Side(side);
        foreach (var ally in p.Alive.ToList())
        {
            foreach (var a in Stats.Abilities(_db, p, ally))
            {
                if (a.Ability.Trigger != Registry.Triggers.Avenge) continue;
                ally.AvengeProgress ??= new List<int>();
                while (ally.AvengeProgress.Count <= a.Index) ally.AvengeProgress.Add(0);
                ally.AvengeProgress[a.Index]++;
                if (ally.AvengeProgress[a.Index] < a.Ability.Count) continue;
                ally.AvengeProgress[a.Index] = 0;
                Enqueue(new PendingTrigger
                {
                    Side = side, SourceCardId = ally.CardId, SourceCreature = ally, Ability = a, Effects = a.Ability.Effects,
                });
            }
        }
    }

    /// <summary>
    /// Перерождение: базовая копия карты без усилений и без Перерождения, здоровье 1, на месте погибшего.
    /// На полном столе копия всё равно возвращается и сразу погибает — со всеми триггерами смерти (8.8).
    /// </summary>
    private void ResolveReborn(PendingTrigger t)
    {
        var p = Side(t.Side);
        var original = t.SourceCreature!;
        int index = p.Board.IndexOf(original);
        if (index < 0) index = p.Board.Count;
        bool noRoom = p.AliveCount >= _config.BoardSize;
        var c = NewEntity(p, original.CardId);
        c.RemovedKeywords = new List<string> { Registry.Keywords.Reborn };
        c.Damage = Stats.MaxHealth(_db, c) - 1;
        p.Board.Insert(index, c);
        // Смерть обработается сразу после разрешения этого триггера (9.1).
        if (noRoom) c.MarkedForDestroy = true;
        Events.Add(new GameEvent
        {
            Type = EventTypes.Reborn, Side = p.Side, EntityId = c.EntityId, SourceEntityId = original.EntityId,
            Position = AliveIndex(p, c), Creature = CreatureView.Of(_db, p, c),
        });
    }

    public static int AliveIndex(PlayerState p, Creature c)
    {
        int index = 0;
        foreach (var x in p.Board)
        {
            if (ReferenceEquals(x, c)) return index;
            if (x.IsAlive) index++;
        }
        return index;
    }

    /// <summary>
    /// Новая сущность. В подготовке — постоянный id стороны; в бою — из отдельного отрицательного диапазона (4.1),
    /// поэтому у неё нет оригинала и её изменения «навсегда» теряются (8.9).
    /// </summary>
    private Creature NewEntity(PlayerState p, string cardId)
    {
        if (Phase == Phase.Planning) return Entities.NewCreature(p, cardId, null);
        int id = -(_combatSeq[(int)p.Side]++ * 2 + (int)p.Side);
        return new Creature { EntityId = id, CardId = cardId, Timestamp = Entities.Stamp(p) };
    }

    // ---------- Урон (8.7) ----------

    /// <summary>нанести_урон(источник, цель, x). source == null — урон «без источника», Яд к нему не применяется.</summary>
    public void Deal(Creature? source, Creature target, int amount)
    {
        if (amount <= 0) return;
        var tp = OwnerOf(target);
        if (Stats.HasKeyword(_db, tp, target, Registry.Keywords.Shield))
        {
            (target.RemovedKeywords ??= new List<string>()).Add(Registry.Keywords.Shield);
            Events.Add(new GameEvent { Type = EventTypes.ShieldBroken, Side = tp.Side, EntityId = target.EntityId });
            return;
        }
        target.Damage += amount;
        Events.Add(new GameEvent
        {
            Type = EventTypes.Damage, Side = tp.Side, SourceEntityId = source?.EntityId, Target = target.EntityId,
            Amount = amount, Health = Stats.Health(_db, target),
        });
        if (source != null && Stats.HasKeyword(_db, OwnerOf(source), source, Registry.Keywords.Poison))
            target.MarkedForDestroy = true;
        EnqueueCreatureTriggers(tp.Side, target, Registry.Triggers.AfterDamaged);
    }

    // ---------- Селекторы (11.3, 9.3) ----------

    private List<Creature> Select(PendingTrigger t, Selector s)
    {
        var owner = Side(t.Side);
        if (s.IsRef)
        {
            Creature? found = s.Ref switch
            {
                Registry.Refs.Self => t.SourceCreature is { IsAlive: true } self && owner.Board.Contains(self) ? self : null,
                Registry.Refs.Chosen => t.Chosen is int id ? owner.FindAlive(id) : null,
                Registry.Refs.EventTarget => t.EventTarget is int id ? FindAliveAnywhere(id) : null,
                Registry.Refs.EventAttacker => t.EventAttacker is int id ? FindAliveAnywhere(id) : null,
                _ => throw new InvalidOperationException($"Неизвестный селектор «{s.Ref}»"),
            };
            // 9.3: цель исчезла — эффект на неё пропускается.
            return found == null ? new List<Creature>() : new List<Creature> { found };
        }

        var side = s.Side == Registry.Sides.Enemy ? Side(Other(t.Side)) : owner;
        var candidates = Selectors.Filter(_db, side, t.SourceCreature, s, Stats.Keywords).ToList();
        if (s.Pick == Registry.Picks.Random)
            return _rng(t.Side).Sample(candidates, s.Count ?? 1);
        return Selectors.PickFixed(owner, t.SourceCreature, s, candidates);
    }

    private Creature? FindAliveAnywhere(int entityId)
    {
        foreach (var p in _sides)
            if (p?.FindAlive(entityId) is { } c) return c;
        return null;
    }

    public PlayerState OwnerOf(Creature c)
    {
        foreach (var p in _sides)
            if (p != null && p.Board.Contains(c)) return p;
        throw new InvalidOperationException($"Сущность {c.EntityId} не на столе");
    }

    // ---------- Операции (11.2) ----------

    private void StatsChanged(PlayerState p, Creature c, string duration) =>
        Events.Add(new GameEvent
        {
            Type = EventTypes.StatsChanged, Side = p.Side, EntityId = c.EntityId,
            Attack = Stats.Attack(_db, p, c), Health = Stats.Health(_db, c), Duration = duration,
        });

    /// <summary>7.3: эффекты подготовки — всегда навсегда; эффекты боя — до конца боя, если не сказано «навсегда».</summary>
    private string DurationOf(EffectDef e) =>
        Phase == Phase.Planning ? Registry.Durations.Permanent : e.Duration ?? Registry.Durations.Combat;

    /// <summary>Изменение «навсегда» в бою: применяется к копии сразу и записывается для переноса (7.3).</summary>
    private void RecordPermanent(PlayerState p, Creature c, int attack, int health, string? keyword)
    {
        if (Phase == Phase.Combat && c.EntityId > 0)
            PermanentDeltas.Add(new PermanentDelta(p.Side, c.EntityId, attack, health, keyword));
    }

    private void OpBuff(PendingTrigger t, EffectDef e)
    {
        string duration = DurationOf(e);
        int attack = e.Attack ?? 0, health = e.Health ?? 0;
        foreach (var c in Select(t, e.Target!))
        {
            var p = OwnerOf(c);
            if (duration == Registry.Durations.Permanent)
            {
                c.BonusAttack += attack;
                c.BonusHealth += health;
                RecordPermanent(p, c, attack, health, null);
            }
            else
            {
                c.CombatAttack += attack;
                c.CombatHealth += health;
            }
            StatsChanged(p, c, duration);
        }
    }

    private void OpGiveKeyword(PendingTrigger t, EffectDef e)
    {
        string duration = DurationOf(e);
        string keyword = e.Keyword!;
        foreach (var c in Select(t, e.Target!))
        {
            var p = OwnerOf(c);
            // Снова полученное слово возвращается, даже если было потеряно в этом бою (например, Щит).
            c.RemovedKeywords?.Remove(keyword);
            if (duration == Registry.Durations.Permanent)
            {
                if (!c.GainedKeywords.Contains(keyword)) c.GainedKeywords.Add(keyword);
                RecordPermanent(p, c, 0, 0, keyword);
            }
            else
            {
                c.CombatKeywords ??= new List<string>();
                if (!c.CombatKeywords.Contains(keyword)) c.CombatKeywords.Add(keyword);
            }
            Events.Add(new GameEvent
            {
                Type = EventTypes.KeywordChanged, Side = p.Side, EntityId = c.EntityId,
                Keyword = keyword, Gained = true, Duration = duration,
            });
        }
    }

    private void OpDamage(PendingTrigger t, EffectDef e)
    {
        // 11.2: источник — владелец эффекта, если это существо, иначе «без источника».
        foreach (var c in Select(t, e.Target!)) Deal(t.SourceCreature, c, e.Amount!.Value);
    }

    private void OpDestroy(PendingTrigger t, EffectDef e)
    {
        foreach (var c in Select(t, e.Target!)) c.MarkedForDestroy = true;
    }

    private void OpConsume(PendingTrigger t, EffectDef e)
    {
        var owner = Side(t.Side);
        var source = t.SourceCreature is { IsAlive: true } s && owner.Board.Contains(s) ? s : null;
        foreach (var c in Select(t, e.Target!))
        {
            if (ReferenceEquals(c, source)) continue;
            int attack = Stats.Attack(_db, OwnerOf(c), c);
            int health = Stats.Health(_db, c);
            c.MarkedForDestroy = true;
            if (source == null) continue;
            source.BonusAttack += attack;
            source.BonusHealth += health;
            RecordPermanent(owner, source, attack, health, null);
            StatsChanged(owner, source, Registry.Durations.Permanent);
        }
    }

    private void OpReduceDamage(PendingTrigger t, EffectDef e)
    {
        if (PendingHeroDamage is int damage) PendingHeroDamage = Math.Max(0, damage - e.Amount!.Value);
    }

    private void OpDraw(PendingTrigger t, EffectDef e) =>
        Zones.Draw(Side(t.Side), _rng(t.Side), _config, e.Count!.Value, Events);

    private void OpManaNextRound(PendingTrigger t, EffectDef e) => Side(t.Side).ManaBonus += e.Amount!.Value;

    private void OpSummon(PendingTrigger t, EffectDef e)
    {
        var p = Side(t.Side);
        var def = _db.Get(e.Card!);
        int index = SummonIndex(p, t, e.At!);
        for (int k = 0; k < (e.Count ?? 1); k++)
        {
            if (p.AliveCount >= _config.BoardSize)
            {
                Events.Add(new GameEvent { Type = EventTypes.SummonFailed, Side = p.Side, CardId = def.Id, Reason = "board_full" });
                continue;
            }
            var c = NewEntity(p, def.Id);
            p.Board.Insert(index, c);
            index++;
            Events.Add(new GameEvent
            {
                Type = EventTypes.CreatureSummoned, Side = p.Side, EntityId = c.EntityId, CardId = c.CardId,
                Position = AliveIndex(p, c), Creature = CreatureView.Of(_db, p, c),
                SourceEntityId = t.SourceCreature?.EntityId, SourceInstanceId = t.SourceInstanceId,
            });
        }
    }

    /// <summary>Индекс в списке стола (с надгробиями), куда встаёт первый призванный.</summary>
    private static int SummonIndex(PlayerState p, PendingTrigger t, string at)
    {
        int sourceIndex = t.SourceCreature == null ? -1 : p.Board.IndexOf(t.SourceCreature);
        switch (at)
        {
            case Registry.Placements.DeathPosition when sourceIndex >= 0:
                // Вплотную слева от надгробия; если источник жив — вплотную слева от него (8.8).
                return sourceIndex;
            case Registry.Placements.RightOfSelf when sourceIndex >= 0:
                // У погибшего источника работает как death_position (11.2).
                return t.SourceCreature!.IsAlive ? sourceIndex + 1 : sourceIndex;
            case Registry.Placements.Center:
            {
                int n = p.AliveCount, target = n / 2, alive = 0;
                for (int i = 0; i < p.Board.Count; i++)
                {
                    if (!p.Board[i].IsAlive) continue;
                    if (alive == target) return i;
                    alive++;
                }
                return p.Board.Count;
            }
            default:
                // rightmost; а также позиция от источника, которого нет на столе.
                return p.Board.Count;
        }
    }
}
