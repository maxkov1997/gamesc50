using Game.Core;

namespace Game.Tests;

/// <summary>
/// Песочница для тестов: матч с пустыми зонами, в который состояние выставляется напрямую (раздел 13).
/// </summary>
public sealed class Sandbox
{
    public GameData Data { get; }
    public GameRules Rules { get; }
    public MatchState State { get; }

    public Sandbox(ulong seed = 1, GameData? data = null)
    {
        Data = data ?? TestData.Data;
        Rules = new GameRules(Data);
        State = Match.CreateMatch(Data, Data.GetDeck("pack"), Data.GetDeck("bones"), seed, PlanningMode.OpponentFirst);
        foreach (var p in State.Players)
        {
            p.Hand.Clear();
            p.DrawPile.Clear();
            p.Instances.Clear();
        }
        State.Log.Clear();
    }

    public PlayerState P(Side side) => State.Player(side);

    public PlayerState A => P(Side.A);

    /// <summary>Перейти в подготовку раунда round с manа маны у обеих сторон, без триггеров начала раунда.</summary>
    public Sandbox Planning(int round = 1, int mana = 10)
    {
        State.Round = round;
        State.Stage = RoundStage.Planning;
        foreach (var p in State.Players)
        {
            p.ManaMax = Math.Min(round, Data.Config.ManaMax);
            p.Mana = mana;
        }
        return this;
    }

    public int NewInstance(Side side, string cardId)
    {
        var inst = new CardInstance { InstanceId = State.NextInstanceId++, CardId = cardId };
        P(side).Instances.Add(inst);
        return inst.InstanceId;
    }

    public int AddToHand(string cardId, Side side = Side.A)
    {
        int id = NewInstance(side, cardId);
        P(side).Hand.Add(id);
        return id;
    }

    public int AddToDrawPile(string cardId, Side side = Side.A)
    {
        int id = NewInstance(side, cardId);
        P(side).DrawPile.Add(id);
        return id;
    }

    public int AddToDiscard(string cardId, Side side = Side.A)
    {
        int id = NewInstance(side, cardId);
        P(side).Discard.Add(id);
        return id;
    }

    /// <summary>Поставить существо на стол справа. Токен — без экземпляра.</summary>
    public Creature Put(string cardId, Side side = Side.A)
    {
        var p = P(side);
        int? instanceId = Data.Cards.Get(cardId).Token ? null : NewInstance(side, cardId);
        var c = Entities.NewCreature(p, cardId, instanceId);
        p.Board.Add(c);
        return c;
    }

    /// <summary>Положить чары на поле в слот.</summary>
    public int PutField(string cardId, int slot, Side side = Side.A)
    {
        int id = NewInstance(side, cardId);
        P(side).Field[slot] = id;
        return id;
    }

    public List<GameEvent> Apply(GameAction action, Side side = Side.A) => Rules.ApplyAction(State, side, action);

    public List<GameAction> Legal(Side side = Side.A) => Rules.LegalActions(State, side);

    public int Attack(Creature c, Side side = Side.A) => Stats.Attack(Data.Cards, P(side), c);

    public int Health(Creature c) => Stats.Health(Data.Cards, c);

    public (int, int) AH(Creature c, Side side = Side.A) => (Attack(c, side), Health(c));

    public string[] BoardIds(Side side = Side.A) => P(side).Board.Select(c => c.CardId).ToArray();

    public string CardOf(int instanceId, Side side = Side.A) => P(side).GetInstance(instanceId).CardId;

    public Creature Find(string cardId, Side side = Side.A) => P(side).Board.Single(c => c.CardId == cardId);
}
