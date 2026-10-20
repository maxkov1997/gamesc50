using System.Collections.Generic;
using System.Linq;
using Game.Client;
using Game.Core;
using Godot;
using Side = Game.Core.Side;

namespace Game.View;

/// <summary>Данные перетаскивания: карта из руки или существо со стола.</summary>
public static class Drag
{
    public const string Card = "card";
    public const string Creature = "creature";

    public static Variant Make(string kind, int id) =>
        new Godot.Collections.Dictionary { ["kind"] = kind, ["id"] = id };

    public static bool Is(Variant data, string kind, out int id)
    {
        id = 0;
        if (data.VariantType != Variant.Type.Dictionary) return false;
        var d = data.AsGodotDictionary();
        if (!d.ContainsKey("kind") || d["kind"].AsString() != kind) return false;
        id = d["id"].AsInt32();
        return true;
    }

    public static Control Preview(string text)
    {
        var p = new PanelContainer();
        p.AddThemeStyleboxOverride("panel", UiTheme.Box(UiTheme.Card, UiTheme.Target));
        p.AddChild(UiTheme.Text(text));
        return p;
    }
}

/// <summary>Карта в руке игрока: перетаскивается на стол, на существо, в слот поля или в боевую зону.</summary>
public partial class CardWidget : PanelContainer
{
    public MatchScreen Screen = null!;
    public CardView Card = null!;
    public bool Playable;

    public override void _Ready()
    {
        CustomMinimumSize = UiTheme.HandCardSize;
        AddThemeStyleboxOverride("panel", UiTheme.Box(UiTheme.Card, Playable ? UiTheme.CardPlayable : UiTheme.Border));
        var def = Card.Def;
        var box = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        AddChild(box);
        float width = UiTheme.HandCardSize.X - 16;
        var head = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        head.AddChild(UiTheme.Text(Screen.S.T(UiKeys.Cost, ("cost", def.Cost)), UiTheme.BigFont, UiTheme.Target));
        head.AddChild(UiTheme.Text(Screen.S.CardTypeName(def.Type), UiTheme.SmallFont, UiTheme.Muted));
        box.AddChild(head);
        var name = UiTheme.Text(def.Name, UiTheme.NormalFont, null, wrap: true);
        name.CustomMinimumSize = new Vector2(width, 0);
        box.AddChild(name);
        if (def.Type == CardType.Creature)
            box.AddChild(UiTheme.Text(Screen.S.T(UiKeys.AttackHealth, ("attack", def.Attack), ("health", def.Health)), UiTheme.NormalFont));
        var text = UiTheme.Text(def.Text, UiTheme.SmallFont, null, wrap: true);
        text.CustomMinimumSize = new Vector2(width, 0);
        box.AddChild(text);
        TooltipText = Screen.CardTooltip(def, def.Keywords);
    }

    public override Variant _GetDragData(Vector2 atPosition)
    {
        if (!Playable || !Screen.IsPlanning) return default;
        SetDragPreview(Drag.Preview(Card.Def.Name));
        return Drag.Make(Drag.Card, Card.InstanceId);
    }

    public override void _GuiInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left })
            Screen.ClickHandCard(Card.InstanceId);
    }
}

/// <summary>Существо на столе — в подготовке (своё перетаскивается) и в бою (только показ по логу).</summary>
public partial class CreatureWidget : PanelContainer
{
    public MatchScreen Screen = null!;
    public CreatureView View = null!;
    public Side Side;
    public BoardCreature? Extra;
    public bool Draggable;
    public Color? Highlight;
    public string? Flash;

    public override void _Ready()
    {
        CustomMinimumSize = UiTheme.CreatureSize;
        var bg = Side == MatchSession.PlayerSide ? UiTheme.Card : UiTheme.PanelEnemy;
        AddThemeStyleboxOverride("panel", UiTheme.Box(bg, Highlight ?? UiTheme.Border, Highlight == null ? 2 : 4));
        var box = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        AddChild(box);
        var def = Screen.Data.Cards.Get(View.CardId);
        var name = UiTheme.Text(def.Name, UiTheme.SmallFont, null, wrap: true);
        name.CustomMinimumSize = new Vector2(UiTheme.CreatureSize.X - 16, 0);
        box.AddChild(name);
        var stats = UiTheme.Text(Screen.S.T(UiKeys.AttackHealth, ("attack", View.Attack), ("health", View.Health)), UiTheme.BigFont,
            View.Health < View.MaxHealth ? UiTheme.Hit : null);
        box.AddChild(stats);
        if (View.Keywords.Count > 0)
            box.AddChild(UiTheme.Text(string.Join(", ", View.Keywords.Select(Screen.S.KeywordName)), UiTheme.SmallFont, UiTheme.Target, wrap: true));
        if (Extra is { Enchantments.Count: > 0 })
            box.AddChild(UiTheme.Text(Screen.S.T(UiKeys.EnchantedBy, ("names", string.Join(", ", Extra.Enchantments.Select(e => e.Name)))),
                UiTheme.SmallFont, UiTheme.Muted, wrap: true));
        if (Flash != null) box.AddChild(UiTheme.Text(Flash, UiTheme.BigFont, UiTheme.Hit));

        if (Extra != null && Screen.IsPlanning && Side == MatchSession.PlayerSide)
        {
            foreach (var a in Extra.Activated)
            {
                bool usable = Screen.AbilityUsable(View.EntityId, a.Index);
                var b = new Button
                {
                    Text = Screen.S.T(UiKeys.Ability, ("cost", a.Ability.Cost ?? 0)),
                    Disabled = !usable,
                    TooltipText = usable ? "" : Screen.S.T(UiKeys.Used),
                };
                b.AddThemeFontSizeOverride("font_size", UiTheme.SmallFont);
                int index = a.Index;
                b.Pressed += () => Screen.ClickAbility(View.EntityId, index);
                box.AddChild(b);
            }
        }
        TooltipText = Screen.CardTooltip(def, View.Keywords);
    }

    public override Variant _GetDragData(Vector2 atPosition)
    {
        if (!Draggable || !Screen.IsPlanning) return default;
        SetDragPreview(Drag.Preview(Screen.Data.Cards.Get(View.CardId).Name));
        return Drag.Make(Drag.Creature, View.EntityId);
    }

    public override bool _CanDropData(Vector2 atPosition, Variant data) =>
        Drag.Is(data, Drag.Card, out int inst) && Screen.CanDropCardOnCreature(inst, View.EntityId);

    public override void _DropData(Vector2 atPosition, Variant data)
    {
        if (Drag.Is(data, Drag.Card, out int inst)) Screen.DropCardOnCreature(inst, View.EntityId);
    }

    public override void _GuiInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left })
            Screen.ClickCreature(View.EntityId);
    }
}

/// <summary>Ряд стола на 7 мест. Свой ряд принимает существа (позиция по месту броска), заклинания без цели и перестановку.</summary>
public partial class BoardRow : PanelContainer
{
    public MatchScreen Screen = null!;
    public Side Side;
    public HBoxContainer Row = new();

    public override void _Ready()
    {
        CustomMinimumSize = new Vector2(0, UiTheme.CreatureSize.Y + 16);
        SizeFlagsHorizontal = SizeFlags.ExpandFill;
        AddThemeStyleboxOverride("panel", UiTheme.Box(Side == MatchSession.PlayerSide ? UiTheme.Panel : UiTheme.PanelEnemy, UiTheme.Border, 1));
        Row.Alignment = BoxContainer.AlignmentMode.Center;
        Row.MouseFilter = MouseFilterEnum.Pass;
        AddChild(Row);
    }

    /// <summary>Позиция вставки 0…n по координате броска.</summary>
    public int InsertIndex(Vector2 atPosition)
    {
        float x = GlobalPosition.X + atPosition.X;
        return Row.GetChildren().OfType<CreatureWidget>().Count(w => w.GlobalPosition.X + w.Size.X / 2 < x);
    }

    public override bool _CanDropData(Vector2 atPosition, Variant data)
    {
        if (Side != MatchSession.PlayerSide) return false;
        if (Drag.Is(data, Drag.Card, out int inst)) return Screen.CanDropCardOnBoard(inst, InsertIndex(atPosition));
        if (Drag.Is(data, Drag.Creature, out int entity)) return Screen.CanMoveTo(entity, InsertIndex(atPosition));
        return false;
    }

    public override void _DropData(Vector2 atPosition, Variant data)
    {
        if (Drag.Is(data, Drag.Card, out int inst)) Screen.DropCardOnBoard(inst, InsertIndex(atPosition));
        else if (Drag.Is(data, Drag.Creature, out int entity)) Screen.MoveTo(entity, InsertIndex(atPosition));
    }
}

/// <summary>Слот поля чар: принимает чары на поле (свободный слот или замена).</summary>
public partial class FieldSlotWidget : PanelContainer
{
    public MatchScreen Screen = null!;
    public Side Side;
    public int Slot;
    public CardView? Card;

    public override void _Ready()
    {
        CustomMinimumSize = UiTheme.SlotSize;
        AddThemeStyleboxOverride("panel", UiTheme.Box(UiTheme.Panel, UiTheme.Border, 1));
        AddChild(UiTheme.Text(Card?.Def.Name ?? Screen.S.T(UiKeys.EmptySlot), UiTheme.SmallFont, Card == null ? UiTheme.Muted : null, wrap: true));
        if (Card != null) TooltipText = Card.Def.Text;
    }

    public override bool _CanDropData(Vector2 atPosition, Variant data) =>
        Side == MatchSession.PlayerSide && Drag.Is(data, Drag.Card, out int inst) && Screen.CanDropCardOnField(inst, Slot);

    public override void _DropData(Vector2 atPosition, Variant data)
    {
        if (Drag.Is(data, Drag.Card, out int inst)) Screen.DropCardOnField(inst, Slot);
    }
}

/// <summary>Боевая зона: свои карты видны, у противника — рубашки (5.7). Принимает боевые заклинания и секреты.</summary>
public partial class BattleZoneWidget : PanelContainer
{
    public MatchScreen Screen = null!;
    public Side Side;
    public IReadOnlyList<string> Names = new List<string>();
    public int Count;
    public int Slots;

    public override void _Ready()
    {
        AddThemeStyleboxOverride("panel", UiTheme.Box(UiTheme.Panel, UiTheme.Border, 1));
        var row = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        AddChild(row);
        for (int i = 0; i < Slots; i++)
        {
            var slot = new PanelContainer { CustomMinimumSize = UiTheme.SlotSize, MouseFilter = MouseFilterEnum.Ignore };
            bool filled = i < Count;
            bool hidden = filled && i >= Names.Count;
            slot.AddThemeStyleboxOverride("panel", UiTheme.Box(hidden ? UiTheme.CardBack : UiTheme.Card, UiTheme.Border, 1));
            string text = !filled ? Screen.S.T(UiKeys.EmptySlot) : hidden ? Screen.S.T(UiKeys.CardBack) : Names[i];
            slot.AddChild(UiTheme.Text(text, UiTheme.SmallFont, null, wrap: true));
            row.AddChild(slot);
        }
    }

    public override bool _CanDropData(Vector2 atPosition, Variant data) =>
        Side == MatchSession.PlayerSide && Drag.Is(data, Drag.Card, out int inst) && Screen.CanDropCardInZone(inst);

    public override void _DropData(Vector2 atPosition, Variant data)
    {
        if (Drag.Is(data, Drag.Card, out int inst)) Screen.DropCardInZone(inst);
    }
}

/// <summary>Рука: сюда перетаскивают своё существо, чтобы отозвать его (5.5).</summary>
public partial class HandArea : PanelContainer
{
    public MatchScreen Screen = null!;
    public HBoxContainer Row = new();

    public override void _Ready()
    {
        SizeFlagsHorizontal = SizeFlags.ExpandFill;
        CustomMinimumSize = new Vector2(0, UiTheme.HandCardSize.Y + 16);
        AddThemeStyleboxOverride("panel", UiTheme.Box(UiTheme.Panel, UiTheme.Border, 1));
        Row.Alignment = BoxContainer.AlignmentMode.Center;
        Row.MouseFilter = MouseFilterEnum.Pass;
        AddChild(Row);
    }

    public override bool _CanDropData(Vector2 atPosition, Variant data) =>
        Drag.Is(data, Drag.Creature, out int entity) && Screen.CanRecall(entity);

    public override void _DropData(Vector2 atPosition, Variant data)
    {
        if (Drag.Is(data, Drag.Creature, out int entity)) Screen.Recall(entity);
    }
}

/// <summary>Сброс: нажатие — просмотр; перетащить своё существо — «Убрать».</summary>
public partial class DiscardButton : Button
{
    public MatchScreen Screen = null!;
    public Side Side;

    public override bool _CanDropData(Vector2 atPosition, Variant data) =>
        Side == MatchSession.PlayerSide && Drag.Is(data, Drag.Creature, out int entity) && Screen.CanDismiss(entity);

    public override void _DropData(Vector2 atPosition, Variant data)
    {
        if (Drag.Is(data, Drag.Creature, out int entity)) Screen.Dismiss(entity);
    }
}
