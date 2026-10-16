using Game.Core;
using Xunit;
using static Game.Tests.CombatTestKit;

namespace Game.Tests.M3;

/// <summary>Чары и ауры в бою (7.4) на синтетических картах: в стартовом наборе таких нет.</summary>
public class EnchantmentAuraTests
{
    private static readonly GameData Data = TestData.WithExtraCards("""
        { "id": "test_spear", "name": "Ядовитое копьё", "type": "enchantment", "attach": "creature", "cost": 1, "text": "",
          "play_target": { "side": "friendly" }, "grants": { "attack": 2, "keywords": ["poison"] } },
        { "id": "test_banner", "name": "Знамя", "type": "enchantment", "attach": "field", "cost": 1, "text": "",
          "abilities": [{ "trigger": "aura", "aura": { "target": { "pick": "all", "side": "friendly", "tag": "beast" }, "attack": 1, "keywords": ["taunt"] } }] }
        """);

    [Fact]
    public void M3_enchantment_grants_attack_and_keyword_in_combat()
    {
        var s = new Sandbox(data: Data).Planning();
        var ferret = s.Put("ferret");
        s.Apply(new PlayCard(s.AddToHand("test_spear"), Target: ferret.EntityId));
        Assert.Equal(4, s.Attack(ferret));
        Assert.Contains("poison", Stats.Keywords(s.Data.Cards, s.A, ferret));

        // Яд от чар: удар по Броневепрю 3/6 уничтожает его; гибнут оба.
        s.Put("iron_boar", Side.B);
        foreach (var (_, r) in FightAll(s))
        {
            Assert.Equal(4, r.Of(EventTypes.Damage).Single(e => e.SourceEntityId == ferret.EntityId).Amount);
            AssertWinner(r, Outcome.Draw, 0);
        }

        // Чары живут, пока существо на столе: после отзыва — в сброс.
        s.Apply(new Recall(ferret.EntityId));
        Assert.Single(s.A.Discard);
    }

    [Fact]
    public void M3_field_aura_applies_in_combat_and_follows_tags()
    {
        var s = new Sandbox(data: Data);
        s.PutField("test_banner", 0);
        var ferret = s.Put("ferret");
        var guard = s.Put("gate_guard");
        // Аура действует только на Зверей: Хорёк 2 → 3, Страж ворот (не Зверь) остаётся 1.
        Assert.Equal((3, 1), (s.Attack(ferret), s.Attack(guard)));
        Assert.Contains("taunt", Stats.Keywords(s.Data.Cards, s.A, ferret));
        Assert.Equal(0, ferret.BonusAttack);

        // Снимок боя в логе видит атаку и Провокацию от ауры чар на поле.
        s.Put("tok_skeleton", Side.B);
        foreach (var (_, r) in FightAll(s))
        {
            var start = r.Of(EventTypes.CombatStarted).Single();
            var view = start.Sides![(int)Side.A].Board.Single(c => c.EntityId == ferret.EntityId);
            Assert.Equal(3, view.Attack);
            Assert.Contains("taunt", view.Keywords);
        }
    }
}
