using System;
using System.Collections.Generic;
using System.Linq;
using Game.Client;
using Game.Content;
using Game.Core;
using Godot;
using Side = Game.Core.Side;

namespace Game.View;

/// <summary>
/// Экран матча (12.6). В подготовке — состояние стороны из ядра и действия только из legalActions;
/// в бою — стол, восстановленный из лога (LogBoard): интерфейс проигрывает уже посчитанный бой и на исход не влияет.
/// </summary>
public partial class MatchScreen : VBoxContainer
{
    public GameData Data = null!;
    public UiStrings S = null!;
    public Action? NewMatchRequested;

    private LogText _logText = null!;
    private MatchSession? _session;
    private ReplaySession? _replay;
    private CombatPlayback? _playback;
    private double _waitMs;
    private int _speed = CombatPlayback.Speeds[0];

    // Выбор цели: действия-кандидаты, у каждого своя цель.
    private List<GameAction>? _pending;

    // Подсветка текущего события боя.
    private int? _attacker, _target;
    private readonly Dictionary<int, string> _flash = new();
    private Side? _heroHit;

    private Label _seed = null!, _round = null!, _status = null!, _banner = null!;
    private HBoxContainer _enemyInfo = null!, _myInfo = null!, _controls = null!;
    private BoardRow _enemyRow = null!, _myRow = null!;
    private HandArea _hand = null!;
    private RichTextLabel _log = null!;
    private Button _fight = null!, _next = null!, _replayNext = null!, _skip = null!, _cancel = null!;
    private FileDialog _open = null!;
    private AcceptDialog _discard = null!;

    public MatchSession? Session => _session;
    public ReplaySession? ReplayView => _replay;
    public bool IsPlanning => _session != null && _playback == null && _session.Phase == SessionPhase.Planning;
    public bool PlaybackRunning => _playback is { Done: false };
    public bool HasPendingTarget => _pending != null;

    public override void _Ready()
    {
        _logText = new LogText(S, Data.Cards);
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddThemeConstantOverride("separation", 6);

        // Верхняя панель: seed, раунд, отладка.
        var top = new HBoxContainer();
        AddChild(top);
        _seed = UiTheme.Text("", UiTheme.NormalFont, UiTheme.Target);
        _round = UiTheme.Text("", UiTheme.NormalFont);
        _status = UiTheme.Text("", UiTheme.SmallFont, UiTheme.Muted);
        _status.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        top.AddChild(_seed);
        top.AddChild(_round);
        top.AddChild(_status);
        top.AddChild(MakeButton(S.T(UiKeys.Restart), RestartSameSeed));
        top.AddChild(MakeButton(S.T(UiKeys.SaveReplay), () => SaveReplayTo(null)));
        top.AddChild(MakeButton(S.T(UiKeys.LoadReplay), () => _open.PopupCentered(new Vector2I(800, 500))));
        top.AddChild(MakeButton(S.T(UiKeys.NewMatch), () => NewMatchRequested?.Invoke()));

        var main = new HBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        AddChild(main);
        var game = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        main.AddChild(game);

        _enemyInfo = new HBoxContainer();
        game.AddChild(_enemyInfo);
        _enemyRow = new BoardRow { Screen = this, Side = MatchSession.BotSide };
        game.AddChild(_enemyRow);
        _banner = UiTheme.Text("", UiTheme.BigFont, UiTheme.Target);
        _banner.HorizontalAlignment = HorizontalAlignment.Center;
        game.AddChild(_banner);
        _myRow = new BoardRow { Screen = this, Side = MatchSession.PlayerSide };
        game.AddChild(_myRow);
        _myInfo = new HBoxContainer();
        game.AddChild(_myInfo);
        _hand = new HandArea { Screen = this };
        game.AddChild(_hand);

        _controls = new HBoxContainer { Alignment = AlignmentMode.Center };
        game.AddChild(_controls);
        _fight = MakeButton(S.T(UiKeys.Fight), PressFight);
        _fight.CustomMinimumSize = new Vector2(180, 44);
        _next = MakeButton(S.T(UiKeys.NextRound), PressNextRound);
        _replayNext = MakeButton(S.T(UiKeys.ReplayNext), PressReplayNext);
        _cancel = MakeButton(S.T(UiKeys.Cancel), CancelPending);
        _controls.AddChild(_fight);
        _controls.AddChild(_next);
        _controls.AddChild(_replayNext);
        _controls.AddChild(_cancel);

        // Лог боя, скорость, «Пропустить».
        var side = new VBoxContainer { CustomMinimumSize = new Vector2(360, 0) };
        main.AddChild(side);
        side.AddChild(UiTheme.Text(S.T(UiKeys.CombatLog), UiTheme.NormalFont, UiTheme.Muted));
        _log = new RichTextLabel { ScrollFollowing = true, SizeFlagsVertical = SizeFlags.ExpandFill, FitContent = false };
        _log.AddThemeFontSizeOverride("normal_font_size", UiTheme.SmallFont);
        side.AddChild(_log);
        var speedRow = new HBoxContainer();
        side.AddChild(speedRow);
        speedRow.AddChild(UiTheme.Text(S.T(UiKeys.Speed), UiTheme.SmallFont));
        foreach (int speed in CombatPlayback.Speeds)
        {
            int s = speed;
            speedRow.AddChild(MakeButton(S.T(UiKeys.SpeedX, ("n", s)), () => { _speed = s; Refresh(); }));
        }
        _skip = MakeButton(S.T(UiKeys.Skip), PressSkip);
        side.AddChild(_skip);

        _open = new FileDialog
        {
            FileMode = FileDialog.FileModeEnum.OpenFile,
            Access = FileDialog.AccessEnum.Userdata,
            CurrentDir = ReplayDir,
            Filters = new[] { "*.json" },
            Title = S.T(UiKeys.LoadReplay),
        };
        _open.FileSelected += path => LoadReplayFrom(path);
        AddChild(_open);
        _discard = new AcceptDialog { OkButtonText = S.T(UiKeys.Close) };
        AddChild(_discard);
    }

    private static Button MakeButton(string text, Action onPressed)
    {
        var b = new Button { Text = text };
        b.Pressed += onPressed;
        return b;
    }

    // ---------- Режимы ----------

    public void ShowLive(MatchSession session)
    {
        _session = session;
        _replay = null;
        _playback = null;
        _pending = null;
        ClearHighlights();
        _log.Clear();
        _status.Text = "";
        Refresh();
    }

    public void ShowReplay(ReplaySession replay)
    {
        _session = null;
        _replay = replay;
        _pending = null;
        _log.Clear();
        PressReplayNext();
    }

    private void RestartSameSeed()
    {
        if (_session != null) ShowLive(_session.Restart());
        else if (_replay != null)
            ShowLive(MatchSession.Start(Data, _replay.Record.DeckA, _replay.Record.DeckB, _replay.Record.Seed));
    }

    // ---------- Кнопки ----------

    public void PressFight()
    {
        if (!IsPlanning) return;
        _pending = null;
        _log.Clear();
        StartPlayback(_session!.Fight());
    }

    public void PressSkip()
    {
        if (_playback == null) return;
        while (!_playback.Done) Step(refresh: false);
        Refresh();
    }

    public void PressNextRound()
    {
        if (_session == null || _session.Phase != SessionPhase.CombatResult || PlaybackRunning) return;
        _session.NextRound();
        _playback = null;
        ClearHighlights();
        Refresh();
    }

    public void PressReplayNext()
    {
        if (_replay == null || _replay.Done || PlaybackRunning) return;
        _log.Clear();
        StartPlayback(_replay.NextCombat());
    }

    private void StartPlayback(CombatPlayback playback)
    {
        _playback = playback;
        if (playback.Current is { } first && _logText.Describe(first, playback.Board) is { } line) _log.AppendText(line + "\n");
        _waitMs = playback.Current is { } e ? CombatPlayback.DurationMs(e, _speed) : 0;
        ClearHighlights();
        Refresh();
    }

    // ---------- Проигрывание лога ----------

    public override void _Process(double delta)
    {
        if (_playback == null || _playback.Done) return;
        _waitMs -= delta * 1000;
        bool changed = false;
        while (_waitMs <= 0 && !_playback.Done)
        {
            Step(refresh: false);
            changed = true;
        }
        if (changed) Refresh();
    }

    private void Step(bool refresh)
    {
        var e = _playback!.Events[_playback.Position];
        string? line = _logText.Describe(e, _playback.Board);
        _playback.Next();
        Highlight(e);
        if (line != null) _log.AppendText(line + "\n");
        _waitMs += CombatPlayback.DurationMs(e, _speed);
        if (refresh) Refresh();
    }

    private void Highlight(GameEvent e)
    {
        switch (e.Type)
        {
            case EventTypes.AttackDeclared:
                ClearHighlights();
                _attacker = e.SourceEntityId;
                _target = e.Target;
                break;
            case EventTypes.Damage:
                _flash[e.Target!.Value] = $"-{e.Amount}";
                break;
            case EventTypes.ShieldBroken:
                _flash[e.EntityId!.Value] = S.KeywordName(Registry.Keywords.Shield);
                break;
            case EventTypes.HeroDamaged:
                _heroHit = e.Side;
                break;
            case EventTypes.CombatEnded:
                ClearHighlights();
                break;
        }
    }

    private void ClearHighlights()
    {
        _attacker = _target = null;
        _flash.Clear();
        _heroHit = null;
    }

    // ---------- Перерисовка ----------

    private static void Clear(Node node)
    {
        foreach (var child in node.GetChildren())
        {
            node.RemoveChild(child);
            child.QueueFree();
        }
    }

    public void Refresh()
    {
        if (_session == null && _replay == null) return;
        var state = _session?.State ?? _replay!.State;
        _seed.Text = S.T(UiKeys.Seed) + " " + state.Seed;
        _round.Text = _replay != null ? S.T(UiKeys.ReplayMode, ("round", state.Round)) : S.T(UiKeys.Round, ("round", state.Round));

        Clear(_enemyRow.Row);
        Clear(_myRow.Row);
        Clear(_hand.Row);
        Clear(_enemyInfo);
        Clear(_myInfo);

        if (_playback != null) DrawCombat(state);
        else DrawPlanning();

        bool combatOver = _playback is { Done: true };
        _fight.Visible = IsPlanning;
        _fight.Disabled = _pending != null;
        _cancel.Visible = _pending != null;
        _next.Visible = _session != null && combatOver && _session.Phase == SessionPhase.CombatResult;
        _replayNext.Visible = _replay != null && combatOver && !_replay.Done;
        _skip.Disabled = !PlaybackRunning;
        _banner.Text = BannerText(state, combatOver);
    }

    private string BannerText(MatchState state, bool combatOver)
    {
        if (_pending != null) return S.T(UiKeys.ChooseTarget);
        if (state.Result is Outcome result && (combatOver || _playback == null))
        {
            string match = result == Outcome.Draw ? S.T(UiKeys.ResultDraw)
                : result == Outcome.A ? S.T(UiKeys.ResultWin) : S.T(UiKeys.ResultLoss);
            return _replay != null && _replay.Done ? S.T(UiKeys.ReplayDone) + " — " + match : match;
        }
        if (combatOver && _playback!.Board.Result is Outcome combat)
        {
            string text = combat == Outcome.Draw ? S.T(UiKeys.CombatDraw)
                : combat == Outcome.A ? S.T(UiKeys.CombatWin) : S.T(UiKeys.CombatLoss);
            return _replay != null && _replay.Done ? S.T(UiKeys.ReplayDone) + " — " + text : text;
        }
        if (IsPlanning) return S.T(UiKeys.PlanningHint) + " · " + S.T(UiKeys.BotReady);
        return "";
    }

    private void DrawPlanning()
    {
        var me = _session!.View(MatchSession.PlayerSide);
        var bot = _session.View(MatchSession.BotSide);
        DrawInfo(_enemyInfo, bot, bot.HeroHp, false);
        DrawInfo(_myInfo, me, me.HeroHp, false);

        var pendingTargets = _pending?.Select(TargetOf).Where(t => t != null).Select(t => t!.Value).ToHashSet() ?? new HashSet<int>();
        foreach (var c in bot.Board)
            _enemyRow.Row.AddChild(new CreatureWidget { Screen = this, View = c.View, Side = MatchSession.BotSide, Extra = c });
        foreach (var c in me.Board)
            _myRow.Row.AddChild(new CreatureWidget
            {
                Screen = this, View = c.View, Side = MatchSession.PlayerSide, Extra = c, Draggable = true,
                Highlight = pendingTargets.Contains(c.View.EntityId) ? UiTheme.Target : null,
            });
        foreach (var card in me.Hand)
            _hand.Row.AddChild(new CardWidget { Screen = this, Card = card, Playable = _session.CanPlay(card.InstanceId) });
    }

    private void DrawCombat(MatchState state)
    {
        var board = _playback!.Board;
        foreach (var side in new[] { MatchSession.BotSide, MatchSession.PlayerSide })
        {
            var view = board.Side(side);
            var row = side == MatchSession.PlayerSide ? _myRow.Row : _enemyRow.Row;
            foreach (var c in view.Board)
            {
                Color? hl = c.EntityId == _attacker ? UiTheme.Attacker : c.EntityId == _target ? UiTheme.Hit : null;
                row.AddChild(new CreatureWidget
                {
                    Screen = this, View = c, Side = side, Highlight = hl,
                    Flash = _flash.TryGetValue(c.EntityId, out var f) ? f : null,
                });
            }
            var snap = SideSnapshot.Of(Data.Cards, state.Player(side), owner: side == MatchSession.PlayerSide);
            DrawInfo(side == MatchSession.PlayerSide ? _myInfo : _enemyInfo, snap, view.HeroHp, _heroHit == side);
        }
        if (_session != null)
            foreach (var card in _session.View(MatchSession.PlayerSide).Hand)
                _hand.Row.AddChild(new CardWidget { Screen = this, Card = card, Playable = false });
    }

    /// <summary>Герой, мана, зоны и счётчики стороны.</summary>
    private void DrawInfo(HBoxContainer info, SideSnapshot s, int heroHp, bool hit)
    {
        var hero = new PanelContainer { CustomMinimumSize = new Vector2(190, 0) };
        hero.AddThemeStyleboxOverride("panel", UiTheme.Box(s.IsOwner ? UiTheme.Panel : UiTheme.PanelEnemy, hit ? UiTheme.Hit : UiTheme.Border, hit ? 4 : 2));
        var box = new VBoxContainer();
        hero.AddChild(box);
        box.AddChild(UiTheme.Text(s.IsOwner ? S.T(UiKeys.You) : S.T(UiKeys.Opponent), UiTheme.NormalFont, UiTheme.Muted));
        box.AddChild(UiTheme.Text(S.T(UiKeys.Hp, ("hp", heroHp)), UiTheme.BigFont, hit ? UiTheme.Hit : UiTheme.Good));
        box.AddChild(UiTheme.Text(S.T(UiKeys.Mana, ("mana", s.Mana), ("max", s.ManaMax)), UiTheme.NormalFont));
        if (s.ManaBonus > 0) box.AddChild(UiTheme.Text(S.T(UiKeys.ManaBonus, ("bonus", s.ManaBonus)), UiTheme.SmallFont, UiTheme.Muted));
        info.AddChild(hero);

        var counts = new VBoxContainer();
        counts.AddChild(UiTheme.Text(S.T(UiKeys.DeckCount, ("count", s.DrawCount)), UiTheme.SmallFont));
        counts.AddChild(UiTheme.Text(S.T(UiKeys.HandCount, ("count", s.HandCount)), UiTheme.SmallFont));
        counts.AddChild(UiTheme.Text(S.T(UiKeys.ExileCount, ("count", s.ExileCount)), UiTheme.SmallFont));
        var discard = new DiscardButton
        {
            Screen = this, Side = s.Side,
            Text = S.T(UiKeys.DiscardCount, ("count", s.Discard.Count)),
            TooltipText = s.IsOwner ? S.T(UiKeys.DropDismiss) : "",
        };
        var names = s.Discard.Select(c => c.Def.Name).ToList();
        discard.Pressed += () => ShowDiscard(s.IsOwner ? S.T(UiKeys.You) : S.T(UiKeys.Opponent), names);
        counts.AddChild(discard);
        info.AddChild(counts);

        var zones = new VBoxContainer();
        zones.AddChild(UiTheme.Text(S.T(UiKeys.Field), UiTheme.SmallFont, UiTheme.Muted));
        var field = new HBoxContainer();
        for (int i = 0; i < s.Field.Count; i++)
            field.AddChild(new FieldSlotWidget { Screen = this, Side = s.Side, Slot = i, Card = s.Field[i] });
        zones.AddChild(field);
        info.AddChild(zones);

        var battle = new VBoxContainer();
        battle.AddChild(UiTheme.Text(S.T(UiKeys.BattleZone), UiTheme.SmallFont, UiTheme.Muted));
        battle.AddChild(new BattleZoneWidget
        {
            Screen = this, Side = s.Side, Count = s.BattleZoneCount, Slots = Data.Config.BattleSlots,
            Names = s.BattleZone.Select(c => c.Def.Name).ToList(),
        });
        info.AddChild(battle);
    }

    private void ShowDiscard(string side, List<string> names)
    {
        _discard.Title = S.T(UiKeys.DiscardTitle, ("side", side));
        _discard.DialogText = names.Count == 0 ? S.T(UiKeys.EmptySlot) : string.Join("\n", names);
        _discard.PopupCentered(new Vector2I(360, 420));
    }

    /// <summary>Подсказка карты: текст и описания её ключевых слов (12.6).</summary>
    public string CardTooltip(CardDef def, IEnumerable<string> keywords)
    {
        var lines = new List<string> { def.Name, S.CardTypeName(def.Type) };
        if (def.Tags.Count > 0) lines.Add(string.Join(", ", def.Tags.Select(S.TagName)));
        if (def.Text.Length > 0) lines.Add(def.Text);
        var kws = keywords.Concat(def.Keywords).Distinct().ToList();
        if (kws.Count > 0)
        {
            lines.Add("");
            lines.Add(S.T(UiKeys.KeywordsTitle));
            foreach (var k in kws) lines.Add($"{S.KeywordName(k)} — {S.KeywordText(k, Data.Config)}");
        }
        return string.Join("\n", lines);
    }

    // ---------- Действия игрока (только из legalActions) ----------

    private void Perform(GameAction action)
    {
        if (_session == null) return;
        try
        {
            _session.Apply(action);
            _status.Text = "";
        }
        catch (InvalidActionException ex)
        {
            _status.Text = S.T(UiKeys.Error, ("message", ex.Message));
        }
        _pending = null;
        Refresh();
    }

    private static int? TargetOf(GameAction a) => a switch
    {
        PlayCard pc => pc.Target,
        UseAbility ua => ua.Target,
        _ => null,
    };

    private void BeginTargeting(List<GameAction> options)
    {
        _pending = options;
        Refresh();
    }

    public void CancelPending()
    {
        _pending = null;
        Refresh();
    }

    public void ClickHandCard(int instanceId)
    {
        if (!IsPlanning) return;
        if (_session!.KindOf(instanceId) == PlayKind.Target && _session.CanPlay(instanceId))
            BeginTargeting(_session.PlaysFor(instanceId).Cast<GameAction>().ToList());
        else if (_session.KindOf(instanceId) == PlayKind.Creature)
            _status.Text = S.T(UiKeys.ChoosePosition);
    }

    public void ClickCreature(int entityId)
    {
        if (_pending == null) return;
        var choice = _pending.FirstOrDefault(a => TargetOf(a) == entityId);
        if (choice != null) Perform(choice);
    }

    public bool AbilityUsable(int entityId, int abilityIndex) =>
        IsPlanning && _session!.AbilitiesFor(entityId).Any(a => a.AbilityIndex == abilityIndex);

    public void ClickAbility(int entityId, int abilityIndex)
    {
        if (!IsPlanning) return;
        var options = _session!.AbilitiesFor(entityId).Where(a => a.AbilityIndex == abilityIndex).ToList();
        if (options.Count == 0) return;
        if (options.Count == 1 && options[0].Target == null) Perform(options[0]);
        else BeginTargeting(options.Cast<GameAction>().ToList());
    }

    public bool CanDropCardOnBoard(int instanceId, int index)
    {
        if (!IsPlanning || _pending != null) return false;
        return _session!.KindOf(instanceId) switch
        {
            PlayKind.Creature => _session.SlotsFor(instanceId).Contains(index),
            PlayKind.NoTarget => _session.CanPlay(instanceId),
            _ => false,
        };
    }

    public void DropCardOnBoard(int instanceId, int index)
    {
        if (!CanDropCardOnBoard(instanceId, index)) return;
        if (_session!.KindOf(instanceId) == PlayKind.NoTarget)
        {
            Perform(_session.PlaysFor(instanceId)[0]);
            return;
        }
        var plays = _session.PlaysFor(instanceId).Where(p => p.Slot == index).ToList();
        // «При розыгрыше» с выбором цели: после позиции — цель с подсветкой.
        if (plays.Count == 1 && plays[0].Target == null) Perform(plays[0]);
        else BeginTargeting(plays.Cast<GameAction>().ToList());
    }

    public bool CanDropCardOnCreature(int instanceId, int entityId) =>
        IsPlanning && _pending == null && _session!.KindOf(instanceId) == PlayKind.Target &&
        _session.TargetsFor(instanceId).Contains(entityId);

    public void DropCardOnCreature(int instanceId, int entityId)
    {
        if (CanDropCardOnCreature(instanceId, entityId)) Perform(new PlayCard(instanceId, Target: entityId));
    }

    public bool CanDropCardOnField(int instanceId, int slot) =>
        IsPlanning && _pending == null && _session!.KindOf(instanceId) == PlayKind.FieldSlot &&
        _session.SlotsFor(instanceId).Contains(slot);

    public void DropCardOnField(int instanceId, int slot)
    {
        if (CanDropCardOnField(instanceId, slot)) Perform(new PlayCard(instanceId, Slot: slot));
    }

    public bool CanDropCardInZone(int instanceId)
    {
        if (!IsPlanning || _pending != null || _session!.KindOf(instanceId) != PlayKind.NoTarget) return false;
        var type = Data.Cards.Get(_session.State.Player(MatchSession.PlayerSide).GetInstance(instanceId).CardId).Type;
        return type is CardType.BattleSpell or CardType.Secret && _session.CanPlay(instanceId);
    }

    public void DropCardInZone(int instanceId)
    {
        if (CanDropCardInZone(instanceId)) Perform(_session!.PlaysFor(instanceId)[0]);
    }

    /// <summary>Позиция вставки → toIndex для Move (существо сначала убирается со своего места).</summary>
    private int? MoveIndex(int entityId, int insertIndex)
    {
        var board = _session!.State.Player(MatchSession.PlayerSide).Board;
        int from = board.FindIndex(c => c.EntityId == entityId);
        if (from < 0) return null;
        int to = insertIndex > from ? insertIndex - 1 : insertIndex;
        return to == from ? null : to;
    }

    public bool CanMoveTo(int entityId, int insertIndex) =>
        IsPlanning && _pending == null && MoveIndex(entityId, insertIndex) is int to && _session!.CanMove(entityId, to);

    public void MoveTo(int entityId, int insertIndex)
    {
        if (CanMoveTo(entityId, insertIndex)) Perform(new Move(entityId, MoveIndex(entityId, insertIndex)!.Value));
    }

    public bool CanRecall(int entityId) => IsPlanning && _pending == null && _session!.CanRecall(entityId);

    public void Recall(int entityId)
    {
        if (CanRecall(entityId)) Perform(new Recall(entityId));
    }

    public bool CanDismiss(int entityId) => IsPlanning && _pending == null && _session!.CanDismiss(entityId);

    public void Dismiss(int entityId)
    {
        if (CanDismiss(entityId)) Perform(new Dismiss(entityId));
    }

    // ---------- Для автопроверки интерфейса ----------

    public CardWidget? FindHandCard(int instanceId) =>
        _hand.Row.GetChildren().OfType<CardWidget>().FirstOrDefault(w => w.Card.InstanceId == instanceId);

    /// <summary>Точка у правого края своего стола: бросок сюда ставит существо в правый край.</summary>
    public Vector2 PlayerBoardRightEnd() =>
        new(_myRow.GlobalPosition.X + _myRow.Size.X - 24, _myRow.GlobalPosition.Y + _myRow.Size.Y / 2);

    // ---------- Реплеи ----------

    public const string ReplayDir = "user://replays";

    /// <summary>Сохранить запись матча (seed + колоды + действия). Возвращает путь user://.</summary>
    public string? SaveReplayTo(string? path)
    {
        var record = _session?.Record ?? _replay?.Record;
        if (record == null) return null;
        DirAccess.MakeDirRecursiveAbsolute(ReplayDir);
        var state = _session?.State ?? _replay!.State;
        path ??= $"{ReplayDir}/replay_seed{record.Seed}_round{state.Round}.json";
        using (var file = Godot.FileAccess.Open(path, Godot.FileAccess.ModeFlags.Write))
        {
            if (file == null)
            {
                _status.Text = S.T(UiKeys.Error, ("message", Godot.FileAccess.GetOpenError().ToString()));
                return null;
            }
            file.StoreString(StateSerializer.SerializeRecord(record));
        }
        _status.Text = S.T(UiKeys.ReplaySaved, ("path", ProjectSettings.GlobalizePath(path)));
        return path;
    }

    public void LoadReplayFrom(string path)
    {
        try
        {
            using var file = Godot.FileAccess.Open(path, Godot.FileAccess.ModeFlags.Read)
                ?? throw new InvalidOperationException(Godot.FileAccess.GetOpenError().ToString());
            ShowReplay(ReplaySession.Load(Data, file.GetAsText()));
        }
        catch (Exception ex)
        {
            _status.Text = S.T(UiKeys.Error, ("message", ex.Message));
        }
    }
}
