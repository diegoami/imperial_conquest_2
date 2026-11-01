# Barbarians: v0.9.0 design and implementation contract

Delivery: [#1683](https://github.com/diegoami/imperial_conquest_2/issues/1683).
Campaigns: [1350 BC, #1681](https://github.com/diegoami/imperial_conquest_2/issues/1681)
and [750 BC, #1682](https://github.com/diegoami/imperial_conquest_2/issues/1682).
Historical research: [#1308](https://github.com/diegoami/imperial_conquest_2/issues/1308).

## Owner decisions

On 2026-11-01 the owner placed barbarians in v0.9.0 and chose:

- Random arrivals from outside the map, entering through land or sea boundaries,
  without fixed spawning camps. Barbarian fleets are included. This replaces
  the earlier remote-interior spawning proposal.
- Capture and retention of cities, rather than only raiding or razing.
- Permanent hostility, without peace or trade.
- One AI-controlled faction within the scenario nation budget, with no required
  capital, active without cities and excluded from winning. Cities it holds
  still count for total conquest.
- The provisional seasonal tuning values described below.
- Include Italy as well as the two new campaigns.

The historical campaigns are separate settings around 1350 BC and 750 BC,
not an alternate-history collection of empires from different centuries.
Research must identify what populations or pressures a scenario's barbarian
forces represent. The label is a gameplay category, not evidence of a historical
unified nation. Spawning is an explicitly designed abstraction.

## Approved provisional tuning and remaining decisions

The owner approved one spawn check per season, a 25% chance of one new army,
and a cap of four cityless roaming armies. Scenario data specifies composition
and strength; headless runs inform tuning. Precisely define the roaming-army
cap without inventing an untracked distinction between field forces and garrisons.
Sea arrivals require a decision about transport/army counting, fleet limits and
the land/sea selection rule.

The owner rejected the proposed 10-tile city and 5-tile army exclusion distances:
entry is from outside the map instead. Represent arrival on a legal in-bounds
boundary tile; never create entities with out-of-bounds coordinates. Land
entrants require valid empty land; sea entrants require navigable sea and valid
ship/transport capacity. No legal entry means no forced spawn or overlapping
units. Define blocked-edge handling and arrival action timing explicitly.

Further gameplay decisions must be explicit: whether fleets carry landing armies
and also fight independently, whether captured towns recruit/build fleets, whether armies need
normal supply and upkeep, whether they replenish from captured towns, and
whether any capital-capture cascade applies. Do not obtain these behaviors
accidentally by treating the faction as ordinary AI.

The follow-up proposal presented to the owner is a shared seasonal arrival roll
with land/sea selection among legal entry types, transport fleets carrying
landing armies, and limits of four total live barbarian armies and four fleets.
It proposes normal supply consumption and replenishment at captured towns,
without treasury upkeep or town recruitment/fleet construction. These additions
remain proposals until answered; they do not supersede the approved tuning
automatically. Landing and ordinary naval combat must use existing commands.

## Integration findings

These observations concern the current remake, not original-game research.
Paths below are relative to `src/IC2.Engine/`.

| Area | Existing behavior | Required work |
|---|---|---|
| Initialization | `Model/GameStateFactory.cs` marks nations with zero cities eliminated. | Explicit opted-in cityless faction lifecycle. |
| Turns | `Calendar/SeatRotationSystem.cs` skips eliminated nations; `Ai/AiTurn.cs` rejects them. | Cityless barbarian turns without reviving ordinary eliminated nations. |
| Elimination | `Cities/Capture/NationElimination.cs` eliminates a nation after its last city is lost; capture can dispose of forces and reset relations. | A persistent faction must not silently lose roaming armies with its last town. |
| Capture | `Cities/Capture/CityCaptureResolver.cs` coordinates capital handling and conquest. | Explicit barbarian capture policy, including normal capitals and recaptured towns. |
| Diplomacy | Thaw, offers, AI diplomacy and post-battle treaties have separate entry points. | Enforce hostility in authoritative commands and systems, not only UI. |
| Victory | `Victory/VictoryEvaluator.cs` evaluates nation/city counts. | Explicit winner eligibility and owned-city counting for affected conditions. |
| Spawning | `Core/Pipeline/QuarterBoundary.cs` provides a seasonal hook. | Use the existing hook for seasonal arrivals; no new global phase. |
| RNG | `Core/Pipeline/TurnCoordinator.cs` derives named system/seasonal streams. | Dedicated stream and deterministic candidate order; no wall-clock RNG. |

## Implementation slices

### 1. Faction identity, lifecycle and permanent hostility

Own optional scenario/world configuration, validation, initialization, cityless
lifecycle, diplomacy and victory exceptions. Preserve worlds without the
feature. Reserve campaign roster slots after the representation decision.

Exercise public production paths: start with no barbarian cities; rotate through
its turn; capture and lose its last town; attempt peace, trade, alliance and
automatic treaties in both directions; cross a seasonal thaw; evaluate relevant
victory conditions. Normal elimination, rebirth and peace retain their controls.

### 2. Deterministic spawning and movement/capture

Own boundary-entry eligibility, cadence, chance, strength and caps, stable
army/fleet IDs, transport links and a dedicated barbarian decision path. Ordinary strategic AI's
diplomacy, capital defence, hiring and economy are not the barbarian contract.

Define target selection/ties, unreachable targets, hostile contact, sieges and
battle interruptions. Reuse authoritative movement and battle/capture commands
where applicable. Never overwrite occupancy, teleport through water, resolve a
human battle invisibly, or fabricate success after refusal. Define supply,
upkeep and town management before activating those paths.

Controls: no candidate; occupied/invalid edge tiles; land/sea eligibility;
transport capacity and reciprocal links; map bounds; cap;
chance failure; deterministic seed replay; save/load across the seasonal
boundary; consecutive seasons; tactical interruption; capture and recapture.
A disabled feature must consume no extra draws or alter existing campaigns.

### 3. Presentation and campaign acceptance

Own non-playable faction presentation, notifications and campaign integration.
Show armies and fleets on the map, explain permanent hostility, and prevent human
selection and misleading diplomatic actions. Consult relevant private manual
sections/screenshots before changing existing screens; record this designed
exception to original behavior.

Run fixed-seed headless campaigns measuring spawns, live armies, captures and
recaptures, supply survival, rejected actions and runtime. Results support tuning,
not historical parity. Italy and both new campaigns need separate data, save/load and packaged
launch acceptance. Do not interrupt the owner's game for these checks.

## Compatibility and persistence

Existing classical scenarios do not acquire barbarians implicitly. Missing
optional configuration means disabled, preserving old simulation and RNG
behavior. Validate bounds and references rather than silently repairing data.

Italy opts in for new campaigns. Persist activation explicitly so an existing
Italy save does not silently gain new mechanics merely because its installed
scenario definition has changed. Decide any existing-save opt-in separately.

Authoritative faction, spawn and ownership state must survive native save/load
and participate in state hashing and relevant snapshots. Choose optional fields
and migrations after the rules are approved. Never infer faction role from its
display name or erase it on load.

Original SAV has no representation for this feature. Existing classical export
retains its gates; the new campaigns retain non-classical export refusal. Never
export by silently dropping armies, ownership or mechanics. Tactical Undo must
restore affected state and RNG through its own contract.
