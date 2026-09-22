# MVP implementation contract — 22 September 2026

This extends `COMPONENT_CONTRACT.md`; where conflicting, this v2 contract applies only when `HotelState.mvp != null`. Legacy constructors/fixtures remain v1. No worker changes shared DTOs without integrator agreement.

## Data and authority

`HotelMvpTypes.cs` is the exact serialized schema. `HotelTypes.cs` adds nullable `mvp` to hotel/room/guest; item `size` (small/large), `condition` (clean/dirty); command `guestId`. v2/content2 stores SIX room records: 101–104 owned, 105–106 unowned. Hotel root remains same object during promotion. Parent owns creation/migration/validation/save/network/core hooks. State.cash is the single balance; negative is debt.

Room linen remains `room.bed`, linen quality `mvp.betterLinen`, bed furniture quality `room.mvp.bedQuality`; capacity is independent (102,104,106 double, others single). All rooms have sink/toilet/tv/lamp equipment rows. `room.leak` is operations-owned projection of sink.localFault; toilet fault and utilities do NOT silently fix a sink. room.trash is bin presence; dirt and dirty towels separate.

Hotel calendar uses `State.day`, `State.time`, default dayLength=1080. `mvp.elapsed` advances only during open/closing, never preparation/summary. Guest departureDay is exclusive: arrivalDay=1/departureDay=2 is one billable night, checkout during day1 final quarter. arrival1/departure3 remains into day2. Reservations use [arrivalDay,departureDay), including current stay. Quote/timing/requirements stored in immutable `MvpGuestState` per arrival. Each GuestState owns a clone, never shares mutable contract with reservation/schedule.

Families have one visible NPC explicitly representing partySize=2. Other archetypes partySize1. Four archetypes tourist/business/family/vip; five traits patient/impatient/messy/demanding/friendly. Request kinds towel/coffee/cleaning/luggage. statuses open/fulfilled/cancelled. Cause complaints status active/resolved, escalation0/1/2. Satisfaction stays 0..100, numeric reviews1..5 and bounded history determine reputation.

## Shared private partial hooks (exact signatures)

All partials are `public sealed partial class HotelSimulation` in WorstHotel. Integrator edits existing `HotelSimulation.cs` and lifecycle partial. Worker files may access existing private helpers Player/Room/Guest/Item/Held/Give/Consume/Cancel/Near/Remember/Log/RetireBags/UpdateCartCargo/MoveRoute.

Operations worker defines:
```
bool TryOperationsCommand(PlayerState p, HotelCommand c, out string error)
bool TryOperationsInteract(PlayerState p, string target, out string error)
string OperationsWorkError(PlayerState p, string target)
float OperationsWorkDuration(string target)
void OperationsCompleteWork(PlayerState p)
void OperationsStep(float dt)
bool TryRaiseFault(int room, string kind) // room=0,kind=water/power for utility
```
It owns ALL MVP work (including bed/trash/sink/water), item delivery, coffee brewing, dirty towels, furnishing/upgrades. Normal pickup/drop/cart is shared. Wrong bag delivery allowed; owner never changes, recompute luggageDelivered. Expose pure static `HotelOperationsRules.Equipment(HotelState,int,string)` -> MvpEquipmentState, `EffectiveEquipment(HotelState,int,string)` -> bool, `PurchaseBlockReason(HotelState,string,int)` -> string and `Tasks(HotelState)` -> List<string>. Upgrade IDs toolbox/cart/linen/coffee/bed/tv/room105/room106; finishRoom target original/warm/cool. Supplies restock at prep via lifecycle, baseline coffee source always usable. Required public catalog `HotelUpgradeCatalog.All` array/list of entries with id/name/description/price; room-sensitive bed/tv.

Hospitality worker defines:
```
bool TryHospitalityCommand(PlayerState p, HotelCommand c, out string error)
void HospitalityStep(float dt)
void PrepareDemand() // pure generation only once per new preparation, no economy or phase mutation
void FinishHospitality() // only due guests; preserve continuing stays
void PruneHospitality() // terminal unreferenced records, bounded history
bool TryScheduleArrival(string archetype, string source, string groupId = "")
bool TryReserveBatch(int count, string source, out string groupId)
void RefreshRequestFulfillment() // after physical state/item changes; requests reward exactly once
```
It owns guest contracts, schedule/reservations, group records, movement, requests, cause complaints, relocation/refusal, settlement/reviews. Pure `HotelHospitalityRules.AssignmentBlockReason(HotelState,int guestId,int room)` -> string, `GuestIssues(HotelState,GuestState)` -> string, `Tasks(HotelState)` -> List<string>. Selected checkin uses guestId, number=room. checkout/compensate accept guestId if nonzero else number for old UI. Commands checkin/checkout/compensate/relocate/refuse require near desk, either player. price target low/normal/high host+board+preparation. Reservation overlap common allocator, batch transactional, refuse queued+future bookings supported via target=reservationId. Numeric reviews once. Do not call legacy Settle/Review/TickGuest (new equivalents).

Pacing worker defines:
```
void PacingStep(float dt)
void PacingUpgradeGrace()
```
Pure `HotelWorkload.Measure(HotelState)` -> float; `HotelMvpDirector.Status(HotelState)` -> string. Uses TryScheduleArrival/TryReserveBatch/TryRaiseFault, not direct guest/equipment mutation. Four event kinds rush/walkin_group/vip/water, category and global cooldowns, anti-repeat, optional nothing, overload suppression, bounded history. Persist RNG and all draws. Shared private `MvpRandom(int exclusiveMax)` and `MvpId(string prefix)` supplied by integrator. Reserve planned tourist group in PrepareDemand (e.g. every third day if fits); separate unexpected walk-in group event.

Integrator lifecycle: after common work leases, if MVP -> MvpStep exclusively. Open/finish/nextday host/desk wrappers; active step advances elapsed and time (unless guided ClockHeld), OperationsStep -> HospitalityStep -> RefreshRequestFulfillment -> PacingStep. Preparation can do physical work but guest clocks freeze. End-day cost and minimum restock once; no infinite resources or credit-funded optional upgrades. Guided legacy director remains first-guest introduction: operations reports Repaired/Mopped, hospitality reports Arrived and respects ClockHeld; no random events during guide. Preserve continuing guests/requests/items across day transition.

## Geometry / input / networking

Rooms 101/102 z5.5,103/104 z12.5,105/106 z19.5; x +/-4.7. Corridor ends z23; north bound24. Existing room positions unchanged. HotelLayout defines ALL new target coordinates. Room targets bed/sink/water/towel/trash/bag/door plus toilet/tv/lamp/coffee/clean/dirtytowel. World targets coffee, utility_water, utility_power. Water and power controls same service station in southwest lobby. Essential plunger starts near tools. Locked rooms have physical/authoritative access guard until owned; no lift decoration overlapping them. Buy activates furnished room visuals, tasks skip unowned rooms.

Ping command target validated world/room target, no arbitrary text, anyone; bounded2 pings, lifetime6 gameplay seconds or a separate realtime projection if frozen. Keyboard middle mouse or F. UI views Rooms/Guests/Operations/Schedule, selected guest explicit, scrollable; management prices/upgrades/furnishing/forecast; no silent first queued selection when user chose someone.

Production paths `HotelSimulation.CreateNewMvp(int? seed=null)` / `ResumeForPlay(HotelState)` used by both IP/Steam host. Legacy open/closing/summary plays old shift then migrates after nextday; prepare migrates immediately. Writing v1 stays envelope1, writing v2 envelope2; futureversion protections; original v1 archive before first v2 overwrite. Protocol bump to WHE-mvp-4 in BOTH Session/HotelSteam (Steam file must compile standalone against existing validation harness).

## Ownership / integration

Integrator: shared DTO/layout, original Simulation/hooks/lifecycle/factory, SaveStore/validator/migration, Session/Steam, Game/Feedback/Audio, build harness; UI assigned separately. Operations: NEW Operations partial/Rules/UpgradeCatalog/json + its tests/meta only. Hospitality: NEW Hospitality partial/Rules/MvpGuestCatalog/json + its tests/meta only. Pacing: NEW Pacing partial/Director/Workload + its tests/meta only. World: HotelWorld.cs + visual tests/meta only. UI: HotelUI/HotelPresentation/HotelOnboarding + tests as explicitly assigned. No worker runs Unity/game or touches user save/.vsconfig. Build/testing by parent only.
