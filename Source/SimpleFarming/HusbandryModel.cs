using System;
using System.Collections.Generic;
using System.Text;
using RimWorld;
using UnityEngine;
using Verse;

namespace SimpleFarming
{
    /// <summary>
    /// <para>Species-level husbandry math for one animal <see cref="ThingDef"/>. Everything is
    /// derived from def data (race props, life stages, egg layer comp, stat bases), so vanilla,
    /// DLC and modded animals are handled identically - nothing is hardcoded per species.</para>
    ///
    /// <para>Mechanics the model mirrors (decompiled 1.6 sources in brackets):</para>
    /// <list type="bullet">
    /// <item>Males seek a fertile female with MTB <c>race.mateMtbHours</c>, checked hourly, only
    /// while awake [ThinkNode_ChancePerHour_Mate, JobGiver_Mate]. With ~16 awake hours a male
    /// makes <c>awakeHours / mateMtbHours</c> attempts per day. Awake hours are a setting.</item>
    /// <item>Each mating of a mammal impregnates with a flat 50% chance; mating an egg layer
    /// fertilizes her next <c>eggFertilizationCountMax</c> eggs with certainty [PawnUtility.Mated] -
    /// and every clutch laid from that batch is fertilized whole, so one mating covers whole
    /// clutches, not single eggs [CompEggLayer.ProduceEgg].</item>
    /// <item>Mammal gestation lasts <c>gestationPeriodDays</c> and yields a litter rolled from
    /// <c>litterSizeCurve</c> (average via Rand.ByCurveAverage, min 1) [Hediff_Pregnant].
    /// Egg layers produce a clutch from <c>eggCountRange</c> every <c>eggLayIntervalDays</c>
    /// [CompEggLayer]; fertilized eggs hatch after the hatcher comp's days.</item>
    /// <item>Egg layers with <c>eggProgressUnfertilizedMax &lt; 1</c> stall partway and cannot lay
    /// unfertilized at all - they need males to produce anything [CompEggLayer].</item>
    /// <item>Food per day = life stage <c>hungerRateFactor</c> x <c>race.baseHungerRate</c> x
    /// <c>Need_Food.BaseFoodFallPerTick</c> x ticks-per-day (= x1.6) for a well-fed animal
    /// [Need_Food.BaseHungerRate]. Growth/egg/gestation progress also assumes the animal is fed
    /// (hunger multiplier 1) [PawnUtility.BodyResourceGrowthSpeed].</item>
    /// <item>Butchering returns the MeatAmount stat scaled by the current life stage's
    /// <c>bodySizeFactor</c> [Pawn.ButcherProducts + StatPart_BodySize]; each meat unit is worth
    /// the meat def's Nutrition stat (0.05 in core, read from the def - not hardcoded).</item>
    /// </list>
    ///
    /// <para>Deliberate differences from the vanilla dev tool (DebugOutputsEconomy.AnimalEconomy)
    /// and the wiki tables built on it: growth food uses each stage's own hunger factor for the
    /// time spent in that stage (the vanilla helper applies the next stage's factor - an
    /// off-by-one that overestimates baby food), and the average conception delay is included in
    /// cycles, throughput and efficiency as requested.</para>
    /// </summary>
    public class HusbandryModel
    {
        /// <summary>Flat impregnation chance per mating for live-birth animals
        /// [PawnUtility.Mated]. Not read from XML because the game hardcodes it.</summary>
        public const float MammalPregnancyChancePerMating = 0.5f;

        private const float Epsilon = 1e-6f;

        public ThingDef def;

        public bool applicable;
        public string skipReason;

        public CompProperties_EggLayer eggProps;
        public bool isEggLayer;
        public List<LifeStageAge> stages;

        // ---- reproduction timing ----

        /// <summary>Average litter/clutch size (curve average for mammals, range average for eggs).</summary>
        public float litterSizeAvg;

        /// <summary>Days per litter: gestation length for mammals, egg lay interval for layers.</summary>
        public float cycleDays;

        /// <summary>Mother-days of reproduction per offspring: cycleDays / litterSizeAvg.</summary>
        public float daysPerOffspring;

        public float hatchDays;
        public bool canLayUnfertilized;
        public bool malesNeededForEggs;
        public float mateMtbHours;
        public float attemptsPerMalePerDay;

        /// <summary>Clutches one mating covers for egg layers. Mating sets a fertilization
        /// batch of eggFertilizationCountMax eggs, and every clutch laid while the batch
        /// lasts is fertilized whole - CompEggLayer.ProduceEgg consumes the entire clutch
        /// from the batch at once - so a mating covers about ceil(batch / average clutch)
        /// clutches: exactly 1 for every vanilla def (batch 1, clutch always >= 1), even
        /// for multi-egg clutches like tortoise (1~3) or cobra (1~2).</summary>
        public float clutchesPerMating;

        /// <summary>Average days a fertile female waits to conceive (or a hen to be
        /// fertilized) when males are available: 1 / (attempts x success).</summary>
        public float conceptionDelayDays;

        /// <summary>Cycle days including the conception/fertilization wait. Charged to every
        /// species: unfertilized-capable layers keep laying, but each fertilized clutch costs
        /// one mating and only fertilized clutches yield offspring.</summary>
        public float cycleDaysWithDelay;

        /// <summary>Optimal males per female: 1 / (conceptions a male delivers per day x days of
        /// female cycle one conception sustains). Keeps every female reproducing continuously.</summary>
        public float malesPerFemale;
        public float femalesPerMale;

        /// <summary>Offspring (births / hatchlings) per female per day at the optimal ratio;
        /// the cycle includes the time to a successful mating with a free male.</summary>
        public float offspringPerFemalePerDay;

        // ---- food ----

        /// <summary>Nutrition an adult of this species eats per day.</summary>
        public float adultFoodPerDay;

        /// <summary>Adult upkeep per female including her share of males at the optimal ratio.</summary>
        public float herdFoodPerFemalePerDay;

        /// <summary>Mother's food during gestation/egg formation, per offspring.</summary>
        public float gestationFoodPerOffspring;

        /// <summary>Mother's food during the conception/fertilization wait, per offspring.</summary>
        public float conceptionFoodPerOffspring;

        /// <summary>Fathers' share: breeding-male food per offspring produced. The mother is
        /// fully charged via gestation + conception wait (that is her whole calendar); the male
        /// eats alongside her every day of the cycle, so all-in accounting adds this too.</summary>
        public float maleFoodPerOffspring;

        // ---- feeding space / overeating ----

        /// <summary>Adult stomach capacity in nutrition: the MaxNutrition stat = 1 x
        /// baseBodySize for vanilla adults [StatPart_BodySize; StatPart_LifeStageMaxFood is a
        /// pawn-only part and vanilla adults have foodMaxFactor 1].</summary>
        public float maxNutritionAdult;

        /// <summary>Hunger percentage where an adult goes looking for food - diet-dependent:
        /// herbivores/dendrovores 0.45, carnivores/omnivores 0.3, ovivores 0.4
        /// [RaceProperties.FoodLevelPercentageWantEat; JobGiver_GetFood priority 9.5].</summary>
        public float wantEatLevel;

        /// <summary>Usable space per feeding: maxNutritionAdult x (1 - wantEatLevel). A feed
        /// item bigger than this wastes the overflow, which is why large processed meals can
        /// be worse than raw pieces for small animals.</summary>
        public float feedingSpace;

        /// <summary>[i] = max nutrition the stomach holds in stage i: abstract adult
        /// MaxNutrition x the stage's bodySizeFactor x the stage's foodMaxFactor - matches a
        /// pawn's MaxNutrition exactly (babies have oversized stomachs: vanilla tiny babies
        /// run foodMaxFactor 6, juveniles 1.5).</summary>
        public float[] stageMaxNutrition;

        /// <summary>[i] = days the animal spends in stage i (this stage's minAge up to the
        /// next stage's). The last (adult) entry stays 0 - adult is open-ended.</summary>
        public float[] stageSpanDays;

        /// <summary>Best usable feed's effective multiplier over raw nutrition: 1.00 for raw
        /// pieces, 1.25 kibble / 1.60 pemmican / 1.80 simple meals when the diet allows and
        /// the stomach fits the pieces. All displayed food costs are raw nutrition at this
        /// multiplier.</summary>
        public float feedMultiplier = 1f;

        /// <summary>Label of the assumed herd feed ("raw feed", "kibble", "pemmican",
        /// "simple meals").</summary>
        public string feedLabel = "raw feed";

        /// <summary>Every feed this herd could be raised on with its effective multiplier over
        /// raw nutrition: the raw baseline plus each processed feed - regardless of the
        /// settings' feed selection, which only picks the one the headline numbers assume
        /// (<see cref="feedMultiplier"/>). Feeds the diet refuses or that have no valid recipe
        /// are kept as unusable options so the efficiency tooltip can disclose that.</summary>
        public List<FeedOption> feedOptions;

        /// <summary>The chosen feed's effective multiplier per life stage
        /// (<see cref="FeedOption.stageMultipliers"/>); null for the raw baseline, whose
        /// multiplier is uniformly 1. Young stages have their own stomach size, so a bulky
        /// feed can absorb very differently in a chick than in the adult.</summary>
        public float[] stageFeedMultiplier;

        /// <summary>One feed type the herd could be raised on.</summary>
        public class FeedOption
        {
            public string label;

            /// <summary>Effective nutrition multiplier over raw feeding in the adult stage:
            /// 1.00 raw, 1.25 kibble, 1.60 pemmican, up to 1.80 simple meals (less when the
            /// pieces are bigger than the usable stomach space and overflow is wasted).</summary>
            public float multiplier = 1f;

            /// <summary>[stage] = the same multiplier in that life stage, from the stage's
            /// own stomach size (babies run foodMaxFactor 6). Null for the raw baseline,
            /// whose multiplier is uniformly 1.</summary>
            public float[] stageMultipliers;

            /// <summary>False when the diet refuses the feed or its recipe/def is missing.</summary>
            public bool usable = true;

            /// <summary>Whether the player's settings include this feed for the headline
            /// numbers. The tooltip lists every option either way.</summary>
            public bool enabled = true;
        }

        /// <summary>[i] = raw-feed nutrition eaten from birth until reaching stage i ([0] = 0):
        /// each stage charged at the chosen feed's multiplier for that stage.</summary>
        public float[] growthFoodToStage;

        /// <summary>[i] = nutrition per day eaten while in stage i, feed-independent eaten
        /// basis (the need drains item nutrition whatever feed carries it). Convert to raw
        /// feed nutrition with <see cref="StageFeedMultiplier"/>.</summary>
        public float[] stageFoodPerDay;

        // ---- meat ----

        /// <summary>Butcher yield factor of the current difficulty, already baked into the
        /// abstract MeatAmount/LeatherAmount stats (StatPart_Difficulty_ButcherYield applies
        /// to def-level queries too). Kept for display so numbers reconcile with wiki/dev-table
        /// expectations that assume 100%.</summary>
        public float butcherYieldFactor = 1f;

        public bool hasMeat;
        public float meatNutritionPerUnit;
        public float adultMeatAmount;
        public float adultMeatNutrition;
        public ThingDef leatherDef;
        public float leatherAmount;

        /// <summary>MeatAmount's postProcessCurve: (0,0)(5,14)(40,40)(100000,100000) in core -
        /// small raw yields get boosted to a ~14 minimum, so baby/juvenile meat is NOT a linear
        /// body-size proportion. LeatherAmount has the same curve (adult-basis rows already
        /// include it via the abstract stat, so no extra handling needed there).</summary>
        public SimpleCurve meatCurve;

        /// <summary>True when the curve changes any stage's meat vs linear scaling - gates a
        /// tooltip note so players understand why baby meat beats proportion.</summary>
        public bool meatCurveActive;

        /// <summary>[i] = meat nutrition when butchering an animal in stage i.</summary>
        public float[] stageMeatNutrition;

        // ---- verdicts ----

        /// <summary>[i] = stageMeatNutrition[i] / totalFoodToStage(i) - mother + growth, no
        /// male share. Shown as the subtotal in tooltips.</summary>
        public float[] efficiencyToStage;

        /// <summary>[i] = stageMeatNutrition[i] / allInFoodToStage(i) - mother + growth +
        /// fathers' share. This is the headline efficiency: the true steady-state farm
        /// economics, matching what the whole operation eats per animal produced.</summary>
        public float[] allInEfficiencyToStage;

        public int bestStage = -1;

        /// <summary>Stage with the highest net meat per day - the best meat-minus-growth-food
        /// margin, not always the best efficiency ratio (the meat curve can inflate a small
        /// stage's ratio while its absolute margin stays tiny). The net row is evaluated
        /// here; see <see cref="NetAt"/>.</summary>
        public int bestNetStage = -1;

        public float newbornLitterMeat;

        /// <summary>Average food still to spend when a random pregnant female is inspected
        /// (uniform gestation progress -> half the gestation).</summary>
        public float remainingGestationFoodAvg;

        public bool letBirthPaysOff;

        public LifeStageAge AdultStage => stages[stages.Count - 1];

        public int StageCount => stages.Count;

        /// <summary>Total nutrition spent per offspring raised to the start of stage i.</summary>
        public float TotalFoodToStage(int stageIndex)
        {
            return gestationFoodPerOffspring + conceptionFoodPerOffspring + growthFoodToStage[stageIndex];
        }

        /// <summary>Everything the farm eats per offspring raised to stage i, breeding males
        /// included. Divide the stage's meat nutrition by this for the headline efficiency.</summary>
        public float AllInFoodToStage(int stageIndex)
        {
            return TotalFoodToStage(stageIndex) + maleFoodPerOffspring;
        }

        /// <summary>Net meat nutrition per female per day when offspring are slaughtered at
        /// stage i: throughput x (stage meat - stage growth food) - breeding stock upkeep.
        /// The fixed per-day costs (mother calendar, fathers) are stage-independent, so the
        /// net-best stage maximizes the meat-minus-growth margin - not always the best
        /// efficiency ratio (<see cref="bestNetStage"/> vs <see cref="bestStage"/>).</summary>
        public float NetAt(int stageIndex)
        {
            return offspringPerFemalePerDay
                * (stageMeatNutrition[stageIndex] - growthFoodToStage[stageIndex])
                - herdFoodPerFemalePerDay;
        }

        /// <summary>The chosen feed's effective multiplier in stage i - per-stage when the
        /// feed defines one (processed feeds), else the uniform adult multiplier.</summary>
        public float StageFeedMultiplier(int i)
        {
            if (stageFeedMultiplier != null && i >= 0 && i < stageFeedMultiplier.Length)
            {
                return stageFeedMultiplier[i];
            }
            return feedMultiplier;
        }

        /// <summary>Raw nutrition the farm spends per offspring raised to stage i if the herd
        /// ran on feed <paramref name="o"/>: parent-side food (an adult's stomach) at the
        /// feed's adult multiplier, each growth stage at its own stage multiplier. Passing
        /// the chosen feed reproduces <see cref="AllInFoodToStage"/> exactly; the efficiency
        /// breakdown uses this to price every feed per stage.</summary>
        public float AllInFoodWithFeed(int stageIndex, FeedOption o)
        {
            if (o == null)
            {
                return AllInFoodToStage(stageIndex);
            }
            // Parent food is stored in raw nutrition OF THE CHOSEN FEED - undo its adult
            // multiplier to get eaten nutrition, then apply this feed's.
            float parentEaten = (gestationFoodPerOffspring + conceptionFoodPerOffspring
                + maleFoodPerOffspring) * feedMultiplier;
            float adultMult = StageMultOf(o, int.MaxValue);
            float food = parentEaten / Mathf.Max(adultMult, Epsilon);
            if (stageSpanDays != null)
            {
                for (int j = 0; j < stageIndex; j++)
                {
                    food += stageSpanDays[j] * stageFoodPerDay[j]
                        / Mathf.Max(StageMultOf(o, j), Epsilon);
                }
            }
            return food;
        }

        private static float StageMultOf(FeedOption o, int stage)
        {
            return o.stageMultipliers != null && stage < o.stageMultipliers.Length
                ? o.stageMultipliers[stage]
                : o.multiplier;
        }

        /// <summary>Feeding attempts per day while in stage i, assuming each attempt fills
        /// the stomach from the seek threshold to full: the stage's eaten-nutrition rate
        /// divided by its usable space. Feed-independent - the need drains item nutrition
        /// no matter what feed carries it.</summary>
        public float MealsPerDayInStage(int i)
        {
            // Null/length guard for skipped or failed placeholders (their arrays were never
            // allocated) - bulk consumers call this for every row.
            if (stageMaxNutrition == null || i < 0 || i >= stageMaxNutrition.Length)
            {
                return 0f;
            }
            float usable = stageMaxNutrition[i] * (1f - wantEatLevel);
            return usable > Epsilon ? stageFoodPerDay[i] / usable : 0f;
        }

        // ==================== construction ====================

        public static HusbandryModel Build(ThingDef def)
        {
            HusbandryModel m = new HusbandryModel();
            m.def = def;
            RaceProperties race = def?.race;
            if (race == null || !race.Animal)
            {
                return Skip(m, null); // patch already filters to animals; silent
            }
            m.eggProps = def.GetCompProperties<CompProperties_EggLayer>();
            m.isEggLayer = m.eggProps != null;
            m.stages = race.lifeStageAges;
            string skipReason = ValidateDef(m, race);
            if (skipReason != null)
            {
                return Skip(m, skipReason);
            }
            m.applicable = true;

            float delayDaysPerCycle = ComputeReproduction(m, race);
            ComputeFeedingSpace(m, def, race);
            ComputeFeedOptions(m);
            ComputeFood(m, race, delayDaysPerCycle);
            ComputeMeat(m, def, race);
            ComputeEfficiency(m);
            ComputePregnantAdvice(m);
            return m;
        }

        /// <summary>Def-data sanity gates. Returns the skip reason, or null when the model
        /// is computable.</summary>
        private static string ValidateDef(HusbandryModel m, RaceProperties race)
        {
            if (m.stages.NullOrEmpty())
            {
                return "no life stages";
            }
            if (race.disableMating)
            {
                return "mating disabled for this species";
            }
            if (!race.hasGenders)
            {
                return "species has no genders";
            }
            if (race.mateMtbHours <= 0f)
            {
                return "never seeks mates (mateMtbHours <= 0)";
            }
            if (!m.isEggLayer && race.gestationPeriodDays <= 0f)
            {
                return "cannot gestate (gestationPeriodDays <= 0)";
            }
            if (m.isEggLayer && m.eggProps.eggFertilizedDef == null)
            {
                return "egg layer has no fertilized egg def";
            }
            return null;
        }

        /// <summary>Litter/clutch size, cycle length, mating rate, optimal ratio and
        /// throughput. Returns the average conception-wait days per cycle, which the food
        /// math charges to the mother.</summary>
        private static float ComputeReproduction(HusbandryModel m, RaceProperties race)
        {
            // ---- litter & cycles ----
            if (m.isEggLayer)
            {
                m.litterSizeAvg = Mathf.Max(m.eggProps.eggCountRange.Average, 0.001f);
                m.cycleDays = Mathf.Max(m.eggProps.eggLayIntervalDays, 0.001f);
                m.canLayUnfertilized = m.eggProps.eggUnfertilizedDef != null
                    && m.eggProps.eggProgressUnfertilizedMax >= 1f;
                m.malesNeededForEggs = !m.canLayUnfertilized;
                CompProperties_Hatcher hatcher = m.eggProps.eggFertilizedDef
                    .GetCompProperties<CompProperties_Hatcher>();
                m.hatchDays = hatcher?.hatcherDaystoHatch ?? 0f;
            }
            else
            {
                m.litterSizeAvg = race.litterSizeCurve != null
                    ? Rand.ByCurveAverage(race.litterSizeCurve)
                    : 1f;
                m.litterSizeAvg = Mathf.Max(m.litterSizeAvg, 0.001f);
                m.cycleDays = Mathf.Max(race.gestationPeriodDays, 0.001f);
            }
            m.daysPerOffspring = m.cycleDays / m.litterSizeAvg;

            // ---- mating ----
            // ThinkNode_ChancePerHour rolls once per hour while awake: Rand.MTBEventOccurs
            // (mtb, 2500, 2500) fires with probability 1/mateMtbHours per hourly check, so
            // attempts/day = awakeHours/mtb exactly (capped at one attempt per hour).
            m.mateMtbHours = race.mateMtbHours;
            m.attemptsPerMalePerDay = Mathf.Min(
                SimpleFarmingMod.AwakeHoursPerDay / m.mateMtbHours,
                SimpleFarmingMod.AwakeHoursPerDay);
            float successPerMating = m.isEggLayer ? 1f : MammalPregnancyChancePerMating;
            float conceptionsPerMalePerDay = Mathf.Max(
                m.attemptsPerMalePerDay * successPerMating, Epsilon);

            // Days of one female's reproduction that a single successful mating sustains:
            // a full gestation for mammals, clutchesPerMating whole clutches for layers -
            // ProduceEgg fertilizes each clutch whole and consumes it from the
            // eggFertilizationCountMax-egg batch, so a mating covers
            // ceil(batch / average clutch) clutches (exactly 1 for vanilla defs).
            m.clutchesPerMating = m.isEggLayer
                ? Mathf.Max(1f, Mathf.Ceil(Mathf.Max(m.eggProps.eggFertilizationCountMax, 1)
                    / m.litterSizeAvg))
                : 1f;
            float daysCoveredPerMating = m.isEggLayer
                ? m.clutchesPerMating * m.cycleDays
                : m.cycleDays;

            m.malesPerFemale = 1f / (conceptionsPerMalePerDay * daysCoveredPerMating);
            m.femalesPerMale = 1f / m.malesPerFemale;
            m.conceptionDelayDays = 1f / conceptionsPerMalePerDay;

            // ---- throughput ----
            // Every offspring comes from a conception, so every species pays the wait. One
            // mating covers cyclesPerMating cycles, so the wait amortizes: mammals wait once
            // per gestation, layers once per clutchesPerMating clutches (batches > 1 egg
            // exist only in mods - vanilla defs are all 1). Auto-laying hens lose no
            // egg-laying time, but hatchling throughput counts fertilized clutches only and
            // each of those costs one mating - same convention as mammals.
            float cyclesPerMating = daysCoveredPerMating / m.cycleDays;
            float delayDaysPerCycle = m.conceptionDelayDays / Mathf.Max(cyclesPerMating, Epsilon);
            m.cycleDaysWithDelay = m.cycleDays + delayDaysPerCycle;
            m.offspringPerFemalePerDay = m.litterSizeAvg / Mathf.Max(m.cycleDaysWithDelay, Epsilon);
            return delayDaysPerCycle;
        }

        /// <summary>Adult stomach capacity, the diet-dependent seek threshold and the
        /// resulting usable feeding space, per life stage.</summary>
        private static void ComputeFeedingSpace(HusbandryModel m, ThingDef def, RaceProperties race)
        {
            m.maxNutritionAdult = def.GetStatValueAbstract(StatDefOf.MaxNutrition);
            m.wantEatLevel = race.FoodLevelPercentageWantEat;
            m.feedingSpace = m.maxNutritionAdult * (1f - m.wantEatLevel);
            m.stageMaxNutrition = new float[m.stages.Count];
            for (int i = 0; i < m.stageMaxNutrition.Length; i++)
            {
                // A pawn's MaxNutrition = base x bodySizeFactor x foodMaxFactor - the latter
                // is a pawn-only stat part, vanilla runs 6 on tiny babies and 1.5 on
                // juveniles. Ignoring it understated young-stage stomachs up to 6x.
                m.stageMaxNutrition[i] = m.maxNutritionAdult * m.stages[i].def.bodySizeFactor
                    * m.stages[i].def.foodMaxFactor;
            }
        }

        /// <summary>Computes every feed the diet allows, settings or not, then picks the
        /// headline feed: the best multiplier among usable feeds the player enabled.</summary>
        private static void ComputeFeedOptions(HusbandryModel m)
        {
            // Raw pieces (x1.00 baseline), kibble (x1.25), pemmican (x1.60), simple meals
            // (up to x1.80 - items bigger than the usable stomach space waste the overflow).
            // Every food cost below is raw nutrition at the chosen multiplier.
            SimpleFarmingSettings s = SimpleFarmingMod.Instance?.settings;
            m.feedOptions = new List<FeedOption>
            {
                new FeedOption { label = "raw feed" }
            };
            AddFeedOption(m, "kibble", ThingDefOf.Kibble, "Make_Kibble",
                s != null && s.feedKibble);
            AddFeedOption(m, "pemmican", ThingDefOf.Pemmican, "Make_Pemmican",
                s != null && s.feedPemmican);
            AddFeedOption(m, "simple meals", ThingDefOf.MealSimple, "CookMealSimple",
                s != null && s.feedMeals);
            FeedOption chosen = m.feedOptions[0];
            for (int i = 1; i < m.feedOptions.Count; i++)
            {
                FeedOption o = m.feedOptions[i];
                if (o.usable && o.enabled && o.multiplier > chosen.multiplier)
                {
                    chosen = o;
                }
            }
            m.feedMultiplier = chosen.multiplier;
            m.feedLabel = chosen.label;
            m.stageFeedMultiplier = chosen.stageMultipliers;
        }

        /// <summary>Food rates per day, the mother's calendar food per offspring and the
        /// fathers' share at the optimal ratio, plus per-stage rates, stage lengths and
        /// cumulative growth food. All displayed costs are raw nutrition of the chosen
        /// feed: eaten nutrition divided by the feed's effective multiplier - the adult
        /// multiplier for the breeding stock, each stage's own multiplier for its growth
        /// food (a bulky feed wastes more in a small stage's stomach).</summary>
        private static void ComputeFood(HusbandryModel m, RaceProperties race,
            float delayDaysPerCycle)
        {
            float eatenPerDayPerHungerRate = Need_Food.BaseFoodFallPerTick * GenDate.TicksPerDay;
            float adultFactor = m.AdultStage.def.hungerRateFactor;
            m.adultFoodPerDay = adultFactor * race.baseHungerRate
                * eatenPerDayPerHungerRate / m.feedMultiplier;
            m.herdFoodPerFemalePerDay = m.adultFoodPerDay * (1f + m.malesPerFemale);
            m.gestationFoodPerOffspring = m.daysPerOffspring * m.adultFoodPerDay;
            m.conceptionFoodPerOffspring = delayDaysPerCycle / m.litterSizeAvg * m.adultFoodPerDay;
            m.maleFoodPerOffspring = m.malesPerFemale
                * (m.gestationFoodPerOffspring + m.conceptionFoodPerOffspring);

            int stageCount = m.stages.Count;
            m.growthFoodToStage = new float[stageCount];
            m.stageFoodPerDay = new float[stageCount];
            m.stageSpanDays = new float[stageCount];
            for (int i = 0; i < stageCount; i++)
            {
                // Eaten nutrition per day in stage i, before feed conversion.
                m.stageFoodPerDay[i] = m.stages[i].def.hungerRateFactor * race.baseHungerRate
                    * eatenPerDayPerHungerRate;
            }
            for (int i = 1; i < stageCount; i++)
            {
                float spanDays = Mathf.Max(m.stages[i].minAge - m.stages[i - 1].minAge, 0f)
                    * GenDate.DaysPerYear;
                // The animal spends that span IN stage i-1, so stage i-1's factor applies -
                // unlike the vanilla debug helper, which charges the next stage's rate.
                m.stageSpanDays[i - 1] = spanDays;
                m.growthFoodToStage[i] = m.growthFoodToStage[i - 1]
                    + spanDays * m.stageFoodPerDay[i - 1] / m.StageFeedMultiplier(i - 1);
            }
        }

        /// <summary>Meat and leather from the defs; per-stage meat recovers the raw adult
        /// MeatAmount, scales by body-size factor and re-applies the post-process curve.</summary>
        private static void ComputeMeat(HusbandryModel m, ThingDef def, RaceProperties race)
        {
            int stageCount = m.stages.Count;
            m.butcherYieldFactor = (Find.Storyteller != null)
                ? Find.Storyteller.difficulty.butcherYieldFactor
                : 1f;
            ThingDef meatDef = race.meatDef;
            m.meatNutritionPerUnit = meatDef != null
                ? meatDef.GetStatValueAbstract(StatDefOf.Nutrition)
                : 0f;
            m.adultMeatAmount = def.GetStatValueAbstract(StatDefOf.MeatAmount);
            m.leatherDef = race.leatherDef;
            m.leatherAmount = def.GetStatValueAbstract(StatDefOf.LeatherAmount);
            m.adultMeatNutrition = m.adultMeatAmount * m.meatNutritionPerUnit;
            m.hasMeat = m.adultMeatNutrition > Epsilon;

            m.stageMeatNutrition = new float[stageCount];
            m.meatCurve = StatDefOf.MeatAmount.postProcessCurve;
            // The abstract adult value is post-curve; recover the raw (pre-curve) value, scale
            // it by the stage's body size factor, then re-apply the curve - exactly what the
            // game does per pawn (StatPart_BodySize feeds pawn.BodySize into FinalizeValue,
            // which evaluates the curve after the parts). Straight linear scaling
            // (adultAbstract x factor) understates small stages by up to ~2x.
            float rawAdultMeat = InvertCurve(m.meatCurve, m.adultMeatAmount);
            for (int i = 0; i < stageCount; i++)
            {
                float rawStage = rawAdultMeat * m.stages[i].def.bodySizeFactor;
                float meat = float.IsNaN(rawStage)
                    ? m.adultMeatAmount * m.stages[i].def.bodySizeFactor // un-invertible curve: fall back
                    : (m.meatCurve != null ? m.meatCurve.Evaluate(rawStage) : rawStage);
                if (m.meatCurve != null && !m.meatCurveActive
                    && !Mathf.Approximately(meat, m.adultMeatAmount * m.stages[i].def.bodySizeFactor))
                {
                    m.meatCurveActive = true;
                }
                m.stageMeatNutrition[i] = meat * m.meatNutritionPerUnit;
            }
        }

        /// <summary>Per-stage slaughter efficiencies. Headline number is all-in (fathers'
        /// share included); the mother-only figure is kept per stage as a tooltip subtotal.
        /// Also picks the best stage to slaughter at.</summary>
        private static void ComputeEfficiency(HusbandryModel m)
        {
            int stageCount = m.stages.Count;
            m.efficiencyToStage = new float[stageCount];
            m.allInEfficiencyToStage = new float[stageCount];
            if (!m.hasMeat)
            {
                return;
            }
            float best = float.NegativeInfinity;
            float bestMargin = float.NegativeInfinity;
            for (int i = 0; i < stageCount; i++)
            {
                if (m.stageMeatNutrition[i] <= Epsilon)
                {
                    continue; // e.g. a stage with 0 body size
                }
                float input = m.TotalFoodToStage(i);
                m.efficiencyToStage[i] = input > Epsilon
                    ? m.stageMeatNutrition[i] / input
                    : 0f;
                float allInInput = m.AllInFoodToStage(i);
                m.allInEfficiencyToStage[i] = allInInput > Epsilon
                    ? m.stageMeatNutrition[i] / allInInput
                    : 0f;
                if (m.allInEfficiencyToStage[i] >= best)
                {
                    best = m.allInEfficiencyToStage[i];
                    m.bestStage = i;
                }
                // The net row's stage: net per day = throughput x (meat - growth food) -
                // fixed herd upkeep. The fixed parts are stage-independent, so this is a
                // margin argmax, which can differ from the ratio argmax above.
                float margin = m.stageMeatNutrition[i] - m.growthFoodToStage[i];
                if (margin >= bestMargin)
                {
                    bestMargin = margin;
                    m.bestNetStage = i;
                }
            }
        }

        /// <summary>Whether a half-done pregnancy is worth finishing: the newborn litter vs
        /// the food the remaining gestation still costs (live birth with meat only).</summary>
        private static void ComputePregnantAdvice(HusbandryModel m)
        {
            if (!m.isEggLayer && m.hasMeat)
            {
                m.newbornLitterMeat = m.litterSizeAvg * m.stageMeatNutrition[0];
                m.remainingGestationFoodAvg = m.cycleDays * m.adultFoodPerDay * 0.5f;
                m.letBirthPaysOff = m.newbornLitterMeat > m.remainingGestationFoodAvg;
            }
        }

        private static HusbandryModel Skip(HusbandryModel m, string reason)
        {
            m.applicable = false;
            m.skipReason = reason;
            return m;
        }

        /// <summary>A non-applicable placeholder for a def whose computation threw; used by
        /// bulk consumers so one broken modded def cannot break the debug table or card.</summary>
        public static HusbandryModel Failed(ThingDef def, string reason)
        {
            return Skip(new HusbandryModel { def = def }, reason);
        }

        // ---- feed options ----

        /// <summary>Considers one processed feed for the option list: nominal conversion
        /// (product nutrition per raw nutrition per piece, both from def + recipe) times the
        /// fraction of each piece the animal absorbs before its stomach fills. Diet refusals
        /// [CanEverEat] and invalid recipes mark the option unusable so the tooltip can
        /// disclose it; every considered feed lands in the list either way - the settings
        /// only set <paramref name="enabled"/>.</summary>
        private static void AddFeedOption(HusbandryModel m, string label, ThingDef foodDef,
            string recipeName, bool enabled)
        {
            FeedOption option = new FeedOption { label = label, enabled = enabled };
            m.feedOptions.Add(option);
            if (foodDef == null || !m.def.race.CanEverEat(foodDef))
            {
                option.usable = false;
                return;
            }
            RecipeDef recipe = DefDatabase<RecipeDef>.GetNamedSilentFail(recipeName);
            float itemNutrition = foodDef.GetStatValueAbstract(StatDefOf.Nutrition);
            if (recipe == null || recipe.products.Count == 0 || recipe.ingredients.Count == 0
                || itemNutrition <= Epsilon)
            {
                option.usable = false;
                return;
            }
            if (!(recipe.IngredientValueGetter is IngredientValueGetter_Nutrition))
            {
                // The conversion below treats ingredient counts as nutrition - only valid
                // for nutrition-getter recipes (all vanilla cooking ones). A volume-based
                // modded recipe would silently produce a wrong multiplier: refuse it and
                // let the tooltip disclose the refusal.
                option.usable = false;
                FarmingLog.Debug(m.def.defName + ": feed '" + label + "' recipe '" + recipeName
                    + "' does not count ingredients by nutrition - marked unusable");
                return;
            }
            float rawNutrition = 0f;
            for (int i = 0; i < recipe.ingredients.Count; i++)
            {
                rawNutrition += recipe.ingredients[i].GetBaseCount();
            }
            float nominal = itemNutrition / Mathf.Max(rawNutrition / recipe.products[0].count, Epsilon);
            // Per-stage absorption: pieces bigger than a stage's usable space waste the
            // overflow, and young stages have their own (foodMaxFactor-sized) stomachs -
            // the same 0.9 meal converts x1.8 for a cow adult but can be worse than raw
            // for a chick at any age. The adult entry doubles as the headline multiplier.
            if (m.stageMaxNutrition != null && m.stageMaxNutrition.Length > 0)
            {
                option.stageMultipliers = new float[m.stageMaxNutrition.Length];
                for (int i = 0; i < option.stageMultipliers.Length; i++)
                {
                    option.stageMultipliers[i] = AbsorptionMultiplier(m, nominal,
                        itemNutrition, m.stageMaxNutrition[i] * (1f - m.wantEatLevel));
                }
                option.multiplier = option.stageMultipliers[option.stageMultipliers.Length - 1];
            }
            else
            {
                option.multiplier = AbsorptionMultiplier(m, nominal, itemNutrition,
                    m.feedingSpace);
            }
        }

        /// <summary>Nominal conversion times the fraction of each piece that fits into the
        /// usable feeding space; zero space converts nothing.</summary>
        private static float AbsorptionMultiplier(HusbandryModel m, float nominal,
            float itemNutrition, float usableSpace)
        {
            return usableSpace > Epsilon
                ? nominal * Mathf.Min(1f, usableSpace / itemNutrition)
                : 0f;
        }

        /// <summary>Inverts a monotonic piecewise-linear SimpleCurve. Returns NaN when the
        /// curve is null (identity - callers handle), degenerate, or not strictly increasing
        /// (a modded weird curve - callers fall back to linear scaling).</summary>
        private static float InvertCurve(SimpleCurve curve, float y)
        {
            if (curve == null || curve.PointsCount < 2)
            {
                return y; // no curve = identity
            }
            if (y <= curve[0].y)
            {
                return curve[0].x;
            }
            for (int i = 0; i < curve.PointsCount - 1; i++)
            {
                float y1 = curve[i].y;
                float y2 = curve[i + 1].y;
                float x1 = curve[i].x;
                float x2 = curve[i + 1].x;
                if (y2 <= y1)
                {
                    return float.NaN; // not strictly increasing -> not invertible
                }
                if (y <= y2)
                {
                    return x1 + (y - y1) * (x2 - x1) / (y2 - y1);
                }
            }
            // Above the last point: extrapolate the final segment.
            int n = curve.PointsCount;
            float slope = (curve[n - 1].x - curve[n - 2].x) / (curve[n - 1].y - curve[n - 2].y);
            return curve[n - 1].x + (y - curve[n - 1].y) * slope;
        }

        // ==================== debug output ====================

        /// <summary>Joins values with "/" for the compact stomach lists shared by the
        /// info-card value and the debug line. Null-safe: bulk consumers (debug table)
        /// also feed it Failed placeholders whose arrays do not exist.</summary>
        public static string JoinedSlash(float[] values, string format)
        {
            if (values == null)
            {
                return "";
            }
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < values.Length; i++)
            {
                if (i > 0)
                {
                    sb.Append("/");
                }
                sb.Append(values[i].ToString(format));
            }
            return sb.ToString();
        }

        /// <summary>One compact line for the dev dump / debug action.</summary>
        public string ToDebugLine()
        {
            if (!applicable)
            {
                return (def != null ? def.defName : "?") + ": skipped (" + skipReason + ")";
            }
            StringBuilder sb = new StringBuilder();
            sb.Append(def.defName);
            sb.Append(isEggLayer ? " [eggs]" : " [live]");
            sb.Append(" ratio=").Append(RatioString());
            sb.Append(" T=").Append(conceptionDelayDays.ToString("0.##")).Append("d");
            sb.Append(" cycle=").Append(cycleDaysWithDelay.ToString("0.##")).Append("d");
            sb.Append(" litter=").Append(litterSizeAvg.ToString("0.##"));
            if (isEggLayer && hatchDays > 0f)
            {
                sb.Append(" hatch=").Append(hatchDays.ToString("0.##")).Append("d");
            }
            sb.Append(" offspring/day=").Append(offspringPerFemalePerDay.ToString("0.###"));
            sb.Append(" feed=").Append(feedLabel).Append(" x").Append(feedMultiplier.ToString("0.##"));
            if (feedOptions != null)
            {
                sb.Append(" feeds=");
                for (int i = 0; i < feedOptions.Count; i++)
                {
                    FeedOption o = feedOptions[i];
                    if (i > 0)
                    {
                        sb.Append(", ");
                    }
                    sb.Append(o.label).Append(" x").Append(o.multiplier.ToString("0.##"));
                    if (!o.usable)
                    {
                        sb.Append("(refused)");
                    }
                    else if (!o.enabled)
                    {
                        sb.Append("(off)");
                    }
                }
            }
            if (stageFeedMultiplier != null)
            {
                bool uniform = true;
                for (int i = 0; i < stageFeedMultiplier.Length; i++)
                {
                    if (!Mathf.Approximately(stageFeedMultiplier[i], feedMultiplier))
                    {
                        uniform = false;
                        break;
                    }
                }
                if (!uniform)
                {
                    sb.Append(" stageMult=").Append(JoinedSlash(stageFeedMultiplier, "0.##"));
                }
            }
            if (stageMaxNutrition != null)
            {
                sb.Append(" stomach=").Append(JoinedSlash(stageMaxNutrition, "0.##"));
                sb.Append(" mealsPerDay=");
                for (int i = 0; i < stageMaxNutrition.Length; i++)
                {
                    if (i > 0)
                    {
                        sb.Append("/");
                    }
                    sb.Append(MealsPerDayInStage(i).ToString("0.##"));
                }
            }
            sb.Append(" adultFood=").Append(adultFoodPerDay.ToString("0.##")).Append("/d");
            sb.Append(" herdFood=").Append(herdFoodPerFemalePerDay.ToString("0.##")).Append("/d");
            if (leatherDef != null && leatherAmount > 1e-6f)
            {
                sb.Append(" leather/day=").Append((offspringPerFemalePerDay * leatherAmount).ToString("0.##"))
                    .Append(' ').Append(leatherDef.defName);
            }
            if (hasMeat)
            {
                int last = stages.Count - 1;
                sb.Append(" adultMeat=").Append(adultMeatNutrition.ToString("0.##"));
                sb.Append(" yield=").Append(butcherYieldFactor.ToStringPercent());
                sb.Append(" foodPerAdult=").Append(AllInFoodToStage(last).ToString("0.##"));
                sb.Append(" (gest ").Append(gestationFoodPerOffspring.ToString("0.##"));
                sb.Append(" + wait ").Append(conceptionFoodPerOffspring.ToString("0.##"));
                sb.Append(" + growth ").Append(growthFoodToStage[last].ToString("0.##"));
                sb.Append(" + fathers ").Append(maleFoodPerOffspring.ToString("0.##")).Append(")");
                sb.Append(" meat=").Append(JoinedSlash(stageMeatNutrition, "0.##"));
                sb.Append(" eff(all-in|ex-males):");
                for (int i = 0; i < stages.Count; i++)
                {
                    sb.Append(' ').Append(stages[i].def.defName).Append('=')
                        .Append(allInEfficiencyToStage[i].ToStringPercent())
                        .Append('|').Append(efficiencyToStage[i].ToStringPercent());
                }
                if (meatCurveActive)
                {
                    sb.Append(" minYieldCurve");
                }
                if (bestNetStage >= 0)
                {
                    sb.Append(" best=").Append(stages[bestStage].def.defName);
                    if (bestNetStage != bestStage)
                    {
                        sb.Append(" netBest=").Append(stages[bestNetStage].def.defName);
                    }
                    sb.Append(" net=").Append(NetAt(bestNetStage).ToString("+0.00;-0.00"))
                        .Append("/d");
                }
                if (!isEggLayer)
                {
                    sb.Append(" letBirth=").Append(letBirthPaysOff ? "yes" : "no");
                }
            }
            return sb.ToString();
        }

        public string RatioString()
        {
            return malesPerFemale <= 1f
                ? "1m:" + femalesPerMale.ToString("0.##") + "f"
                : malesPerFemale.ToString("0.##") + "m:1f";
        }
    }
}
