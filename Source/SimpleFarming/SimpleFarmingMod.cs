using RimWorld;
using UnityEngine;
using Verse;

namespace SimpleFarming
{
    public class SimpleFarmingSettings : ModSettings
    {
        public const float DefaultAwakeHoursPerDay = 16f;

        /// <summary>Dev aid: logs the full husbandry calculation per animal once, plus skips.</summary>
        public bool debugLogging;

        /// <summary>Animals only seek mates while awake (they sleep roughly a third of the
        /// day). Feeds every timing/ratio number. Exposed as a setting because it is a
        /// modeling assumption, not game data.</summary>
        public float awakeHoursPerDay = DefaultAwakeHoursPerDay;

        public string awakeHoursBuffer;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref debugLogging, "debugLogging", false);
            Scribe_Values.Look(ref awakeHoursPerDay, "awakeHoursPerDay", DefaultAwakeHoursPerDay);
        }
    }

    public class SimpleFarmingMod : Mod
    {
        public static SimpleFarmingMod Instance;

        public SimpleFarmingSettings settings;

        public SimpleFarmingMod(ModContentPack content) : base(content)
        {
            Instance = this;
            settings = GetSettings<SimpleFarmingSettings>();
        }

        public static bool DebugLogging => Instance != null && Instance.settings.debugLogging;

        public static float AwakeHoursPerDay => Mathf.Clamp(
            Instance != null && Instance.settings != null
                ? Instance.settings.awakeHoursPerDay
                : SimpleFarmingSettings.DefaultAwakeHoursPerDay,
            0.1f, GenDate.HoursPerDay);

        public override string SettingsCategory()
        {
            return "SimpleFarming_SettingsCategory".Translate();
        }

        public override void DoSettingsWindowContents(Rect inRect)
        {
            float awakeBefore = settings.awakeHoursPerDay;
            Listing_Standard listing = new Listing_Standard();
            listing.Begin(inRect);
            listing.CheckboxLabeled("SimpleFarming_DebugLogging".Translate(), ref settings.debugLogging);
            listing.Label("SimpleFarming_DebugLoggingHint".Translate());
            listing.Gap();
            if (settings.awakeHoursBuffer == null)
            {
                settings.awakeHoursBuffer = settings.awakeHoursPerDay.ToString("0.#");
            }
            listing.TextFieldNumericLabeled("SimpleFarming_AwakeHoursLabel".Translate(),
                ref settings.awakeHoursPerDay, ref settings.awakeHoursBuffer, 1f, GenDate.HoursPerDay);
            listing.Label("SimpleFarming_AwakeHoursHint".Translate());
            listing.End();
            if (Mathf.Abs(awakeBefore - settings.awakeHoursPerDay) > 1e-4f)
            {
                HusbandryStats.ClearCache();
            }
        }
    }
}
