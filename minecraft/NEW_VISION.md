# Antithesis Conquering Simulator

> You're in your spaceship. You go to randomly generated planets. You kill things.

---

## Design Pillars

- All combat, no filler
- Fun over narrative
- Still descending
- Darker palette + bright electronic enemies
- Antithesis aesthetic
- Destructible world

---

## Disqualifiers

1. No Cube Space redux — this is a distinct game.
2. Players love the combat and fun — not the world they're impacting.
3. Focus is all combat. No side quests, escort missions, or filler.

---

## Game Loop — Inscription-like Structure

**Choose 1 of 3 worlds → Easy / Medium / Hard → Clear / Survive it → Get upgrades → Next world → Boss**

- Planet map visible throughout
- Demo = one map
- Future: multiple bosses, per-galaxy theming, an actual ending

---

## Win Conditions

| Type | Description |
|---|---|
| **Exploration** | Kill X things across caves and the map |
| **Survival** | Survive X minutes vs. a massive creature or swarm |
| **Combat** | Kill everything — or reach the core |

---

## World Customization

Specify aspects of your world:

- Enemy hostility
- Environment hostility
- World modifiers
- **Terrain density** *(visible on planet select — signals how much geometric freedom the player starts with)*
- Gravity

> 10 planets need to be wiped out. Worlds are pre-generated from the above parameters. Entry via crashlanding.

---

## Weaponry

### Laser Arm
Can be used for both major laser attacks and jackhammer bounces.

### Mech Wings
Double jump and directional dash.

### Flexible Left Arm
Grappling hook that grapples to walls and enemies. Release grapple to lunge toward target.

---

## Movement & Geometry Philosophy

The base kit is designed to make the world feel open without accessories. The Jackhammer's impact **destroys a small radius of blocks by default** — not just the targeted block. This gives every run a baseline of passive arena sculpting, regardless of accessory choices. Destruction accessories (Explosive Bounce, Destructive Laser, Super Slam, Dig Dig Dig) amplify this behavior rather than introduce it from scratch.

Combat-only accessory builds are fully viable. Terrain density is a visible planet modifier so players can make informed tradeoffs between combat power and geometric agency.

---

## Accessories

1. Super Jump
2. Super Slam *(amplifies jackhammer impact radius and damage)*
3. Explosive Bounce *(jackhammer release triggers explosion at impact point)*
4. Destructive Laser *(laser also destroys blocks)*
5. Little Friend
6. Glide
7. Dig Dig Dig! *(jackhammer mines blocks faster; vertical escape tool)*
8. Flaming Grapple *(plus extra fire on pull)*
9. Tech Vision
10. Exo Suit *(mobility)*

---

## Pros and Cons

### Pros
- High replay value
- Unique experience per run
- Progression with landmarks
- No world attachment — pure destructive capability

### Cons
- Randomness can break pacing
- "The Encounter" problem: a good run can be ruined by a poorly balanced final boss
- Loop may feel repetitive with limited win conditions
