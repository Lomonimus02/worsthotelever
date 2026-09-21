# Worst Hotel Ever — Content / Data Rules & Extensibility Review

## Philosophy

Core systems understand categories and contracts.

Content describes concrete instances.

## Stable IDs

Use stable IDs separate from display names.

## Tags

For categorical meaning.

Examples:

- Towel
- RepairTool
- Beverage
- FamilyRoom
- Electrical

## Properties

For values.

Examples:

- Quality
- Capacity
- Cost
- Durability

## Capabilities

What something can do.

Examples:

- BasicRepair
- MopWetFloor
- CarryLuggage

Tasks should request capability where possible, not exact Item ID.

## Requirements

Generic:

- Mandatory
- Preferred

Used by:

- Guests
- Rooms
- Events
- Upgrades

## Guest Definition

Possible data:

- Archetype
- Traits
- Requirements
- Preferences
- Budget
- Duration
- Party
- Request Pool
- Patience
- Demand Tags

## Traits

Modifiers should be composable.

Support conflicts / exclusions where needed.

## Request Template

- Trigger
- Target Requirement
- Urgency
- Resolution
- Escalation

## Task Template

- Target
- Action
- Capability
- Priority
- Completion
- Source

## Problem

- Category
- Severity
- Visibility
- Source
- Escalation
- Effects
- Resolution
- Affected Entities

Problem should not directly edit Reputation.

## Breakdown

Technical cause is separate from generic Problem consequences.

## Furniture

Data can include:

- Slot
- Cost
- Quality
- Capacity
- Tags
- Appeal
- Infrastructure Requirements
- Condition
- Breakdown Pool
- Cleaning Impact
- Resale later

## Room Shell

Contains:

- physical layout
- slots
- infrastructure
- navigation
- size

Category derived where possible.

## Amenity

- Space
- Capacity
- Guest Segments
- Infrastructure
- Tasks
- Breakdowns
- Demand
- Operating Cost
- Upgrades

## Upgrade

- Cost
- Requirements
- Physical Target
- Effects
- Unlocks
- Workload
- Maintenance

Prefer creating / modifying real entities over booleans.

## Event

- Category
- Eligibility
- Weight
- Cost
- Cooldown
- Conflicts
- Phases
- Modifiers
- Guests
- Completion

## Infrastructure

Consumers declare dependencies one-way.

## Source of Truth

Each system owns its authoritative state.

Avoid duplicated flags across managers.

## Validation

Need content validators and debug inspectors.

## Data-Driven Values

Frequently tuned values belong in data/config.

## Composition Over Inheritance

Guest = Archetype + Traits + Requirements, etc.

## Special Cases

Unique code is allowed when genuinely unique.

Rule of three:

generalize when repeated special behavior proves a reusable pattern.

## Vertical Slice Extensibility Test

We should be able to add:

- one Bed
- one Trait
- one Breakdown
- one Request
- one Event

without rewriting core architecture.

## Avoid Content Islands

Prefer content that multiplies existing systems.

## Core Principle

Concrete first. Generalize repeated patterns. Keep authoritative state clear.
