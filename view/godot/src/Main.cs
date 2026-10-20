using System;
using System.Linq;
using Game.Client;
using Game.Content;
using Game.Core;
using Godot;
using Side = Game.Core.Side;

namespace Game.View;

/// <summary>
/// Корень интерфейса: загрузка данных из res://content, стартовый экран (колоды, seed, реплей) и экран матча.
/// Аргументы после «--»: --autoplay — прогнать матч целиком через обработчики интерфейса и выйти;
/// --screenshots &lt;каталог&gt; — сохранить снимки экрана (нужен рендер, не --headless).
/// </summary>
public partial class Main : Control
{
    public const string ContentRoot = "res://content";

    public GameData Data { get; private set; } = null!;
    public UiStrings Strings { get; private set; } = null!;
    public MatchScreen Screen { get; private set; } = null!;

    private Control _start = null!;
    private OptionButton _playerDeck = null!, _botDeck = null!;
    private LineEdit _seed = null!;
    private Label _error = null!;

    public override void _Ready()
    {
        var bg = new ColorRect { Color = UiTheme.Background };
        bg.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(bg);

        try
        {
            var source = new GodotContentSource(ContentRoot);
            Data = ContentLoader.Load(source);
            Strings = ContentLoader.LoadStrings(source);
        }
        catch (Exception ex)
        {
            GD.PrintErr(ex.ToString());
            AddChild(UiTheme.Text(ex.Message, UiTheme.NormalFont, UiTheme.Hit, wrap: true));
            if (OS.GetCmdlineUserArgs().Contains("--autoplay")) GetTree().Quit(1);
            return;
        }
        DisplayServer.WindowSetTitle(Strings.T(UiKeys.Title));

        Screen = new MatchScreen { Data = Data, S = Strings, Visible = false };
        Screen.NewMatchRequested = ShowStart;
        AddChild(Screen);
        _start = BuildStart();
        AddChild(_start);

        var args = OS.GetCmdlineUserArgs();
        if (args.Contains("--autoplay"))
        {
            int i = Array.IndexOf(args, "--screenshots");
            AddChild(new AutoPlay { Main = this, ShotsDir = i >= 0 && i + 1 < args.Length ? args[i + 1] : null });
        }
    }

    private Control BuildStart()
    {
        var center = new CenterContainer();
        center.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var panel = new PanelContainer();
        panel.AddThemeStyleboxOverride("panel", UiTheme.Box(UiTheme.Panel, UiTheme.Border));
        center.AddChild(panel);
        var box = new VBoxContainer { CustomMinimumSize = new Vector2(420, 0) };
        box.AddThemeConstantOverride("separation", 10);
        panel.AddChild(box);

        box.AddChild(UiTheme.Text(Strings.T(UiKeys.Title), UiTheme.BigFont, UiTheme.Target));
        box.AddChild(UiTheme.Text(Strings.T(UiKeys.PlayerDeck)));
        _playerDeck = DeckPicker(0);
        box.AddChild(_playerDeck);
        box.AddChild(UiTheme.Text(Strings.T(UiKeys.BotDeck)));
        _botDeck = DeckPicker(Math.Min(1, Data.Decks.Count - 1));
        box.AddChild(_botDeck);
        box.AddChild(UiTheme.Text(Strings.T(UiKeys.Seed)));
        _seed = new LineEdit { Text = "1" };
        box.AddChild(_seed);
        _error = UiTheme.Text("", UiTheme.SmallFont, UiTheme.Hit, wrap: true);
        box.AddChild(_error);

        var start = new Button { Text = Strings.T(UiKeys.Start) };
        start.Pressed += () =>
        {
            if (!ulong.TryParse(_seed.Text.Trim(), out ulong seed))
            {
                _error.Text = Strings.T(UiKeys.Error, ("message", Strings.T(UiKeys.Seed)));
                return;
            }
            StartMatch(Data.Decks[_playerDeck.Selected].Id, Data.Decks[_botDeck.Selected].Id, seed);
        };
        box.AddChild(start);

        var load = new Button { Text = Strings.T(UiKeys.LoadReplay) };
        var dialog = new FileDialog
        {
            FileMode = FileDialog.FileModeEnum.OpenFile,
            Access = FileDialog.AccessEnum.Userdata,
            CurrentDir = MatchScreen.ReplayDir,
            Filters = new[] { "*.json" },
            Title = Strings.T(UiKeys.LoadReplay),
        };
        dialog.FileSelected += path =>
        {
            ShowScreen();
            Screen.LoadReplayFrom(path);
        };
        center.AddChild(dialog);
        load.Pressed += () =>
        {
            DirAccess.MakeDirRecursiveAbsolute(MatchScreen.ReplayDir);
            dialog.PopupCentered(new Vector2I(800, 500));
        };
        box.AddChild(load);
        return center;
    }

    private OptionButton DeckPicker(int selected)
    {
        var picker = new OptionButton();
        foreach (var d in Data.Decks) picker.AddItem(d.Name);
        picker.Selected = selected;
        return picker;
    }

    private void ShowStart()
    {
        Screen.Visible = false;
        _start.Visible = true;
    }

    private void ShowScreen()
    {
        _start.Visible = false;
        Screen.Visible = true;
    }

    public void StartMatch(string playerDeck, string botDeck, ulong seed)
    {
        ShowScreen();
        Screen.ShowLive(MatchSession.Start(Data, Data.GetDeck(playerDeck), Data.GetDeck(botDeck), seed));
    }
}
