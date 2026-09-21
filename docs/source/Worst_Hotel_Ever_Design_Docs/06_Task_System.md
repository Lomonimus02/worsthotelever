# Worst Hotel Ever — Task System

## Definition

Task — actionable goal derived from some source.

Source  
→ Task  
→ physical action  
→ world state changes  
→ completion detected  
→ source resolves

Task is **not** the Problem itself.

## Sources

- Guest Request
- Room state
- Problem
- Complaint
- Guest lifecycle
- Preparation need

## Categories

- Service
- Cleaning
- Repair
- Room Preparation
- Complaint Resolution
- Reception

## Atomic vs Composite

Atomic:

- Deliver Towels
- Mop Floor
- Repair Sink

Composite:

- Prepare Room
- Move Guest

## Completion

Task should detect completion from the authoritative world state.

No manual “mark done”.

## Multiple Valid Resolutions

A complaint might resolve through:

- repair
- room move
- compensation
- delivery

Task architecture should allow alternatives.

## Ownership

Tasks are shared by default.

Optional claim later.

No rigid classes.

## Priority

Internal / UI:

- Normal
- Important
- Critical

## Presentation

World-first.

Use:

- world indicators
- concise notifications
- Hotel Overview

Avoid giant quest log.

## Grouping

Tasks can be grouped by:

- Room
- Guest
- Source
- Category

## Failure

Task often transforms instead of hard-failing.

Example:

Request ignored  
→ Complaint.

## Persistence

Persist the source state where possible.

Reconstruct Tasks from source after load.

## Rewards

No Task XP.

Rewards / consequences come from source:

- Guest satisfaction
- room readiness
- revenue
- reputation

## Reusable Templates

- Deliver Item
- Repair
- Clean
- Prepare
- Move
- Serve
- Find
- Interact

## MVP Tasks

- Check-in
- Check-out
- Towels
- Coffee
- Baggage
- Trash
- Linen
- Mop
- Repair Sink / Toilet / TV
- Prepare Room
- Move Guest
- Resolve Complaint
