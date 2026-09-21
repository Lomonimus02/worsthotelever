# Worst Hotel Ever — Cleaning & Hygiene

## Goal

Cleaning should be simple to execute but interesting through logistics, timing and priorities.

## Concrete States

MVP:

- Used Bed
- Dirty Linen
- Used Towels
- Trash
- Full Bin
- Wet Floor

Optional:

- Stains

## Cleanliness vs Hygiene

Can conceptually differ later, but avoid overcomplication in MVP.

## Guest Dirt Generation

Depends on:

- stay duration
- Messy / Neat traits
- group size
- activities
- parties later

## Room Ready

Ready requires a minimum clean state.

Minor imperfection may still allow rental and create risk.

## Bed Loop

Used Bed  
→ remove dirty linen  
→ get clean linen  
→ prepare bed

No full cloth simulation.

## Towels

States:

- Clean
- Present
- Used
- Dirty
- Missing

## Trash

Use grouped clutter.

Not dozens of individual cans.

## Wet Floor

Can come from:

- leaks
- spills

Mopping removes consequence, not source.

## Common Areas

Later.

## Daily Service

Later lighter cleaning for multi-day Guests.

## DND

Later.

## Partial Work

Cleaning progress should persist naturally through physical states.

## Prepare Room

Composite state composed from individual world conditions.

## Reservation Deadline

Creates urgency naturally.

## MVP

- Used Bed
- Dirty Linen
- Used Towels
- Trash
- Full Bin
- Wet Floor
- Mop
- Linen Storage
- Trash Disposal

## Core Principle

Cleaning is about physical flow and prioritization, not long repetitive minigames.
