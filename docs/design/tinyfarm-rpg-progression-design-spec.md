# TinyFarm RPG progression design proposal

Status: approved initial design, subject to refinement. [Gate A1](../milestones/tinyfarm-rpg-gate-a1-report.md) implements the agent properties screen and authored profile foundation; skill training and gameplay modifiers remain pending.

This extends the [MVP design](tinyfarm-mvp-design-spec.md) and the existing [agent authoring contract](../research/tinyfarm-agent-authoring.md). The new request supersedes the MVP's deferral of skill XP and skill levels. It does not expand the opening into a complete crafting, spell, or campaign implementation.

## 1. Design thesis

**What an agent does develops its skills. Skills develop its permanent abilities. Traits make it distinctive. Conditions describe what is happening to it now.**

The farm and the adventure strengthen each other: farming helps build a sturdy swordsman; preparing meals can develop fire affinity; exploring improves agility. Specialization still matters because skill mastery improves the relevant activity directly, beyond its contribution to general stats.

There are no allocated stat points, random per-use stat increases, or mandatory grinding gates. Reading enemy attacks and preparing useful food should matter more than repeatedly raising numbers. The first ten to twenty minutes must remain enjoyable with starter abilities.

All four systems belong to the Dominatus agent. A human controller and an AI controller issue actions against the same rules. An agent's progression eligibility is authored independently of its current controller. Possessing Mara must preserve Mara's skills, injuries, equipment, and identity; leaving her under AI control must not turn her into a different character.

Objects use the same agent foundation with only applicable capabilities. A chest may have inventory, a trait such as `Locked`, and a burning condition if explicitly supported; it does not need six character stats or a sleeping skill.

## 2. Stats: foundation and derived capabilities

Keep six familiar ability scores. Ten is the ordinary starting reference, not a hard requirement for every species or agent.

| Ability | Main effects | Typical training |
|---|---|---|
| Strength | Physical attack power and demanding work efficiency | Weapons, farming, forging, woodcutting |
| Agility | Modest walking speed bonus and movement control | Traversal, light weapons, fishing |
| Constitution | Maximum HP and resistance to physical exhaustion | Farming, heavy weapons, recovery habits |
| Intelligence | Elemental technique power and technical crafting aptitude | Attunement, alchemy, forging, tailoring |
| Wisdom | Maximum SP and recovery/support effectiveness | Attunement, foraging, cooking, restorative habits |
| Charisma | Social aptitude and authored persuasion opportunities | Meaningful conversations, negotiation, cooperation |

Add **Social Insight** as a small life skill so Charisma has a deliberate training route. It earns practice from resolved social situations, not repeated greetings. It is not required in the first implementation.

Ability scores are inputs, not substitutes for skills. A strong novice can swing hard but cannot automatically forge a fine sword. A trained cook can make a good meal without becoming an equally skilled alchemist.

An agent has authored base abilities, skill ranks, equipment, trait instances, condition instances, and current resource pools. Its effective abilities and combat/work values are derived. Persist their inputs, rather than an independently editable copy of the derived totals.

### HP, SP, and work energy

- **HP:** bodily health. Zero HP enters the defeat/knockout policy.
- **SP (Spirit Points):** supernatural exertion. A technique declares its SP cost; a failed affordability check has no effect or practice reward.
- **Work energy:** physical capacity for farm chores, using the current opening's energy owner. It remains separate from SP. Running out of spirit must not prevent ordinary gardening or escape.

For the first implementation, preserve the existing free sword/dodge and free, cooldown-based Briar Charm. Do not make a required traversal ability unavailable because SP ran out. Reserve SP for subsequently introduced optional techniques; do not invent a spell just to justify a new bar.

### Initial balance model

These are starting tuning values, not fixed engine constants:

| Derived value | Starting rule |
|---|---|
| Maximum HP | Authored base HP + 2 HP per Constitution above the agent's authored baseline |
| Maximum SP | Authored base SP + 3 SP per Wisdom above its authored baseline |
| Physical power | +5% per Strength above baseline, applied to weapon base power |
| Elemental power | +5% per Intelligence above baseline, then the relevant attunement bonus |
| Recovery power | +3% per Wisdom above baseline; affects authored healing, not all food indiscriminately |
| Walking speed | +1% per Agility above baseline; final bonus bounded to +15% for the opening |
| Work cost | Constitution can reduce eligible costs by up to 15%; positive-cost work still costs at least one unit |

Signed changes below baseline reduce the corresponding value. Definitions declare legal minima; HP maximum is at least one and SP maximum is nonnegative. Do not derive attack cadence, dodge invulnerability, collision footprint, or interaction reach from stats initially. Those are readability and spatial contracts.

Use integer fixed-point calculations. Damage/healing use hundredths of an HP internally so a small power increase is real rather than rounded away on every two-HP hit. Convert legacy whole-HP values exactly at the migration boundary. Round only at explicitly declared boundaries and display appropriately; never use frame-dependent floating accumulation for progression.

Charisma contributes to named authored checks or utility considerations. Dialogue checks should identify the relevant skill, relationship, trait, and context, with visible requirements where appropriate. Charisma does not override consent, faction policy, or every NPC's personality. Store conversation outcomes as ordinary semantic results, rather than letting the dialogue UI award XP.

## 3. Skills: practice, mastery, and growth

### Catalog direction

| Family | Skills |
|---|---|
| Cultivation and gathering | Farming, Woodcutting, Foraging, Fishing |
| Craft | Cooking, Forging, Alchemy, Carpentry, Leatherworking, Tailoring |
| Life | Nourishment (eating), Rest (sleeping), Traversal (walking/agility), Social Insight |
| Weapons | Sword, Axe, Hammer, Scythe; further weapon skills when those weapons exist |
| Attunement | Water, Fire, Earth, Storm, Wood, Light, Dark, Aether |

The catalog is an authoring direction, not a promise to implement all activities immediately. Each skill definition declares practice rules, stat affinities, mastery effects, and any unlock requirements. Missing gameplay cannot be simulated by an ornamental progress bar.

The same implement can have different action contexts. An axe hitting a tree trains Woodcutting; an axe damaging an enemy trains Axe. It does not automatically award full credit to both. Farming with a scythe does not train combat technique merely because the equipment is a scythe.

### Practice comes from resolved outcomes

Award practice after a meaningful, successful semantic outcome. Pressing a button, starting an animation, or spending SP is insufficient. An action can train several skills if its authored context justifies them.

Illustrative starting awards:

| Resolved outcome | Practice awards | Qualification |
|---|---|---|
| Plant a seed | Farming 2 XP | Seed consumed and previously unplanted plot changed |
| Water a crop | Farming 1 XP | That crop's daily watering obligation was actually satisfied |
| Harvest a crop | Farming 5 XP | Mature crop collected through a successful inventory transaction |
| Gather a wild ingredient | Foraging 3 XP | Resource node actually depleted |
| Fell a marked sapling | Woodcutting 5 XP | Credit for completed resource outcome, not each repeated swing |
| Cook a stove meal | Cooking 5 XP + Fire 1 XP | Recipe explicitly declares heated processing; output produced |
| Forge a heated workpiece | Forging practice + Fire practice | Future recipe declares heat exposure; no passive forge proximity XP |
| Damage a hostile with a sword | Sword practice | Encounter budget apportioned by effective damage, including partial HP |
| Resolve a water technique | Water practice | Useful eligible target affected; no empty casting reward |
| Eat a meal | Nourishment practice | Actual recovery or first useful food effect; amount capped by need |
| Sleep | Rest practice | Eligible recovery/rest window consumed; repeated immediate sleep earns none |
| Explore on foot | Traversal practice | New route coverage during meaningful activity, not wall pushing or laps |

Heat does not imply Fire XP for every nearby action. Likewise, standing in rain does not train Water. Cross-training is explicit in the activity/recipe definition. A future cold preparation may grant Cooking with no Fire, or an elemental recipe may train a different affinity.

Each outcome has a semantic identity, such as the resolved recipe transaction, crop cycle, or encounter. Credit is awarded once. Accepted partial credit and remaining budgets are semantic state and survive saves. Batched crafting multiplies eligible units subject to the same rules as individual crafting; it must not receive an accidental multiplier.

Combat has a finite practice budget per eligible encounter, distributed by effective contribution. Healing and repeatedly damaging the same enemy cannot create additional budget. Player-spawned/training targets grant none unless deliberately authored. Renewable content may supply new eligible encounters, but respawn farming is never required by the campaign.

Life skills use bounded practice windows. Nourishment credits actual deficit recovery, capped per calendar day; refreshing the same food condition does not repeatedly pay. Rest credits one normal overnight recovery, including a small capped credit for a valid full rest even when uninjured. Traversal credits newly visited route cells per day, subject to a daily cap and successful gameplay activity since the previous credit window. The visited set/budget is saved. These rules prevent idle loops without inventing a universal anti-cheat system.

Attunement from crafts is deliberately slower than directly practicing elemental techniques. A cook can become fire-attuned, but stove work does not teach spell targeting or unlock every fire technique automatically.

### Rank curve and mastery

Start at rank 0; first shipping cap is rank 50. Cost from rank `r` to `r + 1` is initially `25 + 10r + 5r²` XP. Keep remainder XP and resolve multiple rank gains deterministically. Costs begin 25, 40, 65, 100, 145; early progress is visible while late mastery takes sustained activity.

Skill rank has three uses:

1. Contribute to permanent abilities through declared affinities.
2. Improve the specific activity through bounded mastery effects, such as sword power, recipe yield predictability, or work efficiency.
3. Meet authored recipe/technique requirements when the relevant content exists.

Initial sword mastery adds 1% weapon power per rank, capped at +20% for the opening. Craft mastery must not introduce random quality, free infinite ingredients, or new recipes before those systems exist. Show the benefit a rank actually provides; a first implementation can use stat contribution plus a small explicit efficiency effect.

### Skill-to-stat contributions

Affinities are integer weights from 0 to 100. A weight of 100 grants one ability point per ten effective skill ranks; 50 grants half that contribution. Accumulate fractional contributions across skills before taking the integer result.

`effectiveRank(r) = min(r, 20) + floor(max(r - 20, 0) / 3)`

`skillBonus(stat) = min(12, floor(sum(affinity(skill, stat) * effectiveRank(skillRank)) / 1000))`

The +12 cap is per ability and applies only to skill growth. Diminishing high-rank contribution keeps mastery from causing unchecked broad stat inflation. Rank-specific activity benefits remain separate. Caps and affinities are named content policy with inspection output, not hidden renderer math.

Initial affinities for the first relevant skills:

| Skill | STR | AGI | CON | INT | WIS | CHA |
|---|---:|---:|---:|---:|---:|---:|
| Farming | 100 | 0 | 100 | 0 | 25 | 0 |
| Sword | 100 | 50 | 50 | 0 | 0 | 0 |
| Woodcutting | 100 | 0 | 100 | 0 | 0 | 0 |
| Foraging | 0 | 50 | 0 | 0 | 100 | 0 |
| Cooking | 0 | 0 | 25 | 50 | 100 | 0 |
| Fire | 0 | 0 | 0 | 100 | 50 | 0 |
| Traversal | 0 | 100 | 50 | 0 | 0 | 0 |
| Nourishment | 0 | 0 | 100 | 0 | 25 | 0 |
| Rest | 0 | 0 | 50 | 0 | 100 | 0 |
| Social Insight | 0 | 0 | 0 | 0 | 50 | 100 |

Other skill affinities are authored when their real activity enters the game. Equipment, conditions, and trait bonuses never become inputs to this growth formula; that would produce recursive or permanent buff exploits.

Example: Farming 10 and Sword 10 grant +2 Strength, +1 Constitution (1.5 rounded down), and no Agility point yet (0.5 accumulated). Adding Woodcutting 5 raises Constitution to +2. Those same agents still differ from someone with Sword 25 because sword mastery is independent of general Strength.

Cooking 10 and Fire 10 together grant +1 Intelligence (1.5 rounded down) and +1 Wisdom (1.5 rounded down). A Fire talent can further improve fire damage without increasing these permanent ability contributions.

### Character level

Level summarizes development; it is not a separate XP track or another source of stat points.

For progression-enabled agents: `level = authoredBaseLevel + floor(totalEarnedSkillRanks / 5)`, initially capped at 30. Count ranks earned after the authored starting profile, not starting ranks granted by a template. Fixed-profile enemies/NPCs retain their authored level. Level never automatically scales enemies or replaces the actual ability/capability checks.

This means broad life experience can raise level. That is intentional: compare combat readiness using actual weapon mastery, equipment, abilities, and conditions, rather than assuming every level-5 agent is equally dangerous. Tune the summary threshold after observing real opening play.

## 4. Traits: durable identity with explicit behavior

A trait has a stable definition ID, acquisition source, and optional parameters. Innate traits are fixed; acquired or removable traits change only through an explicit semantic operation. A trait is not a condition merely because it can eventually be removed.

Three mechanisms cover the desired range:

| Mechanism | Example | Owned behavior |
|---|---|---|
| Typed modifier | Fireborn: +30% fire damage; Medic: +20% eligible healing | Modifier evaluation on the corresponding action |
| Capability or identity tag | Gingerbread body; understands aquatic speech | Queries used by explicitly authored interactions |
| Named typed rule | Aquatic authority grants a command interaction with eligible creatures | Resolver handler with declared targets, costs, limits, and resulting intents |

Gingerbread can combine faster movement, a species/material identity, and an authored vulnerability. Being delicious does not implicitly make every NPC eat the agent; an encounter must declare how it responds to that identity.

Nimbus-style authority can grant aquatic command eligibility and a police faction standing. Police allegiance is an authored faction/interaction rule, not universal mind control via a stat modifier. Those hooks can exist later without building those factions now.

Traits describe why an agent differs, not just a pile of multipliers. Definitions require a short player-facing explanation and inspectable provenance. Innate traits do not emerge randomly from normal skill practice unless a specific milestone explicitly awards one.

## 5. Conditions: temporary state and removal rules

A condition instance holds definition ID, source agent/action, potency, stack count, start tick, and its declared expiry/removal state. Definitions own effects and stacking policy. A string such as `poisoned` cannot carry enough authority for this system.

| Condition | Effect direction | Removal | Initial stacking policy |
|---|---|---|---|
| Well-fed | Small eligible work/recovery benefit | Active-play duration expires | One food benefit group; stronger food replaces, equal food refreshes within cap |
| Rested | Modest work benefit | Authored daily rest window ends | One instance; repeated sleeping does not stack |
| Tired | Reduced work efficiency; no blocked escape | Adequate rest/recovery predicate | One severity value, bounded |
| Poisoned | Periodic HP damage | Duration or explicit antidote/dispel | One instance per poison group; stronger potency replaces; bounded refresh |
| Burned | Brief periodic HP damage | Duration or eligible extinguishing action | One instance, bounded refresh |
| Angry | Contextual utility/social considerations | Time or explicit reconciliation | One instance; not forced player input |
| Cursed | Named penalty/capability restriction | Matching dispel key or quest resolution | No automatic time expiry unless declared |
| Knocked out | Agent cannot perform ordinary actions | Authoritative recovery/defeat transition | Derived from zero HP, never a freely removable timed buff |

Conditions may combine modifiers, periodic effects, and action restrictions. Keep those effects typed and supported by explicit handlers. A restriction such as stun is checked by action admission, not simulated by setting movement speed to zero alone.

Use three declared clock policies: active simulation ticks, calendar boundary, or explicit predicate/action. Active poison and burn continue in a dungeon whose calendar is frozen. Menus, dialogue, pause, and focus loss freeze active-play durations. Sleep advances the calendar and resolves its recovery transaction; it does not blindly run thousands of combat ticks. Ordinary poison/burn are cleared by the opening's sleep policy; curses persist unless their rule says otherwise. Offline wall-clock time changes nothing.

Periodic effects resolve in deterministic tick and instance-ID order. Reapplication does not reset the next damage tick, which would allow continuous refresh to suppress damage. Do not mutate a collection while enumerating it; collect and resolve condition outcomes through the existing semantic transaction path.

## 6. Modifier composition and resource rules

Evaluate in one declared order:

1. Authored base abilities + bounded skill contributions = permanent abilities.
2. Flat equipment, trait, and condition ability changes = effective abilities.
3. Derive action/resource values from effective abilities and equipment base values.
4. Apply flat derived-value changes, then sum eligible percentage changes in basis points.
5. Apply declared clamps and fixed-point rounding once at the value's boundary.
6. Resolve typed overrides/restrictions by explicit priority; reject equal-priority conflicting definitions during content validation.

Percentage changes add within a value, rather than repeatedly multiplying in arbitrary source order. A +30% fire trait and +10% fire condition give +40% fire power. Resistance is a separate defender-side value with its own clamp; damage resolution computes outgoing power, then defender mitigation, once. No generic resistance table needs shipping in the first slice.

When maximum HP/SP changes, preserve the current absolute pool and clamp it to the new maximum. Raising a maximum does not heal; removing a maximum bonus cannot leave the pool above its limit. Food/sleep healing remains an explicit effect. This prevents equipment swapping and buff expiry from generating free recovery. The inspector shows base, skill, equipment, trait, and condition contributions separately.

At zero HP, route through one defeat policy. The current opening's return/recovery behavior remains the player policy. NPC/enemy policies may differ, but none may simultaneously retain zero HP and issue normal actions because a condition label was removed.

## 7. Authority, authoring, and migration

Keep semantic truth in TinyFarm Core and the existing resolver. Dominatus controllers choose intents; the runtime coordinates time/persistence; Aurelian and Machina present the result. There is no renderer-owned XP and no parallel RPG save file.

Definitions should author base abilities, starting skill profiles, progression eligibility, trait references, supported condition references, and capability requirements through the existing template/spawn path. Definitions are immutable shared content; skill XP, pools, trait acquisitions, and condition instances belong to the spawned agent.

Use typed C# records/enums or validated generated IDs, source-generated serialization, and explicit handler registration. No permanent runtime reflection, arbitrary method-name lookup, or new general-purpose effect scripting language. Extract engine-level reusable primitives only when the game has demonstrated their real use; TinyFarm's skill catalog and balancing stay game content.

The intended reducer flow is: validate intent and capabilities -> resolve action and inventory/resource changes -> record eligible practice from its outcomes -> advance skills -> derive changed values -> apply conditions/defeat and emit presentation effects. These steps commit atomically. A failed harvest due to a full bag changes neither crop nor practice. XP from an action cannot retroactively make that same action affordable or increase its already-resolved damage.

Current migration seams are explicit:

- `TinyFarmSliceState` still owns Gardener combat HP; authored agent health is not yet the combat owner for every agent.
- Equipment has a legacy world-level owner alongside authored-agent data. Consolidate behind one authoritative agent access path before making equipment-dependent RPG calculations universal.
- Authored `Level` and condition strings are persisted metadata, not an implemented growth/effect system.
- Existing NPC energy is not proof of a universal RPG resource model.

Migrate the opening player and NPCs to the common owner with versioned legacy-save adapters. Preserve existing 12-HP starter combat, current gear, and defeat behavior. Do not keep independently mutable legacy and new HP/equipment/level copies. Unknown old condition strings remain explicit unsupported metadata until mapped; never silently invent an effect for them.

Ability inputs, XP, credit budgets, progression eligibility, pools, active conditions, and acquisitions participate in semantic save/replay state. Derived values use the versioned definition identity. HUD choices and presentation scale remain excluded. If balance definitions change, the save compatibility policy must explicitly migrate or reject that content version.

## 8. First implementation slice

Build this in bounded steps against the existing farm -> food -> slime -> home loop:

1. **Common agent RPG owner.** Base abilities, HP/SP capacities, typed condition instances, template validation, inspector provenance, and legacy migration. Preserve starter behavior before adding growth.
2. **Practice and durable growth.** Farming, Foraging, Woodcutting, Cooking, Sword, and Fire. Cooking is the first real elemental cross-training example. Ship the declared XP curve and stat contribution rules with a readable details view using the existing inventory/UI infrastructure.
3. **One identity and two conditions.** One authored agent gets `Green Thumb` (+10% eligible farming work efficiency), another `Hearthwise` (+20% eligible food healing produced by that agent). Add Well-fed and Tired; exercise replacement, expiry, sleep removal, and attribution. Healing provenance travels with the produced food, so switching control or transferring the meal cannot erase or duplicate the cook's contribution.

Life-skill accrual, Social Insight, harmful periodic conditions, eight spell schools, and the remaining crafting professions follow only when their activities need them. Definitions and clocks must accommodate them; the first slice need not implement unsupported effects. SP can be inspected without adding a HUD meter until the player has an SP-consuming action.

Suggested opening target: roughly two to four total rank gains across several skills during a normal ten-to-twenty-minute loop, one useful food condition, and a clear explanation of where growth will lead. Avoid accelerating the economy or stat growth solely to force an ability point into the first session. Full-stat progress can be fractional in the details view before a whole point is earned.

Hearthwise is an example requiring food provenance; if that owner is not yet ready, defer the trait rather than attaching it to whoever happens to eat. The first capability gate is shared-agent correctness, not collecting feature labels.

## 9. Qualification examples

The implementation should prove these through the actual reducer and save/load path:

- Gardener and Mara can resolve the same eligible action; progression permission comes from their profiles, not human/AI control. Existing controller-switch limitations remain separately reported until possession exists.
- Watering the same crop twice earns practice once; rejected/full-inventory harvesting earns none.
- Heated cooking grants Cooking and Fire once; cold preparation grants only its declared skills.
- Skill ranks produce the contribution examples above and preserve remainder fractions; temporary stat boosts do not alter permanent growth.
- A successful hit awards only its share of an encounter budget; enemy healing/reload of the same committed state cannot duplicate credit.
- Modifier insertion order produces identical results. Max-HP equipment swapping cannot heal.
- A condition survives save/load with its remaining duration, next periodic tick, and source. Pausing freezes it; dungeon calendar freeze does not freeze active effects.
- Curse removal requires the correct semantic dispel rule. Removing a knockout display marker cannot revive a zero-HP agent.
- Identity/capability traits authorize only supported target interactions; an unsupported Nimbus behavior fails content validation clearly.
- The legacy opening keeps its starter HP, damage, collision, inventory, and return policy through migration. Existing replays retain their declared compatibility path; new RPG replays are deterministic under the new version.

## 10. Product decision

Adopt **use-based, shared-agent progression with bounded cross-training**, six abilities, activity-specific mastery, authored identity traits, and typed temporary conditions. Keep character level descriptive and make sources of every effective value inspectable.

The first pressure is making common agent state and the existing opening actions agree. It is not a comprehensive magic system, profession catalog, passive buff framework, or new character-menu architecture.
