namespace Game.Core;

/// <summary>
/// Стол боя, восстановленный только из событий лога (12.3): так его видит интерфейс.
/// Ядро ведёт такую же модель и дописывает stats_changed, если лог отстал от настоящего состояния
/// (например, аура пропала вместе с источником), — поэтому в конце каждой очереди модель совпадает с боем.
/// </summary>
public sealed class LogBoard
{
    public List<SideView> Sides { get; } = new();
    public bool Started { get; private set; }
    public Outcome? Result { get; private set; }
    public string? Reason { get; private set; }

    /// <summary>Раскрытые в бою боевые заклинания и секреты: (сторона, cardId) в порядке раскрытия.</summary>
    public List<(Side Side, string CardId)> Revealed { get; } = new();

    public SideView Side(Side side) => Sides[(int)side];

    public CreatureView? Find(int entityId)
    {
        foreach (var s in Sides)
            foreach (var c in s.Board)
                if (c.EntityId == entityId) return c;
        return null;
    }

    public void Apply(GameEvent e)
    {
        // До начала боя стола в логе ещё нет.
        if (!Started && e.Type != EventTypes.CombatStarted) return;
        switch (e.Type)
        {
            case EventTypes.CombatStarted:
                Sides.Clear();
                foreach (var s in e.Sides!) Sides.Add(Copy(s));
                Started = true;
                break;
            case EventTypes.CreatureSummoned:
            case EventTypes.Reborn:
            {
                var board = Side(e.Side!.Value).Board;
                board.Insert(Math.Clamp(e.Position ?? board.Count, 0, board.Count), Copy(e.Creature!));
                break;
            }
            case EventTypes.Died:
                Side(e.Side!.Value).Board.RemoveAll(c => c.EntityId == e.EntityId);
                break;
            case EventTypes.Damage:
                if (Find(e.Target!.Value) is { } hurt) hurt.Health = e.Health!.Value;
                break;
            case EventTypes.ShieldBroken:
                Find(e.EntityId!.Value)?.Keywords.Remove(Registry.Keywords.Shield);
                break;
            case EventTypes.StatsChanged:
                if (Find(e.EntityId!.Value) is { } changed)
                {
                    if (e.Creature != null)
                    {
                        changed.Attack = e.Creature.Attack;
                        changed.Health = e.Creature.Health;
                        changed.MaxHealth = e.Creature.MaxHealth;
                        changed.Keywords = new List<string>(e.Creature.Keywords);
                    }
                    else
                    {
                        changed.Attack = e.Attack ?? changed.Attack;
                        changed.Health = e.Health ?? changed.Health;
                    }
                }
                break;
            case EventTypes.KeywordChanged:
                if (Find(e.EntityId!.Value) is { } kc)
                {
                    if (e.Gained == true) { if (!kc.Keywords.Contains(e.Keyword!)) kc.Keywords.Add(e.Keyword!); }
                    else kc.Keywords.Remove(e.Keyword!);
                }
                break;
            case EventTypes.HeroDamaged:
                Side(e.Side!.Value).HeroHp = e.Health!.Value;
                break;
            case EventTypes.BattleSpellRevealed:
            case EventTypes.SecretRevealed:
                Revealed.Add((e.Side!.Value, e.CardId!));
                break;
            case EventTypes.CombatEnded:
                Result = e.Result;
                Reason = e.Reason;
                break;
        }
    }

    /// <summary>Совпадает ли существо из лога с настоящим (порядок ключевых слов не важен).</summary>
    public static bool Same(CreatureView a, CreatureView b) =>
        a.Attack == b.Attack && a.Health == b.Health && a.MaxHealth == b.MaxHealth &&
        a.Keywords.Count == b.Keywords.Count && a.Keywords.All(b.Keywords.Contains);

    private static SideView Copy(SideView s) => new()
    {
        Side = s.Side,
        HeroHp = s.HeroHp,
        Board = s.Board.Select(Copy).ToList(),
        Field = new List<string?>(s.Field),
        BattleZone = new List<string>(s.BattleZone),
    };

    private static CreatureView Copy(CreatureView c) => new()
    {
        EntityId = c.EntityId, CardId = c.CardId, Attack = c.Attack, Health = c.Health,
        MaxHealth = c.MaxHealth, Keywords = new List<string>(c.Keywords),
    };
}
