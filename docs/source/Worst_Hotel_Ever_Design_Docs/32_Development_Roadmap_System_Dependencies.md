# Worst Hotel Ever — Development Roadmap & System Dependencies

## Purpose

Defines practical implementation order.

Main development sequence:

**Feel → Co-op → Physical World → Hotel Loop → Consequences → Progression → Replayability → Content**

## Main Risk

The biggest risk is not Economy or Reputation.

It is:

**Is it fun to physically exist together inside the Hotel?**

If movement, carry, collision and spatial flow are bad, deeper simulation cannot save the game.

---

# Dependency Overview

```text
Player Movement
    ↓
Interaction
    ↓
Physical Items
    ↓
Multiplayer Shared World
    ↓
Room + World Objects
    ↓
Guest Navigation
    ↓
Guest Lifecycle
    ↓
Tasks
   ↙ ↓ ↘
Cleaning Delivery Repair
   \ | /
   Problems
      ↓
Complaints
      ↓
Reviews / Reputation
      ↓
Demand
      ↓
Reservations
```

Parallel:

```text
Rooms + Equipment
      ↓
Breakdowns
      ↓
Infrastructure
```

Meta:

```text
Core Daily Gameplay
      ↓
Economy
      ↓
Upgrades
      ↓
Expansion
      ↓
More Workload
      ↓
Difficulty + Event Director
```

---

# Phase 0 — Technical Playground

Do not build Hotel yet.

Create:

- Floor
- Walls
- Player
- Camera
- Movement

## 0A Player Controller

Validate:

- movement
- rotation
- acceleration
- collision
- camera
- tight spaces

Gate:

**running around a grey room feels good.**

## 0B Interaction

Add:

- focus
- interact
- pick up
- carry
- drop
- place

Start with cubes.

## 0C Physics Feel

Replace with:

- Suitcase
- Toolbox
- Towels
- Cart

Validate:

- stability
- collision
- carry
- snaps
- recovery

### P0 — Physical Toy

If movement + physical objects are not fun, stop and tune.

---

# Phase 1 — Co-op Prototype

Add second Player.

Need:

- distinction
- shared objects
- soft collision
- shared Cart

If online co-op is required, test real networking early after basic feel is proven.

Validate:

- movement replication
- Item ownership
- simultaneous grab
- drop
- Cart

### P1 — Two Players + Physical World

---

# Phase 2 — Graybox Hotel

Build:

- Lobby
- Reception
- Hallway
- Storage
- Utility
- Room 101

No final art.

## Room Entity

Minimal:

- ID
- Occupancy
- Ready
- associated objects

## Room States

Add:

- Bed Prepared / Used
- Towels Present / Missing
- Trash Present
- Sink Working

### P2 — Prepare A Room

Without Guests, cleaning / preparing a Room should already feel understandable.

---

# Phase 3 — First Guest

Add only basic lifecycle:

Spawn  
→ Reception  
→ Wait  
→ Check-in  
→ Room  
→ Stay  
→ Leave

No Traits / Reviews.

## Navigation First

Prove:

Entrance  
→ Reception  
→ Room  
→ Exit

with obstacles / Players.

## Reception

Temporary UI is fine.

### P3 — Hotel Exists

1–2 Guests + 2 Players.

---

# Phase 4 — Core Work

Do not build full Task UI first.

Use actual world states.

## 4A Delivery

Extra Towels.

Guest Request  
→ Storage  
→ Item  
→ Delivery  
→ Resolve

## 4B Cleaning

Guest Check-out  
→ Room Not Ready  
→ Linen / Towels / Trash  
→ Ready

Laundry remains stubbed.

## 4C Repair

Equipment Condition + Toolbox.

Start with Sink.

## 4D Leak

Broken Sink  
→ Leak  
→ Wet Floor

One Player repairs source.

Other mops consequence.

### P4 — First Emergent Problem

If this does not create natural co-op, refine before expanding.

---

# Phase 5 — Task System Proper

Only now formalize generic Tasks because real sources exist.

First templates:

- Interact
- Deliver Item
- Clean State
- Repair Object
- Prepare Room

Task comes from source state.

Debug list first, polished UI later.

---

# Phase 6 — Complaints & Satisfaction

Add:

- Satisfaction
- Waiting tolerance
- Problem detection
- Complaint

Initial Complaint cases:

- long wait
- missing Towels
- broken TV
- Leak

Developer debug should show exact reasons.

### P5 — Consequences Matter

---

# Phase 7 — Time & Daily Lifecycle

Add:

- Clock
- Arrival windows
- Check-outs
- Day end

Reservations remain fixed / hand-authored stub.

Demand not built yet.

---

# Phase 8 — Economy

Add:

- Room payment
- Compensation
- Supply Cost
- Major Repair Cost
- shared Cash

Then first upgrades:

- Second Toolbox
- Luggage Cart

### P6 — Day → Money → Upgrade → Next Day

Major Go / No-Go gate.

Question:

**Do Players want another Day?**

---

# Phase 9 — Reviews & Reputation

Add:

- simple review score
- contextual topics
- Reputation Poor / Average / Good

---

# Phase 10 — Demand

Replace fixed Guest schedule with system based on:

- Reputation
- Price
- Rooms
- Quality

Add:

- Reservations
- Walk-ins

## Guest Variety

Only after generic Guest works well:

Archetypes:

- Tourist
- Business
- Family
- VIP

Traits:

- Patient
- Impatient
- Messy
- Demanding
- Friendly

### P7 — Replayable Hotel Days

Normal systems should already create variety before Event Director.

---

# Phase 11 — Workload Tracking

Track:

- Tasks
- Complaints
- Queue
- Breakdowns
- Critical Problems

Bands:

- Low
- Normal
- High
- Critical

---

# Phase 12 — Event Director

Only now.

First version:

- 3–5 Events
- eligibility
- cooldown
- anti-repeat
- workload protection

Pool:

- Guest Rush
- Walk-in Group
- VIP
- Elevator Failure
- Minor Water Event

---

# Phase 13 — Furnishing

Add:

- Basic Bed
- Comfortable Bed
- TV None / Basic

Slot-based only.

Prove that Room setup affects Guest / Economy.

---

# Phase 14 — Structural Expansion

Open:

- Room 5
- Room 6

Compare:

4 Rooms vs 6 Rooms.

Question:

**Did it become more interesting, or only more work?**

### P8 — MVP Hotel Loop

At this stage the main systemic loop exists.

---

# Phase 15 — Persistence

Actual save can come here after data model stabilizes.

Save at:

- End Day
- Preparation

But stable IDs and clear source-of-truth must be planned earlier.

---

# Phase 16 — Preparation Phase

Replace debug upgrade menu with real between-day flow:

- Summary
- Forecast
- Reservations
- Repairs
- Upgrades
- Price
- Start Day

Allow physical preparation.

---

# Phase 17 — Real UI Pass

Priority:

1. World readability
2. Interaction prompts
3. Notifications
4. Room Overview
5. Reservations
6. Day Summary

Do not polish UI before system behavior stabilizes.

---

# Phase 18 — Art Direction Pass

Replace greybox with target stylized art.

First production pack:

Characters:
- 2 Staff
- Guest variants

Environment:
- Lobby
- Room
- Hall
- Storage
- Utility

Props:
- Bed
- Sink
- TV
- Toolbox
- Luggage
- Towels
- Cart
- Mop

---

# Phase 19 — Animation

Critical first:

- locomotion
- carry
- interact
- repair
- mop
- Guest walk
- wait
- happy
- annoyed

Readability before variety.

---

# Phase 20 — Audio

Placeholder audio should exist earlier.

Production pass later:

- footsteps
- pickup
- luggage
- cart
- doors
- leak
- repair
- Guest reaction
- arrival
- complaint

---

# Vertical Slice

Not whole MVP.

Target:

## Players
2

## Hotel
Lobby + Storage + Utility + 4 Rooms

## Guests
Tourist + Business

## Traits
Patient + Impatient + Messy

## Requests
Towels + Coffee

## Cleaning
Bed + Towels + Trash + Wet Floor

## Breakdowns
Sink Leak + Broken TV

## Tools
Toolbox + Mop

## Progression
Second Toolbox + Luggage Cart + Better Bed

## Days
1–2

Questions:

- Did Players divide work naturally?
- Did physical comedy emerge?
- Were problems understandable?
- Did upgrades change behavior?
- Did Players want another Day?

If not, do not solve it by adding content.

---

# MVP Production

After Vertical Slice is proven:

- Family
- VIP
- more Complaints
- more Breakdowns
- Rooms 5–6
- Reputation
- proper Demand
- Events
- Persistence
- Tourist Group

---

# Infrastructure Timing

Basic Infrastructure comes after local breakdown loop works.

Add:

- Floor/Zone Power
- Water
- Elevator dependency

Avoid building deep infrastructure too early.

---

# MVP Balancing

Balance only after full loop exists.

Tune:

- arrival rate
- cleaning duration
- repair frequency
- pricing
- upgrade costs
- demand
- event pacing

Systemic balance matters more than isolated values.

---

# Alpha Priorities

After MVP:

1. 3–4 Players
2. Floor 2
3. Better Infrastructure
4. Laundry
5. Room Categories
6. Hotel Identity
7. Planned Events
8. Inspector / Conference
9. More Furnishing
10. Mid-day Save

---

# Stub Strategy

Use stubs when dependency is needed before gameplay system is worth building.

## Economy Stub
Guest pays flat amount.

## Demand Stub
Fixed Guest schedule.

## Laundry Stub
Dirty Linen disappears into bin; Clean Linen auto-restocks.

## Reputation Stub
Average review stored.

## Infrastructure Stub
Local Sink can break before Water system exists.

## Furnishing Stub
Fixed Bed.

## Event Director Stub
Debug button triggers Event.

### Stub Rule

Preserve future interface if possible, not full future logic.

---

# Debug Tools

Required for systemic development:

- Spawn Guest
- Break Object
- Dirty Room
- Set Room Ready
- Set Satisfaction
- Trigger Event
- Set Time
- Add Money

## Guest Inspector

Show:

- state
- Room
- satisfaction
- requirements
- traits
- request
- complaints
- destination

## Room Inspector

Show:

- occupancy
- readiness reasons
- cleanliness
- equipment
- Problems
- Guest

## Task Inspector

Show:

- Source
- target
- priority
- completion condition

## Event Inspector

Show:

- eligible pool
- cooldown
- reason unavailable

---

# Automated Scenario Tests

Useful examples:

Guest Checks Out  
→ Room becomes Vacant + Not Ready

Repair Sink  
→ Leak source stops

Power Restored  
→ healthy TV works again

---

# Refactor Gates

Intentional review points:

- after Physical Prototype
- after first Guest + Tasks
- after Vertical Slice

Check:

- duplicated state
- coupling
- source of truth
- extensibility

---

# Production Priority Rule

Prefer a feature that multiplies existing interactions over isolated content.

Good:

Noise  
because it connects Guests + Rooms + Layout + Complaints.

Good:

Room 5  
because it connects Economy + Demand + Cleaning + Workload + Layout.

Lower priority:

20 wallpaper colors.

---

# Multiplayer Rule

Every mechanic must answer:

**What happens if two Players do it at the same time?**

Examples:

- grab same Item
- interact with same Guest
- repair same Sink
- buy same Upgrade

---

# Recommended Milestones

### P0
Movement feels good.

### P1
Two Players + physical objects feel good.

### P2
Preparing a Room feels good.

### P3
One Guest lifecycle works.

### P4
Cleaning / Delivery / Repair work physically.

### P5
Problems and Complaints matter.

### P6
Day → Money → Upgrade loop.

### P7
Demand creates replayable Days.

### P8
Expansion increases complexity.

### VS
Polished Vertical Slice.

### MVP
Persistent replayable small Hotel.

### Alpha
Expanded multi-floor Hotel ecosystem.

---

# What To Build First

Tomorrow:

1. Grey test room
2. Player movement
3. Camera
4. Interaction focus
5. Pick up cube
6. Carry through doorway
7. Drop
8. Second Player
9. Exchange / conflict over same cube
10. Replace cube with Suitcase

Not:

- Economy Manager
- Guest class hierarchy
- Room mega-manager

---

# First Prototype Sequence

## Physical Prototype
2 Players + Suitcase + Toolbox + Cart + hallway.

## First Hotel Prototype
1 Guest + 1 Room + Check-in + Suitcase + Towels + Check-out + Cleaning.

## First Chaos Prototype
2–3 Guests + Dirty Room + Request + Leak.

No Event Director.

## First Progression Prototype
End Day → Money → choose Cart or Second Toolbox → Day 2.

## First Expansion Prototype
Unlock Room 5.

Question:

**Did success create interesting new problems?**

---

# Anti-Scope Rule

Every new idea gets one label:

- Needed Now
- MVP
- Alpha
- Later
- Parking Lot

A feature enters current milestone only if it:

- blocks core loop
- tests critical risk
- is required for usability
- is required for technical foundation

---

# Core Principle

Build:

**Feel first.  
Then co-op.  
Then Hotel.  
Then consequences.  
Then progression.  
Then replayability.  
Then content.**

The first real success criterion is simple:

> **Two Players, one corridor, one Cart and several Suitcases should already be a little funny.**
