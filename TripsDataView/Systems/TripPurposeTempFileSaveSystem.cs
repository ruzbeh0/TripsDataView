using Game;
using Unity.Entities;
using TripsDataView.Systems;

namespace TripsDataView
{
    public partial class TripPurposeTempFileSaveSystem : GameSystemBase
    {

        protected override void OnCreate()
        {
            base.OnCreate();
        }


        protected override void OnUpdate()
        {
            World.DefaultGameObjectInjectionWorld.GetOrCreateSystemManaged<TripPurposeUISystem>().SaveCimTravelPurposes();
        }
    }
}