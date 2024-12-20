// Organized imports based on usage groups
using Colossal.IO.AssetDatabase;
using Colossal.Logging;
using Colossal.PSI.Environment;
using Game;
using Game.Modding;
using Game.Net;
using Game.SceneFlow;
using Game.Settings;
using Game.Simulation;
using HarmonyLib;
using TripsDataView.Systems;
using System.Linq;
using System.IO;
using Unity.Entities;

// Mod namespace
namespace TripsDataView
{
    // Mod class implementing IMod interface
    public class Mod : IMod
    {
        public static readonly string harmonyId = nameof(TripsDataView);
        // Static fields and properties
        public static Setting setting;
        public static readonly string Id = "TripsDataView";
        public static Mod Instance { get; private set; }
     
        public static ExecutableAsset modAsset { get; private set; }    
        internal ILog Log { get; private set; }

        // Static logger instance with custom logger name and settings
        public static ILog log = LogManager.GetLogger($"{nameof(TripsDataView)}.{nameof(Mod)}")
            .SetShowsErrorsInUI(false);

        public static string transit_passengers = "transit_passengers";
        public static string transit_waiting = "transit_waiting";
        public static string trip_purpose = "trip_purpose";
        public static string cim_travel_history = "cim_travel_history";
        public static string trip_purpose_dir = "trip_purpose_directional";
        public static string commute_time = "commute_time";

        public static string outputPath = Path.Combine(EnvPath.kUserDataPath, "ModsData", nameof(TripsDataView));

        // Method that runs when the mod is loaded
        public void OnLoad(UpdateSystem updateSystem)
        {
            // Log entry for debugging purposes
            log.Info(nameof(OnLoad));
            Instance = this;
            // Try to fetch the mod asset from the mod manager
            setting = new Setting(this);
            if (setting == null)
            {
                Log.Error("Failed to initialize settings.");
                return;
            }
            setting.RegisterInOptionsUI();
            AssetDatabase.global.LoadSettings(nameof(TripsDataView), setting, new Setting(this));

            // Load localization
            GameManager.instance.localizationManager.AddSource("en-US", new LocaleEN(setting));

            log.Info($"Output Path: {outputPath}");
            if (!Directory.Exists(outputPath))
            {
                Directory.CreateDirectory(outputPath);
            }

            // Register custom update systems for UI updates
            updateSystem.UpdateAt<TransitUISystem>(SystemUpdatePhase.UIUpdate);
            updateSystem.UpdateAt<TripPurposeUISystem>(SystemUpdatePhase.UIUpdate);
            updateSystem.UpdateAt<TripPurposeTempFileSaveSystem>(SystemUpdatePhase.Serialize);
            updateSystem.UpdateAt<CommuteTimeUISystem>(SystemUpdatePhase.UIUpdate);
            updateSystem.UpdateAt<PathTripsUISystem>(SystemUpdatePhase.UIUpdate);
        }

        // Method that runs when the mod is disposed of
        public void OnDispose()
        {
            log.Info(nameof(OnDispose));
            //World.DefaultGameObjectInjectionWorld.GetOrCreateSystemManaged<TripPurposeUISystem>().SaveCimTravelPurposes();

            // Log entry for debugging purposes

            if (setting != null)
            {
                setting.UnregisterInOptionsUI();
                setting = null;
            }

            var harmony = new Harmony(harmonyId);
            harmony.UnpatchAll(harmonyId);

        }
    }
}
