# Barbarians: v0.9.0 design and implementation contract

Delivery: [#1683](https://github.com/diegoami/imperial_conquest_2/issues/1683).
Campaigns: [1350 BC, #1681](https://github.com/diegoami/imperial_conquest_2/issues/1681)
and [750 BC, #1682](https://github.com/diegoami/imperial_conquest_2/issues/1682).
Historical research: [#1308](https://github.com/diegoami/imperial_conquest_2/issues/1308).

## Owner decisions

On 2026-11-01 the owner placed barbarians in v0.9.0 and chose:

- Random appearance in remote areas, without fixed spawning camps.
- Capture and retention of cities, rather than only raiding or razing.
- Permanent hostility, without peace or trade.

The historical campaigns are separate settings around 1350 BC and 750 BC,
not an alternate-history collection of empires from different centuries.
Research must identify what populations or pressures a scenario's barbarian
forces represent. The label is a gameplay category, not evidence of a historical
unified nation. Spawning is an explicitly designed abstraction.

## Proposal requiring owner decisions

Three questions were presented to the owner before implementation:

1. Use one AI-controlled faction within the 16-slot scenario budget, with no
   required capital, remaining active without cities. It cannot win the
   campaign; its occupied cities still count for total conquest. Regional
   factions are the alternative.
2. Initially check spawning once per season, with a 25% chance of one new army
   and a cap of four cityless roaming armies. Scenario data specifies army
   strength; headless runs inform tuning. Precisely define the roaming-army cap
   without inventing an untracked distinction between field forces and garrisons.
3. Initially require valid empty land at least 10 tiles from every city and
   5 tiles from every non-barbarian army. No legal candidate means no spawn.
   Specify the distance metric in the implementation contract; tile distances
   are not historical distances in kilometres.

These are proposals, not approved defaults. Further gameplay decisions must be
explicit: whether captured towns recruit/build fleets, whether armies need
normal supply and upkeep, whether they replenish from captured towns, and
whether any capital-capture cascade applies. Do not obtain these behaviors
accidentally by treating the faction as ordinary AI.

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
| Spawning | `Core/Pipeline/QuarterBoundary.cs` provides a seasonal hook. | Use the existing hook if seasonal spawning is approved; no new global phase. |
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

Own approved spawn eligibility, cadence, chance, strength and caps, stable IDs
and ordering, and a dedicated barbarian decision path. Ordinary strategic AI's
diplomacy, capital defence, hiring and economy are not the barbarian contract.

Define target selection/ties, unreachable targets, hostile contact, sieges and
battle interruptions. Reuse authoritative movement and battle/capture commands
where applicable. Never overwrite occupancy, teleport through water, resolve a
human battle invisibly, or fabricate success after refusal. Define supply,
upkeep and town management before activating those paths.

Controls: no candidate; occupied/water/invalid tiles; distance boundaries; cap;
chance failure; deterministic seed replay; save/load across the seasonal
boundary; consecutive seasons; tactical interruption; capture and recapture.
A disabled feature must consume no extra draws or alter existing campaigns.

### 3. Presentation and campaign acceptance

Own non-playable faction presentation, notifications and campaign integration.
Show the faction on the map, explain permanent hostility, and prevent human
selection and misleading diplomatic actions. Consult relevant private manual
sections/screenshots before changing existing screens; record this designed
exception to original behavior.

Run fixed-seed headless campaigns measuring spawns, live armies, captures and
recaptures, supply survival, rejected actions and runtime. Results support tuning,
not historical parity. Both campaigns need separate data, save/load and packaged
launch acceptance. Do not interrupt the owner's game for these checks.

## Compatibility and persistence

Existing classical scenarios do not acquire barbarians implicitly. Missing
optional configuration means disabled, preserving old simulation and RNG
behavior. Validate bounds and references rather than silently repairing data.

Authoritative faction, spawn and ownership state must survive native save/load
and participate in state hashing and relevant snapshots. Choose optional fields
and migrations after the rules are approved. Never infer faction role from its
display name or erase it on load.

Original SAV has no representation for this feature. Existing classical export
retains its gates; the new campaigns retain non-classical export refusal. Never
export by silently dropping armies, ownership or mechanics. Tactical Undo must
restore affected state and RNG through its own contract.
