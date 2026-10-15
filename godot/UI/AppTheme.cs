using Godot;

namespace IC2.Slice.UI;

/// <summary>The single project theme: approved T168 pairing A, Bronze Night and compact scale.</summary>
public static class AppTheme
{
    public static Theme Current => GD.Load<Theme>("res://UI/AppTheme.tres");
    private static int Size(string role) => Current.GetFontSize(role, "App");
    private static int Metric(string name) => Current.GetConstant(name, "App");
    public static Color Color(string name) => Current.GetColor(name, "App");
    public static Font BodyFont => Current.DefaultFont;
    public static Font DisplayFont => Current.GetFont("font", "Button");
    public static int Title => Size("title");
    public static int Header => Size("header");
    public static int Body => Size("body");
    public static int Note => Size("note");
    public static int Button => Size("button");
    public static int Menu => Size("menu");
    public static int Spacer => Size("spacer");
    public static int PanelGap => Metric("panel_gap");
    public static int DialogGap => Metric("dialog_gap");
    public static int TableRowGap => Metric("table_row_gap");
    public static int PanelMarginSide => Metric("panel_margin_side");
    public static int PanelMarginVertical => Metric("panel_margin_vertical");
    public static int ScreenMarginSide => Metric("screen_margin_side");
    public static int ScreenMarginVertical => Metric("screen_margin_vertical");
    public static int ButtonHeight => Metric("button_height");
    public static int CornerRadius => Metric("corner_radius");
    public static Color Scrim => Color("scrim");
    public static Color NewsPanel => Color("news_panel");
}
