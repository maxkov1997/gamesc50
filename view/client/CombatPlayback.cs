using Game.Core;

namespace Game.Client;

/// <summary>
/// Проигрывание боя только по логу (12.3): стол — LogBoard, который обновляется событие за событием.
/// К началу проигрывания бой уже посчитан; скорость и «Пропустить» на исход не влияют.
/// </summary>
public sealed class CombatPlayback
{
    /// <summary>Длительность шага при скорости ×1, мс. Это оформление, а не правило игры.</summary>
    private static readonly IReadOnlyDictionary<string, int> StepMs = new Dictionary<string, int>(StringComparer.Ordinal)
    {
        [EventTypes.CombatStarted] = 600,
        [EventTypes.BattleSpellRevealed] = 700,
        [EventTypes.SecretRevealed] = 800,
        [EventTypes.AttackDeclared] = 550,
        [EventTypes.Dodge] = 450,
        [EventTypes.Damage] = 300,
        [EventTypes.ShieldBroken] = 350,
        [EventTypes.Died] = 350,
        [EventTypes.CreatureSummoned] = 350,
        [EventTypes.Reborn] = 450,
        [EventTypes.StatsChanged] = 150,
        [EventTypes.KeywordChanged] = 250,
        [EventTypes.HeroDamaged] = 900,
        [EventTypes.CombatEnded] = 500,
    };

    public static readonly IReadOnlyList<int> Speeds = new[] { 1, 2, 4 };

    public IReadOnlyList<GameEvent> Events { get; }
    public LogBoard Board { get; } = new();
    public int Position { get; private set; }
    public bool Done => Position >= Events.Count;
    public GameEvent? Current { get; private set; }

    /// <summary>events — события runCombat (или simulateCombat), начиная с combat_started.</summary>
    public CombatPlayback(IReadOnlyList<GameEvent> events)
    {
        int start = events.ToList().FindIndex(e => e.Type == EventTypes.CombatStarted);
        if (start < 0) throw new ArgumentException("В событиях нет combat_started", nameof(events));
        Events = events.Skip(start).ToList();
    }

    /// <summary>Следующее событие: применяется к столу лога и возвращается для анимации.</summary>
    public GameEvent? Next()
    {
        if (Done) return Current = null;
        Current = Events[Position++];
        Board.Apply(Current);
        return Current;
    }

    public void SkipToEnd()
    {
        while (!Done) Next();
    }

    /// <summary>Сколько показывать событие, мс, при скорости speed (×1/×2/×4); 0 — без паузы.</summary>
    public static int DurationMs(GameEvent e, int speed) =>
        StepMs.TryGetValue(e.Type, out int ms) ? ms / Math.Max(1, speed) : 0;
}
