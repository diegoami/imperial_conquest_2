using IC2.Engine.Core;
using IC2.Engine.Model;

namespace IC2.Engine.Battle.Candidates;

/// <summary>
/// The original's tactical battle run headless (§6.2, C2), and C5's variant of it (§6.5), which differs
/// ONLY in what happens after a break. One engine for both, so that the scorecard can attribute any
/// C2/C5 difference to the retreat rules and not to a different driver (§6.5, "Building C5 on C2").
/// </summary>
/// <remarks>
/// <para>The exchange arithmetic is the original's (§4.4, K15–K22); the rout routine is
/// <c>FUN_00438fb0</c> with its one-level cascade (§5, K08–K14); the driver is the designed D01–D06,
/// D09–D12 placeholder set (§2.2).</para>
/// <para>Two readings this implementation had to make, both recorded as findings in the results document:
/// <list type="bullet">
/// <item><description>D04 defines only <c>N</c> as side-relative ("toward the enemy home row"). <c>E</c> is
/// taken as increasing column for BOTH sides (the grid's own axis), so the defender's compass is the
/// attacker's reflected, not rotated.</description></item>
/// <item><description>§6.5.2 keeps two separate per-round flags (<c>pursuedThisRound</c>,
/// <c>firedPursuitThisRound</c>), and its step 3/4 filters read one each; D41's summary line ("pursues or
/// shoots at most once per round") is read through the algorithm, so a light-cavalry unit (a cavalry type
/// with shots) may both pursue and fire in one round.</description></item>
/// </list></para>
/// </remarks>
internal sealed class TacticalBattle
{
    private readonly CandidateTables _tables;
    private readonly DetailedResolverRules _rules;
    private readonly IRng _rng;
    private readonly bool _retreat;
    private readonly List<CandidateEvent>? _events;
    private readonly TacticalUnit[][] _sides;
    private readonly TacticalUnit?[] _grid = new TacticalUnit?[CandidateConstants.GridColumns * CandidateConstants.GridRows];
    private int _round;
    private int _roundsFought;
    private bool _ended;
    private int _winnerSide = -1;
    private CandidateEnding _ending;
    private readonly BreakCause[] _lastRemovalCause = { BreakCause.None, BreakCause.None };

    internal TacticalBattle(CandidateBattle battle, IRng rng, bool retreat, bool recordEvents)
    {
        _tables = CandidateTables.For(battle.Ruleset);
        _rules = battle.Ruleset.Combat.DetailedResolver;
        _rng = rng;
        _retreat = retreat;
        _events = recordEvents ? new List<CandidateEvent>() : null;
        _sides = new[] { Build(battle.Attacker, 0), Build(battle.Defender, 1) };
    }

    /// <summary>Counted per event, for §8.5's stated formula when no event log is kept.</summary>
    public long StatedDraws { get; private set; }

    /// <summary>At least one K13 cascade removal.</summary>
    public bool CascadeBreak { get; private set; }

    /// <summary>Runs one battle.</summary>
    public static CandidateOutcome Run(CandidateBattle battle, IRng rng, bool retreat, bool recordEvents)
    {
        ArgumentNullException.ThrowIfNull(battle);
        ArgumentNullException.ThrowIfNull(rng);
        var counting = new DrawCountingRng(rng);
        var engine = new TacticalBattle(battle, counting, retreat, recordEvents);
        engine.Fight(battle);
        return engine.Outcome(battle, counting.Draws);
    }

    private TacticalUnit[] Build(CandidateArmy army, int side)
    {
        if (army.Units.Count > 20)
        {
            throw new ArgumentException("K31: at most 20 units per army.", nameof(army));
        }

        var units = new TacticalUnit[army.Units.Count];
        for (var i = 0; i < units.Length; i++)
        {
            var unit = army.Units[i];
            if (unit.Troops <= 0)
            {
                throw new ArgumentException("A candidate army's units must have troops > 0.", nameof(army));
            }

            var type = _tables.IndexOf(unit.UnitTypeId);
            units[i] = new TacticalUnit(side, i, type, unit.Troops, unit.Quality, _tables.Shots[type]);
        }

        return units;
    }

    private void Log(CandidateEventKind kind, int side, int slot, BreakCause cause = BreakCause.None)
    {
        var e = new CandidateEvent(kind, _round, side, slot, cause);
        StatedDraws += e.StatedDraws;
        _events?.Add(e);
    }

    private int Random(long n)
    {
        // D13: Random(0) returns 0 and consumes no draw. §3: Random(n) is rng.NextInt(0, n).
        if (n <= 0)
        {
            return 0;
        }

        return _rng.NextInt(0, (int)Math.Min(n, int.MaxValue));
    }

    private void Fight(CandidateBattle battle)
    {
        Setup(battle);
        Rounds();
    }

    /// <summary>§6.2's setup: the seeded tactical morale (K27, K26, D08), the shot pools, and placement (D01).</summary>
    internal void Setup(CandidateBattle battle)
    {
        // Setup, §6.2: K27's placeholder +3 to BOTH sides, seam-local, used only to seed m (not written back).
        var seedMorale = new[]
        {
            battle.Attacker.Morale + CandidateConstants.BattleEntryMoraleBonus,
            battle.Defender.Morale + CandidateConstants.BattleEntryMoraleBonus,
        };

        for (var side = 0; side < 2; side++)
        {
            foreach (var u in _sides[side])
            {
                // K26 with D08's confirmed [60, 90] clamp: min(90, ·) then max(60, ·).
                var drawn = Random((long)u.Quality * CandidateConstants.InitialMoraleQualityMultiplier);
                Log(CandidateEventKind.MoraleSeed, side, u.Slot);
                u.Morale = Math.Max(
                    CandidateConstants.InitialMoraleMin,
                    Math.Min(CandidateConstants.InitialMoraleMax, drawn + seedMorale[side]));
            }
        }

        Place(); // D01
    }

    private void Rounds()
    {
        for (_round = 1; _round <= CandidateConstants.TacticalRoundCap && !_ended; _round++) // D09
        {
            if (_retreat)
            {
                ResetPursuitFlags(0);
                ResetPursuitFlags(1);
            }

            for (var side = 0; side < 2 && !_ended; side++) // D02: attacker first; K33 alternation
            {
                SideTurn(side);
            }

            if (_ended)
            {
                break;
            }

            if (_retreat && _round >= CandidateConstants.C5EarliestWithdrawalRound)
            {
                // §6.5.3: end of every round from round 3, attacker tested first, then defender.
                for (var side = 0; side < 2 && !_ended; side++)
                {
                    if (LiveP(side) * 100 < CandidateConstants.C5WithdrawalPercent * LiveP(1 - side))
                    {
                        Withdraw(side, CandidateEnding.Withdrawal);
                    }
                }
            }

            if (!_ended && _round == CandidateConstants.TacticalRoundCap)
            {
                ResolveCap();
            }
        }
    }

    private void Place()
    {
        // D01: slot order from column 0 along the home row; units 15–20 in the next row inward.
        for (var side = 0; side < 2; side++)
        {
            var home = side == 0 ? CandidateConstants.AttackerHomeRow : CandidateConstants.DefenderHomeRow;
            var inward = side == 0 ? 1 : -1;
            foreach (var u in _sides[side])
            {
                var row = home + (inward * (u.Slot / CandidateConstants.UnitsPerHomeRow));
                var column = u.Slot % CandidateConstants.UnitsPerHomeRow;
                u.X = column;
                u.Y = row;
                _grid[Index(column, row)] = u;
            }
        }
    }

    private static int Index(int x, int y) => (y * CandidateConstants.GridColumns) + x;

    private static int Chebyshev(int x1, int y1, int x2, int y2) => Math.Max(Math.Abs(x1 - x2), Math.Abs(y1 - y2));

    private static int Chebyshev(TacticalUnit a, TacticalUnit b) => Chebyshev(a.X, a.Y, b.X, b.Y);

    private void SideTurn(int side)
    {
        var queued = new List<TacticalUnit>();
        foreach (var u in _sides[side])
        {
            if (!u.Live)
            {
                continue;
            }

            if (u.SkipNextAction)
            {
                // §6.5.2 step 3: a cavalry pursuer skips its next D05 action.
                u.SkipNextAction = false;
                continue;
            }

            if (u.Target is null || !u.Target.Live)
            {
                u.Target = PickTarget(u); // D03
            }

            var target = u.Target!;
            var distance = Chebyshev(u, target);
            var range = _tables.Range[u.Type];

            if (distance == 1)
            {
                queued.Add(u); // D05 (a)
            }
            else if (u.ShotsLeft > 0 && range > 0 && distance <= range + 1)
            {
                Shoot(u, target, distance); // D05 (b), D11
                if (_ended)
                {
                    return;
                }
            }
            else
            {
                Move(u, target); // D05 (c), D04
                if (Chebyshev(u, target) == 1)
                {
                    queued.Add(u);
                }
            }
        }

        // D05: the melee pass runs FUN_004393ec once over the queued attackers in slot order [confirmed].
        foreach (var u in queued)
        {
            if (!u.Live || u.Target is not { Live: true } target || Chebyshev(u, target) != 1)
            {
                continue;
            }

            Melee(u, target);
            if (_ended)
            {
                return;
            }
        }
    }

    private TacticalUnit PickTarget(TacticalUnit u)
    {
        // D03: nearest live enemy by Chebyshev distance, ties to the lowest enemy slot.
        TacticalUnit? best = null;
        var bestDistance = int.MaxValue;
        foreach (var e in _sides[1 - u.Side])
        {
            if (!e.Live)
            {
                continue;
            }

            var d = Chebyshev(u, e);
            if (d < bestDistance)
            {
                best = e;
                bestDistance = d;
            }
        }

        return best ?? throw new InvalidOperationException("K32 ends the battle before a side runs out of targets.");
    }

    private void Move(TacticalUnit u, TacticalUnit target)
    {
        // D04. N is toward the enemy home row; E is increasing column for both sides (see remarks).
        var forward = u.Side == 0 ? 1 : -1;
        ReadOnlySpan<(int Dx, int Dy)> compass = stackalloc (int, int)[]
        {
            (0, forward), (1, forward), (1, 0), (1, -forward), (0, -forward), (-1, -forward), (-1, 0), (-1, forward),
        };

        for (var step = 0; step < _tables.Moves[u.Type]; step++)
        {
            var current = Chebyshev(u, target);
            if (current <= 1)
            {
                return;
            }

            var bestX = -1;
            var bestY = -1;
            var bestDistance = current;
            foreach (var (dx, dy) in compass)
            {
                var nx = u.X + dx;
                var ny = u.Y + dy;
                if (nx < 0 || ny < 0 || nx >= CandidateConstants.GridColumns || ny >= CandidateConstants.GridRows
                    || _grid[Index(nx, ny)] is not null)
                {
                    continue;
                }

                var d = Chebyshev(nx, ny, target.X, target.Y);
                if (d < bestDistance)
                {
                    bestDistance = d;
                    bestX = nx;
                    bestY = ny;
                }
            }

            if (bestX < 0)
            {
                return; // no step reduces the distance
            }

            _grid[Index(u.X, u.Y)] = null;
            u.X = bestX;
            u.Y = bestY;
            _grid[Index(bestX, bestY)] = u;
        }
    }

    private long ShotBase(TacticalUnit s, int targetType) =>
        // K21 with K20: ts × qs × ms × vuln[type_t] / (ts × 5 + 150000)
        s.Troops * s.Quality * s.Morale * _tables.Vulnerability[targetType]
        / ((s.Troops * CandidateConstants.ShotTroopMultiplier) + CandidateConstants.ShotDenominatorConstant);

    private void Shoot(TacticalUnit s, TacticalUnit t, int distance)
    {
        var shotBase = ShotBase(s, t.Type);
        if (distance - 1 < _tables.Range[s.Type])
        {
            shotBase *= _rules.InRangeShotMultiplier; // D06's placeholder reading of the K21 doubling
        }

        var r = Math.Min(
                    Math.Min(s.Troops / CandidateConstants.ShotShooterDivisor, t.Troops / CandidateConstants.ShotTargetDivisor),
                    shotBase)
                + 1;
        long loss = Random(r);
        loss += Random(r);
        Log(CandidateEventKind.Shot, s.Side, s.Slot);
        loss = Math.Min(loss, t.Troops); // D14
        t.Troops -= loss;
        s.ShotsLeft--;
        var hit = Math.Min(CandidateConstants.ShotMoraleCap, loss * CandidateConstants.ShotMoraleMultiplier / (t.Troops + 1)); // K22
        t.Morale = ClampMorale(t.Morale - (int)hit);
        RoutCheck(t);
    }

    private void Melee(TacticalUnit a, TacticalUnit d)
    {
        // §4.4, FUN_004393ec.
        var focus = 0;
        foreach (var f in _sides[a.Side])
        {
            if (f.Live && ReferenceEquals(f.Target, d))
            {
                focus++; // D12: includes the attacker itself
            }
        }

        long dF = Math.Min(CandidateConstants.FocusCap, focus); // K17

        var atkPow = (_tables.Matrix[a.Type, d.Type] * a.Troops * ((a.Quality * CandidateConstants.QualityWeight) + a.Morale)
                      / _rules.MeleePowerDivisor) + _rules.MeleeBasePowerFloor;
        var defPow = (_tables.Matrix[d.Type, a.Type] * d.Troops * ((d.Quality * CandidateConstants.QualityWeight) + d.Morale)
                      / _rules.MeleePowerDivisor) + _rules.MeleeBasePowerFloor;

        var exA = (a.Troops * defPow / atkPow / CandidateConstants.MeleeAttackerExchangeDivisor) + 1;
        long drawA = Random(exA);
        drawA += Random(exA);
        var lossA = Math.Min(
                        Math.Min(_rules.MeleeLossHardCap, drawA * (CandidateConstants.FocusBase - dF) / CandidateConstants.FocusBase),
                        a.Troops * _rules.MeleeLossCapPercent / 100)
                    + _rules.MeleeLossCapOffset;

        var exD = (d.Troops * atkPow / defPow / CandidateConstants.MeleeDefenderExchangeDivisor) + 1;
        long drawD = Random(exD);
        drawD += Random(exD);
        var lossD = Math.Min(
                        Math.Min(_rules.MeleeLossHardCap, drawD * ((2 * dF) + CandidateConstants.FocusBase) / CandidateConstants.FocusBase),
                        d.Troops * _rules.MeleeLossCapPercent / 100)
                    + _rules.MeleeLossCapOffset;
        Log(CandidateEventKind.MeleeExchange, a.Side, a.Slot);

        a.Troops -= lossA;
        d.Troops -= lossD;

        // K19 with D07's placeholder: the attacker is the better side when atkPow >= defPow.
        if (atkPow >= defPow)
        {
            a.Morale = ClampMorale(a.Morale + CandidateConstants.MeleeMoraleGain);
            d.Morale = ClampMorale(d.Morale - CandidateConstants.MeleeMoraleLoss);
        }
        else
        {
            a.Morale = ClampMorale(a.Morale - CandidateConstants.MeleeMoraleLoss);
            d.Morale = ClampMorale(d.Morale + CandidateConstants.MeleeMoraleGain);
        }

        RoutCheck(a); // D10: attacker first
        if (!_ended && d.Live)
        {
            RoutCheck(d);
        }
    }

    private static int ClampMorale(int m) =>
        Math.Max(CandidateConstants.TacticalMoraleFloor, Math.Min(CandidateConstants.TacticalMoraleCap, m)); // D08

    /// <summary>For <see cref="TacticalProbe"/>: one unit's state.</summary>
    internal TacticalUnit UnitAt(int side, int slot) => _sides[side][slot];

    /// <summary>For <see cref="TacticalProbe"/>: whether K32 ended the battle, and who won.</summary>
    internal (bool Ended, int WinnerSide) State => (_ended, _winnerSide);

    /// <summary>For <see cref="TacticalProbe"/>: the event log (when recorded).</summary>
    internal IReadOnlyList<CandidateEvent> Events => (IReadOnlyList<CandidateEvent>?)_events ?? Array.Empty<CandidateEvent>();

    /// <summary>For <see cref="TacticalProbe"/>: runs §5's routine on one unit.</summary>
    internal void RoutCheckAt(int side, int slot) => RoutCheck(_sides[side][slot]);

    private void RoutCheck(TacticalUnit x)
    {
        // §5, FUN_00438fb0 [confirmed].
        if (!x.Live)
        {
            return;
        }

        var floor = _tables.RoutFloor[x.Type];
        if (x.Troops >= floor && x.Morale > CandidateConstants.RoutMoraleFloor)
        {
            if (x.Morale > CandidateConstants.RoutSafeMorale)
            {
                return;
            }

            var band = Random(x.Morale) + Random(x.Morale);
            Log(CandidateEventKind.BandCheck, x.Side, x.Slot);
            if (band > CandidateConstants.RoutBandThreshold)
            {
                return;
            }
        }

        var cause = x.Troops < floor
            ? BreakCause.StrengthFloor
            : x.Morale <= CandidateConstants.RoutMoraleFloor ? BreakCause.MoraleFloor : BreakCause.Band;

        // Step 1: X is removed (C2: FUN_00438f78; C5: Flee(X, ordered = false), §6.5.1).
        Remove(x, cause, ordered: false);

        // Step 2: each surviving friend, in slot order: m −= 6; below 30 removed with NO cascade of its own.
        foreach (var f in _sides[x.Side])
        {
            if (!f.Live)
            {
                continue;
            }

            f.Morale = ClampMorale(f.Morale - CandidateConstants.CascadeMoraleLoss);
            if (f.Morale < CandidateConstants.CascadeRemovalBelow)
            {
                CascadeBreak = true;
                Remove(f, BreakCause.Cascade, ordered: false);
            }
        }

        // Step 3: every live enemy +5 (capped at 99), and clears its target if it was X (X only, K14).
        foreach (var e in _sides[1 - x.Side])
        {
            if (!e.Live)
            {
                continue;
            }

            e.Morale = Math.Min(CandidateConstants.TacticalMoraleCap, e.Morale + CandidateConstants.RoutReward);
            if (ReferenceEquals(e.Target, x))
            {
                e.Target = null;
            }
        }

        // Step 4: the battle-end test (K32).
        EndTest();
    }

    private void Remove(TacticalUnit x, BreakCause cause, bool ordered)
    {
        x.Live = false;
        x.Cause = cause;
        _grid[Index(x.X, x.Y)] = null;
        _lastRemovalCause[x.Side] = cause;
        Log(CandidateEventKind.Break, x.Side, x.Slot, cause);

        if (_retreat)
        {
            Flee(x, ordered);
        }
        else
        {
            x.Troops = 0; // C2: FUN_00438f78, troops 0
        }
    }

    private void Flee(TacticalUnit x, bool ordered)
    {
        // §6.5.2. Step 1 (square cleared, not live) is done by Remove.
        x.Fled = true;
        var t = x.Troops;
        t -= t * CandidateConstants.C5DisorderPercent / 100; // D40, always paid

        // Step 3: cavalry pursuit, the first up-to-2 live enemy cavalry units not yet pursuing this round.
        var k = 0;
        foreach (var j in _sides[1 - x.Side])
        {
            if (k == CandidateConstants.C5MaxPursuers)
            {
                break;
            }

            if (!j.Live || !_tables.IsCavalry[j.Type] || j.PursuedThisRound)
            {
                continue;
            }

            k++;
            var atkPow = (_tables.Matrix[j.Type, x.Type] * j.Troops * ((j.Quality * CandidateConstants.QualityWeight) + j.Morale)
                          / _rules.MeleePowerDivisor) + _rules.MeleeBasePowerFloor;
            var defPow = (_tables.Matrix[x.Type, j.Type] * t * ((x.Quality * CandidateConstants.QualityWeight) + x.Morale)
                          / _rules.MeleePowerDivisor) + _rules.MeleeBasePowerFloor;
            var exD = (t * atkPow / defPow / CandidateConstants.MeleeDefenderExchangeDivisor) + 1;
            long draw = Random(exD);
            draw += Random(exD);
            Log(CandidateEventKind.PursuitHit, j.Side, j.Slot);
            var loss = Math.Min(
                           Math.Min(_rules.MeleeLossHardCap, draw * ((2 * k) + CandidateConstants.FocusBase) / CandidateConstants.FocusBase),
                           t * _rules.MeleeLossCapPercent / 100)
                       + _rules.MeleeLossCapOffset;
            if (ordered)
            {
                loss /= CandidateConstants.C5OrderedDiscountDivisor; // D42
            }

            t -= Math.Min(t, loss);
            j.PursuedThisRound = true;
            j.SkipNextAction = true;
        }

        // Step 4: pursuing fire, the first up-to-2 live enemy shooters not yet firing in pursuit this round.
        var shots = 0;
        foreach (var s in _sides[1 - x.Side])
        {
            if (shots == CandidateConstants.C5MaxPursuingShots)
            {
                break;
            }

            if (!s.Live || s.ShotsLeft <= 0 || _tables.Range[s.Type] <= 0 || s.FiredPursuitThisRound)
            {
                continue;
            }

            shots++;
            var shotBase = ShotBase(s, x.Type); // un-doubled: X has left the grid
            var r = Math.Min(
                        Math.Min(s.Troops / CandidateConstants.ShotShooterDivisor, t / CandidateConstants.ShotTargetDivisor),
                        shotBase)
                    + 1;
            long loss = Random(r);
            loss += Random(r);
            Log(CandidateEventKind.PursuitShot, s.Side, s.Slot);
            s.ShotsLeft--;
            if (ordered)
            {
                loss /= CandidateConstants.C5OrderedDiscountDivisor; // D42
            }

            t -= Math.Min(t, loss);
            s.FiredPursuitThisRound = true;
        }

        x.FledTroops = t; // step 5; 0 means destroyed in flight
        x.Troops = t;
    }

    private void ResetPursuitFlags(int side)
    {
        foreach (var u in _sides[side])
        {
            u.PursuedThisRound = false;
            u.FiredPursuitThisRound = false;
        }
    }

    private void Withdraw(int side, CandidateEnding ending)
    {
        // §6.5.3: reset the enemy's pursuit capacity, then every live unit flees in order, slot order.
        // No −6 cascade and no +5 reward: the units leave together, and nobody breaks.
        ResetPursuitFlags(1 - side);
        foreach (var u in _sides[side])
        {
            if (u.Live)
            {
                Remove(u, BreakCause.Withdrawal, ordered: true);
            }
        }

        Finish(1 - side, ending);
    }

    private void ResolveCap()
    {
        var attackerP = LiveP(0);
        var defenderP = LiveP(1);
        if (_retreat)
        {
            // §6.5.5: the side with the lower liveP withdraws in order (ties: the attacker withdraws).
            Withdraw(defenderP < attackerP ? 1 : 0, CandidateEnding.Cap);
            return;
        }

        // D09: the greater liveP wins, ties to the defender (K05); the loser's live units are destroyed.
        var winner = defenderP < attackerP ? 0 : 1;
        foreach (var u in _sides[1 - winner])
        {
            if (u.Live)
            {
                Remove(u, BreakCause.Cap, ordered: false);
            }
        }

        Finish(winner, CandidateEnding.Cap);
    }

    private long LiveP(int side)
    {
        // D34: Σ over live units of combatPowerWeight × troops.
        long total = 0;
        foreach (var u in _sides[side])
        {
            if (u.Live)
            {
                total += _tables.Weight[u.Type] * u.Troops;
            }
        }

        return total;
    }

    private bool HasLive(int side)
    {
        foreach (var u in _sides[side])
        {
            if (u.Live)
            {
                return true;
            }
        }

        return false;
    }

    private void EndTest()
    {
        var attackerLive = HasLive(0);
        var defenderLive = HasLive(1);
        if (attackerLive && defenderLive)
        {
            return;
        }

        // K32. If both sides reach zero in the same step, the defender wins (K05, §6.5.5).
        var winner = attackerLive ? 0 : 1;
        var cause = _lastRemovalCause[1 - winner];
        var ending = cause is BreakCause.StrengthFloor ? CandidateEnding.Annihilation : CandidateEnding.Collapse;
        Finish(winner, ending);
    }

    private void Finish(int winnerSide, CandidateEnding ending)
    {
        _ended = true;
        _roundsFought = _round;
        _winnerSide = winnerSide;
        _ending = ending;
    }

    private CandidateOutcome Outcome(CandidateBattle battle, long counted)
    {
        var winner = _sides[_winnerSide];
        var loser = _sides[1 - _winnerSide];
        var after = new int[winner.Length];
        foreach (var u in winner)
        {
            // Live units keep their troops; C2's routed units are 0; C5's fled units rejoin (D45).
            after[u.Slot] = (int)(u.Live ? u.Troops : _retreat ? u.FledTroops : 0);
        }

        var survivors = new int[loser.Length];
        if (battle.OnDefeat == DefeatOutcome.Scatter)
        {
            foreach (var u in loser)
            {
                // C2: none, by construction. C5: the loser's fled units with fledTroops > 0 (§6.5.5).
                survivors[u.Slot] = (int)(_retreat && u.Fled ? u.FledTroops : 0);
            }
        }

        return new CandidateOutcome(
            _winnerSide == 0 ? BattleSide.Attacker : BattleSide.Defender,
            after,
            survivors,
            _ending,
            counted,
            counted,
            StatedDraws,
            CascadeBreak,
            _roundsFought,
            (IReadOnlyList<CandidateEvent>?)_events ?? Array.Empty<CandidateEvent>());
    }

    internal sealed class TacticalUnit
    {
        public TacticalUnit(int side, int slot, int type, long troops, int quality, int shots)
        {
            Side = side;
            Slot = slot;
            Type = type;
            Troops = troops;
            Quality = quality;
            ShotsLeft = shots;
        }

        public int Side { get; }

        public int Slot { get; }

        public int Type { get; }

        public long Troops { get; set; }

        public int Quality { get; }

        public int Morale { get; set; }

        public int ShotsLeft { get; set; }

        public int X { get; set; }

        public int Y { get; set; }

        public bool Live { get; set; } = true;

        public bool Fled { get; set; }

        public long FledTroops { get; set; }

        public BreakCause Cause { get; set; }

        public TacticalUnit? Target { get; set; }

        public bool SkipNextAction { get; set; }

        public bool PursuedThisRound { get; set; }

        public bool FiredPursuitThisRound { get; set; }
    }
}
