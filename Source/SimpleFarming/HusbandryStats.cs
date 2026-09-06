using System.Collections.Generic;
using System.Text;
using RimWorld;
using UnityEngine;
using Verse;

namespace SimpleFarming
{
    /// <summary>Builds the "farming" StatDrawEntry block for an animal def. Entries are plain
    /// label/value rows (like vanilla's AnimalProductionStats), so they flow through the normal
    /// info card rendering, quick search and translation pipeline.</summary>
    public static class HusbandryStats
    {
        private static readonly Dictionary<ThingDef, HusbandryModel> Cache =
            new Dictionary<ThingDef, HusbandryModel>();

        private static StatCategoryDef category;

        private static StatCategoryDef Category =>
            category ?? (category = DefDatabase<StatCategoryDef>.GetNamedSilentFail("SimpleFarmingHusbandry"));

        /// <summary>Drops cached models so the next info card rebuilds them - e.g. after the
        /// awake-hours setting changed, since every timing number bakes it in.</summary>
        public static void ClearCache()
        {
            Cache.Clear();
            FarmingLog.ClearOnceKeys();
        }

        /// <summary>Butcher yield of the current difficulty; cached models bake it into their
        /// meat numbers, so a mid-game difficulty change must drop them.</summary>
        private static float lastButcherYield = float.NaN;

        private static float CurrentButcherYield()
        {
            return Find.Storyteller != null ? Find.Storyteller.difficulty.butcherYieldFactor : 1f;
        }

        public static HusbandryModel ModelFor(ThingDef def)
        {
            if (def == null)
            {
                return null;
            }
            float yield = CurrentButcherYield();
            if (yield != lastButcherYield)
            {
                if (!float.IsNaN(lastButcherYield))
                {
                    ClearCache();
                }
                lastButcherYield = yield;
            }
            if (!Cache.TryGetValue(def, out HusbandryModel model))
            {
                model = HusbandryModel.Build(def);
                Cache[def] = model;
                if (model.applicable)
                {
                    FarmingLog.DebugOnce(def.defName, "computed for " + def.defName + ":\n" + model.ToDebugLine());
                }
                else if (model.skipReason != null)
                {
                    FarmingLog.DebugOnce(def.defName, "skipped " + def.defName + ": " + model.skipReason);
                }
            }
            return model;
        }

        /// <summary>The farming block, or null when the species cannot be farmed (or the
        /// category def is missing). Never throws - a broken exotic animal must not kill the
        /// info card.</summary>
        public static List<StatDrawEntry> EntriesFor(ThingDef def)
        {
            try
            {
                StatCategoryDef cat = Category;
                if (cat == null)
                {
                    FarmingLog.ErrorOnce("MissingCategory",
                        "StatCategoryDef SimpleFarmingHusbandry is missing - farming block disabled.");
                    return null;
                }
                HusbandryModel m = ModelFor(def);
                if (m == null || !m.applicable)
                {
                    return null;
                }
                List<StatDrawEntry> entries = new List<StatDrawEntry>();
                AddBreedingEntries(m, cat, entries);
                if (m.hasMeat)
                {
                    AddSlaughterEntries(m, cat, entries);
                }
                return entries;
            }
            catch (System.Exception e)
            {
                FarmingLog.ErrorOnce("Build_" + (def != null ? def.defName : "?"),
                    "failed to build farming stats for " + (def != null ? def.defName : "?") + ": " + e);
                return null;
            }
        }

        // ==================== breeding section ====================

        private static void AddBreedingEntries(HusbandryModel m, StatCategoryDef cat, List<StatDrawEntry> entries)
        {
            string mateMtb = m.mateMtbHours.ToString("0.#");
            string awake = ((int)SimpleFarmingMod.AwakeHoursPerDay).ToString();
            string attempts = m.attemptsPerMalePerDay.ToString("0.##");

            // -- optimal male:female ratio --
            string ratioValue = m.malesPerFemale <= 1f
                ? "SF_RatioFemalesPerMale".Translate(m.femalesPerMale.ToString("0.#"))
                : "SF_RatioMalesPerFemale".Translate(m.malesPerFemale.ToString("0.##"));
            string ratioTip;
            if (m.isEggLayer)
            {
                string note = m.canLayUnfertilized ? "SF_RatioBreedingOnlyNote".Translate() : "";
                ratioTip = "SF_RatioTipEgg".Translate(mateMtb, awake, attempts,
                    Mathf.Max(m.eggProps.eggFertilizationCountMax, 1).ToString(),
                    m.clutchesPerMating.ToString("0.##"),
                    (m.clutchesPerMating * m.cycleDays).ToString("0.##"),
                    m.femalesPerMale.ToString("0.#"), note);
            }
            else
            {
                ratioTip = "SF_RatioTipMammal".Translate(mateMtb, awake, attempts,
                    HusbandryModel.MammalPregnancyChancePerMating.ToStringPercent(),
                    (m.attemptsPerMalePerDay * HusbandryModel.MammalPregnancyChancePerMating).ToString("0.##"),
                    m.cycleDays.ToString("0.##"),
                    m.femalesPerMale.ToString("0.#"));
            }
            entries.Add(new StatDrawEntry(cat, "SF_RatioLabel".Translate(), ratioValue, ratioTip, 9900));

            // -- egg-specific: are males needed to lay at all? --
            if (m.isEggLayer)
            {
                if (m.malesNeededForEggs)
                {
                    string tip = "SF_MalesForEggsTip".Translate(
                        (m.eggProps.eggProgressUnfertilizedMax * 100f).ToString("0"),
                        (m.conceptionDelayDays / m.clutchesPerMating).ToString("0.##"));
                    entries.Add(new StatDrawEntry(cat, "SF_MalesForEggsLabel".Translate(),
                        "SF_MalesRequired".Translate(), tip, 9890));
                }
                else
                {
                    entries.Add(new StatDrawEntry(cat, "SF_MalesForEggsLabel".Translate(),
                        "SF_MalesNoneNeeded".Translate(), "SF_RatioBreedingOnlyNote".Translate(), 9890));
                }
            }

            // -- time until pregnancy / fertilization --
            string timeLabel = m.isEggLayer
                ? "SF_TimeToFertilizationLabel".Translate()
                : "SF_TimeToPregnancyLabel".Translate();
            string timeTip = m.isEggLayer
                ? "SF_TimeToFertilizationTip".Translate(m.conceptionDelayDays.ToString("0.##"), attempts)
                : "SF_TimeToPregnancyTip".Translate(m.conceptionDelayDays.ToString("0.##"), attempts,
                    HusbandryModel.MammalPregnancyChancePerMating.ToStringPercent());
            entries.Add(new StatDrawEntry(cat, timeLabel,
                "SF_DaysValue".Translate(m.conceptionDelayDays.ToString("0.##")), timeTip, 9880));

            // -- reproduction cycle --
            string cycleValue;
            string cycleTip;
            if (m.isEggLayer)
            {
                string stallNote = m.malesNeededForEggs
                    ? "SF_CycleStallNote".Translate(m.conceptionDelayDays.ToString("0.##"))
                    : "";
                cycleValue = "SF_CycleEggValue".Translate(m.cycleDays.ToString("0.##"));
                cycleTip = "SF_CycleTipEgg".Translate(m.litterSizeAvg.ToString("0.##"),
                    m.cycleDays.ToString("0.##"), stallNote);
            }
            else
            {
                cycleValue = "SF_CycleMammalValue".Translate(m.cycleDays.ToString("0.##"),
                    m.conceptionDelayDays.ToString("0.##"), m.cycleDaysWithDelay.ToString("0.##"));
                cycleTip = "SF_CycleTipMammal".Translate(m.cycleDays.ToString("0.##"),
                    m.conceptionDelayDays.ToString("0.##"), m.cycleDaysWithDelay.ToString("0.##"),
                    m.litterSizeAvg.ToString("0.##"));
            }
            entries.Add(new StatDrawEntry(cat, "SF_CycleLabel".Translate(), cycleValue, cycleTip, 9870));

            // -- egg incubation --
            if (m.isEggLayer && m.hatchDays > 0f)
            {
                entries.Add(new StatDrawEntry(cat, "SF_HatchTimeLabel".Translate(),
                    "SF_DaysValue".Translate(m.hatchDays.ToString("0.##")),
                    "SF_HatchTip".Translate(m.hatchDays.ToString("0.##")), 9860));
            }

            // -- offspring per female per day --
            string offspringTip = m.isEggLayer
                ? "SF_OffspringPerDayTipEgg".Translate(m.litterSizeAvg.ToString("0.##"),
                    m.cycleDays.ToString("0.##"), m.cycleDaysWithDelay.ToString("0.##"),
                    m.offspringPerFemalePerDay.ToString("0.###"))
                : "SF_OffspringPerDayTipMammal".Translate(m.litterSizeAvg.ToString("0.##"),
                    m.cycleDays.ToString("0.##"), m.cycleDaysWithDelay.ToString("0.##"),
                    m.offspringPerFemalePerDay.ToString("0.###"));
            entries.Add(new StatDrawEntry(cat, "SF_OffspringPerDayLabel".Translate(),
                m.offspringPerFemalePerDay.ToString("0.###"), offspringTip, 9850));

            // -- meat per female per day --
            if (m.hasMeat)
            {
                float meatPerDay = m.offspringPerFemalePerDay * m.adultMeatNutrition;
                float meatUnitsPerDay = m.offspringPerFemalePerDay * m.adultMeatAmount;
                entries.Add(new StatDrawEntry(cat, "SF_MeatPerDayLabel".Translate(),
                    MeatValue(meatPerDay, meatUnitsPerDay),
                    "SF_MeatPerDayTip".Translate(meatPerDay.ToString("0.##"),
                        m.offspringPerFemalePerDay.ToString("0.###"),
                        m.adultMeatNutrition.ToString("0.##")) + YieldNote(m), 9840));
            }

            // -- leather per female per day (with leather type) --
            if (m.leatherDef != null && m.leatherAmount > 1e-6f)
            {
                float leatherPerDay = m.offspringPerFemalePerDay * m.leatherAmount;
                entries.Add(new StatDrawEntry(cat, "SF_LeatherPerDayLabel".Translate(),
                    leatherPerDay.ToString("0.##") + " " + m.leatherDef.label,
                    "SF_LeatherPerDayTip".Translate(m.leatherDef.LabelCap,
                        m.leatherAmount.ToString("0.#"), leatherPerDay.ToString("0.##")) + YieldNote(m), 9835,
                    null, Gen.YieldSingle(new Dialog_InfoCard.Hyperlink(m.leatherDef))));
            }

            // -- breeding stock upkeep incl. male share --
            entries.Add(new StatDrawEntry(cat, "SF_HerdFoodLabel".Translate(),
                NutritionValue(m.herdFoodPerFemalePerDay),
                "SF_HerdFoodTip".Translate(m.adultFoodPerDay.ToString("0.##"),
                    m.malesPerFemale.ToString("0.###"), m.herdFoodPerFemalePerDay.ToString("0.##"))
                    + FeedNote(m), 9830));
        }

        // ==================== slaughter section ====================

        /// <summary>Display label for a life stage; falls back to the defName for modded
        /// stages with no label (a null value string would break the info card).</summary>
        private static string StageLabel(HusbandryModel m, int i)
        {
            return (m.stages[i].def.label ?? m.stages[i].def.defName).CapitalizeFirst();
        }

        /// <summary>Appended to tooltips whenever the current difficulty's butcher yield
        /// differs from 100% - both meat and leather stats bake it in.</summary>
        private static string YieldNote(HusbandryModel m)
        {
            return Mathf.Abs(m.butcherYieldFactor - 1f) > 1e-4f
                ? " " + "SF_DifficultyYieldNote".Translate(m.butcherYieldFactor.ToStringPercent())
                : "";
        }

        /// <summary>Discloses the assumed herd feed whenever it beats raw food.</summary>
        private static string FeedNote(HusbandryModel m)
        {
            return m.feedMultiplier > 1f
                ? " " + "SF_FeedAssumed".Translate(m.feedLabel, m.feedMultiplier.ToStringPercent())
                : "";
        }

        /// <summary>Extended efficiency tooltip block: the same stage's slaughter efficiency
        /// on every feed - raw as the baseline plus each processed feed - regardless of the
        /// settings' feed selection (those only pick the feed the headline assumes). Shows
        /// wasteful feeds with their (worse) value and diet refusals outright.</summary>
        private static string FeedBreakdown(HusbandryModel m, int stage)
        {
            if (m.feedOptions == null)
            {
                return "";
            }
            // The model's food numbers are stored in raw-nutrition units OF THE APPLIED
            // FEED (Build divides them by feedMultiplier), so allInEfficiencyToStage already
            // includes the applied multiplier. Recover the raw-feeding basis before scaling
            // to each feed's own multiplier, or every line double-counts.
            float rawEfficiency = m.allInEfficiencyToStage[stage] / m.feedMultiplier;
            StringBuilder sb = new StringBuilder();
            sb.Append("\n\n").Append("SF_FeedBreakdownHeader".Translate(m.feedLabel));
            for (int i = 0; i < m.feedOptions.Count; i++)
            {
                HusbandryModel.FeedOption o = m.feedOptions[i];
                if (!o.usable)
                {
                    sb.Append("\n").Append("SF_FeedBreakdownRefused".Translate(o.label));
                    continue;
                }
                string value = (rawEfficiency * o.multiplier).ToStringPercent();
                if (i == 0)
                {
                    sb.Append("\n").Append("SF_FeedBreakdownBaseline".Translate(o.label, value));
                }
                else if (!o.enabled)
                {
                    sb.Append("\n").Append("SF_FeedBreakdownOff".Translate(o.label, value));
                }
                else
                {
                    sb.Append("\n").Append("SF_FeedBreakdownLine".Translate(o.label, value));
                }
            }
            return sb.ToString();
        }

        /// <summary>Formats a meat-nutrition value with the raw meat it converts to, e.g.
        /// "1.75 (35 meat)" - nutrition for the math, meat pieces for the butcher's bill.
        /// Meat units are the MeatAmount side of the same value (nutrition per unit read
        /// from the meat def), so the pair always reconciles.</summary>
        private static string MeatValue(float meatNutrition, float meatAmount)
        {
            return meatNutrition.ToString("0.##") + " ("
                + "SF_MeatUnits".Translate(meatAmount.ToString("0.#")) + ")";
        }

        /// <summary>" (12 meat)"-style parenthetical for tooltip lines whose nutrition value
        /// is pure meat income, via the given units string ("SF_MeatUnits" /
        /// "SF_MeatUnitsPerDay"). Empty for degenerate meat with no nutrition per unit -
        /// tooltips must never render empty parentheses.</summary>
        private static string MeatUnitsFragment(HusbandryModel m, float meatNutrition,
            string unitsKey, string format)
        {
            return m.meatNutritionPerUnit > 1e-6f
                ? " (" + unitsKey.Translate((meatNutrition / m.meatNutritionPerUnit)
                    .ToString(format)) + ")"
                : "";
        }

        /// <summary>Suffixes a nutrition value with its unit, e.g. "0.34 nutrition" - the
        /// counterpart of <see cref="MeatValue"/> for rows where the number is food, not
        /// meat, and no piece count exists.</summary>
        private static string NutritionValue(float value, string format = "0.##")
        {
            return value.ToString(format) + " " + "SF_NutritionWord".Translate();
        }

        private static void AddSlaughterEntries(HusbandryModel m, StatCategoryDef cat, List<StatDrawEntry> entries)
        {
            int last = m.StageCount - 1;

            // -- adult meat nutrition --
            string yieldNote = YieldNote(m);
            entries.Add(new StatDrawEntry(cat, "SF_AdultMeatNutritionLabel".Translate(),
                MeatValue(m.adultMeatNutrition, m.adultMeatAmount),
                "SF_AdultMeatNutritionTip".Translate(m.adultMeatAmount.ToString("0.#"),
                    m.meatNutritionPerUnit.ToString("0.###"), m.adultMeatNutrition.ToString("0.##"),
                    m.leatherAmount.ToString("0")) + yieldNote, 9820));

            // -- food to grow one adult (all-in: parents + growth, raw nutrition of the feed) --
            float adultGrowth = m.growthFoodToStage[last];
            float adultTotal = m.AllInFoodToStage(last);
            entries.Add(new StatDrawEntry(cat, "SF_FoodPerAdultLabel".Translate(),
                NutritionValue(adultTotal),
                "SF_FoodPerAdultTip".Translate(m.gestationFoodPerOffspring.ToString("0.##"),
                    m.conceptionFoodPerOffspring.ToString("0.##"), adultGrowth.ToString("0.##"),
                    m.maleFoodPerOffspring.ToString("0.##"),
                    adultTotal.ToString("0.##")) + YieldNote(m) + FeedNote(m), 9810));

            // -- max stomach per life stage: what "a meal too big" means for this animal --
            StringBuilder stomach = new StringBuilder();
            for (int i = 0; i < m.StageCount; i++)
            {
                if (i > 0)
                {
                    stomach.Append("/");
                }
                stomach.Append(m.stageMaxNutrition[i].ToString("0.##"));
            }
            // feeding attempts per growth stage (eat-to-full each time); the adult stage is
            // open-ended, so it is expressed per day instead
            StringBuilder meals = new StringBuilder();
            for (int i = 0; i < m.StageCount - 1; i++)
            {
                if (i > 0)
                {
                    meals.Append("/");
                }
                meals.Append((m.MealsPerDayInStage(i) * m.stageSpanDays[i]).ToString("0.#"));
            }
            float adultMealsPerDay = m.MealsPerDayInStage(m.StageCount - 1);
            entries.Add(new StatDrawEntry(cat, "SF_StomachLabel".Translate(),
                stomach + " " + "SF_NutritionWord".Translate(),
                "SF_StomachTip".Translate(m.maxNutritionAdult.ToString("0.##"),
                    m.wantEatLevel.ToStringPercent(),
                    (1f - m.wantEatLevel).ToStringPercent(),
                    m.feedingSpace.ToString("0.##"),
                    m.wantEatLevel.ToStringPercent(),
                    meals.ToString(),
                    adultMealsPerDay.ToString("0.#")), 9805));

            // -- one all-in efficiency row per life stage --
            StringBuilder comparison = new StringBuilder();
            for (int i = 0; i < m.StageCount; i++)
            {
                if (m.stageMeatNutrition[i] <= 1e-6f)
                {
                    continue;
                }
                string stageLabel = StageLabel(m, i);
                string meatUnits = m.meatNutritionPerUnit > 1e-6f
                    ? "SF_MeatUnits".Translate((m.stageMeatNutrition[i] / m.meatNutritionPerUnit)
                        .ToString("0.#"))
                    : "";
                string tip = "SF_EfficiencyTip".Translate(stageLabel,
                    m.feedLabel,
                    m.feedMultiplier.ToStringPercent(),
                    m.stageMeatNutrition[i].ToString("0.##"),
                    m.AllInFoodToStage(i).ToString("0.##"),
                    m.gestationFoodPerOffspring.ToString("0.##"),
                    m.conceptionFoodPerOffspring.ToString("0.##"),
                    m.growthFoodToStage[i].ToString("0.##"),
                    m.maleFoodPerOffspring.ToString("0.##"),
                    m.allInEfficiencyToStage[i].ToStringPercent(),
                    m.efficiencyToStage[i].ToStringPercent(),
                    meatUnits);
                if (m.meatCurveActive)
                {
                    tip += "\n\n" + "SF_CurveNote".Translate();
                }
                tip += FeedBreakdown(m, i);
                entries.Add(new StatDrawEntry(cat, "SF_EfficiencyLabel".Translate(stageLabel),
                    m.allInEfficiencyToStage[i].ToStringPercent()
                        + (m.feedMultiplier > 1f ? " (" + m.feedLabel + ")" : ""), tip, 9800 - i * 10));
                comparison.AppendLine("SF_EfficiencyComparisonLine".Translate(stageLabel,
                    m.allInEfficiencyToStage[i].ToStringPercent()));
            }

            // -- recommended slaughter age --
            if (m.bestStage >= 0)
            {
                entries.Add(new StatDrawEntry(cat, "SF_RecommendedLabel".Translate(),
                    StageLabel(m, m.bestStage),
                    "SF_RecommendedTip".Translate("SF_EfficiencyStagesTip".Translate(comparison.ToString())), 9700));
            }

            // -- net farm economics: all food the farm eats vs meat out --
            if (m.bestStage >= 0)
            {
                int best = m.bestStage;
                float meatPerDay = m.offspringPerFemalePerDay * m.stageMeatNutrition[best];
                float offspringFoodPerDay = m.offspringPerFemalePerDay * m.growthFoodToStage[best];
                float net = meatPerDay - offspringFoodPerDay - m.herdFoodPerFemalePerDay;
                string eggsNote = "";
                if (m.canLayUnfertilized && m.eggProps.eggUnfertilizedDef != null)
                {
                    float eggsPerDay = m.litterSizeAvg / m.cycleDays;
                    float eggNutrition = m.eggProps.eggUnfertilizedDef
                        .GetStatValueAbstract(StatDefOf.Nutrition);
                    eggsNote = "\n\n" + "SF_NetEggsNote".Translate(
                        m.adultFoodPerDay.ToString("0.##"),
                        (eggsPerDay * eggNutrition).ToString("0.##"));
                }
                entries.Add(new StatDrawEntry(cat, "SF_NetPerDayLabel".Translate(),
                    net.ToString("+0.00;-0.00") + " " + "SF_NutritionWord".Translate(),
                    "SF_NetPerDayTip".Translate(StageLabel(m, best), m.feedLabel,
                        meatPerDay.ToString("0.00"),
                        offspringFoodPerDay.ToString("0.00"),
                        m.herdFoodPerFemalePerDay.ToString("0.00"),
                        net.ToString("+0.00;-0.00"),
                        MeatUnitsFragment(m, meatPerDay, "SF_MeatUnitsPerDay", "0.##"))
                        + eggsNote + yieldNote, 9695));
            }

            // -- pregnant females: slaughter or let birth? --
            if (!m.isEggLayer)
            {
                string advice = m.letBirthPaysOff
                    ? "SF_PregnantAdviceLet".Translate()
                    : "SF_PregnantAdviceSlaughter".Translate();
                entries.Add(new StatDrawEntry(cat, "SF_PregnantAdviceLabel".Translate(),
                    m.letBirthPaysOff ? "SF_LetBirthValue".Translate() : "SF_SlaughterPregnantValue".Translate(),
                    "SF_PregnantAdviceTip".Translate(
                        (m.cycleDays * 0.5f).ToString("0.##"),
                        m.adultFoodPerDay.ToString("0.##"),
                        m.remainingGestationFoodAvg.ToString("0.##"),
                        m.newbornLitterMeat.ToString("0.##"),
                        advice,
                        MeatUnitsFragment(m, m.newbornLitterMeat, "SF_MeatUnits", "0.#")), 9690));
            }
        }
    }
}
