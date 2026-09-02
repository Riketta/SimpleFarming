# Simple Farming

A RimWorld 1.6 mod that adds a **farming** block to every animal's info card (the "i" / Stats
tab), covering optimal breeding ratios, reproduction timing, food costs and slaughter
efficiency - computed live from each animal's own def data, so vanilla, DLC and modded animals
all work without any per-species hardcoding.

## What the block shows

**Breeding**

| Stat | Meaning |
| ---- | ------- |
| optimal breeding ratio | Males per female that keeps every female reproducing back-to-back. For egg layers that can lay unfertilized eggs, this ratio is only needed when you want hatching. |
| males needed for laying | Egg layers only: "none" (lays edible unfertilized eggs) or "required" (egg progress stalls unfertilized - no male, no eggs at all). |
| time until pregnancy / fertilization | Average days a fertile female waits to conceive (or a hen to be fertilized) when males are around. |
| reproduction cycle | Gestation (or clutch interval) + conception wait. For eggs, the incubation time is shown separately. |
| offspring per female per day | Throughput including the conception/fertilization delay, at the optimal ratio. |
| meat per female per day | Gross meat nutrition from slaughtering grown offspring. |
| leather per female per day | Leather units from the same grown-adult slaughter, with the leather type designated in the value and hyperlinked (hidden for animals with no leather, e.g. chickens). Bonus income - not part of the nutrition math. |
| food per female per day (incl. males) | Breeding-stock upkeep: female food + her share of males. |

**Slaughter** (animals with meat only)

| Stat | Meaning |
| ---- | ------- |
| adult meat nutrition | Meat units x the meat def's nutrition (usually 0.05/unit), at your current difficulty's butcher yield. See the leather row for leather. |
| food to grow one adult | All-in food per offspring grown to adult: mother's gestation + conception wait + growth food + fathers' share. |
| slaughter efficiency (per life stage) | Meat returned at that stage / the same all-in food figure. The headline value of the mod; tooltip shows the full breakdown plus the ex-fathers subtotal. |
| best slaughter age | Stage with the highest efficiency. |
| slaughter pregnant females? | Whether the newborn litter outvalues the feed a half-done pregnancy still costs. |
| net meat per female per day | All-in farm economics: meat income minus offspring growth food minus breeding stock (incl. males). Negative = the operation eats more than it returns. Egg/milk/wool/leather income not counted. |

Every row has a tooltip with the full calculation and the raw def numbers behind it.

## The model

All mechanics mirror the live game code (references are 1.6 sources):

- **Mating**: males seek a fertile female with MTB `race.mateMtbHours`, checked hourly while
  awake (`ThinkNode_ChancePerHour_Mate`), within a short radius (`JobGiver_Mate`). The check
  is `Rand.MTBEventOccurs(mtb, 2500, 2500)`, which fires with probability `1/mtb` per hourly
  check - so `attemptsPerMalePerDay = awakeHours / mtb` is exact, not an approximation
  (capped at one attempt per hour). Awake hours are a mod setting (default 16) because when
  animals actually reach the mate node is a behavioral assumption, not game data.
- **Conception**: each mating impregnates a mammal with a flat **50%** chance; mating an egg
  layer fertilizes her next `eggFertilizationCountMax` eggs with certainty (`PawnUtility.Mated`).
  Every clutch laid from that batch is fertilized whole (`CompEggLayer.ProduceEgg` consumes the
  entire clutch from the batch at once), so one mating covers whole clutches - with the vanilla
  batch of 1, exactly one clutch per mating, even for multi-egg clutches (tortoise 1~3,
  cobra/iguana 1~2). The average wait for a mating amortizes across everything one mating
  covers - a full gestation for mammals, `ceil(batch / avgClutch)` clutches for layers.
- **Gestation/laying**: mammals gestate `gestationPeriodDays` then birth a litter rolled from
  `litterSizeCurve` (average via `Rand.ByCurveAverage`); egg layers lay `eggCountRange` eggs
  every `eggLayIntervalDays` (`CompEggLayer`), and fertilized eggs hatch after the egg def's
  `hatcherDaystoHatch` days. Egg layers with `eggProgressUnfertilizedMax < 1` cannot lay
  unfertilized eggs at all.
- **Time until pregnancy** `T = 1 / (attemptsPerMalePerDay x pregnancyChance)`, and it is
  included - as requested - in the cycle length, throughput, and the mother's food cost.
- **Optimal ratio**: one successful mating sustains `C` days of one female's reproduction
  (`C = gestationPeriodDays` for mammals, `C = ceil(eggFertilizationCountMax / eggsPerClutch) x
  eggInterval` for layers, since each clutch is fertilized whole), so
  `malesPerFemale = 1 / (attempts x chance x C)`. Sanity checks: ibex -> 1 male
  per 3.8 females (wiki: 0.27 males/female), cow -> 1 per 4.4 (wiki: 0.23).
- **Food per day** = life stage `hungerRateFactor` x `race.baseHungerRate` x
  `Need_Food.BaseFoodFallPerTick` x ticks-per-day (= x1.6) for a well-fed animal
  (`Need_Food.BaseHungerRate`). Growth, gestation and egg progress all assume fed animals
  (`PawnUtility.BodyResourceGrowthSpeed`).
- **Meat** = the MeatAmount stat (already scaled by the species' body size) x meat def
  nutrition. Per life stage, the raw (pre-curve) value is scaled by the stage's
  `bodySizeFactor` and then pushed through the stat's `postProcessCurve` - the game's
  minimum-yield boost (core curve `(0,0)(5,14)(40,40)(100000,100000)`: small raw yields get
  lifted toward ~14 meat) - mirroring `StatPart_BodySize` + `StatWorker.FinalizeValue`
  exactly. Without this, small stages are understated: chicken juvenile is 25.9 meat through
  the curve vs 21 linear; ibex newborn 31 vs 28 (the wiki's 31 is the curve value); squirrel
  even as an adult is 31, not 28. Large animals (scaled raw >= 40 at every stage) are exact
  either way. LeatherAmount has the same curve; the leather row is adult-basis and the
  abstract stat already includes it. Combat kills (x0.66) are not modeled.
- **Slaughter efficiency(stage)** = `stageMeatNutrition / (gestationFood + conceptionWaitFood + growthFood)`
  per offspring. Above 100% the animal grows more food than it eats.
- **Pregnant females**: a randomly inspected pregnant female is on average half done, so the
  pregnancy still costs about `0.5 x gestation x motherFood`. Let her give birth when the
  newborn litter's meat beats that. (If you inspect a specific female, later pregnancies are
  even more clearly worth finishing.)

### Deliberate differences from the vanilla dev table / wiki

The wiki's animal husbandry numbers are generated with the game's own dev tool
(`DebugOutputsEconomy.AnimalEconomy`), which has two quirks this mod fixes on purpose:

1. **Growth food off-by-one**: the vanilla helper charges each life-stage interval at the
   *next* stage's hunger rate (e.g. ibex baby months at the juvenile rate), overestimating
   growth food. This mod uses each stage's own rate for the time spent in it, matching what
   the animal actually eats in game.
2. **Conception delay**: the vanilla table assumes females conceive the moment they are
   fertile. This mod adds the average conception wait (from mate MTB and the 50% chance) to
   cycles, throughput and efficiency, per the design goals.

So expect somewhat *lower but stricter* efficiency numbers than the wiki references (e.g.
ibex adult slaughter: ~129% all-in here vs 161.3% on the wiki's ibex page; newborn: ~54%
all-in, ~68% without wait/fathers vs the wiki's 85.6%). Every wiki figure sits somewhere on
this ladder - the differences are exactly the missing factors, not disagreements about the
game:

| Source | Mother gestation | Conception wait | Fathers | Growth food basis | Ibex adult |
| ------ | ---------------- | --------------- | ------- | ---------------- | ---------- |
| This mod (all-in) | yes | yes | yes | own stage x1.6 | 129% |
| This mod (ex-fathers) | yes | yes | no | own stage x1.6 | 145% |
| Wiki butchery table | yes | no | yes | own stage x1.6 | 145% |
| Wiki per-animal page | yes | no | no | own stage x1.6 | 161% |
| Vanilla dev table | yes | no | no | next stage, NO x1.6 | 200% |

(A neat identity makes rows 2-3 agree for mammals: at the optimal ratio, the fathers' food
over one raw gestation cycle equals the mother's conception-wait food (malesPerFemale x
cycleDays = waitDays, by construction of the ratio), so "males, no wait" and "wait, no
males" produce the same number. For no-wait egg layers like chickens the wiki table's
117.4% equals this mod's all-in figure exactly; for mammals its 145.1% matches the
ex-fathers row. The vanilla dev tool's numbers are in hunger-rate units, not nutrition - it
is missing the x1.6 `Need_Food` conversion - which is why it reads ~200% for ibex.)

### Reading the efficiency numbers (all-in accounting)

The **slaughter efficiency** rows are all-in: per offspring produced they count the mother's
whole calendar (gestation + conception wait - a breeding female cycles continuously, so that
is 100% of her time), the fathers' share (breeding males at the optimal ratio eat alongside
her every day), and the offspring's own growth food to that stage. Divide the stage's meat
nutrition by that total. Above 100% the farm grows more food than it eats; the tooltip breaks
the input down line by line and also shows the ex-fathers subtotal.

The **net meat per female per day** row uses the same accounting, expressed per day instead
of per offspring, so the two always agree. Cross-check: chicken adult slaughter at 100%
butcher yield computes to ~117%, exactly the community-standard figure (2.10 meat nutrition
against 1.79 food: 1.45 growth + 0.22 hen gestation + 0.12 rooster share).

The wiki mixes accountings between its own pages: its animal-husbandry butchery table
includes the male share (at each animal's optimal ratio, from its own "males / female"
column) but not the conception wait; its per-animal pages (ibex 161.3%) include neither.
Vanilla's own dev table (DebugOutputsEconomy) additionally omits the x1.6
hunger-to-nutrition conversion and charges each growth interval at the next stage's hunger
rate. So this mod's numbers are always a little lower than those references - by design,
not by error.

Meat numbers use the game's actual MeatAmount stat, which includes the **current difficulty's
butcher yield** (`StatPart_Difficulty_ButcherYield`; e.g. 0.8/0.9 on the harder presets, 1.0
otherwise). The tooltip states the factor whenever it differs from 100%. Chicken example: at
100% yield an adult is worth 2.10 nutrition and nets +0.31/day per hen as meat; at a custom
0.84 yield it is 1.76 and nets -0.02 - same animal, same formulas, different difficulty.

### Feeding assumption: raw feed, stomach size and overeating

All food and efficiency numbers assume animals eat **raw** feed - grass, hay, raw meat -
straight into the animal, with every piece fully absorbed (no waste). The meat output never
changes with feed type; what changes is how much raw nutrition the farm must spend to
deliver that food:

- **Raw pieces** (hay 0.05, raw meat 0.05, grass grazed whole): x1.00 baseline.
- **Kibble** (0.05/piece, 2.0 raw -> 2.5 = x1.25): pieces are tiny, so the full x1.25
  applies to every animal that can eat it.
- **Pemmican** (0.05/piece, 0.5 raw -> 0.8 = x1.60): same, full x1.60.
- **Simple meals** (0.9/meal, 0.5 raw -> 0.9 = x1.80 nominal): the catch - see below.

**The catch: stomach size.** An animal only seeks food when its stomach is below a
diet-dependent level (herbivores 45% full, omnivores/carnivores 30%, egg-eaters 40%), then
eats whole items. A feed item bigger than the free space has its overflow **wasted**, so the
effective multiplier for an item of size N is `nominal x min(1, usableSpace / N)` with
`usableSpace = MaxNutrition x (1 - wantEat)`:

| Animal (adult) | Stomach | Seeks food below | Kibble | Pemmican | Simple meal |
| -------------- | ------- | ---------------- | ------ | -------- | ----------- |
| Cow (2.4) | 2.40 | 45% | x1.25 | x1.60 | x1.80 (fits) |
| Ibex (1.0) | 1.00 | 45% | x1.25 | x1.60 | x1.10 (0.55 fits of 0.9) |
| Turkey (0.6) | 0.60 | 45% | x1.25 | x1.60 | **x0.66 - worse than raw** |
| Chicken (0.3) | 0.30 | 45% | x1.25 | x1.60 | **x0.33 - worse than raw** (0.165 of 0.9 fits) |
| Rat (0.2, omnivore) | 0.20 | 30% | x1.25 | x1.60 | **x0.28 - worse than raw** |

`MaxNutrition` = 1 x body size; want-to-eat is a diet property (45% herbivores, 30%
carnivores/omnivores, 40% egg-eaters). So your exact intuition holds: **small animals gain
nothing from cooked meals - for anything with a stomach under 0.5 nutrition a 0.9 meal is
more than half waste and the ratio gets worse than raw**; meals only pay off for animals
with stomachs of 0.5+ (fully at 1.2+). Kibble and pemmican pieces are small enough to never
waste, which is why they are the reliable way to run a marginal farm at a profit (chicken
meat 105% raw-fed becomes 132% on kibble at a 0.84 butcher yield). The tooltip also marks
feeds the animal's diet refuses outright (wargs vs everything processed, herbivores vs
meat-only feeds).

Also worth knowing: grazing whole plants (0.5 nutrition each) is itself wasteful for small
animals - a chicken only absorbs 0.165 of every 0.5-nutrition plant (67% wasted). The
mod's efficiency rows are absorbed-nutrition based, so they don't charge for that waste;
feeding hay pieces or kibble instead of grazing avoids it entirely.

All of this is computed per animal and listed in the slaughter efficiency tooltips (stomach
size, seek level, usable space, and the effective multiplier per feed, with refusals and
waste warnings).

### Assumptions & limits

- Animals are fed continuously (troughs/hand feeding). Grazing animals overeat whole wild
  plants and waste the overflow - the efficiency rows are absorbed-nutrition based and don't
  charge for that, but the feed list in the efficiency tooltip now quantifies the waste per
  animal (a chicken wastes ~67% of every grazed plant).
- Healthy, fertile, non-sterile adults; no miscarriages; no age fertility falloff.
- "Optimal ratio" assumes the herd is penned together so males always find fertile females;
  males also eat, sleep and wander, so round up in practice.
- Milk, wool and unfertilized-egg nutrition are extra income on top (vanilla already shows
  them in the "animal productivity" block); leather is shown but not counted as food.
- Offspring that you keep as new breeding stock obviously isn't slaughter income.

## Debugging

- **Mod settings** have an *Enable debug logging* toggle: the first time each animal's info
  card is opened, its full calculation is logged (prefixed `[SimpleFarming]`), along with any
  skipped species (genderless, mating disabled, etc.).
- **Dev mode** adds *Debug actions -> Simple Farming -> Log husbandry stats for all animals* -
  one line per loaded animal (ratio, cycle, food split, per-stage efficiencies, verdicts) for
  bulk-checking against the game's own dev tables or modded animals.
- The *hours per day animals seek mates* setting feeds all timing math; keep it at 16 unless
  you have measured otherwise. Changes apply immediately - computed stats are dropped and
  rebuilt with the new value (enable debug logging to see the recomputed lines).

## Files

```
About/                       mod metadata
Defs/                        the "farming" StatCategoryDef (displayOrder 27, right
                             after vanilla's "animal productivity" block)
Languages/English/Keyed/     all labels, values and tooltips
Source/SimpleFarming/        C# source + csproj
Assemblies/                  SimpleFarming.dll + bundled 0Harmony.dll
```

Code layout: `HusbandryModel` (pure math), `HusbandryStats` (StatDrawEntry rows + tooltips),
`HarmonyPatches` (one postfix on `RaceProperties.SpecialDisplayStats` - the single funnel the
info card uses for every animal), `SimpleFarmingMod` (settings), `FarmingLog`,
`FarmingDebugActions`.

## Building

Requires the .NET SDK. The csproj defaults to
`E:\SteamLibrary\steamapps\common\RimWorld`; override with your install path:

```
cd Source/SimpleFarming
dotnet build -p:RimWorldDir="C:\Path\To\RimWorld"
```

Output lands in `Assemblies/SimpleFarming.dll`. The whole `SimpleFarming` folder can be
symlinked/copied into the game's `Mods` directory.
