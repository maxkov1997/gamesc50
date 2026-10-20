using Game.Content;
using Game.Core;
using Xunit;
using static Game.Tests.CombatTestKit;

namespace Game.Tests.M3;

/// <summary>Q10: фильтр условия секрета по атакующему (condition.attacker) рядом с фильтром по цели (condition.target).</summary>
public class SecretFilterTests
{
    private static readonly GameData Data = TestData.WithExtraCards("""
        { "id": "test_net", "name": "Сеть", "type": "secret", "cost": 1, "text": "",
          "condition": { "event": "enemy_attacks", "attacker": { "keyword": "flying" } },
          "effects": [{ "op": "destroy", "target": "event_attacker" }] }
        """);

    [Fact]
    public void Q10_secret_filters_by_attacker()
    {
        var s = new Sandbox(data: Data);
        var harpy = s.Put("harpy");
        s.Put("stone_wall", Side.B);
        s.PutBattle("test_net", Side.B);
        foreach (var (_, r) in FightAll(s))
        {
            Assert.Single(r.Of(EventTypes.SecretRevealed));
            Assert.Contains(r.Events, e => e.Type == EventTypes.Died && e.EntityId == harpy.EntityId);
        }

        var s2 = new Sandbox(data: Data);
        s2.Put("ferret");
        s2.Put("stone_wall", Side.B);
        s2.PutBattle("test_net", Side.B);
        foreach (var (_, r) in FightAll(s2)) Assert.Empty(r.Of(EventTypes.SecretRevealed));
    }

    [Fact]
    public void Q10_attacker_filter_is_validated()
    {
        var ex = Assert.Throws<GameDataException>(() => TestData.WithExtraCards("""
            { "id": "bad_net", "name": "Сеть", "type": "secret", "cost": 1, "text": "",
              "condition": { "event": "enemy_attacks", "attacker": { "keyword": "stealth" } },
              "effects": [{ "op": "destroy", "target": "event_attacker" }] },
            { "id": "bad_net2", "name": "Сеть", "type": "secret", "cost": 1, "text": "",
              "condition": { "event": "enemy_attacks", "attacker": { "colour": "red" } },
              "effects": [{ "op": "destroy", "target": "event_attacker" }] }
            """));
        Assert.Contains(ex.Errors, e => e.Code == ErrorCodes.UnknownKeyword && e.Path.Contains("condition.attacker.keyword"));
        Assert.Contains(ex.Errors, e => e.Code == ErrorCodes.UnknownField && e.Path.Contains("condition.attacker.colour"));
    }
}
