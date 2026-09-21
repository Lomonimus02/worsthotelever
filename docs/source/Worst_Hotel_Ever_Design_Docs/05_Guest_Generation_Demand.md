# Worst Hotel Ever — Guest Generation & Demand

## Separation

Demand отвечает:

**кто и сколько хочет приехать.**

Guest Generation отвечает:

**какие конкретно NPC / reservations создаются.**

## Inputs

MVP:

- Reputation
- Price
- Available Rooms
- Room Quality
- Hotel Capacity
- selected basic Amenities

Later:

- Hotel Rating
- Identity Tags
- external modifiers
- seasons
- events

## Capacity Is Not Demand

Свободные Rooms не гарантируют Guests.

И наоборот, высокий Demand может превышать Capacity.

## Guest Segments

Examples:

- Budget Tourist
- Regular Tourist
- Business
- Family
- VIP

## Attraction

Segment attraction depends on:

- price
- quality
- reputation
- amenities
- room availability
- expectations

## Price

MVP simple modes:

- Cheap
- Standard
- Expensive

Price influences:

- demand
- revenue
- expectations
- perceived value

## Value

Guest evaluates roughly:

Quality + Service relative to Price.

Expensive stay creates higher expectations.

## Reputation

Reputation can change:

- number of bookings
- type of Guests
- price tolerance

## Hotel Rating vs Reputation

Rating = formal / structural quality.  
Reputation = current public opinion.

Keep separate.

## Reservations

Reservation data may include:

- arrival window
- archetype
- stay duration
- party size
- room requirements
- special needs

## Walk-ins

Distributed through day.

Can be accepted or refused.

## Stay Duration

Different guests can remain multiple days.

## External Modifiers

Later:

- holidays
- local events
- weather
- seasonality

## Randomness

Use controlled randomness + anti-repeat.

## Forecast

Preparation phase should provide approximate next-day demand / reservation information.

## MVP

- Reputation
- Price
- Available Rooms
- Room Quality
- Reservations
- Walk-ins
- Next-day booking generation

## Core Loop

Service  
→ Reviews  
→ Reputation  
→ Demand  
→ Guests  
→ Service
