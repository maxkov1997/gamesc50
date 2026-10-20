using Game.Core;
using Xunit;
using static Game.Tests.CombatTestKit;

namespace Game.Tests.M3;

/// <summary>
/// Q5: способность чар на поле с триггером «этого существа» выдаётся каждому подходящему существу своей стороны (watch).
/// </summary>
public class FieldGrantedAbilityTests
{
    private static readonly GameData Data = TestData.WithExtraCards("""
        { "id": "test_totem", "name": "Тотем", "type": "enchantment", "attach": "field", "cost": 1, "text": "",
          "abilities": [
            { "trigger": "on_death", "watch": { "tag": "beast" },
              "effects": [{ "op": "summon", "card": "tok_spark", "at": "death_position" }] },
            { "trigger": "activated", "cost": 1, "watch": { "tag": "mech" },
              "target": { "side": "friendly", "not_self": true },
              "effects": [{ "op": "buff", "target": "chosen", "attack": 1, "health": 1 }] },
            { "trigger": "avenge", "count": 1, "watch": { "keyword": "taunt" },
              "effects": [{ "op": "buff", "target": "self", "attack": 1 }] }
          ] }
        """);

    [Fact]
    public void Q5_on_death_is_granted_to_matching_creatures()
    {
        var s = new Sandbox(data: Data);
        s.PutField("test_totem", 0);
        var ferret = s.Put("ferret");      // Зверь — получает «Посмертно»
        var guard = s.Put("gate_guard");   // не Зверь
        s.Put("stone_wall", Side.B);

        var engine = s.Engine();
        engine.Destroy(ferret.EntityId, guard.EntityId);

        var spark = Assert.Single(engine.Events, e => e.Type == EventTypes.CreatureSummoned);
        Assert.Equal("tok_spark", spark.CardId);
        Assert.Equal(ferret.EntityId, spark.SourceEntityId);
        Assert.Equal(new[] { "tok_spark" }, engine.Side(Side.A).Board.Select(c => c.CardId));
    }

    [Fact]
    public void Q5_avenge_is_granted_per_creature()
    {
        var s = new Sandbox(data: Data);
        s.PutField("test_totem", 0);
        var skeleton = s.Put("tok_skeleton");
        var guard = s.Put("gate_guard");   // Провокация — получает Возмездие (1)
        var wall = s.Put("stone_wall");    // Провокация — тоже, со своим счётчиком
        s.Put("stone_wall", Side.B);

        var engine = s.Engine();
        engine.Destroy(skeleton.EntityId);
        var p = engine.Side(Side.A);
        Assert.Equal(2, Stats.Attack(s.Data.Cards, p, p.Board.Single(c => c.EntityId == guard.EntityId)));
        Assert.Equal(1, Stats.Attack(s.Data.Cards, p, p.Board.Single(c => c.EntityId == wall.EntityId)));
    }

    [Fact]
    public void Q5_activated_ability_from_field_is_once_per_round_per_creature()
    {
        var s = new Sandbox(data: Data).Planning();
        s.PutField("test_totem", 0);
        var tinkerer = s.Put("tinkerer");
        var scavenger = s.Put("scavenger");
        var ferret = s.Put("ferret");

        // У Механика своя способность (индекс 0) и выданная Тотемом (индекс 1); у Хорька способности нет.
        var granted = s.Legal().OfType<UseAbility>().Where(a => a.EntityId == tinkerer.EntityId && a.AbilityIndex == 1).ToList();
        Assert.NotEmpty(granted);
        Assert.DoesNotContain(s.Legal(), a => a is UseAbility ua && ua.EntityId == ferret.EntityId);

        s.Apply(new UseAbility(tinkerer.EntityId, 1, Target: ferret.EntityId));
        Assert.Equal((3, 2), s.AH(ferret));
        Assert.DoesNotContain(s.Legal(), a => a is UseAbility ua && ua.EntityId == tinkerer.EntityId && ua.AbilityIndex == 1);
        // Своя способность Механика и выданная Сборщику — отдельные флаги.
        Assert.Contains(s.Legal(), a => a is UseAbility ua && ua.EntityId == tinkerer.EntityId && ua.AbilityIndex == 0);
        Assert.Contains(s.Legal(), a => a is UseAbility ua && ua.EntityId == scavenger.EntityId);
    }

    [Fact]
    public void Q5_watch_is_validated()
    {
        var ex = Assert.Throws<GameDataException>(() => TestData.WithExtraCards("""
            { "id": "bad_field", "name": "Без watch", "type": "enchantment", "attach": "field", "cost": 1, "text": "",
              "abilities": [{ "trigger": "on_attack", "effects": [{ "op": "draw", "count": 1 }] }] },
            { "id": "bad_creature", "name": "watch у существа", "type": "creature", "cost": 1, "attack": 1, "health": 1, "text": "",
              "abilities": [{ "trigger": "on_death", "watch": { "tag": "beast" }, "effects": [{ "op": "draw", "count": 1 }] }] },
            { "id": "bad_round", "name": "watch у триггера поля", "type": "enchantment", "attach": "field", "cost": 1, "text": "",
              "abilities": [{ "trigger": "round_end", "watch": {}, "effects": [{ "op": "draw", "count": 1 }] }] }
            """));
        Assert.Contains(ex.Errors, e => e.Code == ErrorCodes.MissingField && e.Path.Contains("(bad_field).abilities[0].watch"));
        Assert.Contains(ex.Errors, e => e.Code == ErrorCodes.FieldNotAllowed && e.Path.Contains("(bad_creature).abilities[0].watch"));
        Assert.Contains(ex.Errors, e => e.Code == ErrorCodes.FieldNotAllowed && e.Path.Contains("(bad_round).abilities[0].watch"));
    }
}
