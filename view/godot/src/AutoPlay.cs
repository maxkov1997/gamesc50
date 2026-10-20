using System;
using System.Linq;
using System.Threading.Tasks;
using Game.Ai;
using Game.Client;
using Game.Core;
using Godot;
using Side = Game.Core.Side;

namespace Game.View;

/// <summary>
/// Проверка интерфейса без человека (--autoplay): матч от первого раунда до конца, где за игрока решает бот,
/// а каждое действие проходит через те же обработчики, что и перетаскивание/клики. Затем реплей сохраняется,
/// открывается и проигрывается; результат обязан совпасть. Код выхода 0 — всё совпало.
/// </summary>
public partial class AutoPlay : Node
{
    public Main Main = null!;
    public string? ShotsDir;

    private const string PlayerDeck = "mechs";
    private const string BotDeck = "pack";
    private const ulong Seed = 1;
    private int _shots;

    public override async void _Ready()
    {
        try
        {
            await Run();
            GetTree().Quit(0);
        }
        catch (Exception ex)
        {
            GD.PrintErr("AUTOPLAY FAILED: " + ex);
            GetTree().Quit(1);
        }
    }

    private async Task Frames(int n)
    {
        for (int i = 0; i < n; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    private async Task Shot(string name)
    {
        if (ShotsDir == null || DisplayServer.GetName() == "headless") return;
        await Frames(2);
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        var image = GetViewport().GetTexture().GetImage();
        DirAccess.MakeDirRecursiveAbsolute(ShotsDir);
        string path = $"{ShotsDir}/{++_shots:00}_{name}.png";
        image.SavePng(path);
        GD.Print($"screenshot {path}");
    }

    private async Task Run()
    {
        Main.StartMatch(PlayerDeck, BotDeck, Seed);
        var screen = Main.Screen;
        var session = screen.Session!;
        var bot = new Bot(Main.Data);
        await Frames(2);

        bool watchedOneCombat = false;
        bool draggedWithMouse = false;
        while (session.Phase != SessionPhase.MatchOver)
        {
            // Ход игрока: бот выбирает, интерфейс исполняет через свои обработчики.
            while (true)
            {
                var action = bot.ChooseAction(session.State, MatchSession.PlayerSide);
                if (action is Ready) break;
                int before = session.Record.Rounds[^1].Count;
                if (!draggedWithMouse && action is PlayCard { Target: null } pc && session.KindOf(pc.InstanceId) == PlayKind.Creature)
                {
                    // Один розыгрыш — настоящим перетаскиванием мышью из руки на правый край стола.
                    await Frames(2); // раскладка только что перестроенных виджетов
                    await MouseDrag(screen.FindHandCard(pc.InstanceId) ?? throw new InvalidOperationException("Нет карты в руке"),
                        screen.PlayerBoardRightEnd());
                    draggedWithMouse = true;
                    GD.Print($"AUTOPLAY mouse drag: {action}");
                }
                else Gesture(screen, session, action);
                var done = session.Record.Rounds[^1];
                if (done.Count != before + 1 || !done[^1].Action.Equals(action))
                    throw new InvalidOperationException($"Интерфейс не выполнил {action}");
            }
            if (session.State.Round == 3) await Shot("planning");

            screen.PressFight();
            if (!watchedOneCombat && session.State.Round >= 3)
            {
                // Один бой — честно по таймеру на ×4, со снимком посередине.
                watchedOneCombat = true;
                int frames = 0;
                while (screen.PlaybackRunning)
                {
                    await Frames(1);
                    if (++frames == 40) await Shot("combat");
                }
            }
            else screen.PressSkip();
            await Frames(1);
            if (session.Phase == SessionPhase.CombatResult) screen.PressNextRound();
        }
        await Shot("match_over");
        var result = session.State.Result;
        GD.Print($"AUTOPLAY match: rounds={session.State.Round} result={result} hp={session.State.Player(Side.A).HeroHp}/{session.State.Player(Side.B).HeroHp}");

        // Реплей: сохранить → открыть → проиграть все бои.
        string path = screen.SaveReplayTo($"{MatchScreen.ReplayDir}/autoplay_seed{Seed}.json")
            ?? throw new InvalidOperationException("Реплей не сохранён");
        screen.LoadReplayFrom(path);
        var replay = screen.ReplayView ?? throw new InvalidOperationException("Реплей не открылся");
        int combats = 1;
        while (true)
        {
            screen.PressSkip();
            await Frames(1);
            if (replay.Done) break;
            screen.PressReplayNext();
            combats++;
        }
        await Shot("replay_done");
        if (replay.State.Result != result || replay.State.Round != session.State.Round)
            throw new InvalidOperationException($"Реплей разошёлся: {replay.State.Result} в раунде {replay.State.Round}");
        GD.Print($"AUTOPLAY replay: combats={combats} result={replay.State.Result} — совпало");
        GD.Print("AUTOPLAY OK");
    }

    /// <summary>
    /// Перетаскивание синтетическими событиями мыши: нажать, протащить, отпустить. События идут в координатах окна —
    /// точки вьюпорта переводятся через GetScreenTransform (окно может быть растянуто).
    /// </summary>
    private async Task MouseDrag(Control from, Vector2 to)
    {
        var screen = GetViewport().GetScreenTransform();
        var start = screen * (from.GlobalPosition + from.Size / 2);
        var end = screen * to;
        Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = start, GlobalPosition = start, ButtonMask = MouseButtonMask.Left });
        await Frames(1);
        const int steps = 12;
        var last = start;
        for (int i = 1; i <= steps; i++)
        {
            var p = start.Lerp(end, (float)i / steps);
            Input.ParseInputEvent(new InputEventMouseMotion { Position = p, GlobalPosition = p, Relative = p - last, ButtonMask = MouseButtonMask.Left });
            last = p;
            await Frames(1);
        }
        Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = end, GlobalPosition = end });
        await Frames(2);
    }

    /// <summary>Действие → жест интерфейса (бросок на стол/существо/слот/зону, клик по способности и цели).</summary>
    private static void Gesture(MatchScreen screen, MatchSession session, GameAction action)
    {
        switch (action)
        {
            case PlayCard pc:
                switch (session.KindOf(pc.InstanceId))
                {
                    case PlayKind.Creature:
                        screen.DropCardOnBoard(pc.InstanceId, pc.Slot!.Value);
                        if (pc.Target is int t) screen.ClickCreature(t);
                        break;
                    case PlayKind.Target:
                        screen.DropCardOnCreature(pc.InstanceId, pc.Target!.Value);
                        break;
                    case PlayKind.FieldSlot:
                        screen.DropCardOnField(pc.InstanceId, pc.Slot!.Value);
                        break;
                    default:
                        if (screen.CanDropCardInZone(pc.InstanceId)) screen.DropCardInZone(pc.InstanceId);
                        else screen.DropCardOnBoard(pc.InstanceId, 0);
                        break;
                }
                break;
            case UseAbility ua:
                screen.ClickAbility(ua.EntityId, ua.AbilityIndex);
                if (ua.Target is int at) screen.ClickCreature(at);
                break;
            case Move mv:
            {
                int from = session.State.Player(MatchSession.PlayerSide).Board.FindIndex(c => c.EntityId == mv.EntityId);
                screen.MoveTo(mv.EntityId, mv.ToIndex > from ? mv.ToIndex + 1 : mv.ToIndex);
                break;
            }
            case Recall rc:
                screen.Recall(rc.EntityId);
                break;
            case Dismiss ds:
                screen.Dismiss(ds.EntityId);
                break;
        }
    }
}
