using Game.Ai;
using Game.Client;
using Game.Content;
using Game.Core;
using Xunit;

namespace Game.Tests.M5;

/// <summary>M5: логика интерфейса без движка — матч против бота, видимость, подсказки, лог, реплей.</summary>
public class ClientTests
{
    private static readonly UiStrings Strings = ContentLoader.LoadStrings(new DirectorySource(TestData.ContentDir));

    private static MatchSession NewSession(ulong seed = 7) =>
        MatchSession.Start(TestData.Data, TestData.Data.GetDeck("mechs"), TestData.Data.GetDeck("pack"), seed);

    /// <summary>Игрок, которого изображает бот: решения через API сессии, как у интерфейса.</summary>
    private static void PlayAsBot(MatchSession session)
    {
        var bot = new Bot(session.Data);
        while (true)
        {
            var action = bot.ChooseAction(session.State, MatchSession.PlayerSide);
            if (action is Ready) break;
            Assert.Contains(action, session.Legal());
            session.Apply(action);
        }
    }

    [Fact]
    public void M5_match_vs_bot_plays_from_round_one_to_the_end()
    {
        var session = NewSession();
        Assert.Equal(1, session.State.Round);
        int rounds = 0;
        while (session.Phase != SessionPhase.MatchOver)
        {
            Assert.Equal(SessionPhase.Planning, session.Phase);
            // opponent_first: бот уже готов, его стол виден.
            Assert.True(session.View(MatchSession.BotSide).IsReady);
            PlayAsBot(session);
            var playback = session.Fight();
            playback.SkipToEnd();
            Assert.Equal(session.State.Player(Side.A).HeroHp, playback.Board.Side(Side.A).HeroHp);
            Assert.Equal(session.State.Player(Side.B).HeroHp, playback.Board.Side(Side.B).HeroHp);
            if (session.Phase == SessionPhase.CombatResult) session.NextRound();
            rounds++;
        }
        Assert.NotNull(session.State.Result);
        Assert.Equal(rounds, session.State.Round);
        Assert.DoesNotContain(session.State.Log, e => e.Type == EventTypes.Warning);

        // Запись матча из интерфейса воспроизводит состояние побайтово.
        var replayed = Replay.Run(TestData.Data, StateSerializer.DeserializeRecord(session.SaveReplay()));
        Assert.Equal(StateSerializer.Serialize(session.State), StateSerializer.Serialize(replayed));
    }

    [Fact]
    public void M5_replay_session_reproduces_every_combat_log()
    {
        var session = NewSession(seed: 11);
        var originalLogs = new List<string>();
        while (session.Phase != SessionPhase.MatchOver)
        {
            PlayAsBot(session);
            originalLogs.Add(StateSerializer.SerializeEvents(session.Fight().Events));
            if (session.Phase == SessionPhase.CombatResult) session.NextRound();
        }

        var replay = ReplaySession.Load(TestData.Data, session.SaveReplay());
        var replayLogs = new List<string>();
        while (!replay.Done) replayLogs.Add(StateSerializer.SerializeEvents(replay.NextCombat().Events));
        Assert.Equal(originalLogs, replayLogs);
        Assert.Equal(session.State.Result, replay.State.Result);
    }

    [Fact]
    public void M5_restart_with_same_seed_repeats_the_match()
    {
        var a = NewSession(seed: 3);
        var b = a.Restart();
        Assert.Equal(a.Seed, b.Seed);
        Assert.Equal(StateSerializer.Serialize(a.State), StateSerializer.Serialize(b.State));
    }

    [Fact]
    public void M5_opponent_view_hides_hand_and_battle_zone()
    {
        var session = NewSession();
        var me = session.View(MatchSession.PlayerSide);
        var bot = session.View(MatchSession.BotSide);
        Assert.True(me.IsOwner);
        Assert.Equal(me.HandCount, me.Hand.Count);
        Assert.False(bot.IsOwner);
        Assert.Empty(bot.Hand);
        Assert.Empty(bot.BattleZone);
        Assert.Equal(session.State.Player(Side.B).Hand.Count, bot.HandCount);
        Assert.Equal(session.State.Player(Side.B).BattleZone.Count, bot.BattleZoneCount);
        Assert.Equal(session.State.Player(Side.B).AliveCount, bot.Board.Count);
    }

    [Fact]
    public void M5_interaction_hints_come_from_legal_actions()
    {
        var session = MatchSession.Start(TestData.Data, TestData.Data.GetDeck("pack"), TestData.Data.GetDeck("bones"), 1);
        var p = session.State.Player(Side.A);
        p.Mana = 10;
        p.Hand.Clear();
        var ferret = Entities.NewCreature(p, "tok_spark", null);
        p.Board.Add(ferret);
        int Add(string id)
        {
            var inst = new CardInstance { InstanceId = session.State.NextInstanceId++, CardId = id };
            p.Instances.Add(inst);
            p.Hand.Add(inst.InstanceId);
            return inst.InstanceId;
        }
        int app = Add("apprentice"), blessing = Add("blessing_of_might"), forge = Add("forge"), snare = Add("snare");

        Assert.Equal(PlayKind.Creature, session.KindOf(app));
        Assert.Equal(new[] { 0, 1 }, session.SlotsFor(app));
        Assert.Equal(new[] { ferret.EntityId }, session.TargetsFor(app, 1));
        Assert.Equal(PlayKind.Target, session.KindOf(blessing));
        Assert.Equal(new[] { ferret.EntityId }, session.TargetsFor(blessing));
        Assert.Equal(PlayKind.FieldSlot, session.KindOf(forge));
        Assert.Equal(new[] { 0, 1, 2 }, session.SlotsFor(forge));
        Assert.Equal(PlayKind.NoTarget, session.KindOf(snare));
        Assert.True(session.CanPlay(snare));
        Assert.False(session.CanRecall(ferret.EntityId)); // токен
        Assert.True(session.CanDismiss(ferret.EntityId));
    }

    [Fact]
    public void M5_playback_steps_and_skip_give_same_board()
    {
        var session = NewSession(seed: 5);
        PlayAsBot(session);
        var events = session.Fight().Events;

        var stepped = new CombatPlayback(events);
        int shown = 0;
        while (stepped.Next() is { } e) shown += CombatPlayback.DurationMs(e, 1) > 0 ? 1 : 0;
        var skipped = new CombatPlayback(events);
        skipped.SkipToEnd();

        Assert.True(shown > 0);
        // Стол виден сразу, ещё до первого шага.
        var fresh = new CombatPlayback(events);
        Assert.True(fresh.Board.Started);
        Assert.Equal(EventTypes.CombatStarted, fresh.Current!.Type);
        Assert.Equal(StateSerializerViews(stepped.Board), StateSerializerViews(skipped.Board));
        var attack = events.First(e => e.Type == EventTypes.CombatStarted);
        Assert.Equal(CombatPlayback.DurationMs(attack, 1) / 4, CombatPlayback.DurationMs(attack, 4));
    }

    private static string StateSerializerViews(LogBoard b) =>
        string.Join("|", b.Sides.Select(s => $"{s.HeroHp}:" + string.Join(",", s.Board.Select(c => $"{c.EntityId}={c.Attack}/{c.Health}"))));

    [Fact]
    public void M5_every_ui_key_and_log_line_exists_in_data()
    {
        var missing = UiKeys.All.Where(k => !Strings.Ui.ContainsKey(k)).ToList();
        Assert.Empty(missing);
        foreach (var k in Registry.KeywordList)
        {
            Assert.False(string.IsNullOrWhiteSpace(Strings.KeywordName(k.Id)));
            Assert.DoesNotContain("{", Strings.KeywordText(k.Id, TestData.Data.Config));
        }
        Assert.Contains("25", Strings.KeywordText(Registry.Keywords.Flying, TestData.Data.Config));

        // Каждое событие боя реального матча описывается без пропущенных ключей и неподставленных полей.
        var session = NewSession(seed: 9);
        var text = new LogText(Strings, TestData.Data.Cards);
        while (session.Phase != SessionPhase.MatchOver)
        {
            PlayAsBot(session);
            var playback = session.Fight();
            while (!playback.Done)
            {
                var e = playback.Events[playback.Position];
                var line = text.Describe(e, playback.Board);
                if (line != null)
                {
                    Assert.DoesNotContain("[", line);
                    Assert.DoesNotContain("{", line);
                }
                playback.Next();
            }
            if (session.Phase == SessionPhase.CombatResult) session.NextRound();
        }
    }

    [Fact]
    public void M5_strings_are_validated()
    {
        var ex = Assert.Throws<GameDataException>(() => ContentLoader.LoadStrings("""
            { "ui": {}, "keywords": { "taunt": { "name": "Провокация", "text": "" }, "stealth": { "name": "?", "text": "" } },
              "tags": {}, "card_types": {}, "log": {}, "extra": {} }
            """));
        Assert.Contains(ex.Errors, e => e.Code == ErrorCodes.UnknownKeyword && e.Path.Contains("stealth"));
        Assert.Contains(ex.Errors, e => e.Code == ErrorCodes.UnknownField && e.Path.Contains("extra"));
        Assert.Contains(ex.Errors, e => e.Code == ErrorCodes.MissingField && e.Message.Contains("shield"));
        Assert.Contains(ex.Errors, e => e.Code == ErrorCodes.MissingField && e.Message.Contains("beast"));
    }
}
