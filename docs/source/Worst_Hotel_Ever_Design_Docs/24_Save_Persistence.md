# Worst Hotel Ever — Save & Persistence

## Philosophy

Hotel is a persistent world and should accumulate history.

## Persist Hotel State

- Day
- Cash
- Reputation
- Rating later
- Identity later
- Unlocked content
- Prices

## Building

- Rooms
- Floors / Wings
- Amenities
- Infrastructure
- Structural upgrades

## Rooms

- Furnishing
- Quality
- Condition
- Cleanliness
- Occupancy
- Problems

## Equipment

- Condition
- Breakdown state

## Guests

Persist important multi-day Guests:

- Room
- satisfaction
- stay duration
- complaints
- key memories

At overnight transition, position can normalize to Room.

## Items

Persist important Items:

- tools
- carts
- luggage
- important service objects

Do not persist exact physics transform of every small clutter object if not necessary.

Prefer:

- logical zone
- slot
- safe location

## Reservations

Persist future bookings and planned Events.

## Day Boundary

MVP Save:

- End Day
- Preparation
- Start Day

Mid-day save later.

## Tasks

Prefer reconstructing from sources.

Do not save derived Task list as the only truth.

## Derived State

Recalculate after load.

## RNG / Anti-Reroll

Persist enough Event / Reservation history to avoid trivial reroll abuse.

## Source of Truth

Avoid duplicated states.

## Multiplayer

Conceptually shared Hotel Save.

MVP may be host-owned.

## New Players

Can join a developed Hotel.

No individual power progression required.

## Stable IDs

Content must use stable IDs.

Need migration / fallback strategy later.

## Autosave

Use safe / major transaction points.

## No Offline Progression

Hotel does not simulate real time while user is away.

## Disconnect

If Player disconnects carrying important Item:

drop / recover safely.

## Save Size

Departed Guests can collapse into:

- review
- aggregate stats
- returning-guest record later

## Persistence Classes

- Permanent
- Long-lived
- Session-lived
- Ephemeral

## MVP

Persist:

- Day
- Cash
- Reputation
- unlocked Rooms
- Room states
- furnishing
- equipment
- breakdowns
- important tool locations
- reservations
- upgrades

## Core Principle

Persist consequences that create Hotel history; normalize meaningless physics noise.
