# Simple Farming

A RimWorld 1.6 mod that adds a **farming** block to every animal's info card (the "i" / Stats
tab): optimal breeding ratios, reproduction timing, food costs and slaughter nutrition
efficiency - computed live from each animal's own def data, so vanilla, DLC and modded
animals all work without any per-species hardcoding.

## What the block shows

**Breeding**

| Stat | Meaning |
| ---- | ------- |
| optimal breeding ratio | Males per female that keeps every female reproducing back-to-back. For egg layers that can lay unfertilized eggs, males are only needed when you want the eggs to hatch. |
| males needed for laying | Egg layers only: "none - lays unfertilized eggs" or "required - won't lay unfertilized". |
| time until pregnancy / fertilization | Average days a fertile female waits to conceive (or a hen to be fertilized) when males are around. |
| reproduction cycle | Gestation (or clutch interval) plus the conception wait. Egg incubation is shown separately. |
| offspring per female per day | Throughput including the conception/fertilization delay, at the optimal ratio. |
| meat per female per day | Gross meat nutrition from slaughtering grown offspring. |
| leather per female per day | Leather from the same slaughter, with the leather type hyperlinked. Hidden for animals without leather. Bonus income - not part of the nutrition math. |
| food per female per day (incl. males) | Breeding-stock upkeep: raw nutrition for the female plus her share of males, at the assumed feed. |

**Slaughter** (animals with meat only)

| Stat | Meaning |
| ---- | ------- |
| adult meat nutrition | Meat units x the meat def's nutrition, at your current difficulty's butcher yield. |
| food to grow one adult | All-in food per offspring grown to adult - mother's gestation + conception wait + growth food + fathers' share - as raw nutrition of the assumed feed. |
| max stomach (baby/juvenile/adult) | Stomach capacity per life stage in nutrition. Sets how much the animal can eat per feeding: a food item bigger than the free stomach space wastes the overflow, which is what makes simple meals bad feed for small animals. |
| slaughter efficiency (per life stage) | Meat at that stage divided by the same all-in food. Above 100% the animal returns more nutrition than the operation spends on it. The headline number of the mod; the assumed feed is named in the value. |
| best slaughter age | Stage with the highest efficiency. |
| slaughter pregnant females? | Whether the newborn litter outvalues the feed a half-done pregnancy still costs. |
| net meat per female per day | All-in economics: meat income minus offspring food minus breeding stock (incl. males). Negative = the operation eats more than it returns. Egg/milk/wool/leather income not counted. |

Every row has a tooltip with the full calculation and the raw def numbers behind it.

## How the numbers are computed

All mechanics mirror the live game code (1.6 sources):

- **Mating**: males look for a fertile female with MTB `race.mateMtbHours`, checked once per
  hour while awake (`ThinkNode_ChancePerHour_Mate`), so a male makes
  `awakeHours / mateMtbHours` attempts per day (awake hours default 16, configurable).
  Mating impregnates a mammal with a flat 50% chance; mating an egg layer fertilizes her next
  `eggFertilizationCountMax` eggs with certainty (`PawnUtility.Mated`).
- **Conception wait** `T = 1 / (attemptsPerMalePerDay x pregnancyChance)` - included in the
  cycle length, throughput and the mother's food cost.
- **Optimal ratio** `malesPerFemale = 1 / (attempts x chance x daysCoveredPerMating)`, where
  one mating sustains a full gestation for mammals or `eggFertilizationCountMax` eggs for
  layers. Examples: ibex 1 male per 3.8 females, cow 1 per 4.4.
- **Gestation / laying**: mammals birth a `litterSizeCurve`-averaged litter after
  `gestationPeriodDays`; egg layers lay `eggCountRange`-sized clutches every
  `eggLayIntervalDays`, and fertilized eggs hatch after the egg def's `hatcherDaystoHatch`
  days. Egg layers with `eggProgressUnfertilizedMax < 1` need males to lay at all.
- **Food per day** = life stage `hungerRateFactor` x `race.baseHungerRate` x 1.6 for a
  well-fed animal (`Need_Food.BaseHungerRate`). Growth, gestation and egg progress assume fed
  animals.
- **Meat per stage** = the MeatAmount stat scaled by the stage's `bodySizeFactor`, then
  pushed through the stat's `postProcessCurve` - the game's minimum-yield boost, so small
  animals yield more meat than a linear proportion suggests (a chick is ~12 meat, not 4; an
  ibex newborn 31, not 28). Matches `Pawn.ButcherProducts` exactly. Combat kills (x0.66) are
  not modeled.
- **Slaughter efficiency(stage)** = stage meat nutrition / all-in food to that stage, where
  all-in = mother's gestation food + mother's conception-wait food + offspring growth food +
  fathers' share (breeding males at the optimal ratio eat alongside her every day), divided
  by the assumed feed's multiplier (see below). Above 100% the animal returns more nutrition
  than the operation spends on it.
- **Pregnant females**: a randomly picked pregnant female is on average half done, so let
  her give birth when the newborn litter's meat beats the remaining feed.

## Assumed feed: raw, kibble, pemmican or meals

All food numbers are **raw nutrition** - what the farm actually has to grow, harvest or
cook. The block assumes the herd is fed on the **best feed the animal's diet and stomach
allow**, and applies that feed's multiplier to every food number:

| Feed | Nominal | Effective for |
| ---- | ------- | ------------- |
| raw pieces (hay / raw meat) | x1.00 | everyone - the baseline |
| kibble (0.05 per piece; 2.0 raw -> 2.5) | x1.25 | everyone whose diet allows it - pieces never waste |
| pemmican (0.05 per piece; 0.5 raw -> 0.8) | x1.60 | same - the usual best feed |
| simple meal (0.9 per meal; 0.5 raw -> 0.9) | x1.80 | only animals whose usable stomach space is 0.9+ (cows and other large animals); smaller ones waste the overflow and end up worse than raw |

**Why meals don't scale down**: animals only seek food when their stomach is below a
diet-dependent level (herbivores 45% full, carnivores/omnivores 30%, egg-eaters 40%) and
then eat whole items - anything bigger than the free space is wasted. `MaxNutrition` = 1 x
body size, so a chicken (stomach 0.3, usable 0.17) can only use 0.17 of a 0.9 meal, giving
an effective x0.33 - worse than raw. The block picks pemmican for it instead.

Examples of displayed adult-slaughter efficiency (100% butcher yield, default feed
selection = raw + simple meals): chicken **118%** (raw - meals would be x0.33 for its
stomach, so raw wins), ibex **142%** (simple meals), cow **105%** (simple meals). With
pemmican enabled in the settings the same animals read 188% / 206% / 105%. Diets also
refuse feeds outright and the tooltip says so (wargs refuse everything processed,
herbivores refuse meat-only feeds). Grazing whole plants (0.5 nutrition) wastes similarly
for small animals - a chicken absorbs 0.17 per plant (67% lost); the efficiency rows are
absorbed-nutrition based, so that loss is not charged, and hay pieces or kibble avoid it.

**Which feeds are considered is a mod setting** (mod settings -> Simple Farming). By default
only **raw pieces and simple meals** are enabled - pemmican and kibble are opt-in, since
they need a butcher table/cooker and hauling work. Disable simple meals to force a raw-feed
assumption for everything; disable all processed feeds and the block is a pure raw-feed
calculator. Changes apply immediately.

The tooltip names the assumed feed and its multiplier on every row it applies to; feeding
raw pieces instead simply scales the food numbers back up (and a marginal farm can dip below
100% again).

Each slaughter-efficiency tooltip also breaks that stage's efficiency down **per feed** -
raw baseline, kibble, pemmican, simple meals - no matter what the settings' feed selection
says. Feeds switched off in the settings are marked "(off in settings)", feeds the animal's
diet refuses are listed as refused, and wasteful feeds simply show their (lower) percentage.
The settings only decide which feed the headline numbers assume.

## Assumptions & limits

- Fed, healthy, fertile adults; no miscarriages, no age fertility falloff.
- "Optimal ratio" assumes the herd stays together so males find fertile females; round up in
  practice.
- The feed multiplier is a potential, not free: kibble and pemmican need stove/butcher-table
  work and hauling to the pen. Strict carnivores (wargs) refuse everything processed.
- Milk, wool and unfertilized-egg income are not counted (vanilla shows them separately);
  leather is displayed but is not food.
- Offspring kept as breeding stock is not slaughter income.
- Grazing waste (small animals overeating whole plants) is warned about in the tooltip but
  not charged to the efficiency numbers.

## Debugging

- Mod settings: *Enable debug logging* logs each animal's full calculation the first time its
  info card is opened (prefixed `[SimpleFarming]`), plus skipped species.
- Dev mode: *Debug actions -> Simple Farming -> Log husbandry stats for all animals*.
- The *hours per day animals seek mates* setting (default 16) feeds all timing math and
  applies immediately.

## Files

```
About/                       mod metadata
Defs/                        the "farming" StatCategoryDef
Languages/English/Keyed/     labels, values and tooltips
Source/SimpleFarming/        C# source + csproj
Assemblies/                  SimpleFarming.dll + bundled 0Harmony.dll
```

Code layout: `HusbandryModel` (math), `HusbandryStats` (stat rows + tooltips),
`HarmonyPatches` (one postfix on `RaceProperties.SpecialDisplayStats`), `SimpleFarmingMod`
(settings), `FarmingLog`, `FarmingDebugActions`.

## Building

Requires the .NET SDK. Builds in **Release** - the csproj defaults to it (mods ship as
Release only; pass `-c Debug` explicitly if you ever need a debug build). The csproj defaults
to the game at `E:\SteamLibrary\steamapps\common\RimWorld`; override with your install
path:

```
cd Source/SimpleFarming
dotnet build -c Release -p:RimWorldDir="C:\Path\To\RimWorld"
```

The whole `SimpleFarming` folder can be symlinked or copied into the game's `Mods` directory.
