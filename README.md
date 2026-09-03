# Simple Farming

A RimWorld 1.6 mod that adds a **farming** block to every animal's info card (the "i" / Stats
tab): optimal breeding ratios, reproduction timing, food costs and slaughter nutrition
efficiency - computed live from each animal's own def data, so vanilla, DLC and modded
animals all work without any per-species hardcoding.

Requires the [Harmony](https://steamcommunity.com/sharedfiles/filedetails/?id=2009463077) mod.

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
| food per female per day (incl. males) | Breeding-stock upkeep: female food plus her share of males. |

**Slaughter** (animals with meat only)

| Stat | Meaning |
| ---- | ------- |
| adult meat nutrition | Meat units x the meat def's nutrition, at your current difficulty's butcher yield. |
| food to grow one adult | All-in food per offspring: mother's gestation + conception wait + growth food + fathers' share. |
| slaughter efficiency (per life stage) | Meat at that stage divided by the same all-in food. Above 100% the animal returns more food than the operation spends on it. The headline number of the mod. |
| best slaughter age | Stage with the highest efficiency. |
| slaughter pregnant females? | Whether the newborn litter outvalues the feed a half-done pregnancy still costs. |
| net meat per female per day | All-in economics: meat income minus offspring food minus breeding stock (incl. males). Egg/milk/wool/leather income not counted. |

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
  fathers' share (breeding males at the optimal ratio eat alongside her every day). Above
  100% the animal returns more food than the operation spends on it.
- **Pregnant females**: a randomly picked pregnant female is on average half done, so let
  her give birth when the newborn litter's meat beats the remaining feed.

### Compared to the wiki / the vanilla dev table

Numbers come out lower than community tables on purpose, because more factors are counted.
The wiki's butchery table skips the conception wait; its per-animal pages also skip the
fathers' share; the vanilla dev tool (`DebugOutputsEconomy`) additionally reports hunger-rate
units instead of nutrition and mis-attributes growth food to the next life stage. Example,
ibex adult slaughter: **129%** here (all-in) vs 161.3% on the wiki page vs 200% from the dev
tool - same game, different accounting. Every row tooltip shows exactly what is counted.

Meat values include the current difficulty's butcher yield (x0.9 / x0.8 on the harder
presets, 1.0 otherwise); the tooltip discloses the factor whenever it is not 100%.

## Feeding: raw feed, stomach size and overeating

All food numbers assume **raw** feed (grass, hay, raw meat) fully absorbed. Meat output never
changes with feed type - what changes is how much raw nutrition the farm must spend:

| Feed | Nominal | Note |
| ---- | ------- | ---- |
| raw pieces (hay / raw meat) | x1.00 | baseline |
| kibble (0.05 per piece) | x1.25 | 2.0 raw -> 2.5; pieces are tiny, never wastes |
| pemmican (0.05 per piece) | x1.60 | 0.5 raw -> 0.8; never wastes |
| simple meal (0.9 per meal) | x1.80 | wastes on small stomachs - see below |

**Overeating**: animals only seek food when their stomach is below a diet-dependent level
(herbivores 45% full, carnivores/omnivores 30%, egg-eaters 40%) and then eat whole items.
Anything bigger than the free space is wasted, so the effective multiplier for a feed item of
size N is `nominal x min(1, usableSpace / N)`. Kibble and pemmican pieces are 0.05 and never
waste; a 0.9 simple meal does not fit small stomachs at all:

| Animal (adult) | Stomach | Usable | Simple meals | |
| -------------- | ------- | ------ | ------------ | - |
| Cow (2.4) | 2.40 | 1.32 | x1.80 | fits fully |
| Ibex (1.0) | 1.00 | 0.55 | x1.10 | 0.35 of every meal wasted |
| Turkey (0.6) | 0.60 | 0.33 | x0.66 | worse than raw |
| Chicken (0.3) | 0.30 | 0.17 | x0.33 | worse than raw |
| Rat (0.2, omnivore) | 0.20 | 0.14 | x0.28 | worse than raw |

`MaxNutrition` = 1 x body size; the want-to-eat level is a diet property (45% herbivores, 30%
carnivores/omnivores, 40% egg-eaters). So cooked meals only pay off for animals with a
stomach of 0.5+ nutrition (fully from 1.2+), while kibble and pemmican are the reliable way
to run a marginal farm at a profit - e.g. chicken adult meat goes from 105% raw-fed to 132%
on kibble (and the net row from -0.02 to +0.33 per hen per day) at a 0.84 butcher yield.
Diets also refuse feeds outright; the tooltip marks those (wargs refuse everything
processed, herbivores refuse meat-only feeds).

Grazing whole plants (0.5 nutrition each) has the same waste problem for small animals: a
chicken absorbs only 0.17 of every plant (67% wasted). The efficiency rows are
absorbed-nutrition based, so that loss is not charged - feeding hay pieces or kibble instead
of grazing avoids it.

All of the above is computed per animal and listed in the slaughter efficiency tooltips.

## Assumptions & limits

- Fed, healthy, fertile adults; no miscarriages, no age fertility falloff.
- "Optimal ratio" assumes the herd stays together so males find fertile females; round up in
  practice.
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
Assemblies/                  SimpleFarming.dll + bundled 0Harmony.dll (excluded from uploads)
```

Code layout: `HusbandryModel` (math), `HusbandryStats` (stat rows + tooltips),
`HarmonyPatches` (one postfix on `RaceProperties.SpecialDisplayStats`), `SimpleFarmingMod`
(settings), `FarmingLog`, `FarmingDebugActions`.

## Build from source

Requires the .NET SDK. The csproj defaults to `E:\SteamLibrary\steamapps\common\RimWorld`;
override with your install path. Build the Release configuration for the dll you ship -
a plain `dotnet build` defaults to Debug:

```
cd Source/SimpleFarming
dotnet build -c Release -p:RimWorldDir="C:\Path\To\RimWorld"
```

The output lands in `Assemblies/SimpleFarming.dll`; the whole `SimpleFarming` folder can
be copied or symlinked into the game's `Mods` directory.
