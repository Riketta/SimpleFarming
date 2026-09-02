using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;

namespace SimpleFarming
{
    [StaticConstructorOnStartup]
    public static class SimpleFarmingInit
    {
        static SimpleFarmingInit()
        {
            Harmony harmony = new Harmony("Riketta.SimpleFarming");
            harmony.PatchAll(typeof(SimpleFarmingInit).Assembly);
            FarmingLog.Debug("Harmony patches applied");
        }
    }

    /// <summary>Appends the "farming" block to every animal's info card. The info card pulls
    /// its stat list from RaceProperties.SpecialDisplayStats (via StatsReportUtility), for both
    /// def-only cards (trade screen, animals tab, encyclopedia) and selected pawns - a postfix
    /// here covers all of them for every animal, vanilla or modded.
    ///
    /// SpecialDisplayStats is an iterator method; the postfix replaces the returned enumerable
    /// with a concatenation, which is enumerated later by the card.</summary>
    [HarmonyPatch(typeof(RaceProperties), nameof(RaceProperties.SpecialDisplayStats))]
    public static class RaceProperties_SpecialDisplayStats_SimpleFarmingPatch
    {
        public static void Postfix(ThingDef parentDef, StatRequest req, ref IEnumerable<StatDrawEntry> __result)
        {
            try
            {
                if (parentDef == null || parentDef.race == null || !parentDef.race.Animal)
                {
                    return;
                }
                List<StatDrawEntry> extra = HusbandryStats.EntriesFor(parentDef);
                if (extra == null || extra.Count == 0)
                {
                    return;
                }
                __result = __result == null ? extra : __result.Concat(extra);
            }
            catch (Exception e)
            {
                // Never break the info card over one animal's bad def data.
                FarmingLog.Error("SpecialDisplayStats patch failed for "
                    + (parentDef != null ? parentDef.defName : "?") + ": " + e);
            }
        }
    }
}
