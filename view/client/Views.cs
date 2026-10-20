using Game.Core;

namespace Game.Client;

/// <summary>Карта в руке, в зоне или в сбросе.</summary>
public sealed record CardView(int InstanceId, CardDef Def);

/// <summary>Существо на столе в подготовке: посчитанные характеристики, чары и способности.</summary>
public sealed record BoardCreature(CreatureView View, bool IsToken, IReadOnlyList<CardDef> Enchantments,
    IReadOnlyList<AbilityRef> Activated, IReadOnlyList<string> UsedAbilityKeys);

/// <summary>
/// Сторона так, как её видит игрок (5.7): своё — целиком; у противника рука и боевая зона — только числом.
/// </summary>
public sealed class SideSnapshot
{
    public Side Side { get; init; }
    public bool IsOwner { get; init; }
    public int HeroHp { get; init; }
    public int Mana { get; init; }
    public int ManaMax { get; init; }
    public int ManaBonus { get; init; }
    public int DrawCount { get; init; }
    public int HandCount { get; init; }
    public IReadOnlyList<CardView> Hand { get; init; } = Array.Empty<CardView>();
    public IReadOnlyList<BoardCreature> Board { get; init; } = Array.Empty<BoardCreature>();
    public IReadOnlyList<CardView?> Field { get; init; } = Array.Empty<CardView?>();
    public int BattleZoneCount { get; init; }
    public IReadOnlyList<CardView> BattleZone { get; init; } = Array.Empty<CardView>();
    public IReadOnlyList<CardView> Discard { get; init; } = Array.Empty<CardView>();
    public int ExileCount { get; init; }
    public bool IsReady { get; init; }

    public static SideSnapshot Of(CardDatabase db, PlayerState p, bool owner)
    {
        CardView Card(int id) => new(id, db.Get(p.GetInstance(id).CardId));
        return new SideSnapshot
        {
            Side = p.Side,
            IsOwner = owner,
            HeroHp = p.HeroHp,
            Mana = p.Mana,
            ManaMax = p.ManaMax,
            ManaBonus = p.ManaBonus,
            DrawCount = p.DrawPile.Count,
            HandCount = p.Hand.Count,
            Hand = owner ? p.Hand.Select(Card).ToList() : Array.Empty<CardView>(),
            Board = p.Alive.Select(c => new BoardCreature(
                CreatureView.Of(db, p, c),
                c.InstanceId == null,
                c.Enchantments.Select(e => db.Get(p.GetInstance(e).CardId)).ToList(),
                Stats.Abilities(db, p, c).Where(a => a.Ability.Trigger == Registry.Triggers.Activated).ToList(),
                c.InstanceId is int id ? p.GetInstance(id).UsedAbilities.ToList() : c.UsedAbilities.ToList())).ToList(),
            Field = p.Field.Select(slot => slot is int id ? Card(id) : null).ToList(),
            BattleZoneCount = p.BattleZone.Count,
            BattleZone = owner ? p.BattleZone.Select(Card).ToList() : Array.Empty<CardView>(),
            Discard = p.Discard.Select(Card).ToList(),
            ExileCount = p.Exile.Count,
            IsReady = p.IsReady,
        };
    }
}
