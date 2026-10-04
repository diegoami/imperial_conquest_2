namespace IC2.Engine.Battle.Tactical.General;

/// <summary>
/// The computer general's placement, <c>FUN_004381A4</c>: <strong>[confirmed: code, static,
/// <c>2026-10-04-decompiled-tactical-battle-rules.md</c> §2, "AI placement"]</strong>;
/// <c>docs/game-design.md</c>, "The computer general", "Placement".
/// </summary>
/// <remarks>
/// <para>
/// It clears the side's cells and draws <c>r = Random(5)</c> (the number of rows of
/// <c>combat.tactical.placementFormations</c>). The base row is the innermost home row, 2 stepping −1 for
/// the attacker and 9 stepping +1 for the defender (<c>homeRows − 1</c> and <c>boardHeight −
/// homeRows</c>). For each block <c>g = 0 … 3</c> it takes the type <c>t = Form[r][g]</c> and the columns
/// <c>3g + 1 … 3g + 3</c>, and places every live own unit of type <c>t</c>, in slot order, filling the
/// columns and then the next row. After the third row (<c>homeRows</c>) the row is held, so a block of
/// more than nine units stacks units on occupied cells. When <c>t</c> is heavy infantry, the archers
/// follow in the same block, continuing the same counters.
/// </para>
/// <para>
/// Each placed unit's icon is painted on its cell in placement order, so on a stacked cell the last
/// placed unit's icon shows; the slot-at-cell rule (the last live slot in the scan) is T123's.
/// </para>
/// <para>
/// Two numbers are the <c>Form</c> table's geometry, not ruleset values: a block is 3 columns wide and the
/// first block starts at column 1 (report §2, <c>3g + 1 … 3g + 3</c>). <c>combat.tactical</c> carries no
/// field for them (T122's entry), so they are written here, cited. Which type the archers follow is the
/// slow advance's exempt type, <c>combat.tactical.slowAdvanceExemptType</c>, the ruleset's only field
/// naming heavy infantry (T122's entry: "heavy infantry standing for HI + Ar").
/// </para>
/// </remarks>
public static class GeneralPlacement
{
    /// <summary>A block's width: columns <c>3g + 1 … 3g + 3</c> (report §2).</summary>
    private const int BlockColumns = 3;

    /// <summary>The first block's first column: <c>3 × 0 + 1</c> (report §2).</summary>
    private const int FirstBlockColumn = 1;

    /// <summary>Places the side to move's units by formation.</summary>
    public static TacticalBattleState Place(
        TacticalBattleState state, TacticalContext context, IBattleDraws draws, IList<GeneralDecision>? journal = null)
    {
        var board = TacticalBoard.Thaw(state, context, draws);
        Apply(board, journal);
        return board.Freeze();
    }

    internal static void Apply(TacticalBoard board, IList<GeneralDecision>? journal)
    {
        var rules = board.Rules;
        var context = board.Context;
        var side = board.SideToMove;
        var first = board.FirstSlotOf(side);

        for (var s = first; s < first + board.SlotsPerSide; s++)
        {
            if (board.IsLive(s))
            {
                var unit = board.Slots[s];
                board.Grid[board.CellIndex(unit.X, unit.Y)] = TacticalIcon.Empty;
            }
        }

        var formations = rules.PlacementFormations;
        var r = board.Draw(formations.Count);

        var attacker = side == TacticalBattleState.AttackerSide;
        var baseRow = attacker ? rules.HomeRows - 1 : rules.BoardHeight - rules.HomeRows;
        var rowStep = attacker ? -1 : 1;
        var archersFollow = context.SlowAdvanceExemptType;

        var formation = formations[r];
        for (var g = 0; g < formation.Count; g++)
        {
            var type = context.TypeIndexOf(formation[g]);
            var column = 0;
            var row = 0;
            PlaceType(type);
            if (type == archersFollow && context.ArcherType >= 0)
            {
                PlaceType(context.ArcherType);
            }

            void PlaceType(int placing)
            {
                for (var s = first; s < first + board.SlotsPerSide; s++)
                {
                    var unit = board.Slots[s];
                    if (!board.IsLive(s) || unit.Type != placing)
                    {
                        continue;
                    }

                    var x = FirstBlockColumn + (BlockColumns * g) + column;
                    var y = baseRow + (rowStep * row);
                    board.Slots[s] = unit with { X = x, Y = y };
                    board.PaintIcon(s);
                    board.Log.Add(new TacticalPlacedEvent(s, unit.X, unit.Y, x, y));
                    journal?.Add(new GeneralDecision(s, GeneralStep.Placed, -1, x, y));

                    column++;
                    if (column == BlockColumns)
                    {
                        column = 0;
                        if (row < rules.HomeRows - 1)
                        {
                            row++;
                        }
                    }
                }
            }
        }
    }
}
