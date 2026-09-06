# Simple Farming

Adds a "farming" block to every animal's info card with husbandry math computed straight from the animal's own data - vanilla, DLC and modded animals are all covered, nothing is hardcoded per animal.

## What the block shows

- Optimal male:female breeding ratio; for egg layers, whether males are needed at all and the ratio for fertile eggs.
- Average time until pregnancy / fertilization, the full reproduction cycle, and offspring, meat and leather per female per day.
- Herd food upkeep including the males' share, and the all-in food to grow one adult: mother's gestation and conception-wait food plus growth food per life stage.
- Slaughter nutrition efficiency for every life stage with the best age to slaughter - above 100% the animal returns more food than the operation spends on it - and whether pregnant females are worth keeping until birth.
- Every row has a tooltip with the full calculation and the raw def numbers behind it; the math mirrors the live 1.6 game code and includes the current difficulty's butcher yield.

## Feeds

- The assumed herd feed is the best one enabled in the settings (default: raw food and simple meals; kibble and pemmican opt-in) that the animal's diet and stomach allow - small animals waste the overflow of big meals and do better on raw pieces or kibble.
- Every efficiency tooltip also lists the animal's efficiency per feed - raw baseline, kibble, pemmican, simple meals - including off-in-settings and diet-refused ones.

Steam Workshop: [Simple Farming](https://steamcommunity.com/sharedfiles/filedetails/?id=3794993777).
