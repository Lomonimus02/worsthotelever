# Worst Hotel Ever — Breakdown & Repair System

## Purpose

Breakdowns должны быть контекстными, читаемыми и создавать цепочки последствий.

## Condition

Conceptual scale:

- Excellent
- Good
- Worn
- Poor
- Critical

MVP может использовать сокращённо:

- Good
- Worn
- Poor

## Wear Sources

- normal use
- intensive use
- guest traits
- previous damage
- incomplete / temporary repairs
- events

## Breakdown Is Not The Same As Problem

Technical failure:

**Broken Pipe**

может создать world problem:

**Leak**

которая создаёт:

- Wet Floor
- Guest complaint
- possible room damage
- extra Cleaning task

Это разделение важно.

## Breakdown Categories

- Functional Failure
- Reduced Functionality
- Active Hazard
- Critical Failure

Severity:

- Minor
- Moderate
- Serious
- Critical

## Discovery

Problem can be:

- known by Player
- noticed by Guest
- visible
- hidden until discovered
- detected by future monitoring upgrade

## Repair

Repair Task указывает на source.

MVP tools:

- Toolbox
- Plunger
- Mop

Repair interactions должны быть короткими contextual actions, а не набором уникальных minigames.

## Temporary vs Permanent Fix

Later:

- Temporary Repair
- Permanent Repair
- Replacement
- Preventive Maintenance

MVP может использовать только normal Repair.

## Escalation

Игнорируемая маленькая проблема может стать серьёзной.

Например:

Small Leak  
→ Wet Floor grows  
→ Guest notices  
→ Complaint  
→ Room becomes unusable

## Player Consequences

Guest Satisfaction меняется только если Guest реально:

- увидел проблему;
- испытал её последствия;
- ждал решения.

## Event Director Relationship

Event Director создаёт контекст.

Breakdown System отвечает за реальное техническое состояние.

Director не должен напрямую подменять breakdown logic.

## MVP Repairable Objects

- Sink
- Toilet
- TV
- Lamp
- Elevator

Flagship scenario:

**Sink Leak → Wet Floor → Repair + Mop**

## Core Principle

Игнорируемая мелочь должна иногда превращаться в катастрофу, но цепочка должна быть понятна игроку.
