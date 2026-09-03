# Puppet Master — Project Master Context

> **Status: Living document / working context**
>
> This file exists to give any AI or developer enough shared context to work correctly on the project. It is **not** a frozen specification and must **not** be treated as an immutable contract. The project is intentionally iterative: we build, test, review, revise, and improve continuously. New evidence from gameplay tests, technical constraints, player feedback, visual experiments, monetization considerations, store policy, or better ideas may change any design decision below.
>
> When this document conflicts with a newer explicit instruction from the project owner or a newer documented decision, follow the **newer instruction/decision** and update this file accordingly.

## 1. Project identity

- Game name: **Puppet Master**
- Repository: **Tom-deptrai/Puppet-Master**
- Target platforms: **iOS (App Store)** and **Android (Google Play)**
- Primary goal: build a polished commercial mobile game with strong replayability, fair competitive play, and monetization that does not create pay-to-win advantages.
- Current development approach: AI-assisted development. The project owner is primarily the game director/tester and does not rely on manual programming or 3D-design skills.

## 2. Core game concept

Puppet Master is a **1v1 physics-based fighting game** built around extremely simple touch input but potentially deep player mastery.

Each fighter is a jointed puppet made from simple connected body parts:

- head
- torso
- two arms
- two legs
- physical joints connecting the body
- one or more hand-held weapons depending on the final design

The puppet is not a conventional freely moving fighting-game character.

### Foot and rail constraint

- The two feet are constrained to a **rail / track system**.
- The puppet cannot freely run around the arena.
- The feet can move only within the allowed rail behavior/range defined by gameplay.
- Exact rail geometry, movement range, joint type, and implementation are subject to prototyping and tuning.

### Two control strings

- The core control system uses **two strings/ropes associated directly with the puppet's two legs/feet**.
- The player controls these using **two thumbs**, left and right.
- The fundamental interaction is **pull / hold / release / loosen** rather than traditional fighting-game buttons.
- When both strings become sufficiently tense, the puppet should naturally rise / straighten / stand taller.
- When the strings are loosened, the puppet should naturally lower / collapse / crouch according to the joint and physics system.
- Left/right differences in tension, timing, speed, and release should influence body posture, rotation, momentum, weapon movement, attack angle, defence, and recovery.

The exact mapping from finger motion to string tension is **not yet frozen**. It must be discovered and refined through hands-on prototype testing.

## 3. Combat philosophy

The project should avoid conventional dedicated buttons such as:

- Attack
- Block
- Dodge
- Skill 1 / Skill 2 / Skill 3

The intended design principle is:

> **The player controls the puppet through the two strings; attacks and defensive techniques emerge from physics, timing, posture, momentum, and weapon contact.**

Examples of techniques we currently expect the control system may support include:

- high stance / standing tall
- low stance / lowering the body
- leaning left or right
- horizontal swing
- downward strike
- thrust-like attack
- low sweep
- heavy swing
- quick light attack
- dodge through body positioning
- parry through real weapon collision
- counterattack
- feint
- recovery after a missed attack

These are **desired emergent behaviours**, not necessarily hard-coded animation skills. During prototyping, some may be changed, removed, merged, or replaced if another interaction feels better.

## 4. Fairness and competitive principle

The core competitive goal is:

> **Two players should have access to equivalent combat capability; the better player should win primarily through control skill, timing, reading the opponent, and mastery of the physics system.**

Therefore:

- avoid pay-to-win stats
- avoid purchasable damage/health/speed advantages
- character cosmetics should not create meaningful competitive advantages
- if multiple puppet appearances are used, their gameplay-critical collision and physical balance should remain fair unless a future game mode explicitly introduces balanced asymmetric classes

At present, the preferred direction is **symmetric competitive play**.

## 5. Physics requirements

The game should feel physical and somewhat unpredictable visually, but it must remain **learnable and skill-based**.

Key requirement:

> Physics may look chaotic, but repeated player input must produce sufficiently predictable outcomes for mastery to develop.

Likely implementation principles:

- rigid bodies
- constrained joints
- joint-angle limits
- tuned damping
- controlled mass distribution
- weapon colliders
- meaningful impact velocity
- controlled assistance/stabilization where needed
- avoidance of unstable full-ragdoll behaviour that feels random

Final values and architecture must be established through prototype testing rather than assumed in advance.

## 6. Visual direction — current preference

Current preferred visual direction:

- polished **2.5D / 3D side-view combat presentation**
- camera mainly side-on with a slight perspective angle
- stylized mechanical / wooden / training-puppet aesthetic
- visible physical joints
- clear weapon silhouettes
- clear rails
- clear visual distinction between tense and loose strings
- relatively dark or restrained backgrounds so fighters and weapons remain readable
- strong but controlled impact effects
- mobile landscape layout is currently preferred for two-thumb control and combat readability

Possible presentation effects:

- weapon trails
- sparks on weapon collision
- short hit-stop
- subtle camera shake
- KO slow motion
- concise combat callouts such as PARRY / COUNTER / HEAD HIT when useful

This visual direction is **a working target**, not a fixed art bible. We may change it after real Unity prototypes and device testing.

## 7. Character and environment expansion

Long-term content may include:

### Puppet appearances

Examples:

- wooden puppet
- robot
- samurai
- knight
- ninja
- cyber puppet
- steampunk puppet
- gladiator

Preferred architecture is a shared gameplay base rig / physical template with different visual shells where practical, so cosmetics remain fair and efficient to produce.

### Arenas / scenery

Possible themes include:

- training dojo
- workshop
- medieval arena
- cyber arena
- castle
- pirate setting
- desert temple
- snow environment
- rooftop

Arena visuals should add variety without hurting combat readability or introducing unfair competitive geometry unless explicitly designed as a separate mode.

## 8. Planned game modes and systems

These are **planned directions**, not commitments to build all of them immediately.

Potential long-term systems:

- Training Mode
- AI opponent
- Casual play
- Friend Duel / private room
- Random Online Matchmaking
- Ranked matchmaking
- global and/or country leaderboards
- seasons
- Tournament mode
- replay / highlight system
- cosmetic customization
- multiple arenas
- player statistics and match history

Online PvP is an important long-term goal, but networking should be implemented only after the local physics/combat prototype proves fun and controllable.

## 9. Online multiplayer direction

The current technical direction is that two players should eventually fight from separate devices online.

Likely networking concerns include:

- input synchronization
- authoritative combat state
- physics snapshot synchronization
- interpolation
- client-side responsiveness/prediction where appropriate
- latency handling
- weapon collision consistency
- reconnect/disconnect behavior
- anti-cheat considerations for ranked play

The exact networking stack is **not yet selected** and should not be installed prematurely during early prototype phases.

## 10. Training philosophy

Because the two-string control scheme is unusual, Training Mode should eventually teach players how to understand and master it.

Possible training feedback:

- Good Release
- Too Early
- Too Late
- Perfect Parry
- successful heavy swing
- successful counter

Training should help the player understand cause and effect in the physics system rather than simply teach button combinations.

## 11. Monetization direction

The intended monetization principle is **fair competitive monetization**.

Potential revenue sources:

- rewarded ads
- cosmetic puppet skins
- weapon skins that do not alter competitive stats
- rope/string effects
- victory poses
- KO effects
- optional remove-ads purchase
- cosmetic season pass if the player base eventually supports it

Do not introduce paid combat advantages without an explicit future design decision that supersedes this document.

## 12. Development strategy

Development is intentionally iterative.

The project should proceed approximately through these major milestones:

1. **Puppet feels good** — validate one puppet, rails, two strings, touch control, posture and physics.
2. **Combat feels good** — validate two puppets, weapons, hits, defence, parry/counter potential, and meaningful player skill.
3. **Game becomes presentable** — training, AI, art, UI, effects, characters, arenas.
4. **Online works** — friend duel first, then matchmaking and competitive infrastructure.
5. **Commercial release** — monetization, analytics, QA, store compliance, App Store and Google Play release.

Do **not** build later systems merely because they are listed here. Each major phase should be validated before investing heavily in the next.

## 13. AI collaboration rules

Before making significant gameplay or architecture changes, an AI working on this repository should:

1. Read this file.
2. Inspect the current project state and relevant development log / recent commits.
3. Follow the newest explicit task from the project owner.
4. Do not treat older design ideas as immutable requirements.
5. Avoid implementing unrelated future features prematurely.
6. Prefer the smallest testable implementation for the current phase.
7. Report what was changed, what was tested, and what remains uncertain.
8. Stop at the requested phase boundary rather than automatically continuing.

### Most important rule

> **This is a living project. Gameplay testing has priority over theoretical specifications. If real testing shows that a documented design choice is weak, propose a better solution, test it, and update the documentation after approval.**

## 14. Current project status

At the time this document was created:

- repository has been created
- Phase 0 environment/project initialization is being performed
- core puppet gameplay has not yet been implemented
- all gameplay details remain open to testing and refinement

---

### Document maintenance

Update this file when a meaningful project-level decision changes. Do not rewrite it for every minor implementation detail. Detailed technical changes and day-to-day work should go into the development log instead.
