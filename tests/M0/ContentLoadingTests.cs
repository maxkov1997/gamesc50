using Game.Core;
using Xunit;

namespace Game.Tests.M0;

/// <summary>M0: стартовый набор 11.5 и колоды 11.7 загружаются.</summary>
public class ContentLoadingTests
{
    // Состав стартового набора из 11.5: id, тип, мана, атака, здоровье.
    private static readonly (string Id, CardType Type, int Cost, int Attack, int Health)[] Creatures =
    {
        ("gate_guard", CardType.Creature, 1, 1, 3),
        ("ferret", CardType.Creature, 1, 2, 1),
        ("candle_spirit", CardType.Creature, 1, 1, 1),
        ("apprentice", CardType.Creature, 1, 1, 2),
        ("shieldbearer", CardType.Creature, 2, 2, 2),
        ("venom_toad", CardType.Creature, 2, 1, 2),
        ("bone_servant", CardType.Creature, 2, 2, 1),
        ("watchdog", CardType.Creature, 2, 2, 3),
        ("tinkerer", CardType.Creature, 2, 2, 2),
        ("stone_wall", CardType.Creature, 2, 0, 5),
        ("wolf", CardType.Creature, 2, 2, 2),
        ("mourner", CardType.Creature, 2, 1, 3),
        ("harpy", CardType.Creature, 3, 3, 2),
        ("axeman", CardType.Creature, 3, 2, 4),
        ("whirlblade", CardType.Creature, 3, 2, 3),
        ("gravekeeper", CardType.Creature, 3, 2, 3),
        ("scavenger", CardType.Creature, 3, 3, 3),
        ("wind_up_soldier", CardType.Creature, 3, 2, 2),
        ("battle_mage", CardType.Creature, 4, 3, 4),
        ("iron_boar", CardType.Creature, 4, 3, 6),
        ("pack_leader", CardType.Creature, 4, 3, 4),
        ("berserker", CardType.Creature, 4, 3, 5),
        ("dawn_knight", CardType.Creature, 5, 4, 5),
        ("scrap_golem", CardType.Creature, 5, 5, 5),
        ("devourer", CardType.Creature, 6, 4, 4),
    };

    private static readonly (string Id, CardType Type, int Cost)[] NonCreatures =
    {
        ("blessing_of_might", CardType.Spell, 1),
        ("tempering", CardType.Spell, 2),
        ("holy_veil", CardType.Spell, 2),
        ("call_spirits", CardType.Spell, 2),
        ("rally", CardType.Spell, 3),
        ("insight", CardType.Spell, 1),
        ("hoard", CardType.Spell, 1),
        ("fire_volley", CardType.BattleSpell, 2),
        ("war_hymn", CardType.BattleSpell, 2),
        ("mirror_shield", CardType.Secret, 2),
        ("vengeance", CardType.Secret, 2),
        ("ambush", CardType.Secret, 3),
        ("snare", CardType.Secret, 3),
        ("riposte", CardType.Secret, 1),
        ("seal_of_rebirth", CardType.Enchantment, 2),
        ("forge", CardType.Enchantment, 3),
        ("sanctuary", CardType.Enchantment, 2),
    };

    private static readonly (string Id, int Attack, int Health)[] Tokens =
    {
        ("tok_spark", 1, 1),
        ("tok_skeleton", 1, 1),
        ("tok_gear", 2, 2),
        ("tok_ambusher", 3, 3),
    };

    [Fact]
    public void M0_all_42_cards_and_4_tokens_load()
    {
        var cards = TestData.Data.Cards.Cards;
        Assert.Equal(42, cards.Count(c => !c.Token));
        Assert.Equal(4, cards.Count(c => c.Token));
        Assert.Equal(Creatures.Length + NonCreatures.Length + Tokens.Length, cards.Count);

        foreach (var (id, type, cost, attack, health) in Creatures)
        {
            var c = TestData.Data.Cards.Get(id);
            Assert.Equal((type, cost, attack, health, false), (c.Type, c.Cost, c.Attack, c.Health, c.Token));
            Assert.False(string.IsNullOrEmpty(c.Name));
        }
        foreach (var (id, type, cost) in NonCreatures)
        {
            var c = TestData.Data.Cards.Get(id);
            Assert.Equal((type, cost), (c.Type, c.Cost));
            Assert.False(string.IsNullOrEmpty(c.Text));
        }
        foreach (var (id, attack, health) in Tokens)
        {
            var c = TestData.Data.Cards.Get(id);
            Assert.Equal((CardType.Creature, 0, attack, health, true), (c.Type, c.Cost, c.Attack, c.Health, c.Token));
        }
    }

    [Fact]
    public void M0_card_fields_match_starter_set()
    {
        var db = TestData.Data.Cards;
        Assert.Equal(new[] { "shield", "taunt" }, db.Get("dawn_knight").Keywords);
        Assert.Equal(new[] { "exhaust" }, db.Get("insight").Keywords);
        Assert.Equal(AttachKind.Creature, db.Get("seal_of_rebirth").Attach);
        Assert.Equal(AttachKind.Field, db.Get("forge").Attach);
        Assert.Equal(AttachKind.Field, db.Get("sanctuary").Attach);
        Assert.Equal("hero_would_take_damage", db.Get("riposte").Condition!.Event);
        Assert.Equal("shield", db.Get("mirror_shield").Condition!.Target!.NoKeyword);
        Assert.Equal(2, db.Get("watchdog").Abilities[0].Count);
        Assert.Equal(1, db.Get("tinkerer").Abilities[0].Cost);
        Assert.Equal(new[] { "mech" }, db.Get("tok_gear").Tags);
        Assert.Equal(new[] { "shield" }, db.Get("tok_gear").Keywords);
    }

    [Fact]
    public void M0_three_test_decks_load()
    {
        var decks = TestData.Data.Decks;
        Assert.Equal(new[] { "bones", "mechs", "pack" }, decks.Select(d => d.Id));
        foreach (var d in decks) Assert.Equal(15, d.Cards.Count);

        Assert.Equal(2, TestData.Data.GetDeck("bones").Cards.Count(c => c == "candle_spirit"));
        Assert.Equal(2, TestData.Data.GetDeck("bones").Cards.Count(c => c == "bone_servant"));
        Assert.Equal(2, TestData.Data.GetDeck("mechs").Cards.Count(c => c == "tinkerer"));
        Assert.Equal(15, TestData.Data.GetDeck("pack").Cards.Distinct().Count());
    }

    [Fact]
    public void M0_every_starter_card_is_in_some_deck()
    {
        var used = TestData.Data.Decks.SelectMany(d => d.Cards).ToHashSet();
        var missing = TestData.Data.Cards.Cards.Where(c => !c.Token && !used.Contains(c.Id)).Select(c => c.Id).ToList();
        Assert.Empty(missing);
    }

    /// <summary>11.5: набор покрывает каждое ключевое слово, триггер, условие секрета и операцию.</summary>
    [Fact]
    public void M0_starter_set_covers_every_registry_entry()
    {
        var cards = TestData.Data.Cards.Cards;
        var abilities = cards.SelectMany(c => c.Abilities.Concat(c.Grants?.Abilities ?? Array.Empty<AbilityDef>())).ToList();
        var effects = cards.SelectMany(c => c.Effects).Concat(abilities.SelectMany(a => a.Effects)).ToList();
        var keywords = cards.SelectMany(c => c.Keywords).Concat(effects.Where(e => e.Keyword != null).Select(e => e.Keyword!)).ToHashSet();

        foreach (var k in Registry.KeywordList) Assert.Contains(k.Id, keywords);
        foreach (var t in Registry.TriggerList) Assert.Contains(abilities, a => a.Trigger == t.Id);
        foreach (var c in Registry.ConditionList) Assert.Contains(cards, card => card.Condition?.Event == c.Id);
        foreach (var o in Registry.OpList) Assert.Contains(effects, e => e.Op == o.Id);
    }

    [Fact]
    public void M0_config_matches_section_3()
    {
        var c = TestData.Data.Config;
        Assert.Equal(
            (40, 7, 3, 3, 10, 4, 2, 10, 10, 30, 2, 1, 15, 25, 300, 2000, 30),
            (c.HeroHp, c.BoardSize, c.FieldSlots, c.BattleSlots, c.HandMax, c.StartHand, c.DrawPerRound, c.ManaMax,
             c.DeckMin, c.DeckMax, c.MaxCopies, c.DamageBase, c.DamageCap, c.DodgeChance, c.MaxAttacks, c.MaxTriggers, c.MaxRounds));
    }
}
