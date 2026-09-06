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

        /// <summary>Processed feeds considered when picking the assumed herd feed. Raw pieces
        /// (hay / raw meat) are always the x1.00 baseline fallback. Defaults per design: raw
        /// + simple meals; pemmican and kibble are opt-in.</summary>
        public bool feedKibble;
        public bool feedPemmican;
        public bool feedMeals = true;

        public string awakeHoursBuffer;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref debugLogging, "debugLogging", false);
            Scribe_Values.Look(ref awakeHoursPerDay, "awakeHoursPerDay", DefaultAwakeHoursPerDay);
            Scribe_Values.Look(ref feedKibble, "feedKibble", false);
            Scribe_Values.Look(ref feedPemmican, "feedPemmican", false);
            Scribe_Values.Look(ref feedMeals, "feedMeals", true);
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
            bool kibbleBefore = settings.feedKibble;
            bool pemmicanBefore = settings.feedPemmican;
            bool mealsBefore = settings.feedMeals;
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
            listing.Gap();
            listing.GapLine();
            listing.Label("SimpleFarming_FeedChoiceHeader".Translate());
            listing.CheckboxLabeled("SimpleFarming_FeedKibble".Translate(), ref settings.feedKibble);
            listing.CheckboxLabeled("SimpleFarming_FeedPemmican".Translate(), ref settings.feedPemmican);
            listing.CheckboxLabeled("SimpleFarming_FeedMeals".Translate(), ref settings.feedMeals);
            listing.Label("SimpleFarming_FeedChoiceHint".Translate());
            listing.End();
            if (awakeBefore != settings.awakeHoursPerDay
                || kibbleBefore != settings.feedKibble
                || pemmicanBefore != settings.feedPemmican
                || mealsBefore != settings.feedMeals)
            {
                HusbandryStats.ClearCache();
            }
        }
    }
}
