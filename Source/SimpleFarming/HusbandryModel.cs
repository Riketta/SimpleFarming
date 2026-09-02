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
    /// fertilizes her next <c>eggFertilizationCountMax</c> eggs with certainty [PawnUtility.Mated].</item>
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

        /// <summary>Average days a fertile female waits to conceive (or a stalled hen to be
        /// fertilized) when males are available: 1 / (attempts x success).</summary>
        public float conceptionDelayDays;

        /// <summary>Cycle days including the conception/fertilization wait. For egg layers that
        /// can lay unfertilized eggs the wait costs nothing, so it is not added.</summary>
        public float cycleDaysWithDelay;

        /// <summary>Optimal males per female: 1 / (conceptions a male delivers per day x days of
        /// female cycle one conception sustains). Keeps every female reproducing continuously.</summary>
        public float malesPerFemale;
        public float femalesPerMale;

        /// <summary>Offspring (births / hatchlings) per female per day, ample males, delay included.</summary>
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

        /// <summary>[i] = nutrition eaten from birth until reaching stage i ([0] = 0).</summary>
        public float[] growthFoodToStage;

        /// <summary>[i] = nutrition per day eaten while in stage i.</summary>
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

            if (m.stages.NullOrEmpty())
            {
                return Skip(m, "no life stages");
            }
            if (race.disableMating)
            {
                return Skip(m, "mating disabled for this species");
            }
            if (!race.hasGenders)
            {
                return Skip(m, "species has no genders");
            }
            if (race.mateMtbHours <= 0f)
            {
                return Skip(m, "never seeks mates (mateMtbHours <= 0)");
            }
            if (!m.isEggLayer && race.gestationPeriodDays <= 0f)
            {
                return Skip(m, "cannot gestate (gestationPeriodDays <= 0)");
            }
            if (m.isEggLayer && m.eggProps.eggFertilizedDef == null)
            {
                return Skip(m, "egg layer has no fertilized egg def");
            }
            m.applicable = true;

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
            // a full gestation for mammals, eggFertilizationCountMax eggs for layers.
            float daysCoveredPerMating = m.isEggLayer
                ? Mathf.Max(m.eggProps.eggFertilizationCountMax, 1) * m.daysPerOffspring
                : m.cycleDays;

            m.malesPerFemale = 1f / (conceptionsPerMalePerDay * daysCoveredPerMating);
            m.femalesPerMale = 1f / m.malesPerFemale;
            m.conceptionDelayDays = 1f / conceptionsPerMalePerDay;

            // ---- throughput ----
            // Egg layers that can lay unfertilized eggs lose no laying time waiting for males.
            // One mating covers cyclesPerMating cycles, so the wait amortizes: mammals wait
            // once per gestation, stall layers once per eggFertilizationCountMax eggs
            // (batches > 1 exist only in mods - vanilla defs are all 1).
            bool cycleHasDelay = !m.isEggLayer || m.malesNeededForEggs;
            float cyclesPerMating = daysCoveredPerMating / m.cycleDays;
            float delayDaysPerCycle = cycleHasDelay
                ? m.conceptionDelayDays / Mathf.Max(cyclesPerMating, Epsilon)
                : 0f;
            m.cycleDaysWithDelay = m.cycleDays + delayDaysPerCycle;
            m.offspringPerFemalePerDay = m.litterSizeAvg / Mathf.Max(m.cycleDaysWithDelay, Epsilon);

            // ---- food ----
            float nutritionPerDayPerHungerRate = Need_Food.BaseFoodFallPerTick * GenDate.TicksPerDay; // x1.6
            float adultFactor = m.AdultStage.def.hungerRateFactor;
            m.adultFoodPerDay = adultFactor * race.baseHungerRate * nutritionPerDayPerHungerRate;
            m.herdFoodPerFemalePerDay = m.adultFoodPerDay * (1f + m.malesPerFemale);
            m.gestationFoodPerOffspring = m.daysPerOffspring * m.adultFoodPerDay;
            m.conceptionFoodPerOffspring = delayDaysPerCycle / m.litterSizeAvg * m.adultFoodPerDay;
            m.maleFoodPerOffspring = m.malesPerFemale
                * (m.gestationFoodPerOffspring + m.conceptionFoodPerOffspring);

            int stageCount = m.stages.Count;
            m.growthFoodToStage = new float[stageCount];
            m.stageFoodPerDay = new float[stageCount];
            for (int i = 0; i < stageCount; i++)
            {
                m.stageFoodPerDay[i] = m.stages[i].def.hungerRateFactor * race.baseHungerRate
                    * nutritionPerDayPerHungerRate;
            }
            for (int i = 1; i < stageCount; i++)
            {
                float spanDays = Mathf.Max(m.stages[i].minAge - m.stages[i - 1].minAge, 0f)
                    * GenDate.DaysPerYear;
                // The animal spends that span IN stage i-1, so stage i-1's factor applies -
                // unlike the vanilla debug helper, which charges the next stage's rate.
                m.growthFoodToStage[i] = m.growthFoodToStage[i - 1]
                    + spanDays * m.stageFoodPerDay[i - 1];
            }

            // ---- meat ----
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

            // ---- slaughter efficiency ----
            // Headline number is all-in (fathers' share included); the mother-only figure is
            // kept per stage as a tooltip subtotal.
            m.efficiencyToStage = new float[stageCount];
            m.allInEfficiencyToStage = new float[stageCount];
            if (m.hasMeat)
            {
                float best = float.NegativeInfinity;
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
                }
            }

            // ---- pregnant-female advice (live birth only) ----
            if (!m.isEggLayer && m.hasMeat)
            {
                m.newbornLitterMeat = m.litterSizeAvg * m.stageMeatNutrition[0];
                m.remainingGestationFoodAvg = m.cycleDays * m.adultFoodPerDay * 0.5f;
                m.letBirthPaysOff = m.newbornLitterMeat > m.remainingGestationFoodAvg;
            }

            return m;
        }

        private static HusbandryModel Skip(HusbandryModel m, string reason)
        {
            m.applicable = false;
            m.skipReason = reason;
            return m;
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
            sb.Append(" adultFood=").Append(adultFoodPerDay.ToString("0.##")).Append("/d");
            sb.Append(" herdFood=").Append(herdFoodPerFemalePerDay.ToString("0.##")).Append("/d");
            if (leatherDef != null && leatherAmount > 1e-6f)
            {
                sb.Append(" leather/day=").Append((offspringPerFemalePerDay * leatherAmount).ToString("0.##"))
                    .Append(' ').Append(leatherDef.defName);
            }
            if (hasMeat)
            {
                sb.Append(" adultMeat=").Append(adultMeatNutrition.ToString("0.##"));
                sb.Append(" foodPerAdult=").Append(TotalFoodToStage(stages.Count - 1).ToString("0.##"));
                sb.Append(" (gest ").Append(gestationFoodPerOffspring.ToString("0.##"));
                sb.Append(" + wait ").Append(conceptionFoodPerOffspring.ToString("0.##"));
                sb.Append(" + growth ").Append(growthFoodToStage[stages.Count - 1].ToString("0.##")).Append(")");
                sb.Append(" eff(all-in):");
                for (int i = 0; i < stages.Count; i++)
                {
                    sb.Append(' ').Append(stages[i].def.defName).Append('=')
                        .Append(allInEfficiencyToStage[i].ToStringPercent());
                }
                if (meatCurveActive)
                {
                    sb.Append(" minYieldCurve");
                }
                if (bestStage >= 0)
                {
                    sb.Append(" best=").Append(stages[bestStage].def.defName);
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
                ? "1m:" + femalesPerMale.ToString("0.#") + "f"
                : malesPerFemale.ToString("0.##") + "m:1f";
        }
    }
}
