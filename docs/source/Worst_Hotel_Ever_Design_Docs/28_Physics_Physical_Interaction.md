# Worst Hotel Ever — Physics & Physical Interaction Design

## Purpose

Physics is a core gameplay layer.

It should create:

- co-op interaction
- logistics
- comedy
- spatial consequences

without becoming unstable sandbox chaos.

## Core Philosophy

**Controlled physical comedy.**

## Physical vs Simulated

Physical:

- Luggage
- Towels
- Toolbox
- Mop
- Trash Bag
- Coffee
- Carts

Simulated / controlled:

- Bed
- Sink
- most furniture
- flowing Water
- electrical state

Not everything needs Rigidbody.

## Player Movement

- responsive
- fast
- slightly exaggerated
- short acceleration / deceleration
- quick turning

Avoid heavy realism.

## Carry Movement

Light:
- almost normal speed

Bulky:
- some slowdown

Heavy:
- stronger slowdown later

## Collision

Player vs Player:

- soft push
- no hard trapping

Player vs Guest:

- Guest may yield
- small reactions
- no flying NPCs

## Ragdoll

Use as rare punctuation:

- slip
- stronger impact

Not normal movement.

## Pick Up

Focus  
→ highlight  
→ Pick Up  
→ stable carry position

Avoid pixel hunting.

## Held Objects

Use controlled carry rather than fully loose physics constraint.

## Carry Sizes

- Small
- Medium
- Bulky
- Heavy later

## Small Stacks

Example:

3 Towels.

Still visible physically.

## Drop vs Place

Drop:
- free release

Place:
- controlled placement / snap

## Snap Points

Useful for:

- Tool Rack
- Cart
- Linen Shelf
- Coffee Counter

## Throwing

Good future co-op mechanic, but not first MVP.

If added:

- predictable trajectory
- generous catch
- failure drops item

## Fragility

Only selected items.

Coffee can spill.

Not everything should shatter.

## Water

No real fluid simulation.

Leak creates:

- source
- Wet Floor area
- stylized puddle
- spread state

## Wet Floor

Can grow:

Small  
→ Medium  
→ Large

Mop reduces / removes.

If Leak remains active, water returns.

## Slipping

Rare and short.

Can cause:

- delay
- dropped Item
- Guest annoyance

No health system needed.

## Carts

Important physical object.

Use controlled wheel-like movement.

Needs:

- inertia
- bulk
- collision
- stability

## Cart Items

Use secure slots.

Do not let baggage explode from the cart constantly.

## Hallway Blocking

Large objects can create temporary obstruction.

Navigation should:

- repath
- wait
- recover

## Suitcases

Physical and ownable.

Large Suitcase can be bulkier / slower.

## Tools

Toolbox physical object providing Capability.

No need to simulate individual screwdrivers.

## Linen

Use bundles / stacks.

No cloth physics.

## Bed

Static furniture.

Changing linen = short interaction + state / mesh swap.

## Trash

Aggregate into bags / piles.

## Doors

Controlled doors.

States:

- Open
- Closed
- Locked
- Broken

Avoid free hinge physics by default.

## Furniture

Most permanent furniture static.

Movable furniture limited / Preparation-focused later.

## Heavy Objects

Later:

- drag solo
- carry with two Players

## Weight Categories

Use gameplay categories, not kilograms.

- Light
- Normal
- Bulky
- Heavy

## Recovery

Critical Items must not be permanently lost.

If out-of-bounds:

- restore to safe point
- Storage
- last safe position

## Physics Density

Limit loose-object spam.

Use:

- stacks
- containers
- aggregation
- sleeping

## Interaction Assist

- interaction cone
- pickup magnetism
- placement snapping

Controller-friendly.

## Audio

Important:

- thump
- cart rattle
- tool drop
- splash
- collision
- door sound

## Animation + Physics Hybrid

**Animation controls intention. Physics controls consequence.**

## Multiplayer

One primary holder per object.

Resolve simultaneous grabs deterministically.

Avoid precision-critical physics gameplay because of network latency.

## Physics Importance Categories

### A — Critical Controlled
- Toolbox
- Luggage
- Carts
- delivery Items

### B — Gameplay Physical
- Coffee
- Trash Bags
- Towels
- movable props

### C — Cosmetic Physics
- optional debris
- decoration

## MVP Physics

Player:
- movement
- soft collision
- carry

Items:
- Pick Up
- Carry
- Drop
- Place
- stacks

Objects:
- Suitcase
- Toolbox
- Towels
- Linen
- Trash
- Coffee
- Mop

Cart:
- one Luggage Cart
- controlled movement
- item slots

Environment:
- controlled doors
- stable furniture

Water:
- Leak
- Wet Floor
- Mop
- simple Slip optional

## Not First MVP

- Throw / Catch
- co-op heavy carry
- full ragdolls
- breakable furniture
- movable everything
- cloth sim
- destructible Hotel
- fluid sim

## Prototype Test

2 Players  
+ Lobby/Hallway/Room  
+ Suitcase  
+ Toolbox  
+ Cart  
+ Towel  
+ Doorway

Question:

**Is it fun to move, carry, pass and accidentally interfere?**

## Core Principle

Physics should create **unexpected situations**, not **unpredictable rules**.
