using Game.Content;
using Game.Core;
using Xunit;

namespace Game.Tests.M1;

/// <summary>Действия подготовки 5.3 сверх тест-кейсов раздела 13.</summary>
public class ActionTests
{
    [Fact]
    public void M1_field_enchantment_replaces_slot()
    {
        var s = new Sandbox().Planning();
        int forge = s.PutField("forge", 1);
        int sanctuary = s.AddToHand("sanctuary");
        Assert.Equal(s.A.Field.Count, s.Legal().OfType<PlayCard>().Count(pc => pc.InstanceId == sanctuary));

        s.Apply(new PlayCard(sanctuary, Slot: 1));

        Assert.Equal(sanctuary, s.A.Field[1]);
        Assert.Equal(new[] { forge }, s.A.Discard);
        Assert.Equal(8, s.A.Mana);
    }

    [Fact]
    public void M1_battle_zone_limits()
    {
        var s = new Sandbox().Planning();
        int snare1 = s.AddToHand("snare");
        int snare2 = s.AddToHand("snare");
        s.Apply(new PlayCard(snare1));
        // Карты с тем же id в боевой зоне быть не может.
        Assert.DoesNotContain(s.Legal(), a => a is PlayCard pc && pc.InstanceId == snare2);

        s.Apply(new PlayCard(s.AddToHand("vengeance")));
        s.Apply(new PlayCard(s.AddToHand("riposte")));
        Assert.Equal(3, s.A.BattleZone.Count);
        // Свободных слотов нет.
        int volley = s.AddToHand("fire_volley");
        Assert.DoesNotContain(s.Legal(), a => a is PlayCard pc && pc.InstanceId == volley);
    }

    [Fact]
    [Covers("insight")]
    public void M1_exhaust_goes_to_exile()
    {
        var s = new Sandbox().Planning();
        s.AddToDrawPile("ferret");
        s.AddToDrawPile("wolf");
        int insight = s.AddToHand("insight");

        s.Apply(new PlayCard(insight));

        Assert.Equal(new[] { insight }, s.A.Exile);
        Assert.Empty(s.A.Discard);
        Assert.Equal(2, s.A.Hand.Count);
    }

    [Fact]
    [Covers("call_spirits")]
    public void M1_call_spirits_summons_in_center()
    {
        var s = new Sandbox().Planning();
        s.Put("ferret");
        s.Put("wolf");
        s.Put("harpy");
        s.Apply(new PlayCard(s.AddToHand("call_spirits")));
        // 3 живых → позиция ⌊3/2⌋ = 1, две Искорки подряд.
        Assert.Equal(new[] { "ferret", "tok_spark", "tok_spark", "wolf", "harpy" }, s.BoardIds());
    }

    [Fact]
    public void M1_summon_fails_on_full_board()
    {
        var s = new Sandbox().Planning();
        for (int i = 0; i < 6; i++) s.Put("ferret");
        s.Apply(new PlayCard(s.AddToHand("call_spirits")));
        Assert.Equal(7, s.A.AliveCount);
        Assert.Single(s.State.Log, e => e.Type == EventTypes.SummonFailed);
    }

    [Fact]
    public void M1_invalid_action_does_not_change_state()
    {
        var s = new Sandbox().Planning(mana: 1);
        int boar = s.AddToHand("iron_boar");
        string before = StateSerializer.Serialize(s.State);
        Assert.Throws<InvalidActionException>(() => s.Apply(new PlayCard(boar, Slot: 0)));
        Assert.Throws<InvalidActionException>(() => s.Apply(new Move(999, 0)));
        Assert.Equal(before, StateSerializer.Serialize(s.State));
    }

    [Fact]
    public void M1_ready_ends_planning_for_side()
    {
        var s = new Sandbox().Planning();
        s.Apply(new Ready());
        Assert.Empty(s.Legal());
        Assert.NotEmpty(s.Legal(Side.B));
    }

    [Fact]
    public void M1_legal_actions_order()
    {
        var s = new Sandbox().Planning();
        var a = s.Put("ferret");
        var b = s.Put("tinkerer");
        var c = s.Put("scavenger");
        int app = s.AddToHand("apprentice");

        var legal = s.Legal();
        // Первое — Подмастерье в позицию 0 с первой целью по возрастанию entityId.
        Assert.Equal(new PlayCard(app, Slot: 0, Target: a.EntityId), legal[0]);
        Assert.Equal(new PlayCard(app, Slot: 0, Target: b.EntityId), legal[1]);
        Assert.Equal(new UseAbility(b.EntityId, 0, Target: c.EntityId), legal.First(x => x is not PlayCard));
        Assert.Equal(new Ready(), legal[^1]);
        Assert.IsType<Dismiss>(legal[^2]);
    }

    [Fact]
    public void M1_planning_trigger_limit_drops_rest_with_warning()
    {
        // Синтетика: «Гидра» при смерти призывает Гидру, «Жнец» уничтожает всех Гидр, когда умирает союзник.
        var data = TestData.WithExtraCards("""
            { "id": "test_hydra", "name": "Гидра", "type": "creature", "cost": 1, "attack": 1, "health": 1, "tags": ["spirit"], "text": "",
              "abilities": [{ "trigger": "on_death", "effects": [{ "op": "summon", "card": "test_hydra", "at": "death_position" }] }] },
            { "id": "test_reaper", "name": "Жнец", "type": "creature", "cost": 1, "attack": 1, "health": 1, "text": "",
              "abilities": [{ "trigger": "friendly_died",
                "effects": [{ "op": "destroy", "target": { "pick": "all", "side": "friendly", "tag": "spirit" } }] }] }
            """);
        var s = new Sandbox(data: data).Planning();
        var hydra = s.Put("test_hydra");
        s.Put("test_reaper");
        int devourer = s.AddToHand("devourer");

        s.Apply(new PlayCard(devourer, Slot: 2, Target: hydra.EntityId));

        Assert.Single(s.State.Log, e => e.Type == EventTypes.Warning);
        Assert.Contains("devourer", s.BoardIds());
    }

    [Fact]
    public void M1_validator_rejects_random_aura_and_double_targeted_on_play()
    {
        var ex = Assert.Throws<GameDataException>(() => TestData.WithExtraCards("""
            { "id": "bad_aura", "name": "А", "type": "creature", "cost": 1, "attack": 1, "health": 1, "text": "",
              "abilities": [{ "trigger": "aura", "aura": { "target": { "pick": "random", "side": "friendly" }, "attack": 1 } }] },
            { "id": "bad_play", "name": "Б", "type": "creature", "cost": 1, "attack": 1, "health": 1, "text": "",
              "abilities": [
                { "trigger": "on_play", "target": { "side": "friendly" }, "effects": [{ "op": "buff", "target": "chosen", "attack": 1 }] },
                { "trigger": "on_play", "target": { "side": "friendly" }, "effects": [{ "op": "buff", "target": "chosen", "health": 1 }] }] }
            """));
        Assert.Contains(ex.Errors, e => e.Code == ErrorCodes.ContentRule && e.Path.Contains("(bad_aura).abilities[0].aura.target"));
        Assert.Contains(ex.Errors, e => e.Code == ErrorCodes.ContentRule && e.Path.Contains("(bad_play).abilities"));
    }
}
