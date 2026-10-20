using Game.Ai;
using Game.Core;

namespace Game.Client;

public enum SessionPhase { Planning, CombatResult, MatchOver }

/// <summary>Что нужно от игрока, чтобы разыграть карту (12.6).</summary>
public enum PlayKind
{
    /// <summary>Существо: позиция на столе (и цель «При розыгрыше», если есть допустимые).</summary>
    Creature,
    /// <summary>Заклинание без цели, боевое заклинание, секрет: просто разыграть.</summary>
    NoTarget,
    /// <summary>Заклинание или чары на существо: своё существо-цель.</summary>
    Target,
    /// <summary>Чары на поле: слот поля.</summary>
    FieldSlot,
}

/// <summary>
/// Матч игрока (сторона A) против бота (сторона B) в режиме opponent_first (5.7): в начале раунда бот готовится первым,
/// игрок видит его итоговый стол и поле чар. Все действия идут через legalActions/applyAction ядра и пишутся в запись матча.
/// </summary>
public sealed class MatchSession
{
    public const Side PlayerSide = Side.A;
    public const Side BotSide = Side.B;

    private readonly Bot _bot;
    private List<RecordedAction> _round = new();

    public GameData Data { get; }
    public GameRules Rules { get; }
    public MatchState State { get; }
    public MatchRecord Record { get; }
    public SessionPhase Phase { get; private set; }

    /// <summary>Последний бой — для проигрывания.</summary>
    public CombatPlayback? LastCombat { get; private set; }

    public ulong Seed => State.Seed;

    private MatchSession(GameData data, DeckList playerDeck, DeckList botDeck, ulong seed)
    {
        Data = data;
        Rules = new GameRules(data);
        _bot = new Bot(data);
        State = Match.CreateMatch(data, playerDeck, botDeck, seed, PlanningMode.OpponentFirst);
        Record = new MatchRecord
        {
            Seed = seed,
            Mode = PlanningMode.OpponentFirst,
            DeckA = Copy(playerDeck),
            DeckB = Copy(botDeck),
        };
    }

    public static MatchSession Start(GameData data, DeckList playerDeck, DeckList botDeck, ulong seed)
    {
        var s = new MatchSession(data, playerDeck, botDeck, seed);
        s.BeginRound();
        return s;
    }

    /// <summary>Перезапуск с тем же seed и колодами (12.6, отладка).</summary>
    public MatchSession Restart() => Start(Data, Record.DeckA, Record.DeckB, Seed);

    private static DeckList Copy(DeckList d) => new() { Id = d.Id, Name = d.Name, Cards = new List<string>(d.Cards) };

    private void BeginRound()
    {
        Rules.StartRound(State);
        _round = new List<RecordedAction>();
        Record.Rounds.Add(_round);
        foreach (var a in _bot.PlayPlanning(Rules, State, BotSide)) _round.Add(new RecordedAction(BotSide, a));
        Phase = SessionPhase.Planning;
    }

    // ---------- Действия игрока ----------

    public List<GameAction> Legal() =>
        Phase == SessionPhase.Planning ? Rules.LegalActions(State, PlayerSide) : new List<GameAction>();

    /// <summary>Действие игрока. Недопустимое — InvalidActionException, состояние не меняется.</summary>
    public List<GameEvent> Apply(GameAction action)
    {
        if (Phase != SessionPhase.Planning) throw new InvalidActionException($"Сейчас не подготовка ({Phase})");
        var events = Rules.ApplyAction(State, PlayerSide, action);
        _round.Add(new RecordedAction(PlayerSide, action));
        return events;
    }

    /// <summary>«В бой» (= Готов): бой считается целиком, интерфейс только проигрывает его лог.</summary>
    public CombatPlayback Fight()
    {
        if (Phase != SessionPhase.Planning) throw new InvalidActionException($"Сейчас не подготовка ({Phase})");
        if (!State.Player(PlayerSide).IsReady) Apply(new Ready());
        var events = Rules.RunCombat(State);
        LastCombat = new CombatPlayback(events);
        Phase = State.Result != null ? SessionPhase.MatchOver : SessionPhase.CombatResult;
        return LastCombat;
    }

    /// <summary>Конец раунда и начало следующего (если матч не окончен).</summary>
    public void NextRound()
    {
        if (Phase != SessionPhase.CombatResult) throw new InvalidOperationException($"Сейчас нельзя начать раунд ({Phase})");
        Rules.EndRound(State);
        if (State.Result != null) { Phase = SessionPhase.MatchOver; return; }
        BeginRound();
    }

    public string SaveReplay() => StateSerializer.SerializeRecord(Record);

    // ---------- Что видит игрок ----------

    public SideSnapshot View(Side side) => SideSnapshot.Of(Data.Cards, State.Player(side), owner: side == PlayerSide);

    // ---------- Подсказки для перетаскивания и выбора целей (из legalActions) ----------

    public PlayKind KindOf(int instanceId)
    {
        var def = Data.Cards.Get(State.Player(PlayerSide).GetInstance(instanceId).CardId);
        return def.Type switch
        {
            CardType.Creature => PlayKind.Creature,
            CardType.Enchantment when def.Attach == AttachKind.Field => PlayKind.FieldSlot,
            CardType.Enchantment => PlayKind.Target,
            CardType.Spell when def.PlayTarget != null => PlayKind.Target,
            _ => PlayKind.NoTarget,
        };
    }

    public List<PlayCard> PlaysFor(int instanceId) =>
        Legal().OfType<PlayCard>().Where(p => p.InstanceId == instanceId).ToList();

    public bool CanPlay(int instanceId) => PlaysFor(instanceId).Count > 0;

    /// <summary>Позиции на столе (существо) или слоты поля (чары на поле).</summary>
    public List<int> SlotsFor(int instanceId) =>
        PlaysFor(instanceId).Where(p => p.Slot != null).Select(p => p.Slot!.Value).Distinct().ToList();

    /// <summary>Допустимые цели карты (для существа — при выбранной позиции).</summary>
    public List<int> TargetsFor(int instanceId, int? slot = null) =>
        PlaysFor(instanceId).Where(p => p.Target != null && (slot == null || p.Slot == slot))
            .Select(p => p.Target!.Value).Distinct().ToList();

    public List<UseAbility> AbilitiesFor(int entityId) =>
        Legal().OfType<UseAbility>().Where(a => a.EntityId == entityId).ToList();

    public bool CanMove(int entityId, int toIndex) => Legal().Contains(new Move(entityId, toIndex));

    public bool CanRecall(int entityId) => Legal().Contains(new Recall(entityId));

    public bool CanDismiss(int entityId) => Legal().Contains(new Dismiss(entityId));
}
