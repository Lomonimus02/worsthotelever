# Worst Hotel Ever — Item / Carry / Inventory

## Philosophy

No RPG inventory.

**What you carry is visible in the world.**

## Categories

- Guest Service
- Cleaning
- Tools
- Guest Property
- Hotel Equipment
- Consumables
- Trash

## Carry Rules

MVP:

- one major item
- or a small stack of light identical items

## Basic Actions

- Pick Up
- Carry
- Drop
- Put Down
- Give
- Place
- Use

## Ownership

Item may belong to:

- Guest
- Hotel
- Nobody
- Consumable
- Trash

Ownership remains logical even if physical position changes.

## Storage

Supplies should live in physical locations:

- Linen Storage
- Tool Rack
- Luggage Area
- Coffee source
- Trash Disposal

## Tools

MVP:

- Toolbox
- Plunger
- Mop

Tools provide Capabilities rather than bespoke Task IDs.

Example:

Toolbox → BasicRepair.

## Luggage

Core physical item.

Needs readable identification / ownership.

## Luggage Cart

Can hold multiple bags.

Bulky and spatially important.

## Linen

Separate:

- clean
- used / dirty

MVP Laundry abstracted.

## Trash

Aggregate into:

- piles
- bags
- full bins

Avoid dozens of tiny physics objects.

## Future

- heavy items
- two-player carry
- containers
- throwing
- fragile items

## MVP Item Types

- Clean Towels
- Coffee
- Clean Linen
- Dirty Linen
- Trash
- Toolbox
- Plunger
- Mop
- Small Suitcase
- Large Suitcase
- Luggage Cart

## Core Principle

Inventory itself should create logistics, not hide logistics.
