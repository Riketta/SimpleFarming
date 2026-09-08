using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using LudeonTK;
using RimWorld;
using Verse;

namespace SimpleFarming
{
    /// <summary>Table output for the dev "Output" tab (the debug logging menu): every
    /// animal's full husbandry stat set in sortable columns, mirroring the info-card block.
    /// Values come from the same cached models the cards use, so a row always matches what
    /// the card shows for that animal. Auto-generated corpse defs carry a copy of the
    /// race props, so they are filtered out explicitly.</summary>
    public static class FarmingDebugOutputs
    {
        [DebugOutput("Simple Farming", true)]
        public static void Husbandry()
        {
            List<HusbandryModel> models;
            using (FarmingLog.QuietScope())
            {
                models = DefDatabase<ThingDef>.AllDefs
                    .Where(d => d.race != null && d.race.Animal && !d.IsCorpse)
                    .OrderBy(d => d.defName)
                    .Select(HusbandryStats.ModelFor)
                    .Where(m => m != null)
                    .ToList();
            }
            DebugTables.MakeTablesDialog(models, Getters().ToArray());
        }

        private static IEnumerable<TableDataGetter<HusbandryModel>> Getters()
        {
            List<TableDataGetter<HusbandryModel>> g = new List<TableDataGetter<HusbandryModel>>
            {
                new TableDataGetter<HusbandryModel>("def", m => Name(m)),
                new TableDataGetter<HusbandryModel>("skip reason",
                    m => m.applicable ? (m.skipReason ?? "") : ""),
                new TableDataGetter<HusbandryModel>("type",
                    m => O(m, m.isEggLayer ? "eggs" : "live")),
                new TableDataGetter<HusbandryModel>("ratio", m => O(m, m.RatioString())),
                new TableDataGetter<HusbandryModel>("T,d", m => O(m, F(m.conceptionDelayDays))),
                new TableDataGetter<HusbandryModel>("cycle,d", m => O(m, F(m.cycleDaysWithDelay))),
                new TableDataGetter<HusbandryModel>("litter", m => O(m, F(m.litterSizeAvg))),
                new TableDataGetter<HusbandryModel>("offsp/day",
                    m => O(m, m.offspringPerFemalePerDay.ToString("0.###"))),
                new TableDataGetter<HusbandryModel>("feed",
                    m => O(m, m.feedLabel + " x" + m.feedMultiplier.ToString("0.##"))),
                new TableDataGetter<HusbandryModel>("yield",
                    m => O(m, m.butcherYieldFactor.ToStringPercent())),
                new TableDataGetter<HusbandryModel>("stomach\nb/j/a",
                    m => O(m, HusbandryModel.JoinedSlash(m.stageMaxNutrition, "0.##"))),
                new TableDataGetter<HusbandryModel>("meals/d\nb/j/a",
                    m => Slash(m, i => m.MealsPerDayInStage(i).ToString("0.##"))),
                new TableDataGetter<HusbandryModel>("adult\nfood/d",
                    m => O(m, F(m.adultFoodPerDay))),
                new TableDataGetter<HusbandryModel>("herd\nfood/d",
                    m => O(m, F(m.herdFoodPerFemalePerDay))),
                new TableDataGetter<HusbandryModel>("meat\nb/j/a",
                    m => O(m, HusbandryModel.JoinedSlash(m.stageMeatNutrition, "0.##"))),
                new TableDataGetter<HusbandryModel>("food/adult",
                    m => m.applicable
                        ? F(m.AllInFoodToStage(m.StageCount - 1))
                        : ""),
                new TableDataGetter<HusbandryModel>("fathers\n/child",
                    m => O(m, F(m.maleFoodPerOffspring))),
                new TableDataGetter<HusbandryModel>("eff\nb/j/a",
                    m => O(m, SlashPct(m.allInEfficiencyToStage))),
                new TableDataGetter<HusbandryModel>("eff ex\nmales",
                    m => O(m, SlashPct(m.efficiencyToStage))),
                new TableDataGetter<HusbandryModel>("best",
                    m => O(m, m.bestStage >= 0 ? m.stages[m.bestStage].def.defName : "")),
                new TableDataGetter<HusbandryModel>("net stage",
                    m => O(m, m.bestNetStage >= 0 ? m.stages[m.bestNetStage].def.defName : "")),
                new TableDataGetter<HusbandryModel>("net/d",
                    m => O(m, m.bestNetStage >= 0
                        ? m.NetAt(m.bestNetStage).ToString("+0.00;-0.00") : "")),
                new TableDataGetter<HusbandryModel>("net@raw/d", m => O(m, NetRaw(m))),
                new TableDataGetter<HusbandryModel>("leather/d", m => O(m,
                    m.leatherDef != null && m.leatherAmount > 1e-6f
                        ? (m.offspringPerFemalePerDay * m.leatherAmount).ToString("0.##")
                            + " " + m.leatherDef.defName
                        : "")),
                new TableDataGetter<HusbandryModel>("letBirth",
                    m => O(m, m.isEggLayer ? "" : (m.letBirthPaysOff ? "yes" : "no"))),
            };
            return g;
        }

        private static string Name(HusbandryModel m)
        {
            return m.def != null ? m.def.defName : "?";
        }

        /// <summary>Value only for computable animals - skipped species keep just their
        /// name and reason (their stage arrays do not exist).</summary>
        private static string O(HusbandryModel m, string value)
        {
            return m.applicable ? value : "";
        }

        private static string F(float v)
        {
            return v.ToString("0.##");
        }

        private static string Slash(HusbandryModel m, Func<int, string> perStage)
        {
            if (!m.applicable || m.stages == null)
            {
                return "";
            }
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < m.StageCount; i++)
            {
                if (i > 0)
                {
                    sb.Append("/");
                }
                sb.Append(perStage(i));
            }
            return sb.ToString();
        }

        private static string SlashPct(float[] values)
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
                sb.Append(values[i].ToStringPercent());
            }
            return sb.ToString();
        }

        /// <summary>Best net over stages if the herd ran on raw feed - independent of the
        /// settings' feed selection, so species stay comparable on one basis. Reuses the
        /// model's raw feed option (multiplier 1 at every stage), same math as net/d.</summary>
        private static string NetRaw(HusbandryModel m)
        {
            if (m.feedOptions == null || m.feedOptions.Count == 0
                || m.stageMeatNutrition == null)
            {
                return "";
            }
            HusbandryModel.FeedOption raw = m.feedOptions[0];
            float best = float.NegativeInfinity;
            for (int i = 0; i < m.StageCount; i++)
            {
                if (m.stageMeatNutrition[i] <= 1e-6f)
                {
                    continue;
                }
                float net = m.NetWithFeed(i, raw);
                if (net > best)
                {
                    best = net;
                }
            }
            return best > float.NegativeInfinity ? best.ToString("+0.00;-0.00") : "";
        }
    }
}
