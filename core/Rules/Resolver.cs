namespace Game.Core;

/// <summary>Триггер или эффект в очереди (9.1).</summary>
internal sealed class PendingTrigger
{
    public required Side Side { get; init; }
    public required string SourceCardId { get; init; }

    /// <summary>Источник-существо (может уже стать надгробием).</summary>
    public Creature? SourceCreature { get; init; }

    /// <summary>Источник-карта: заклинание или чары на поле.</summary>
    public int? SourceInstanceId { get; init; }

    /// <summary>null — эффект заклинания, а не способность.</summary>
    public AbilityRef? Ability { get; init; }

    public required IReadOnlyList<EffectDef> Effects { get; init; }

    /// <summary>Триггер срабатывает и после ухода источника со стола: on_death (9.1).</summary>
    public bool SurvivesSource { get; init; }

    public int? Chosen { get; init; }
    public int? EventTarget { get; init; }
    public int? EventAttacker { get; init; }
}

/// <summary>
/// Очередь триггеров и исполнение эффектов (9.1, 8.8, 11.2, 11.3).
/// В подготовке работает на одной стороне — другую не видит вовсе (5.6).
/// </summary>
internal sealed class Resolver
{
    private readonly CardDatabase _db;
    private readonly GameConfig _config;
    private readonly PlayerState?[] _sides;
    private readonly Func<Side, IRandom> _rng;
    private readonly Queue<PendingTrigger> _queue = new();

    public Phase Phase { get; }
    public List<GameEvent> Events { get; }

    private delegate void OpHandler(Resolver r, PendingTrigger t, EffectDef e);

    /// <summary>Реализации операций 11.2. Новая механика — новая запись здесь и в Registry.</summary>
    private static readonly Dictionary<string, OpHandler> Handlers = new(StringComparer.Ordinal)
    {
        [Registry.Ops.Buff] = (r, t, e) => r.OpBuff(t, e),
        [Registry.Ops.GiveKeyword] = (r, t, e) => r.OpGiveKeyword(t, e),
        [Registry.Ops.Destroy] = (r, t, e) => r.OpDestroy(t, e),
        [Registry.Ops.Summon] = (r, t, e) => r.OpSummon(t, e),
        [Registry.Ops.Draw] = (r, t, e) => r.OpDraw(t, e),
        [Registry.Ops.ManaNextRound] = (r, t, e) => r.OpManaNextRound(t, e),
        [Registry.Ops.Consume] = (r, t, e) => r.OpConsume(t, e),
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
        return new Resolver(db, state.Config, Phase.Planning, sides, _ => rng, events);
    }

    private PlayerState Side(Side side) =>
        _sides[(int)side] ?? throw new InvalidOperationException($"Сторона {side} недоступна в подготовке противника (5.6)");

    private static Side Other(Side s) => s == Core.Side.A ? Core.Side.B : Core.Side.A;

    public void Enqueue(PendingTrigger t) => _queue.Enqueue(t);

    /// <summary>
    /// Поставить в очередь способности с триггером trigger в порядке 9.2:
    /// поле чар по слотам → существа слева направо (свои способности, затем от чар).
    /// </summary>
    public void EnqueueSideTriggers(Side side, string trigger, int? eventTarget = null, Creature? except = null)
    {
        var p = Side(side);
        foreach (var slot in p.Field)
        {
            if (slot is not int ench) continue;
            var def = _db.Get(p.GetInstance(ench).CardId);
            for (int i = 0; i < def.Abilities.Count; i++)
            {
                var a = def.Abilities[i];
                if (a.Trigger != trigger) continue;
                Enqueue(new PendingTrigger
                {
                    Side = side, SourceCardId = def.Id, SourceInstanceId = ench,
                    Ability = new AbilityRef(a, i, null), Effects = a.Effects, EventTarget = eventTarget,
                });
            }
        }
        foreach (var c in p.Alive.ToList())
        {
            if (ReferenceEquals(c, except)) continue;
            EnqueueCreatureTriggers(side, c, trigger, eventTarget: eventTarget);
        }
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

    /// <summary>разрешить_очередь() (9.1).</summary>
    public void Run()
    {
        int resolved = 0;
        ProcessDeaths();
        while (_queue.Count > 0)
        {
            var t = _queue.Dequeue();
            if (SourceLeft(t)) continue;
            Resolve(t);
            ProcessDeaths();
            resolved++;
            if (resolved > _config.MaxTriggers)
            {
                // 9.1: в подготовке оставшиеся триггеры отбрасываются, в лог — warning.
                _queue.Clear();
                Events.Add(new GameEvent { Type = EventTypes.Warning, Text = $"превышен предел MAX_TRIGGERS ({_config.MaxTriggers}) за одно действие" });
                break;
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
                throw new InvalidOperationException($"Операция «{e.Op}» не реализована для фазы {Phase}");
            handler(this, t, e);
        }
    }

    // ---------- Смерть (8.8) ----------

    private void ProcessDeaths()
    {
        while (true)
        {
            var dead = new List<(Side Side, Creature C, List<AbilityRef> Abilities)>();
            foreach (var side in new[] { Core.Side.A, Core.Side.B })
            {
                var p = _sides[(int)side];
                if (p == null) continue;
                foreach (var c in p.Alive)
                    if (c.MarkedForDestroy) dead.Add((side, c, Stats.Abilities(_db, p, c)));
            }
            if (dead.Count == 0) return;

            foreach (var (side, c, _) in dead)
            {
                var p = Side(side);
                int position = AliveIndex(p, c);
                c.MarkedForDestroy = false;
                c.Tombstone = true;
                Events.Add(new GameEvent { Type = EventTypes.Died, Side = side, EntityId = c.EntityId, CardId = c.CardId, Position = position });
            }

            foreach (var (side, c, abilities) in dead)
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
                // 3. «Когда умирает союзник»: чары на поле, затем живые союзники.
                EnqueueSideTriggers(side, Registry.Triggers.FriendlyDied, eventTarget: c.EntityId);

                // 8.8: в подготовке погибшее существо уходит в сброс (токен исчезает), чары с него — в сброс.
                if (Phase == Phase.Planning) Zones.DiscardCreature(Side(side), c);
            }
        }
    }

    private static int AliveIndex(PlayerState p, Creature c)
    {
        int index = 0;
        foreach (var x in p.Board)
        {
            if (ReferenceEquals(x, c)) return index;
            if (x.IsAlive) index++;
        }
        return index;
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

    private PlayerState OwnerOf(Creature c)
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

    private string DurationOf(EffectDef e) =>
        Phase == Phase.Planning ? Registry.Durations.Permanent : e.Duration ?? Registry.Durations.Combat;

    private void OpBuff(PendingTrigger t, EffectDef e)
    {
        string duration = DurationOf(e);
        foreach (var c in Select(t, e.Target!))
        {
            c.BonusAttack += e.Attack ?? 0;
            c.BonusHealth += e.Health ?? 0;
            StatsChanged(OwnerOf(c), c, duration);
        }
    }

    private void OpGiveKeyword(PendingTrigger t, EffectDef e)
    {
        string duration = DurationOf(e);
        foreach (var c in Select(t, e.Target!))
        {
            if (c.GainedKeywords.Contains(e.Keyword!)) continue;
            c.GainedKeywords.Add(e.Keyword!);
            Events.Add(new GameEvent
            {
                Type = EventTypes.KeywordChanged, Side = OwnerOf(c).Side, EntityId = c.EntityId,
                Keyword = e.Keyword, Gained = true, Duration = duration,
            });
        }
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
            StatsChanged(owner, source, Registry.Durations.Permanent);
        }
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
            var c = Entities.NewCreature(p, def.Id, null);
            p.Board.Insert(index, c);
            index++;
            Events.Add(new GameEvent
            {
                Type = EventTypes.CreatureSummoned, Side = p.Side, EntityId = c.EntityId, CardId = c.CardId,
                Attack = Stats.Attack(_db, p, c), Health = Stats.Health(_db, c), Position = AliveIndex(p, c),
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
                // Вплотную слева от надгробия.
                // GDD-QUESTION: если источник жив (death_position не из «Посмертно»), встаём слева от него (Q8).
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
