using System;
using System.IO;
using Game.Serialization;        // <-- important
using Game;
using Unity.Entities;

namespace TripsDataView.Systems
{
    public partial class TripPurposeTempFileSaveSystem : GameSystemBase
    {
        protected override void OnCreate()
        {
            base.OnCreate();
        }

        protected override void OnUpdate()
        {
            // Grab the SaveGameSystem that is orchestrating this Serialize pass
            var saveGame = World.GetOrCreateSystemManaged<SaveGameSystem>();

            bool isAutoSave = false;

            // If the stream is a file, inspect its name: autosaves usually include "AutoSave"
            if (saveGame?.stream is FileStream fs)
            {
                var fname = Path.GetFileName(fs.Name);
                if (!string.IsNullOrEmpty(fname))
                    isAutoSave = fname.IndexOf("AutoSave", StringComparison.OrdinalIgnoreCase) >= 0;
            }

            // Optional user toggle (added in step 2)
            var setting = Mod.setting;
            bool allowOnAutoSaves = setting?.saveDuringAutoSaves == true;

            if (!isAutoSave || allowOnAutoSaves)
            {
                World.DefaultGameObjectInjectionWorld
                    .GetOrCreateSystemManaged<TripPurposeUISystem>()
                    .SaveCimTravelPurposes();
            }
        }
    }
}
