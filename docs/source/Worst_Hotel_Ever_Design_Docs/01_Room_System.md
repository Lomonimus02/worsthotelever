# Worst Hotel Ever — Room System Scope

## Purpose

Room — постоянная gameplay-сущность и один из главных источников работы, денег и проблем.

## Independent Room Dimensions

Не использовать один giant RoomState. Комната состоит из независимых характеристик:

- Occupancy: Vacant / Reserved / Occupied / Out of Service
- Readiness: Ready / Not Ready, derived state
- Cleanliness
- Condition
- Guest link
- Quality
- Problems
- Equipment
- Items

## Typical Equipment

- Bed
- Sink
- Toilet
- Shower
- Lamp
- TV
- Trash Bin
- Towels
- Door

Каждый объект имеет своё состояние.

## Room Ready

Ready не должен быть hard-coded под конкретные объекты навсегда.

MVP conditions:

- Bed prepared
- No critical trash
- Towels present
- Critical equipment functional
- No critical problems
- Room is not Out of Service

В будущем Ready должен строиться через configurable requirements.

## Occupancy Lifecycle

Vacant + Ready  
→ Reservation  
→ Check-in  
→ Occupied  
→ use / wear / requests / problems  
→ Check-out  
→ Vacant + Not Ready  
→ cleaning / repair  
→ Ready

## Cleaning and Turnover

После Guest stay Room не reset'ится.

Она может содержать:

- Used Bed
- Dirty Linen
- Used Towels
- Trash
- Wet Floor
- Damage / Problems

Daily Service позже может быть легче, чем полный checkout turnover.

## Multi-Day Stays

Guest остаётся связан с Room несколько дней.

Состояние комнаты меняется постепенно:

- dirt;
- wear;
- requests;
- breakdown risk.

## Quality

Room Quality формируется из:

- Bed quality
- Bathroom
- Furniture
- Room size
- Amenities
- Equipment state
- General condition

Категории вроде Budget / Standard / Deluxe / Suite должны по возможности **выводиться из конфигурации**, а не просто выбираться кнопкой.

## Condition and Wear

Комната и её equipment накапливают wear.

Wear увеличивает вероятность проблем и поломок.

Dirt и Wear — разные вещи:

- Dirt → cleaning
- Wear → repair / maintenance / replacement

## Imperfect Rooms

Не все проблемы должны автоматически блокировать Room.

Можно сознательно заселить Guest в несовершенную комнату.

Но critical conditions переводят Room в Out of Service.

## Guest / Hotel / Trash / Lost Items

Items внутри Room должны иметь ownership category:

- Guest property
- Hotel property
- Consumable
- Trash
- Lost property later

## Doors

MVP:

- Open
- Closed
- Locked / inaccessible where needed

Advanced keycards/privacy later.

## MVP Scope

- Vacant / Occupied / Out of Service
- Guest assignment
- Check-in / Check-out
- Derived Ready
- Bed / Trash / Towels / Sink
- Cleaning
- Equipment wear
- Simple breakdowns
- Non-critical imperfect room rentals
- Room requests
- Persistent reuse

## Deferred

- complex privacy system
- keycards
- theft
- free construction
- deep interior editor
- detailed Daily Service / DND
