namespace Game.Core;

/// <summary>Действие стороны в записи матча.</summary>
public sealed record RecordedAction(Side Side, GameAction Action);

/// <summary>Запись матча (12.2): seed + колоды + действия по раундам. Этого достаточно для реплея (раздел 10).</summary>
public sealed class MatchRecord
{
    public ulong Seed { get; set; }
    public PlanningMode Mode { get; set; }
    public required DeckList DeckA { get; set; }
    public required DeckList DeckB { get; set; }

    /// <summary>Действия подготовки каждого раунда в порядке применения.</summary>
    public List<List<RecordedAction>> Rounds { get; set; } = new();
}

public static class Replay
{
    /// <summary>
    /// replay(record) → MatchState: раунды 4.3 с действиями из записи. Матч детерминирован, поэтому
    /// результат побайтово совпадает с исходным, включая лог.
    /// </summary>
    public static MatchState Run(GameData data, MatchRecord record)
    {
        var rules = new GameRules(data);
        var state = Match.CreateMatch(data, record.DeckA, record.DeckB, record.Seed, record.Mode);
        foreach (var round in record.Rounds)
        {
            if (state.Result != null) break;
            rules.StartRound(state);
            foreach (var a in round) rules.ApplyAction(state, a.Side, a.Action);
            rules.RunCombat(state);
            if (state.Result != null) break;
            rules.EndRound(state);
        }
        return state;
    }
}
