# Worst Hotel Ever — Level Design & Spatial Rules

## Purpose

Hotel layout is part of gameplay, difficulty and progression.

## Core Principle

Hotel should be:

**easy to understand, but not always easy to operate.**

## Productive Friction

Good:

- Players meet in doorway
- Cart becomes awkward
- Elevator creates queue
- Storage location matters

Bad:

- constant deadlocks
- camera failure
- NPC blocking everything
- meaningless long walking

## Distance

Distance is Task cost.

Travel affects:

- prioritization
- role splitting
- value of Carts
- value of Shortcuts
- value of distributed Storage

## Spatial Types

- Public
- Guest Private
- Service
- Transitional

## Starting Hotel

Recommended:

Entrance  
→ Lobby / Reception  
→ short Main Hall  
→ Rooms 101–104  
+ Storage  
+ Utility  
+ Closed Expansion to 105–106  
+ visible future Vertical Core

## Lobby

Main operational stage.

Needs:

- Entrance visibility
- Reception landmark
- Queue
- bypass route
- luggage area
- enough room for rush

Approx first greybox:

~7–9m × 8–11m depending scale.

## Corridor

Starting target:

Guest corridor ~2.2–2.6m.

Service corridor later ~1.7–2.0m.

## Doors

Room door:

~1.1–1.3m starting target.

Service door:

~1.4–1.8m when Cart traffic matters.

## Rooms

Gameplay-scaled, not strictly realistic.

Approx initial test:

~4.5–5.5m wide  
×  
~5–6.5m deep

depending Bathroom arrangement.

Need work space around:

- Bed
- Sink
- Toilet
- TV
- Trash
- luggage

Two Players should be able to work in one Room.

## Bathroom

Compact but usable.

Avoid constant camera / collision issues from realistic tiny geometry.

## Ceiling

Stylized initial test:

~3.2–3.8m.

## Camera

Must be tested from day one.

Room scale is invalid until third-person camera works comfortably.

## Queue

Normal queue should fit.

Rush queue may create congestion.

Queue must not permanently block main Hotel circulation.

## Arrival Buffer

Guests need space near Entrance before Reception.

## Luggage Area

Near Lobby but off the main route.

## Storage

Early Hotel target:

~5–12 sec from Lobby / early Rooms.

Not perfectly central.

## Travel Targets

Useful categories:

Short:
~3–8 sec

Medium:
~8–15 sec

Long:
~15–30 sec

Very Long:
30+ sec, rare without transport / upgrades

Avoid frequent >15 sec empty running.

## Utility

Reachable but not trivial.

Initial target:

~8–15 sec from central hub.

## Elevator

Placed near central circulation.

Needs waiting area:

roughly at least ~3 × 3m early.

Queue should not block entire hallway.

## Stairs

Reliable fallback.

Should remain useful for short vertical trips.

## Floor Numbering

Predictable:

101–106  
201–208  
301–308

## Landmarks

Use:

- wall accents
- windows
- signage
- plants
- service doors
- Elevator hubs

Wayfinding should work without constant Map use.

## Room Clusters

Group ~3–5 Rooms into natural operational clusters.

Supports:

“I’ll take East Wing.”

## Cart Routes

Critical paths must pass tests:

- Player + Suitcase
- Player + Bulky Item
- Cart
- two Players crossing
- Guest Group

## Cart U-Turn

Must be possible in major hubs:

- Lobby
- Storage
- Elevator area

Not necessarily every hallway.

## Clutter Budget

Zones should tolerate reasonable player-created mess.

Lobby can handle more than a narrow corridor.

## Loops

Some route loops are good.

Avoid every Floor being one giant dead end.

But do not give perfect alternate routes everywhere.

## Service Routes

Later:

- public corridor
- Staff shortcut
- service corridor

Strong progression layer.

## Noise and Layout

Adjacency should matter simply.

Bar next to Quiet Wing can create real consequences.

## Premium Location

Premium Room can be farther / quieter.

Benefit:

- quality
- quiet

Responsibility:

- longer service route

## Expansion

Growth should strain old layout:

- Lobby
- Elevator
- Storage
- travel distances

Then structural upgrades relieve it.

## Graybox Tests

### Test 1
2 Players  
4 Rooms  
6 Guests  
1 Cart  
1 Toolbox  
Towels  
1 broken Sink

### Test 2
Add Rooms 5–6.

### Test 3
Add Floor 2 + Elevator + Stairs.

Measure:

- travel
- congestion
- natural team splitting
- navigation failures

## Useful Metrics

- average Task travel
- empty walking ratio
- Player collision stalls
- NPC stuck incidents
- Cart use
- Storage returns
- congestion points
- “Where is this?” moments

## Graybox Starting Targets

- Main Corridor: ~2.4m
- Service Corridor: ~1.8–2.0m
- Room Door: ~1.2m
- Service Door: ~1.5m
- Standard Room: ~5 × 5.5m
- Ceiling: ~3.4m
- Elevator Area: ~3 × 3m minimum
- Lobby: ~8 × 9m
- Storage: ~4 × 5m
- Utility: ~3 × 4m

All are testing values, not final specs.

## Core Principle

**Readable Layout + Meaningful Distance + Controlled Bottlenecks + Physical Logistics + Upgradeable Routes**
