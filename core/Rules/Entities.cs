namespace Game.Core;

public static class Entities
{
    /// <summary>
    /// Новая сущность стороны. entityId = seq·2 + side: не переиспользуются (4.1) и не зависят
    /// от действий противника (5.6), поэтому подготовку сторон можно вести в любом порядке.
    /// </summary>
    public static Creature NewCreature(PlayerState p, string cardId, int? instanceId)
    {
        int id = p.NextEntitySeq++ * 2 + (int)p.Side;
        return new Creature { EntityId = id, InstanceId = instanceId, CardId = cardId };
    }
}

/// <summary>Недопустимое действие: состояние не меняется (12.2).</summary>
public sealed class InvalidActionException : Exception
{
    public InvalidActionException(string message) : base(message) { }
}
