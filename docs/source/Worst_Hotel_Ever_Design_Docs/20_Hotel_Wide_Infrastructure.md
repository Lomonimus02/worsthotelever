# Worst Hotel Ever — Hotel-wide Infrastructure

## Purpose

Infrastructure connects multiple Rooms and zones through shared services.

## Candidate Systems

- Electricity
- Water
- Hot Water
- Climate
- Elevator
- Drainage

MVP only needs a subset.

## Architecture

Source  
→ Network / Zone  
→ Consumers

Keep it abstract.

Not an engineering simulator.

## Local vs Shared Failure

A TV can be locally broken.

Or a whole Floor can lose electricity.

These must remain separate.

## Consumer State

Effective function depends on:

Local Equipment State  
+ Required Infrastructure availability.

Infrastructure outage should not permanently damage every consumer.

## Electricity

MVP:

- On
- Off

Affects:

- Lights
- TV
- Elevator

Later:

- unstable
- load
- generator

## Water

MVP:

- Available
- Unavailable

Affects:

- Sink
- Toilet

Later:

- pressure
- hot water
- zones

## Leak

Technical source + environmental consequence.

Can support shutoff tradeoff later:

shut Water Zone  
→ stop leak  
→ lose water elsewhere.

## Elevator

Infrastructure / equipment hybrid.

Navigation reacts to availability.

## Technical Rooms

Shared infrastructure gives Players physical repair destinations.

## Multi-Step Repair

Later:

shut water  
→ repair source  
→ restore water  
→ mop consequence

## Root Cause Deduplication

One Floor power outage should not create ten independent repair tasks.

## Habitability

Conceptual:

- Fully Usable
- Degraded
- Unusable

## MVP

- Electricity On / Off
- Water On / Off
- Elevator Working / Broken
- one Utility point
- Leaks

## Core Principle

Infrastructure should create readable shared consequences without becoming a detailed engineering simulation.
