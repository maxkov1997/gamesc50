namespace Game.Core;

/// <summary>Снимок перед боем (8.1): копии обеих сторон — существа, чары, боевые зоны, герои.</summary>
public sealed class CombatSnapshot
{
    public int Round { get; set; }
    public GameConfig Config { get; set; } = new();

    /// <summary>Индекс 0 — A, 1 — B.</summary>
    public List<PlayerState> Sides { get; set; } = new();
}

public static class CombatReasons
{
    public const string Wipe = "wipe";
    public const string Stalemate = "stalemate";
    public const string Limit = "limit";
}

/// <summary>Итог simulateCombat: { events, result, reason, heroDamage, permanentDeltas } (12.2).</summary>
public sealed class CombatResult
{
    public List<GameEvent> Events { get; init; } = new();
    public Outcome Result { get; init; }
    public string Reason { get; init; } = "";
    public int Attacks { get; init; }

    /// <summary>Чей герой получает урон; null — ничья.</summary>
    public Side? DamagedSide { get; init; }
    public int HeroDamage { get; init; }
    public List<PermanentDelta> PermanentDeltas { get; init; } = new();

    /// <summary>Сработавшие боевые заклинания и секреты по сторонам (индекс — сторона): после боя в сброс.</summary>
    public List<List<int>> SpentCards { get; init; } = new() { new(), new() };
}

public static class Combat
{
    /// <summary>Снимок обеих сторон. Рука, колода, сброс и изгнание в бой не попадают.</summary>
    public static CombatSnapshot Snapshot(MatchState state)
    {
        var snapshot = new CombatSnapshot { Round = state.Round, Config = state.Config };
        foreach (var p in state.Players)
        {
            var copy = StateSerializer.Clone(p);
            copy.Hand.Clear();
            copy.DrawPile.Clear();
            copy.Discard.Clear();
            copy.Exile.Clear();
            var used = new HashSet<int>(copy.BattleZone);
            foreach (var slot in copy.Field) if (slot is int id) used.Add(id);
            foreach (var c in copy.Board)
            {
                if (c.InstanceId is int id) used.Add(id);
                used.UnionWith(c.Enchantments);
            }
            copy.Instances.RemoveAll(i => !used.Contains(i.InstanceId));
            snapshot.Sides.Add(copy);
        }
        return snapshot;
    }

    /// <summary>
    /// Урон герою проигравшего (8.9): min(DAMAGE_CAP, DAMAGE_BASE + Σ max(1, базовая стоимость)) по выжившим победителя,
    /// включая токены.
    /// </summary>
    public static int HeroDamage(CardDatabase db, GameConfig config, IEnumerable<Creature> survivors) =>
        Math.Min(config.DamageCap, config.DamageBase + survivors.Sum(c => Math.Max(1, db.Get(c.CardId).Cost)));
}

/// <summary>
/// Бой (8.2). Работает на копии снимка: снимок не меняется, исход зависит только от снимка и RNG.
/// </summary>
public sealed class CombatEngine
{
    private readonly CardDatabase _db;
    private readonly GameConfig _config;
    private readonly IRandom _rng;
    private readonly int _round;
    private readonly PlayerState[] _sides;
    private readonly Resolver _r;
    private readonly List<int>[] _sweep = { new(), new() };
    private bool _started;

    public int Attacks { get; private set; }
    public List<GameEvent> Events => _r.Events;

    public CombatEngine(CardDatabase db, CombatSnapshot snapshot, IRandom rng)
    {
        _db = db;
        _config = snapshot.Config;
        _rng = rng;
        _round = snapshot.Round;
        _sides = snapshot.Sides.Select(StateSerializer.Clone).ToArray();
        _r = new Resolver(db, _config, Phase.Combat, _sides, _ => rng, new List<GameEvent>());
    }

    /// <summary>Копия стороны в бою — для проверок в тестах.</summary>
    public PlayerState Side(Side side) => _sides[(int)side];

    /// <summary>Пометить существа «уничтожить» и разрешить очередь (для тест-кейсов вида «destroy по …»).</summary>
    public void Destroy(params int[] entityIds)
    {
        foreach (var p in _sides)
            foreach (var c in p.Alive)
                if (entityIds.Contains(c.EntityId)) c.MarkedForDestroy = true;
        _r.Run();
    }

    private bool IsOver() => _sides.Any(p => p.AliveCount == 0);

    private bool Limit => _r.LimitReached || Attacks > _config.MaxAttacks;

    public CombatResult Run()
    {
        if (_started) throw new InvalidOperationException("Бой уже проведён");
        _started = true;

        var first = Initiative();
        Events.Add(new GameEvent
        {
            Type = EventTypes.CombatStarted, Side = first, Round = _round,
            Sides = _sides.Select(p => SideView.Of(_db, p)).ToList(),
        });

        StartOfCombat(first);
        string? reason = _r.LimitReached ? CombatReasons.Limit : null;

        if (reason == null && !IsOver())
        {
            var active = first;
            int passes = 0;
            while (true)
            {
                var a = NextAttacker(active);
                if (a == null)
                {
                    passes++;
                    if (passes == 2) { reason = CombatReasons.Stalemate; break; }
                }
                else
                {
                    passes = 0;
                    AttackerTurn(active, a);
                    if (Limit)
                    {
                        reason = CombatReasons.Limit;
                        if (!_r.LimitReached)
                            Events.Add(new GameEvent { Type = EventTypes.Warning, Text = $"превышен предел MAX_ATTACKS ({_config.MaxAttacks})" });
                        break;
                    }
                }
                if (IsOver()) break;
                active = Resolver.Other(active);
            }
        }
        return Finish(reason ?? CombatReasons.Wipe);
    }

    /// <summary>8.3: больше существ — атакует первой; при равенстве — rng.</summary>
    private Side Initiative()
    {
        int a = _sides[0].AliveCount, b = _sides[1].AliveCount;
        if (a != b) return a > b ? Core.Side.A : Core.Side.B;
        return _rng.Int(2) == 0 ? Core.Side.A : Core.Side.B;
    }

    /// <summary>
    /// 8.4: раскрыть боевые заклинания; затем по стороне с инициативой и другой — боевые заклинания
    /// в порядке розыгрыша → чары на поле по слотам → существа слева направо.
    /// </summary>
    private void StartOfCombat(Side first)
    {
        _r.ActiveSide = first;
        var order = new[] { first, Resolver.Other(first) };
        foreach (var side in order)
        {
            var p = _sides[(int)side];
            foreach (int inst in p.BattleZone)
            {
                var def = _db.Get(p.GetInstance(inst).CardId);
                if (def.Type == CardType.BattleSpell)
                    Events.Add(new GameEvent { Type = EventTypes.BattleSpellRevealed, Side = side, CardId = def.Id, InstanceId = inst });
            }
        }
        foreach (var side in order)
        {
            _r.EnqueueBattleSpells(side);
            _r.EnqueueSideTriggers(side, Registry.Triggers.StartOfCombat);
        }
        _r.Run();
    }

    /// <summary>8.5: следующий годный атакующий из прохода; проход кончился — новый проход.</summary>
    private Creature? NextAttacker(Side side)
    {
        var a = PopEligible(side);
        if (a != null) return a;
        _sweep[(int)side] = _sides[(int)side].Alive.Select(c => c.EntityId).ToList();
        return PopEligible(side);
    }

    private Creature? PopEligible(Side side)
    {
        var sweep = _sweep[(int)side];
        var p = _sides[(int)side];
        while (sweep.Count > 0)
        {
            int id = sweep[0];
            sweep.RemoveAt(0);
            if (p.FindAlive(id) is { } c && Stats.Attack(_db, p, c) > 0) return c;
        }
        return null;
    }

    /// <summary>8.6.</summary>
    private void AttackerTurn(Side side, Creature a)
    {
        var p = _sides[(int)side];
        var enemy = _sides[(int)Resolver.Other(side)];
        int times = Stats.HasKeyword(_db, p, a, Registry.Keywords.DoubleAttack) ? 2 : 1;
        for (int i = 0; i < times; i++)
        {
            if (!a.IsAlive || !p.Board.Contains(a) || Stats.Attack(_db, p, a) <= 0 || enemy.AliveCount == 0) return;
            var t = ChooseTarget(enemy);
            Attack(side, a, t);
            if (IsOver() || Limit) return;
        }
    }

    /// <summary>8.7: случайная цель; если есть Провокация — только среди таких.</summary>
    private Creature ChooseTarget(PlayerState enemy)
    {
        var enemies = enemy.Alive.ToList();
        var taunts = enemies.Where(c => Stats.HasKeyword(_db, enemy, c, Registry.Keywords.Taunt)).ToList();
        return _rng.Pick(taunts.Count > 0 ? taunts : enemies);
    }

    private void Attack(Side side, Creature a, Creature t)
    {
        var p = _sides[(int)side];
        var enemy = _sides[(int)Resolver.Other(side)];
        Attacks++;
        _r.ActiveSide = side;
        Events.Add(new GameEvent { Type = EventTypes.AttackDeclared, Side = side, SourceEntityId = a.EntityId, Target = t.EntityId });

        _r.EnqueueCreatureTriggers(side, a, Registry.Triggers.OnAttack, eventTarget: t.EntityId);
        _r.EnqueueSecrets(enemy.Side, Registry.Conditions.EnemyAttacks, eventTarget: t.EntityId, eventAttacker: a.EntityId);
        _r.Run();
        if (_r.LimitReached) return;
        // Атака сорвалась — черёд потрачен.
        if (!a.IsAlive || !t.IsAlive) return;

        if (Stats.HasKeyword(_db, enemy, t, Registry.Keywords.Flying) && _rng.Chance(_config.DodgeChance))
        {
            Events.Add(new GameEvent { Type = EventTypes.Dodge, Side = side, SourceEntityId = a.EntityId, Target = t.EntityId });
            return;
        }

        // Значения читаются до урона.
        int x = Stats.Attack(_db, p, a);
        int y = Stats.Attack(_db, enemy, t);
        var victims = new List<Creature> { t };
        if (Stats.HasKeyword(_db, p, a, Registry.Keywords.Cleave)) victims.AddRange(Selectors.Adjacent(enemy, t));
        foreach (var v in victims) _r.Deal(a, v, x);
        _r.Deal(t, a, y);
        _r.Run();
    }

    /// <summary>8.9.</summary>
    private CombatResult Finish(string reason)
    {
        int aliveA = _sides[0].AliveCount, aliveB = _sides[1].AliveCount;
        var result = Outcome.Draw;
        if (reason == CombatReasons.Wipe)
        {
            if (aliveA > 0 && aliveB == 0) result = Outcome.A;
            else if (aliveB > 0 && aliveA == 0) result = Outcome.B;
        }
        Events.Add(new GameEvent { Type = EventTypes.CombatEnded, Result = result, Reason = reason, Count = Attacks });

        Side? damaged = null;
        int damage = 0;
        if (result != Outcome.Draw)
        {
            var winner = result == Outcome.A ? Core.Side.A : Core.Side.B;
            var loser = _sides[(int)Resolver.Other(winner)];
            damaged = loser.Side;
            damage = Combat.HeroDamage(_db, _config, _sides[(int)winner].Alive);

            // hero_would_take_damage: секреты проигравшего могут уменьшить урон (не ниже 0).
            _r.ActiveSide = loser.Side;
            _r.PendingHeroDamage = damage;
            _r.EnqueueSecrets(loser.Side, Registry.Conditions.HeroWouldTakeDamage);
            _r.Run();
            damage = _r.PendingHeroDamage.Value;
            _r.PendingHeroDamage = null;

            loser.HeroHp -= damage;
            Events.Add(new GameEvent { Type = EventTypes.HeroDamaged, Side = loser.Side, Amount = damage, Health = loser.HeroHp });
        }

        return new CombatResult
        {
            Events = Events,
            Result = result,
            Reason = reason,
            Attacks = Attacks,
            DamagedSide = damaged,
            HeroDamage = damage,
            PermanentDeltas = _r.PermanentDeltas,
            SpentCards = _r.SpentCards.Select(l => l.ToList()).ToList(),
        };
    }
}
