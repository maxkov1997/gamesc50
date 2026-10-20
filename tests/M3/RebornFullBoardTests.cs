using Game.Core;
using Xunit;

namespace Game.Tests.M3;

/// <summary>Q9: Перерождение на полном столе — существо возвращается и сразу погибает со всеми триггерами смерти.</summary>
public class RebornFullBoardTests
{
    [Fact]
    public void Q9_reborn_on_full_board_returns_and_dies_immediately()
    {
        // [Сторож, Слуга, Плакальщица, Скелет ×4]; destroy по Сторожу и Слуге одновременно.
        // Сторож призывает двух Скелетов — стол снова полон; Перерождение Слуги вызывает копию, и она сразу погибает.
        var s = new Sandbox();
        var keeper = s.Put("gravekeeper");
        var servant = s.Put("bone_servant");
        var mourner = s.Put("mourner");
        for (int i = 0; i < 4; i++) s.Put("tok_skeleton");
        s.Put("stone_wall", Side.B);

        var engine = s.Engine();
        engine.Destroy(keeper.EntityId, servant.EntityId);

        var reborn = Assert.Single(engine.Events, e => e.Type == EventTypes.Reborn);
        Assert.Equal(servant.EntityId, reborn.SourceEntityId);
        int rebornAt = engine.Events.IndexOf(reborn);
        Assert.Contains(engine.Events.Skip(rebornAt), e => e.Type == EventTypes.Died && e.EntityId == reborn.EntityId);
        Assert.DoesNotContain(engine.Events, e => e.Type == EventTypes.SummonFailed);

        var p = engine.Side(Side.A);
        Assert.Equal(7, p.AliveCount);
        Assert.DoesNotContain(p.Board, c => c.CardId == "bone_servant");
        // «Когда умирает союзник» срабатывает и на смерть вернувшейся копии: Сторож, Слуга, копия Слуги → +3/+3.
        var m = p.Board.Single(c => c.EntityId == mourner.EntityId);
        Assert.Equal((4, 6), (Stats.Attack(s.Data.Cards, p, m), Stats.Health(s.Data.Cards, m)));
    }
}
