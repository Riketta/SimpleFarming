# Steam Workshop description

Paste the text below into the Workshop item's description field when publishing
(the BBCode renders on Steam, but not in-game - `About/About.xml` carries its own
plain-text description).

```
[h3]Simple Farming[/h3]
A "farming" block on every animal's info card that shows how to run the species at a profit: optimal breeding ratio, reproduction timing, food costs and slaughter nutrition efficiency - computed live from each animal's own data, so vanilla, DLC and modded animals are all covered.

[h3]What it shows[/h3]
[list][*]Optimal male:female breeding ratio - for egg layers also whether males are needed at all (unfertilized eggs vs fertile eggs) and the ratio for hatching.
[*]Average time until pregnancy or fertilization, the full reproduction cycle, and offspring, meat and leather each female produces per day.
[*]Herd food upkeep per female, including her share of breeding males.
[*]All-in food spent to grow one adult: the mother's gestation and conception-wait food plus the offspring's growth food, life stage by life stage.
[*]Slaughter nutrition efficiency for every life stage and the best age to slaughter - above 100% the animal returns more food than raising it costs.
[*]Whether a pregnant female is worth keeping until birth or better slaughtered now.
[*]Leather per day with the leather type named, for animals that have one.[/list]

[h3]Feeds[/h3]
The numbers assume the herd is fed on the best feed you enabled in the mod settings - by default raw food and simple meals, with kibble and pemmican as opt-in. Simple meals only pay off for animals whose stomach fits a whole meal: smaller animals waste the overflow and do better on raw pieces or kibble. Every efficiency tooltip lists the animal's efficiency on every feed - raw baseline, kibble, pemmican, simple meals - including the ones switched off in the settings and the ones the animal refuses, so you can compare at a glance.

[h3]Things to keep in mind[/h3]
[list][*]The accounting is deliberately strict: the mother's full cycle, the average wait to conceive and the males' share of food are all charged to each offspring, so numbers sit a bit below wiki tables - if it says above 100%, the farm really is growing food.
[*]Meat and leather include the current difficulty's butcher yield and the game's own small-animal meat bonus, exactly as butchering returns them.
[*]Milk, wool and eggs are extra income not counted here (vanilla shows them separately); leather is displayed but is not food.[/list]

[h3]Compatibility[/h3]
Requires RimWorld 1.6 and [url=https://steamcommunity.com/sharedfiles/filedetails/?id=2009463077]Harmony[/url]; all DLCs are optional.
Works with all DLC and mods by design - every number is read from the animal's own def data at runtime, nothing is hardcoded per species. Safe to add or remove at any time.

Optional debug logging and a dev-mode dump of every animal's stats are available.

Source code and details: [url]https://github.com/Riketta/SimpleFarming[/url]
```
