namespace Game.Core;

/// <summary>
/// Правила раунда и подготовки (4.3, 5). Функции над MatchState; события пишутся в state.Log и возвращаются.
/// </summary>
public sealed class GameRules
{
    private static readonly Side[] BothSides = { Side.A, Side.B };

    private readonly CardDatabase _db;

    public GameData Data { get; }

    public GameRules(GameData data)
    {
        Data = data;
        _db = data.Cards;
    }

    // ---------- Раунд (4.3) ----------

    /// <summary>startRound(state) → Event[].</summary>
    public List<GameEvent> StartRound(MatchState state)
    {
        RequireStage(state, RoundStage.Idle, "начать раунд");
        var cfg = state.Config;
        var events = new List<GameEvent>();

        state.Round++;
        foreach (var side in BothSides)
        {
            var p = state.Player(side);
            p.ManaMax = Math.Min(state.Round, cfg.ManaMax);
            p.Mana = p.ManaMax + p.ManaBonus;
            p.ManaBonus = 0;
        }
        events.Add(new GameEvent
        {
            Type = EventTypes.RoundStarted, Round = state.Round,
            Mana = BothSides.Select(s => state.Player(s).Mana).ToList(),
        });

        foreach (var side in BothSides)
        {
            var p = state.Player(side);
            if (state.Round > 1) Zones.Draw(p, state.Rng(side), cfg, cfg.DrawPerRound, events);
            foreach (var inst in p.Instances) inst.UsedAbilities.Clear();
            foreach (var c in p.Board) c.UsedAbilities.Clear();
            p.IsReady = false;

            var r = Resolver.ForPlanning(_db, state, side, events);
            r.EnqueueSideTriggers(side, Registry.Triggers.RoundStart);
            r.Run();
        }

        state.Stage = RoundStage.Planning;
        state.Log.AddRange(events);
        return events;
    }

    /// <summary>endRound(state) → Event[]: триггеры конца раунда (A, потом B), мана сгорает (4.3, шаг 5).</summary>
    public List<GameEvent> EndRound(MatchState state)
    {
        if (state.Stage is not (RoundStage.Planning or RoundStage.CombatDone))
            throw new InvalidOperationException($"Нельзя закончить раунд на стадии {state.Stage}");
        var events = new List<GameEvent>();

        foreach (var side in BothSides)
        {
            var r = Resolver.ForPlanning(_db, state, side, events);
            r.EnqueueSideTriggers(side, Registry.Triggers.RoundEnd);
            r.Run();
        }
        foreach (var side in BothSides)
        {
            state.Player(side).Mana = 0;
            state.Player(side).IsReady = false;
        }
        events.Add(new GameEvent { Type = EventTypes.RoundEnded, Round = state.Round });
        state.Stage = RoundStage.Idle;

        // 4.4: после раунда MAX_ROUNDS без победителя побеждает сторона с большим HP.
        if (state.Result == null && state.Round >= state.Config.MaxRounds)
        {
            int a = state.Player(Side.A).HeroHp, b = state.Player(Side.B).HeroHp;
            state.Result = a > b ? Outcome.A : b > a ? Outcome.B : Outcome.Draw;
        }
        if (state.Result != null)
        {
            state.Stage = RoundStage.MatchOver;
            events.Add(new GameEvent { Type = EventTypes.MatchEnded, Result = state.Result });
        }

        state.Log.AddRange(events);
        return events;
    }

    // ---------- Бой (8) ----------

    /// <summary>simulateCombat(snapshot, seed): чистая функция; RNG — поток combat[round] от seed матча (раздел 10).</summary>
    public CombatResult SimulateCombat(CombatSnapshot snapshot, ulong matchSeed) =>
        new CombatEngine(_db, snapshot, RngStreams.CombatStream(matchSeed, snapshot.Round)).Run();

    /// <summary>runCombat(state): снимок → simulateCombat → применение итога (8.9).</summary>
    public List<GameEvent> RunCombat(MatchState state)
    {
        RequireStage(state, RoundStage.Planning, "начать бой");
        if (state.Players.Any(p => !p.IsReady))
            throw new InvalidOperationException("Бой начинается, когда обе стороны нажали «Готов»");

        return ApplyCombatResult(state, SimulateCombat(Combat.Snapshot(state), state.Seed));
    }

    /// <summary>
    /// Итог боя в постоянное состояние (8.9): урон герою, сработавшие боевые карты — в сброс,
    /// изменения «навсегда» — на оригиналы по entityId; затем проверка победы (4.4).
    /// </summary>
    public List<GameEvent> ApplyCombatResult(MatchState state, CombatResult result)
    {
        var events = new List<GameEvent>(result.Events);

        if (result.DamagedSide is Side damaged)
            state.Player(damaged).HeroHp -= result.HeroDamage;

        foreach (var side in BothSides)
        {
            var p = state.Player(side);
            foreach (int inst in result.SpentCards[(int)side])
                if (p.BattleZone.Remove(inst)) p.Discard.Add(inst);
        }

        // Перенос изменений «навсегда» на оригиналы по entityId.
        foreach (var d in result.PermanentDeltas)
        {
            var p = state.Player(d.Side);
            var c = p.Board.FirstOrDefault(x => x.EntityId == d.EntityId);
            if (c == null) continue;
            c.BonusAttack += d.Attack;
            c.BonusHealth += d.Health;
            if (d.Keyword != null && !c.GainedKeywords.Contains(d.Keyword)) c.GainedKeywords.Add(d.Keyword);
            events.Add(new GameEvent
            {
                Type = EventTypes.PermanentApplied, Side = d.Side, EntityId = d.EntityId,
                Attack = Stats.Attack(_db, p, c), Health = Stats.Health(_db, c), Keyword = d.Keyword,
            });
        }

        state.Stage = RoundStage.CombatDone;

        // 4.4: герой с HP ≤ 0 проигрывает; оба — ничья.
        bool deadA = state.Player(Side.A).HeroHp <= 0, deadB = state.Player(Side.B).HeroHp <= 0;
        if (deadA || deadB)
        {
            state.Result = deadA && deadB ? Outcome.Draw : deadA ? Outcome.B : Outcome.A;
            state.Stage = RoundStage.MatchOver;
            events.Add(new GameEvent { Type = EventTypes.MatchEnded, Result = state.Result });
        }

        state.Log.AddRange(events);
        return events;
    }

    private static void RequireStage(MatchState state, RoundStage stage, string what)
    {
        if (state.Stage != stage)
            throw new InvalidOperationException($"Нельзя {what} на стадии {state.Stage}");
    }

    // ---------- Допустимые действия (5.3, 12.2) ----------

    /// <summary>
    /// legalActions(state, side): карты руки слева направо, затем способности существ слева направо;
    /// внутри карты — позиции, затем цели по возрастанию entityId; в конце Move, Recall, Dismiss, Ready.
    /// </summary>
    public List<GameAction> LegalActions(MatchState state, Side side)
    {
        var result = new List<GameAction>();
        var p = state.Player(side);
        if (state.Stage != RoundStage.Planning || p.IsReady) return result;
        var cfg = state.Config;

        foreach (int instanceId in p.Hand)
        {
            var def = _db.Get(p.GetInstance(instanceId).CardId);
            if (def.Cost > p.Mana) continue;
            switch (def.Type)
            {
                case CardType.Creature:
                {
                    if (p.AliveCount >= cfg.BoardSize) break;
                    var spec = def.Abilities.FirstOrDefault(a => a.Trigger == Registry.Triggers.OnPlay && a.Target != null)?.Target;
                    // 5.4: разыгрываемое существо ещё не на столе — оно не цель своего «При розыгрыше».
                    var targets = spec == null ? new List<int>() : ValidTargets(p, spec, source: null);
                    for (int pos = 0; pos <= p.Board.Count; pos++)
                    {
                        if (targets.Count == 0) result.Add(new PlayCard(instanceId, Slot: pos));
                        else foreach (int t in targets) result.Add(new PlayCard(instanceId, Slot: pos, Target: t));
                    }
                    break;
                }
                case CardType.Spell:
                    if (def.PlayTarget == null) result.Add(new PlayCard(instanceId));
                    else foreach (int t in ValidTargets(p, def.PlayTarget, null)) result.Add(new PlayCard(instanceId, Target: t));
                    break;
                case CardType.BattleSpell:
                case CardType.Secret:
                    if (p.BattleZone.Count < cfg.BattleSlots && p.BattleZone.All(id => p.GetInstance(id).CardId != def.Id))
                        result.Add(new PlayCard(instanceId));
                    break;
                case CardType.Enchantment when def.Attach == AttachKind.Creature:
                    foreach (int t in ValidTargets(p, def.PlayTarget!, null)) result.Add(new PlayCard(instanceId, Target: t));
                    break;
                case CardType.Enchantment:
                    for (int slot = 0; slot < p.Field.Count; slot++) result.Add(new PlayCard(instanceId, Slot: slot));
                    break;
            }
        }

        foreach (var c in p.Board)
        {
            foreach (var a in Stats.Abilities(_db, p, c))
            {
                if (a.Ability.Trigger != Registry.Triggers.Activated) continue;
                if (UsedAbilities(p, c).Contains(a.Index) || a.Ability.Cost > p.Mana) continue;
                if (a.Ability.Target == null) result.Add(new UseAbility(c.EntityId, a.Index));
                else foreach (int t in ValidTargets(p, a.Ability.Target, c)) result.Add(new UseAbility(c.EntityId, a.Index, Target: t));
            }
        }

        for (int from = 0; from < p.Board.Count; from++)
            for (int to = 0; to < p.Board.Count; to++)
                if (to != from) result.Add(new Move(p.Board[from].EntityId, to));
        foreach (var c in p.Board)
            if (c.InstanceId != null && p.Hand.Count < cfg.HandMax) result.Add(new Recall(c.EntityId));
        foreach (var c in p.Board) result.Add(new Dismiss(c.EntityId));
        result.Add(new Ready());
        return result;
    }

    /// <summary>Допустимые цели игрока (5.4): только свои живые существа, по возрастанию entityId.</summary>
    private List<int> ValidTargets(PlayerState p, TargetSpec spec, Creature? source) =>
        p.Alive
            .Where(c => !(spec.NotSelf && ReferenceEquals(c, source)))
            .Where(c => spec.Tag == null || _db.Get(c.CardId).Tags.Contains(spec.Tag))
            .Select(c => c.EntityId)
            .OrderBy(id => id)
            .ToList();

    /// <summary>Флаги «способность использована»: у карты — на экземпляре, у токена — на сущности (5.3).</summary>
    private static List<int> UsedAbilities(PlayerState p, Creature c) =>
        c.InstanceId is int id ? p.GetInstance(id).UsedAbilities : c.UsedAbilities;

    // ---------- Действия ----------

    /// <summary>applyAction(state, side, action): недопустимое действие — InvalidActionException, состояние не меняется.</summary>
    public List<GameEvent> ApplyAction(MatchState state, Side side, GameAction action)
    {
        if (!LegalActions(state, side).Contains(action))
            throw new InvalidActionException($"Действие {action} недопустимо для стороны {side} (раунд {state.Round}, стадия {state.Stage})");

        var p = state.Player(side);
        var events = new List<GameEvent>();
        switch (action)
        {
            case PlayCard pc: PlayCardAction(state, p, pc, events); break;
            case UseAbility ua: UseAbilityAction(state, p, ua, events); break;
            case Move mv:
            {
                var c = p.Board.Single(x => x.EntityId == mv.EntityId);
                int from = p.Board.IndexOf(c);
                p.Board.RemoveAt(from);
                p.Board.Insert(mv.ToIndex, c);
                events.Add(new GameEvent { Type = EventTypes.CreatureMoved, Side = side, EntityId = c.EntityId, FromIndex = from, ToIndex = mv.ToIndex });
                break;
            }
            case Recall rc:
            {
                // 5.5: экземпляр — в руку в исходном виде; чары — в сброс. Не смерть.
                var c = p.Board.Single(x => x.EntityId == rc.EntityId);
                int from = p.Board.IndexOf(c);
                p.Board.RemoveAt(from);
                p.Discard.AddRange(c.Enchantments);
                p.Hand.Add(c.InstanceId!.Value);
                events.Add(new GameEvent { Type = EventTypes.CreatureRecalled, Side = side, EntityId = c.EntityId, InstanceId = c.InstanceId, FromIndex = from });
                break;
            }
            case Dismiss ds:
            {
                // «Убрать» — не смерть: карта в сброс, токен исчезает, чары в сброс.
                var c = p.Board.Single(x => x.EntityId == ds.EntityId);
                int from = p.Board.IndexOf(c);
                p.Board.RemoveAt(from);
                Zones.DiscardCreature(p, c);
                events.Add(new GameEvent { Type = EventTypes.CreatureDismissed, Side = side, EntityId = c.EntityId, InstanceId = c.InstanceId, FromIndex = from });
                break;
            }
            case Ready:
                p.IsReady = true;
                break;
        }
        state.Log.AddRange(events);
        return events;
    }

    private void PlayCardAction(MatchState state, PlayerState p, PlayCard pc, List<GameEvent> events)
    {
        var def = _db.Get(p.GetInstance(pc.InstanceId).CardId);
        p.Hand.Remove(pc.InstanceId);
        p.Mana -= def.Cost;

        GameEvent Played(int? entityId = null) => new()
        {
            Type = EventTypes.CardPlayed, Side = p.Side, InstanceId = pc.InstanceId, CardId = def.Id,
            EntityId = entityId, Target = pc.Target, Position = def.Type == CardType.Creature ? pc.Slot : null,
            Slot = def.Type == CardType.Enchantment && def.Attach == AttachKind.Field ? pc.Slot : null,
            ManaAfter = p.Mana,
        };

        switch (def.Type)
        {
            case CardType.Creature:
            {
                var c = Entities.NewCreature(p, def.Id, pc.InstanceId);
                p.Board.Insert(pc.Slot!.Value, c);
                events.Add(Played(c.EntityId));
                var r = Resolver.ForPlanning(_db, state, p.Side, events);
                r.EnqueueCreatureTriggers(p.Side, c, Registry.Triggers.OnPlay, chosen: pc.Target);
                r.Run();
                break;
            }
            case CardType.Spell:
            {
                events.Add(Played());
                var r = Resolver.ForPlanning(_db, state, p.Side, events);
                r.Enqueue(new PendingTrigger
                {
                    Side = p.Side, SourceCardId = def.Id, SourceInstanceId = pc.InstanceId,
                    Effects = def.Effects, Chosen = pc.Target,
                });
                r.Run();
                // GDD-QUESTION: заклинание уходит в сброс после разрешения эффекта, поэтому не попадает
                // в прокрутку, вызванную собственным добором (Q7).
                Zones.SpendSpell(_db, p, pc.InstanceId);
                break;
            }
            case CardType.BattleSpell:
            case CardType.Secret:
                p.BattleZone.Add(pc.InstanceId);
                events.Add(Played());
                break;
            case CardType.Enchantment when def.Attach == AttachKind.Creature:
                p.Board.Single(c => c.EntityId == pc.Target).Enchantments.Add(pc.InstanceId);
                events.Add(Played());
                break;
            case CardType.Enchantment:
            {
                int slot = pc.Slot!.Value;
                if (p.Field[slot] is int replaced) p.Discard.Add(replaced);
                p.Field[slot] = pc.InstanceId;
                events.Add(Played());
                break;
            }
        }
    }

    private void UseAbilityAction(MatchState state, PlayerState p, UseAbility ua, List<GameEvent> events)
    {
        var c = p.Board.Single(x => x.EntityId == ua.EntityId);
        var a = Stats.Abilities(_db, p, c)[ua.AbilityIndex];
        p.Mana -= a.Ability.Cost ?? 0;
        UsedAbilities(p, c).Add(a.Index);
        events.Add(new GameEvent
        {
            Type = EventTypes.AbilityActivated, Side = p.Side, EntityId = c.EntityId,
            AbilityIndex = a.Index, Target = ua.Target, ManaAfter = p.Mana,
        });
        var r = Resolver.ForPlanning(_db, state, p.Side, events);
        r.Enqueue(new PendingTrigger
        {
            Side = p.Side, SourceCardId = c.CardId, SourceCreature = c, Ability = a,
            Effects = a.Ability.Effects, Chosen = ua.Target,
        });
        r.Run();
    }
}
