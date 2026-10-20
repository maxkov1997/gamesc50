using Game.Content;
using Game.Core;

namespace Game.Client;

/// <summary>Строка лога боя для игрока. Шаблоны — в content/strings.json (раздел log), имена — из данных карт.</summary>
public sealed class LogText
{
    private readonly UiStrings _s;
    private readonly CardDatabase _db;

    public LogText(UiStrings strings, CardDatabase db)
    {
        _s = strings;
        _db = db;
    }

    public string SideName(Side? side) => side == MatchSession.PlayerSide ? _s.T(UiKeys.You) : _s.T(UiKeys.Opponent);

    private string CardName(string? cardId) => cardId != null && _db.TryGet(cardId, out var def) ? def.Name : cardId ?? "?";

    private string Creature(LogBoard board, int? entityId, string? fallbackCardId = null)
    {
        if (entityId is int id && board.Find(id) is { } c) return CardName(c.CardId);
        return CardName(fallbackCardId);
    }

    /// <summary>Описание события. Вызывать до применения события к board (died/damage читают ещё живое существо).</summary>
    public string? Describe(GameEvent e, LogBoard board)
    {
        switch (e.Type)
        {
            case EventTypes.CombatStarted:
                return _s.LogLine(e.Type, ("round", e.Round ?? 0), ("side", SideName(e.Side)));
            case EventTypes.AttackDeclared:
                return _s.LogLine(e.Type, ("attacker", Creature(board, e.SourceEntityId)), ("target", Creature(board, e.Target)));
            case EventTypes.Dodge:
                return _s.LogLine(e.Type, ("target", Creature(board, e.Target)));
            case EventTypes.Damage:
                return _s.LogLine(e.Type, ("target", Creature(board, e.Target)), ("amount", e.Amount ?? 0), ("health", e.Health ?? 0));
            case EventTypes.ShieldBroken:
                return _s.LogLine(e.Type, ("target", Creature(board, e.EntityId)));
            case EventTypes.Died:
                return _s.LogLine(e.Type, ("target", Creature(board, e.EntityId, e.CardId)));
            case EventTypes.CreatureSummoned:
                return _s.LogLine(e.Type, ("side", SideName(e.Side)), ("target", CardName(e.CardId)));
            case EventTypes.Reborn:
                return _s.LogLine(e.Type, ("target", CardName(e.Creature?.CardId)));
            case EventTypes.BattleSpellRevealed:
            case EventTypes.SecretRevealed:
                return _s.LogLine(e.Type, ("side", SideName(e.Side)), ("card", CardName(e.CardId)));
            case EventTypes.StatsChanged:
                return _s.LogLine(e.Type, ("target", Creature(board, e.EntityId)), ("attack", e.Attack ?? 0), ("health", e.Health ?? 0));
            case EventTypes.KeywordChanged:
                return _s.LogLine(e.Type, ("target", Creature(board, e.EntityId)), ("keyword", _s.KeywordName(e.Keyword ?? "")));
            case EventTypes.SummonFailed:
                return _s.LogLine(e.Type, ("side", SideName(e.Side)), ("card", CardName(e.CardId)));
            case EventTypes.HeroDamaged:
                return _s.LogLine(e.Type, ("side", SideName(e.Side)), ("amount", e.Amount ?? 0), ("health", e.Health ?? 0));
            case EventTypes.CombatEnded:
                return e.Result == Outcome.Draw
                    ? _s.LogLine("combat_ended_draw")
                    : _s.LogLine("combat_ended_win", ("side", SideName(e.Result == Outcome.A ? Side.A : Side.B)));
            case EventTypes.Warning:
                return _s.LogLine(e.Type, ("text", e.Text ?? ""));
            default:
                return null;
        }
    }
}
