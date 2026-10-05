using System.Text.Json.Serialization;

namespace IC2.Engine.Battle.Tactical;

/// <summary>
/// One entry of a tactical battle's trace (<see cref="TacticalBattleState.Log"/>): every draw, placement,
/// move, exchange and rout, in the order the routines made them, so the conformance tests and the golden
/// master (T129) compare a battle half-round by half-round.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(TacticalDrawEvent), "draw")]
[JsonDerivedType(typeof(TacticalCopiedInEvent), "copiedIn")]
[JsonDerivedType(typeof(TacticalSetUpEvent), "setUp")]
[JsonDerivedType(typeof(TacticalHalfRoundEndedEvent), "halfRoundEnded")]
[JsonDerivedType(typeof(TacticalSelectedEvent), "selected")]
[JsonDerivedType(typeof(TacticalPlacedEvent), "placed")]
[JsonDerivedType(typeof(TacticalMovedEvent), "moved")]
[JsonDerivedType(typeof(TacticalMoveForfeitedEvent), "moveForfeited")]
[JsonDerivedType(typeof(TacticalTargetSetEvent), "targetSet")]
[JsonDerivedType(typeof(TacticalTargetClearedEvent), "targetCleared")]
[JsonDerivedType(typeof(TacticalShotEvent), "shot")]
[JsonDerivedType(typeof(TacticalMeleeEvent), "melee")]
[JsonDerivedType(typeof(TacticalRoutedEvent), "routed")]
[JsonDerivedType(typeof(TacticalCascadeRemovedEvent), "cascadeRemoved")]
[JsonDerivedType(typeof(TacticalSurrenderedEvent), "surrendered")]
[JsonDerivedType(typeof(TacticalBattleOverEvent), "battleOver")]
[JsonDerivedType(typeof(TacticalComputerGeneralEvent), "computerGeneral")]
[JsonDerivedType(typeof(TacticalPromotionEvent), "promotion")]
public abstract record TacticalEvent;

/// <summary>One <c>Random(n)</c> through the draw seam: its bound and the value it returned.</summary>
public sealed record TacticalDrawEvent(int Bound, int Value) : TacticalEvent;

/// <summary>Copy-in placed a live army unit in slot <paramref name="Slot"/> with its starting battle morale.</summary>
public sealed record TacticalCopiedInEvent(int Slot, int X, int Y, int Morale) : TacticalEvent;

/// <summary>A half-round setup ran for <paramref name="Side"/>; <paramref name="Counter"/> is the counter after its increment.</summary>
public sealed record TacticalSetUpEvent(int Side, int Counter, int MinDistance) : TacticalEvent;

/// <summary>A half-round ended: <paramref name="Side"/> just moved, and <paramref name="NextSide"/> is to move.</summary>
public sealed record TacticalHalfRoundEndedEvent(int Side, int NextSide, bool Placed) : TacticalEvent;

/// <summary>A human selected a slot.</summary>
public sealed record TacticalSelectedEvent(int Slot) : TacticalEvent;

/// <summary>A unit was placed on a home-row cell.</summary>
public sealed record TacticalPlacedEvent(int Slot, int FromX, int FromY, int ToX, int ToY) : TacticalEvent;

/// <summary>A unit took one movement step, spending one move.</summary>
public sealed record TacticalMovedEvent(int Slot, int FromX, int FromY, int ToX, int ToY, int MovesLeft) : TacticalEvent;

/// <summary>A computer unit gave up its last move next to a stronger enemy.</summary>
public sealed record TacticalMoveForfeitedEvent(int Slot) : TacticalEvent;

/// <summary>A unit took <paramref name="Target"/> as its melee target.</summary>
public sealed record TacticalTargetSetEvent(int Slot, int Target) : TacticalEvent;

/// <summary>A unit's melee target was cleared by an order (a right click, or a move).</summary>
public sealed record TacticalTargetClearedEvent(int Slot) : TacticalEvent;

/// <summary>
/// A shot (<c>FUN_0043910C</c>): the bound <paramref name="Bound"/> (<c>n</c>), the troop loss, the
/// morale the target lost, and the target's troops and morale after the loss (before its rout test).
/// </summary>
public sealed record TacticalShotEvent(
    int Shooter,
    int Target,
    int Bound,
    int Loss,
    int MoraleLoss,
    int TargetTroops,
    int TargetMorale) : TacticalEvent;

/// <summary>
/// A melee exchange (<c>FUN_004393EC</c>): the focus <c>f</c>, the two powers <c>A</c> and <c>D</c>,
/// the two bounds <c>nA</c> and <c>nD</c>, both losses, and both sides' troops and morale after the
/// exchange (before the rout tests).
/// </summary>
public sealed record TacticalMeleeEvent(
    int Attacker,
    int Defender,
    int Focus,
    int AttackerPower,
    int DefenderPower,
    int AttackerBound,
    int DefenderBound,
    int AttackerLoss,
    int DefenderLoss,
    int AttackerTroops,
    int DefenderTroops,
    int AttackerMorale,
    int DefenderMorale) : TacticalEvent;

/// <summary>A unit routed (<c>FUN_00438FB0</c>) and left the battle; it starts the one-level cascade.</summary>
public sealed record TacticalRoutedEvent(int Slot) : TacticalEvent;

/// <summary>A friend of a routed unit fell below the cascade threshold and was removed, with no further cascade.</summary>
public sealed record TacticalCascadeRemovedEvent(int Slot) : TacticalEvent;

/// <summary>A side surrendered: all its units removed, no cascade.</summary>
public sealed record TacticalSurrenderedEvent(int Side) : TacticalEvent;

/// <summary>The battle ended; <paramref name="Winner"/> is the winning side.</summary>
public sealed record TacticalBattleOverEvent(int Winner) : TacticalEvent;

/// <summary><em>Computer general</em> was turned on or off for a side.</summary>
public sealed record TacticalComputerGeneralEvent(int Side, bool On) : TacticalEvent;

/// <summary>The write-back's quality step for one surviving winner unit (army unit <paramref name="Unit"/>).</summary>
public sealed record TacticalPromotionEvent(int Slot, int Unit, int QualityBefore, int QualityAfter) : TacticalEvent;
