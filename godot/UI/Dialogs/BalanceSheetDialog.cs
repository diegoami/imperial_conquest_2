using Godot;
using IC2.Engine.Economy;
using IC2.Engine.Presentation;

namespace IC2.Slice.UI;

/// <summary>
/// The Strategy menu's <strong>Balance sheet</strong> dialog — the original's <c>TBalanceSheet</c>
/// <strong>[confirmed: <c>docs/investigations/original-ui-command-audit.md</c> §1.3; decompiled
/// <c>TBalanceSheet_PaintBalance</c> <c>0x0045376C</c>]</strong>. T104's read-only record with the
/// original's own row captions and order, OK only.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Every figure is <see cref="BalanceSheet.For"/>.</strong> The dialog reads each line from
/// the engine's own projection — never a restated formula — so a ruleset change moves the dialog and
/// the engine's quarterly credit together (T104's own design, see
/// <c>docs/tasks/T104.md</c>).
/// </para>
/// <para>
/// <strong>The captions are the original's, the terms are the engine's.</strong> The original's
/// form spells each line in a player-facing way ("Taxes" / "Tribute" / "Trade", "Administration" /
/// "Fleets" / "Army recruits" / "Regular units" / "Mercenary units"); the engine's terms are
/// <c>TaxIncome</c>, <c>TaxBaseQuarterShare</c>, <c>TradeIncome</c>, <c>CityAndWealthUpkeep</c>,
/// <c>ShipUpkeep</c>, <c>RecruitmentSlotUpkeep</c>, <c>RegularsUpkeep</c> and
/// <c>MercenariesPay</c>. The dialog maps one to the other in the table below, and the term names
/// stay the engine's in the code and comments
/// <strong>[derived: code, <c>TBalanceSheet_PaintBalance</c> <c>@ 0x0045376C</c>; Wine candidate:
/// Rome at 0720, every line matching;
/// <c>2026-10-05-balance-sheet-tribute-line.md</c>, research <c>9ae8924</c>]</strong>.
/// </para>
/// <list type="bullet">
/// <item><description><em>Revenue</em>: <em>Taxes</em> is <c>TaxIncome</c>
/// (<c>taxBase × rate / 100</c>), <em>Tribute</em> is <c>TaxBaseQuarterShare</c>
/// (<c>taxBase / 4</c>, independent of the tax rate), <em>Trade</em> is <c>TradeIncome</c>
/// (<c>taxBase / 12</c> per trade or alliance partner), <em>Total</em> is the revenue total.</description></item>
/// <item><description><em>Expenditure</em>: <em>Administration</em> is
/// <c>CityAndWealthUpkeep</c> (<c>wealth / 20000 + cities × 7</c>), <em>Fleets</em> is
/// <c>ShipUpkeep</c>, <em>Army recruits</em> is <c>RecruitmentSlotUpkeep</c>,
/// <em>Regular units</em> is <c>RegularsUpkeep</c>, <em>Mercenary units</em> is
/// <c>MercenariesPay</c>, <em>Total</em> is the expenditure total.</description></item>
/// <item><description><em>Balance</em>: the treasury, read live.
/// <em>Debt limit</em>: the magnitude of the wealth-based debt line
/// (<see cref="IC2.Engine.Economy.Deposition.DebtLimit"/>).</description></item>
/// </list>
/// <para>
/// <strong>Mercenaries' pay is shown but is not part of the expenditure total.</strong>
/// <see cref="BalanceSheet.ExpenditureTotal"/> excludes it by design: mercenary pay is charged to
/// the army's own purse, not the nation's treasury, so it does not move the treasury and would not
/// reconcile with a quarter's treasury change. The dialog lists the line because the original lists
/// it; the engine's balance sheet records it because the same shared function backs both.
/// </para>
/// </remarks>
public partial class BalanceSheetDialog : Control
{
    /// <summary>The session the dialog reads its figures from.</summary>
    public required GameSession Session { get; init; }

    /// <summary>Raised by OK; <see cref="MainGameScreen"/> closes the overlay.</summary>
    public event Action? Closed;

    private BalanceSheet _sheet = null!;
    private Button _okButton = null!;

    /// <summary>The projected balance sheet, exposed for the headless check.</summary>
    public BalanceSheet SheetForCheck => _sheet;

    public override void _Ready()
    {
        _sheet = BalanceSheet.For(Session.State, Session.State.ActiveNationId, Session.Ruleset);

        var backdrop = new ColorRect { Color = new Color(0f, 0f, 0f, 0.6f), MouseFilter = MouseFilterEnum.Stop };
        backdrop.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(backdrop);

        var center = new CenterContainer();
        center.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(center);

        var panel = UiKit.MakePanel(UiKit.PanelColor);
        panel.CustomMinimumSize = new Vector2(360, 0);
        center.AddChild(panel);

        var column = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        column.AddThemeConstantOverride("separation", 6);
        panel.AddChild(column);

        column.AddChild(UiKit.MakeLabel(StrategyDialogModels.BalanceSheetTitle, 18, UiKit.AccentColor));

        column.AddChild(UiKit.MakeLabel("Revenue", 14, UiKit.MutedTextColor));
        AddLine(column, "Taxes", _sheet.TaxIncome);
        AddLine(column, "Tribute", _sheet.TaxBaseQuarterShare);
        AddLine(column, "Trade", _sheet.TradeIncome);
        AddLine(column, "Total", _sheet.IncomeTotal, isTotal: true);

        column.AddChild(new HSeparator());

        column.AddChild(UiKit.MakeLabel("Expenditure", 14, UiKit.MutedTextColor));
        AddLine(column, "Administration", _sheet.CityAndWealthUpkeep);
        AddLine(column, "Fleets", _sheet.ShipUpkeep);
        AddLine(column, "Army recruits", _sheet.RecruitmentSlotUpkeep);
        AddLine(column, "Regular units", _sheet.RegularsUpkeep);
        AddLine(column, "Mercenary units", _sheet.MercenariesPay);
        AddLine(column, "Total", _sheet.ExpenditureTotal, isTotal: true);

        column.AddChild(new HSeparator());

        AddLine(column, "Balance", _sheet.Treasury, isTotal: true);
        AddLine(column, "Debt limit", _sheet.DebtLimit);

        var buttons = new HBoxContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            Alignment = BoxContainer.AlignmentMode.End,
        };
        buttons.AddThemeConstantOverride("separation", 10);
        column.AddChild(buttons);
        _okButton = UiKit.MakeButton("OK", Ok);
        buttons.AddChild(_okButton);
    }

    private static void AddLine(VBoxContainer column, string caption, int value, bool isTotal = false)
    {
        var row = new HBoxContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        var label = new Label
        {
            Text = caption,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        label.AddThemeFontSizeOverride("font_size", isTotal ? 15 : 14);
        label.AddThemeColorOverride("font_color", isTotal ? UiKit.AccentColor : UiKit.TextColor);
        row.AddChild(label);

        var valueLabel = new Label
        {
            Text = value.ToString(System.Globalization.CultureInfo.InvariantCulture),
        };
        valueLabel.AddThemeFontSizeOverride("font_size", isTotal ? 15 : 14);
        valueLabel.AddThemeColorOverride("font_color", isTotal ? UiKit.AccentColor : UiKit.TextColor);
        row.AddChild(valueLabel);

        column.AddChild(row);
    }

    /// <summary>The dialog's OK, exactly as its button does.</summary>
    public void OkForCheck() => Ok();

    private void Ok() => Closed?.Invoke();

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventKey { Pressed: true, Keycode: Key.Escape })
        {
            Ok();
            GetViewport().SetInputAsHandled();
        }
    }
}
