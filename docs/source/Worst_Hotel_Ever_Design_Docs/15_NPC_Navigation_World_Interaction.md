# Worst Hotel Ever — NPC Navigation & World Interaction

## Philosophy

Guests should physically move through Hotel.

Avoid routine teleportation.

## Core Destinations

- Entrance
- Reception
- Assigned Room
- Elevator
- Stairs
- Lobby
- Exit
- Amenities later

## Zones

- Public
- Guest
- Staff-only
- Restricted

## Doors

Navigation understands door state.

## Assigned Room

Guest keeps persistent relationship to Room.

## Stairs

Reliable baseline vertical path.

## Elevator

Shared resource:

- availability
- capacity
- queue
- waiting

MVP elevator failure = unavailable.

## Obstacles

Large objects like Cart can matter.

Small clutter should not completely block NPCs.

## Recovery

Hard blockage:

- repath
- wait
- fallback
- anti-stuck recovery

Never let AI bugs become “gameplay”.

## Player Collision

Soft.

Guests can yield.

## Hazard Avoidance

Guests may avoid:

- large puddles
- inaccessible areas
- closed zones

## Activities

Guest movement should have purpose.

No random wandering just to look alive.

## Groups

Simple group following / shared destinations.

## Arrival / Departure

Guests physically enter and leave.

## UI

Show useful location:

- Floor
- Room
- Lobby

No GPS-like exact tracker required.

## MVP

Entrance  
↔ Reception  
↔ Room  
↔ Stairs / Elevator  
↔ Exit

plus:

- queues
- doors
- obstacle avoidance
- repath

## Core Principle

Navigation should be reliable enough that spatial problems come from Hotel design, not broken AI.
