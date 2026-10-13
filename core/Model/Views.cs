namespace Game.Core;

/// <summary>Самодостаточный снимок существа для лога (12.3): значения уже посчитаны.</summary>
public sealed class CreatureView
{
    public int EntityId { get; set; }
    public string CardId { get; set; } = "";
    public int Attack { get; set; }
    public int Health { get; set; }
    public int MaxHealth { get; set; }
    public List<string> Keywords { get; set; } = new();

    public static CreatureView Of(CardDatabase db, PlayerState p, Creature c) => new()
    {
        EntityId = c.EntityId,
        CardId = c.CardId,
        Attack = Stats.Attack(db, p, c),
        Health = Stats.Health(db, c),
        MaxHealth = Stats.MaxHealth(db, c),
        Keywords = Stats.Keywords(db, p, c),
    };
}

/// <summary>Снимок стороны для combat_started. Что скрыть от противника (5.7), решает интерфейс.</summary>
public sealed class SideView
{
    public Side Side { get; set; }
    public int HeroHp { get; set; }
    public List<CreatureView> Board { get; set; } = new();

    /// <summary>cardId чар по слотам поля; null — пусто.</summary>
    public List<string?> Field { get; set; } = new();

    /// <summary>cardId карт боевой зоны в порядке розыгрыша.</summary>
    public List<string> BattleZone { get; set; } = new();

    public static SideView Of(CardDatabase db, PlayerState p) => new()
    {
        Side = p.Side,
        HeroHp = p.HeroHp,
        Board = p.Alive.Select(c => CreatureView.Of(db, p, c)).ToList(),
        Field = p.Field.Select(slot => slot is int id ? p.GetInstance(id).CardId : null).ToList(),
        BattleZone = p.BattleZone.Select(id => p.GetInstance(id).CardId).ToList(),
    };
}
