using Godot;

namespace Game.View;

/// <summary>Оформление: цвета и размеры интерфейса. Это не правила игры — константы раздела 3 тут не дублируются.</summary>
public static class UiTheme
{
    public static readonly Color Background = new(0.10f, 0.11f, 0.14f);
    public static readonly Color Panel = new(0.16f, 0.17f, 0.21f);
    public static readonly Color PanelEnemy = new(0.20f, 0.14f, 0.15f);
    public static readonly Color Card = new(0.24f, 0.26f, 0.32f);
    public static readonly Color CardPlayable = new(0.38f, 0.70f, 0.42f);
    public static readonly Color CardBack = new(0.30f, 0.22f, 0.40f);
    public static readonly Color Border = new(0.35f, 0.37f, 0.44f);
    public static readonly Color Target = new(0.95f, 0.80f, 0.25f);
    public static readonly Color Attacker = new(0.30f, 0.75f, 1.00f);
    public static readonly Color Hit = new(1.00f, 0.35f, 0.30f);
    public static readonly Color Good = new(0.55f, 0.90f, 0.55f);
    public static readonly Color Muted = new(0.65f, 0.67f, 0.72f);

    public static readonly Vector2 CreatureSize = new(118, 128);
    public static readonly Vector2 HandCardSize = new(132, 176);
    public static readonly Vector2 SlotSize = new(118, 46);
    public const int SmallFont = 12;
    public const int NormalFont = 14;
    public const int BigFont = 20;

    public static StyleBoxFlat Box(Color bg, Color border, int borderWidth = 2)
    {
        var sb = new StyleBoxFlat { BgColor = bg, BorderColor = border };
        sb.SetBorderWidthAll(borderWidth);
        sb.SetCornerRadiusAll(6);
        sb.SetContentMarginAll(6);
        return sb;
    }

    public static Label Text(string text, int size = NormalFont, Color? color = null, bool wrap = false)
    {
        var l = new Label { Text = text, MouseFilter = Control.MouseFilterEnum.Ignore };
        l.AddThemeFontSizeOverride("font_size", size);
        if (color is Color c) l.AddThemeColorOverride("font_color", c);
        if (wrap) l.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        return l;
    }
}
