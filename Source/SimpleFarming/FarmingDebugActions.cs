using System.Linq;
using System.Text;
using LudeonTK;
using RimWorld;
using Verse;

namespace SimpleFarming
{
    /// <summary>Dev-mode tools (visible with development mode on). Handy for verifying the
    /// model against the game's own dev tables and for checking modded animals in bulk.</summary>
    public static class FarmingDebugActions
    {
        [DebugAction("Simple Farming", "Log husbandry stats for all animals",
            actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.Playing)]
        public static void LogAllHusbandryStats()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("Husbandry stats for all loaded animals (awake hours setting: "
                + SimpleFarmingMod.AwakeHoursPerDay.ToString("0.#") + "):");
            using (FarmingLog.QuietScope())
            {
                foreach (ThingDef def in DefDatabase<ThingDef>.AllDefs
                    .Where(d => d.race != null && d.race.Animal && !d.IsCorpse)
                    .OrderBy(d => d.defName))
                {
                    HusbandryModel m = HusbandryStats.ModelFor(def);
                    sb.AppendLine(m != null ? m.ToDebugLine() : def.defName + ": skipped");
                }
            }
            Log.Message("[SimpleFarming] " + sb.ToString());
        }
    }
}
