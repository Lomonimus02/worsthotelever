# Full MVP: command-driven long-run scenario

Status: implemented; **native execution pending in the parent integration task**. This checkout has not run Unity, the game, a compiler, or a mock simulation. It contains only the two new test/helper C# files, their metadata and this report. Base: `100a358b72847d08427e8fdc6973f4104050aa49`.

## Entry points and ownership

- `WorstHotel.HotelMvpLongRunTests.RunAll(): List<string>` is the Editor suite entry point. It throws on the first failed invariant and returns daily diagnostics plus pass descriptions only after successful completion.
- `WorstHotel.HotelMvpScenario.AdvanceDays(HotelSimulation sim, int count): List<string>` is a pure runtime test driver with no startup hook, `MonoBehaviour`, default save path, file IO or production input integration.
- The overload `AdvanceDays(sim, count, Evidence evidence, bool buyUpgrades = true)` accumulates assertions/counters across model resumes. Disabling purchases is used only to isolate debt recovery.
- `AssertCheckpoint(HotelState state): string` validates a world without modifying it and returns size/count/RNG diagnostics. `AssertTenDayCoverage(state, evidence)` checks the accumulated main-run coverage.

`AdvanceDays` accepts an MVP world in **preparation**, with a 1080-second day length and `count` from 1 through 10. It completes that many full shifts and returns the same simulation in the following day's preparation. It joins test actors 0 and 1, alternates the worker by day, and releases both actors in `finally`. It is intended only for isolated test worlds. The parent can run chunks **3 + 3 + 4 in distinct Windows processes**, explicitly save/load its isolated checkpoint between invocations, and retain their returned logs. OS process creation, bootstrap integration and input/network validation remain parent-owned.

## What the main ten days actually do

The starting world comes from `CreateNewMvp(24681357)`. The bot uses `Execute` for purchases, pose, pickup, drop, supply collection, physical delivery, work acquisition, heartbeat, registration, checkout and lifecycle transitions. It advances `Tick(.1f)` without wall-clock waits. Every shift must reach closing at time 1080 before finish; due guests must have completed their departure before finish can run. The supported `endGuidedOpening` command opts out of onboarding on the first day; this is not onboarding coverage.

Pose commands use canonical target/item positions, with the authoritative simulation validating each pose. Travel is instantaneous. This tests model commands and geometry acceptance, not walking skill, first-person controls, two human players or network delivery.

The bot prepares owned rooms, assigns guests using current hospitality rules, delivers their own luggage, fulfills towel/coffee/cleaning requests, repairs equipment and utilities when they actually fail, changes dirty beds, mops water/floors, and carries dirty linen/towels/trash to their receivers. All work must remain active through repeated heartbeats and produce a physical result. Dirty towels use `kind=towel, condition=dirty`. Repairs must preserve equipment quality and episode identity.

The purchase order is bed, TV, room 105, room 106, toolbox, linen, coffee and cart. It uses catalog prices, checks affordability and current purchase rules, and verifies both installation and the exact charge. Unaffordable entries remain pending and are reconsidered each preparation. Bed/TV initially target room 102; an occupied room can be replaced by another eligible room. The ten-day coverage gate requires all eight purchase IDs and both expansion rooms. Failure to earn enough in this selected scenario is reported as a coverage failure, not silently waived. The cart is purchased/persisted here; detailed weighted-cart behavior belongs to the separate persistence/operations suite.

Main-scenario state is never assigned directly to complete a job, advance a day, manufacture income, create a request or induce a fault. Equipment/utility failures come from actual starting conditions, usage and pacing. Coverage requires at least one local equipment repair and one utility repair, without requiring every random event or every equipment fault type.

## Semantic assertions

- After every tick and successful command, cash delta must equal earned delta minus expense delta. Earned delta must equal the exact agreed tariff times billable nights of newly paid guests. A seen paid guest cannot become unpaid. Each manual checkout is repeated and must fail without changing any world data.
- Room links must identify one live unpaid occupant. Committed bookings in the same room must use non-overlapping half-open intervals. The runtime validator independently checks remaining structural/reference constraints.
- Finish/next-day preserve continuing guests, agreed contracts, assigned rooms and their physical luggage. Future bookings are observed and serialized at multiple boundaries.
- All four request kinds must be observed fulfilled/rewarded by the actual systems. Reward guards cannot roll back. In preparation, repeated reconciliation must not change guest satisfaction, time or RNG; at least one such probe must include a rewarded continuing request.
- Guest economic/service contract fields remain unchanged across periodic observations and resumes. Previously seen reviews cannot be rewritten, and a retained review list cannot contain the same guest twice.
- At least ten actual check-ins, ten manual checkouts and ten paid guests are required. Timed bed/cleaning/coffee work, dirty carry, dirty towels and more than 100 heartbeats are mandatory. Merely opening and finishing ten shifts cannot pass.
- Each simulated minute and every day boundary validates the save, bounded guest/item/hospitality/director collections, expanded snapshot budget and actual compressed snapshot encode/decode round-trip. The peak expanded size is reported. No claim is made about real transport delivery from this codec assertion.

## Save/resume and replay

The Editor test saves at initial preparation and after days 3, 6 and 10. It uses a GUID directory under the OS temporary directory, never the user's normal slot. Full native `JsonUtility` payload equality is checked across `HotelSaveStore.Save`, `Load` and `ResumeForPlay`, including demand, pending bookings, contracts, cash, physical dirt/equipment, IDs and RNG. At least two saved boundaries must contain future confirmed reservations. Prepared day, RNG state and draw count must not change on resume.

Two independent model resumes of the final checkpoint then play another full day with the same driver. Their complete resulting JSON must match, and the RNG draw count must increase, proving the comparison crossed real demand/director draws. The checkpoint file must remain untouched. These two branches are **model replay**, not an operating-system restart. The main ten-day path does not include their extra days.

## Debt fixture and bounds

A separate `CreateNewMvp(1357911)` fixture sets only initial cash to **−200**. It saves/reloads that debt, proves an optional purchase is rejected without mutation, and plays three full days with optional purchases disabled. Physical work, essential supply replenishment, settlement income and every subsequent balance change use real commands. Income must exceed initial debt plus replenishment costs, and the saved final balance must be positive.

The suite therefore simulates 15 full days in total: 10 main days, two one-day deterministic branches, and three debt days. Each driver day has a maximum of 20,000 service cycles, 18,000 ticks before closing, and 150,000 commands; timed work is capped at 150 heartbeats and late departure waiting at 2,400 ticks. Preparation loops are independently bounded. A failure throws diagnostic day/time/phase context rather than continuing indefinitely.

Temporary cleanup checks the exact generated directory's parent and GUID name before deleting only that directory, including its checkpoint/backup. Cleanup runs in `finally` on success or failure.

## Validation performed in this checkout

Read the component contract, execution plan and evolving parent/operations-worker source read-only. Reconciled current DTOs, factory/resume API, command names, dirty-towel representation, purchase prices, request fulfillment timing, exclusive departure semantics, checkout position and snapshot codec. Source/whitespace/scope inspection only; no native pass count is claimed. Parent integration must compile and invoke `RunAll`, then exercise its separate process-restart and real input/network scenarios. Any seed-dependent coverage failure must be investigated from actual metrics before adjusting scenario expectations.
