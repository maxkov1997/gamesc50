using Game.Core;
using Xunit;

namespace Game.Tests.M3;

/// <summary>
/// Q6: слои MTG для аур (7.4). Слой ключевых слов — по зависимостям, при равенстве и в петле — по timestamp;
/// затем слой атаки по итоговым ключевым словам.
/// </summary>
public class LayerTests
{
    private static readonly GameData Data = TestData.WithExtraCards("""
        { "id": "test_venom_banner", "name": "Знамя яда", "type": "creature", "cost": 1, "attack": 1, "health": 1, "text": "",
          "abilities": [{ "trigger": "aura", "aura": { "target": { "pick": "all", "side": "friendly", "keyword": "taunt" }, "keywords": ["poison"] } }] },
        { "id": "test_guard_banner", "name": "Знамя стражи", "type": "creature", "cost": 1, "attack": 1, "health": 1, "text": "",
          "abilities": [{ "trigger": "aura", "aura": { "target": { "pick": "all", "side": "friendly", "tag": "beast" }, "keywords": ["taunt"] } }] },
        { "id": "test_wall_banner", "name": "Знамя стены", "type": "creature", "cost": 1, "attack": 1, "health": 1, "text": "",
          "abilities": [{ "trigger": "aura", "aura": { "target": { "pick": "all", "side": "friendly", "keyword": "poison" }, "keywords": ["taunt"] } }] },
        { "id": "test_war_banner", "name": "Знамя войны", "type": "creature", "cost": 1, "attack": 1, "health": 1, "text": "",
          "abilities": [{ "trigger": "aura", "aura": { "target": { "pick": "all", "side": "friendly", "keyword": "taunt", "not_self": true }, "attack": 2 } }] }
        """);

    private static List<string> Kw(Sandbox s, Creature c) => Stats.Keywords(s.Data.Cards, s.A, c);

    [Fact]
    public void Q6_dependency_beats_timestamp()
    {
        // «С Провокацией — Яд» появилось раньше, «Звери — Провокация» позже. Первое зависит от второго,
        // поэтому применяется после него: Хорёк получает и Провокацию, и Яд.
        var s = new Sandbox(data: Data);
        s.Put("test_venom_banner");
        s.Put("test_guard_banner");
        var ferret = s.Put("ferret");
        Assert.Contains("taunt", Kw(s, ferret));
        Assert.Contains("poison", Kw(s, ferret));
    }

    [Fact]
    public void Q6_dependency_loop_resolves_by_timestamp()
    {
        // Петля: «С Провокацией — Яд» и «С Ядом — Провокация». Применяются по timestamp, без зацикливания.
        var s = new Sandbox(data: Data);
        s.Put("test_venom_banner");
        s.Put("test_wall_banner");
        var guard = s.Put("gate_guard");   // Провокация
        var toad = s.Put("venom_toad");    // Яд
        var ferret = s.Put("ferret");      // ничего
        // 1) Знамя яда: Провокация у Стража → Страж получает Яд. 2) Знамя стены: Яд у Жабы и Стража → Провокация.
        Assert.Equal(new[] { "taunt", "poison" }, Kw(s, guard));
        Assert.Equal(new[] { "poison", "taunt" }, Kw(s, toad));
        Assert.Empty(Kw(s, ferret));
    }

    [Fact]
    public void Q6_attack_layer_uses_final_keywords()
    {
        // «С Провокацией +2 к атаке» видит Провокацию, выданную другой аурой.
        var s = new Sandbox(data: Data);
        s.Put("test_war_banner");
        s.Put("test_guard_banner");
        var ferret = s.Put("ferret");
        var harpy = s.Put("harpy");
        var guard = s.Put("gate_guard");
        Assert.Equal(4, s.Attack(ferret));
        Assert.Equal(5, s.Attack(harpy));
        Assert.Equal(3, s.Attack(guard));
        Assert.Equal(0, ferret.BonusAttack);
    }

    [Fact]
    public void Q6_layers_apply_in_combat_and_follow_source_removal()
    {
        var s = new Sandbox(data: Data);
        var banner = s.Put("test_guard_banner");
        s.Put("test_venom_banner");
        var ferret = s.Put("ferret");
        s.Put("stone_wall", Side.B);

        var engine = s.Engine();
        var p = engine.Side(Side.A);
        var combatFerret = p.Board.Single(c => c.EntityId == ferret.EntityId);
        Assert.Contains("poison", Stats.Keywords(s.Data.Cards, p, combatFerret));
        // Источник Провокации исчез — пропадают и Провокация, и зависящий от неё Яд.
        engine.Destroy(banner.EntityId);
        Assert.Empty(Stats.Keywords(s.Data.Cards, p, combatFerret));
    }

    [Fact]
    public void Q6_timestamps_are_set_on_entry()
    {
        var s = new Sandbox().Planning();
        var first = s.Put("ferret");
        s.Apply(new PlayCard(s.AddToHand("wolf"), Slot: 0));
        var wolf = s.A.Board[0];
        Assert.True(wolf.Timestamp > first.Timestamp);
        int forge = s.AddToHand("forge");
        s.Apply(new PlayCard(forge, Slot: 0));
        Assert.True(s.A.GetInstance(forge).Timestamp > wolf.Timestamp);
    }
}
