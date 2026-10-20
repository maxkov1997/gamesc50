using Game.Core;

namespace Game.Client;

/// <summary>
/// Воспроизведение сохранённого реплея: раунды идут по записи (seed + колоды + действия), бой каждого раунда
/// заново считает ядро и отдаёт лог для проигрывания. Матч детерминирован (раздел 10), поэтому всё совпадает с оригиналом.
/// </summary>
public sealed class ReplaySession
{
    private readonly GameRules _rules;
    private int _next;

    public MatchRecord Record { get; }
    public MatchState State { get; }
    public GameData Data { get; }

    public int RoundsTotal => Record.Rounds.Count;
    public bool Done => _next >= Record.Rounds.Count || State.Result != null;

    public ReplaySession(GameData data, MatchRecord record)
    {
        Data = data;
        Record = record;
        _rules = new GameRules(data);
        State = Match.CreateMatch(data, record.DeckA, record.DeckB, record.Seed, record.Mode);
    }

    public static ReplaySession Load(GameData data, string json) => new(data, StateSerializer.DeserializeRecord(json));

    /// <summary>Сыграть следующий раунд записи; возвращает бой для проигрывания.</summary>
    public CombatPlayback NextCombat()
    {
        if (Done) throw new InvalidOperationException("Реплей закончен");
        if (_next > 0) _rules.EndRound(State);
        _rules.StartRound(State);
        foreach (var a in Record.Rounds[_next]) _rules.ApplyAction(State, a.Side, a.Action);
        _next++;
        return new CombatPlayback(_rules.RunCombat(State));
    }

    /// <summary>Доиграть до конца без проигрывания (итоговое состояние совпадает с Replay.Run).</summary>
    public void RunToEnd()
    {
        while (!Done) NextCombat();
    }
}
