using Game.Core;

namespace Game.Ai;

/// <summary>
/// Базовый бот (12.4). Детерминированный: решения зависят только от состояния своей стороны,
/// случайности не использует. Действует только через legalActions/applyAction.
/// </summary>
public sealed class Bot
{
    /// <summary>Предохранитель от бесконечного цикла в самом боте — это ошибка кода, а не правило игры.</summary>
    private const int MaxActionsPerPlanning = 1000;

    private readonly CardDatabase _db;
    private readonly GameRules _rules;

    public Bot(GameData data)
    {
        _db = data.Cards;
        _rules = new GameRules(data);
    }

    /// <summary>Вся подготовка стороны до «Готов». Возвращает выполненные действия (для записи матча).</summary>
    public List<GameAction> PlayPlanning(GameRules rules, MatchState state, Side side)
    {
        var done = new List<GameAction>();
        while (!state.Player(side).IsReady)
        {
            if (done.Count >= MaxActionsPerPlanning)
                throw new InvalidOperationException($"Бот не нажал «Готов» за {MaxActionsPerPlanning} действий");
            var action = ChooseAction(state, side);
            rules.ApplyAction(state, side, action);
            done.Add(action);
        }
        return done;
    }

    /// <summary>Следующее действие бота.</summary>
    public GameAction ChooseAction(MatchState state, Side side)
    {
        var p = state.Player(side);
        var legal = _rules.LegalActions(state, side);
        if (legal.Count == 0) throw new InvalidOperationException($"У стороны {side} нет допустимых действий");

        if (DismissForBetterCreature(state, p) is { } dismiss) return dismiss;

        GameAction? best = null;
        int bestScore = int.MinValue;
        var preferred = new Dictionary<string, int?>(StringComparer.Ordinal);
        foreach (var action in legal)
        {
            // Строго больше: при равенстве остаётся то, что раньше в legalActions.
            if (Score(p, action, legal, preferred) is int score && score > bestScore)
            {
                best = action;
                bestScore = score;
            }
        }
        if (best != null) return best;

        if (ArrangeMove(p) is { } move) return move;
        return new Ready();
    }

    /// <summary>
    /// Стол полон, а в руке есть оплачиваемое существо дороже самого дешёвого на столе на 2+ —
    /// убрать самое дешёвое (при равенстве — самое левое).
    /// </summary>
    private Dismiss? DismissForBetterCreature(MatchState state, PlayerState p)
    {
        // GDD-QUESTION: правило проверяется до оценки действий в каждой итерации, даже когда других
        // допустимых действий из таблицы нет — иначе на полном столе с одними существами в руке оно не срабатывает (Q11).
        if (p.AliveCount < state.Config.BoardSize) return null;
        var payable = p.Hand.Select(id => _db.Get(p.GetInstance(id).CardId))
            .Where(d => d.Type == CardType.Creature && d.Cost <= p.Mana)
            .ToList();
        if (payable.Count == 0) return null;
        int best = payable.Max(d => d.Cost);

        Creature? cheapest = null;
        foreach (var c in p.Alive)
            if (cheapest == null || Cost(c) < Cost(cheapest)) cheapest = c;
        return cheapest != null && best >= Cost(cheapest) + 2 ? new Dismiss(cheapest.EntityId) : null;
    }

    private int Cost(Creature c) => _db.Get(c.CardId).Cost;

    /// <summary>Оценка из таблицы 12.4; null — действие бот не рассматривает.</summary>
    private int? Score(PlayerState p, GameAction action, List<GameAction> legal, Dictionary<string, int?> preferred)
    {
        switch (action)
        {
            case PlayCard pc:
            {
                var def = _db.Get(p.GetInstance(pc.InstanceId).CardId);
                switch (def.Type)
                {
                    case CardType.Creature:
                        // Существо ставится в правый край.
                        if (pc.Slot != p.Board.Count) return null;
                        if (!IsPreferredTarget(p, $"card:{pc.InstanceId}", pc.Target, PlayTargetEffects(def), preferred,
                                legal.OfType<PlayCard>().Where(x => x.InstanceId == pc.InstanceId && x.Slot == pc.Slot).Select(x => x.Target)))
                            return null;
                        return 100 + 10 * def.Cost;
                    case CardType.BattleSpell:
                    case CardType.Secret:
                        return 60 + def.Cost;
                    case CardType.Enchantment when def.Attach == AttachKind.Field:
                    {
                        // Только в свободный слот — первый свободный.
                        int free = p.Field.FindIndex(slot => slot == null);
                        return free >= 0 && pc.Slot == free ? 55 + def.Cost : null;
                    }
                    default:
                        if (!IsPreferredTarget(p, $"card:{pc.InstanceId}", pc.Target, PlayTargetEffects(def), preferred,
                                legal.OfType<PlayCard>().Where(x => x.InstanceId == pc.InstanceId).Select(x => x.Target)))
                            return null;
                        if (def.Type == CardType.Spell && def.Effects.Any(e => e.Op is Registry.Ops.Draw or Registry.Ops.ManaNextRound))
                            return 40;
                        return 50 + def.Cost;
                }
            }
            case UseAbility ua:
            {
                var c = p.Board.Single(x => x.EntityId == ua.EntityId);
                var ability = Stats.Abilities(_db, p, c)[ua.AbilityIndex].Ability;
                if (!IsPreferredTarget(p, $"ability:{ua.EntityId}:{ua.AbilityIndex}", ua.Target, ability.Effects, preferred,
                        legal.OfType<UseAbility>().Where(x => x.EntityId == ua.EntityId && x.AbilityIndex == ua.AbilityIndex).Select(x => x.Target)))
                    return null;
                return 30 + (ability.Cost ?? 0);
            }
            default:
                return null;
        }
    }

    /// <summary>Эффекты, которые получат выбранную игроком цель.</summary>
    private static IReadOnlyList<EffectDef> PlayTargetEffects(CardDef def) =>
        def.Type == CardType.Creature
            ? def.Abilities.FirstOrDefault(a => a.Trigger == Registry.Triggers.OnPlay && a.Target != null)?.Effects ?? Array.Empty<EffectDef>()
            : def.Effects;

    /// <summary>
    /// Цель — выбранная по правилу 12.4 среди допустимых: для consume — союзник с наименьшей стоимостью,
    /// для остального — с наибольшей атакой; при равенстве — левый.
    /// </summary>
    private bool IsPreferredTarget(PlayerState p, string key, int? target, IReadOnlyList<EffectDef> effects,
        Dictionary<string, int?> cache, IEnumerable<int?> candidates)
    {
        if (!cache.TryGetValue(key, out var best))
        {
            var options = candidates.Where(t => t != null).Select(t => t!.Value).ToHashSet();
            best = null;
            if (options.Count > 0)
            {
                bool consume = effects.Any(e => e.Op == Registry.Ops.Consume);
                Creature? pick = null;
                foreach (var c in p.Alive)
                {
                    if (!options.Contains(c.EntityId)) continue;
                    if (pick == null) { pick = c; continue; }
                    bool better = consume
                        ? Cost(c) < Cost(pick)
                        : Stats.Attack(_db, p, c) > Stats.Attack(_db, p, pick);
                    if (better) pick = c;
                }
                best = pick?.EntityId;
            }
            cache[key] = best;
        }
        return target == best;
    }

    /// <summary>
    /// Расстановка — стабильная сортировка стола по ключу (первое подходящее правило):
    /// 0 — токен или есть «Посмертно»; 2 — есть Провокация; 1 — остальные.
    /// Возвращает первую нужную перестановку или null, если стол уже упорядочен.
    /// </summary>
    private Move? ArrangeMove(PlayerState p)
    {
        // OrderBy в .NET — стабильная сортировка.
        var target = p.Board.OrderBy(c => ArrangeKey(p, c)).ToList();
        for (int i = 0; i < target.Count; i++)
            if (!ReferenceEquals(p.Board[i], target[i])) return new Move(target[i].EntityId, i);
        return null;
    }

    private int ArrangeKey(PlayerState p, Creature c)
    {
        if (_db.Get(c.CardId).Token || Stats.Abilities(_db, p, c).Any(a => a.Ability.Trigger == Registry.Triggers.OnDeath)) return 0;
        if (Stats.HasKeyword(_db, p, c, Registry.Keywords.Taunt)) return 2;
        return 1;
    }
}
