using System.Runtime.CompilerServices;
using Colossal.UI.Binding;
using Game.Agents;
using Game.Citizens;
using Game.Common;
using Game.Simulation;
using Game.Tools;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Entities;
using UnityEngine.Scripting;
using Game.UI;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using System;
using Game.Buildings;
using Game;
using TripsDataView.Extensions;
using Game.Prefabs;
using Game.Routes;
using Colossal.Entities;
using Game.Vehicles;
using System.IO;
using UnityEngine.Diagnostics;
using Game.City;
using System.Runtime.Remoting.Messaging;
using System.Data.SqlTypes;

namespace TripsDataView.Systems;

// This System calculates trips by Transit of transportation
[CompilerGenerated]
public partial class TransitUISystem : ExtendedUISystemBase
{

    /// <summary>
    /// Holds info about transportation mode by hour
    /// </summary>
    /// 
    private int previous_index = -1;
    private struct TransitByHourInfo
    {
        public double Hour;
        public int Total; // Total is a sum of the below parts
        public int Bus;
        public int Tram;
        public int Subway;
        public int Train;
        public int Ship;
        public int Airplane; 
        public TransitByHourInfo(int _hour) { Hour = _hour; }
    }

    private static void WriteData(IJsonWriter writer, TransitByHourInfo info)
    {
        writer.TypeBegin("transitByHourInfo");
        writer.PropertyName("hour");
        writer.Write(info.Hour);
        writer.PropertyName("total");
        writer.Write(info.Total);
        writer.PropertyName("bus");
        writer.Write(info.Bus);
        writer.PropertyName("tram");
        writer.Write(info.Tram);
        writer.PropertyName("subway");
        writer.Write(info.Subway);
        writer.PropertyName("train");
        writer.Write(info.Train);
        writer.PropertyName("ship");
        writer.Write(info.Ship);
        writer.PropertyName("airplane");
        writer.Write(info.Airplane);
        writer.TypeEnd();
    }

    private const string kGroup = "transitPassengersInfo";
    protected const string group = "transit";

    private EntityQuery m_TransitPassengersQuery;

    private RawValueBinding m_uiResults;

    private NativeArray<TransitByHourInfo> m_Results; // final results, will be filled via jobs and then written as output

    // 240209 Set gameMode to avoid errors in the Editor
    public override GameMode gameMode => GameMode.Game;

    //[Preserve]
    protected override void OnCreate()
    {
        base.OnCreate();
        Setting setting = Mod.setting;
        //m_AgeCapUISetting = CreateBinding("AgeCap", setting.AgeCapSetting);

        m_TransitPassengersQuery = GetEntityQuery(new EntityQueryDesc
        {
            All = new[] {
                    ComponentType.ReadOnly<TransportLine>(),
                    ComponentType.ReadOnly<VehicleModel>(),
                    ComponentType.ReadOnly<PrefabRef>(),
                }
        });
        RequireForUpdate(m_TransitPassengersQuery);

        AddBinding(m_uiResults = new RawValueBinding(kGroup, "transitDetails", delegate (IJsonWriter binder)
        {
            binder.ArrayBegin(m_Results.Length);
            for (int i = 0; i < m_Results.Length; i++)
            {
                WriteData(binder, m_Results[i]);
            }
            binder.ArrayEnd();
        }));

        m_Results = new NativeArray<TransitByHourInfo>(24, Allocator.Persistent); // INFIXO: TODO
        Mod.log.Info("TransitUISystem created.");
    }

    //[Preserve]
    protected override void OnDestroy()
    {
        m_Results.Dispose();
        base.OnDestroy();
    }

    public override int GetUpdateInterval(SystemUpdatePhase phase)
    {
        // One day (or month) in-game is '262144' ticks
        return TimeSystem.kTicksPerDay / 64;
    }

    protected override void OnUpdate()
    {
        base.OnUpdate();

        Setting setting = Mod.setting;
        //m_AgeCapUISetting.UpdateCallback(setting.AgeCapSetting);

        //ResetResults();

        var results = m_TransitPassengersQuery.ToEntityArray(Allocator.Temp);

        CityConfigurationSystem m_CityConfigurationSystem = this.World.GetOrCreateSystemManaged<CityConfigurationSystem>();

        DateTime currentDateTime = this.World.GetExistingSystemManaged<TimeSystem>().GetCurrentDateTime();
        int index = currentDateTime.Hour;

        string path = Path.Combine(Mod.outputPath, Mod.transit_passengers);
        string fileName = path +
            "_" + m_CityConfigurationSystem.cityName + "_" + currentDateTime.DayOfYear + "_" + currentDateTime.Year + ".csv";

        if (!File.Exists(fileName))
        {
            string header = "hour,bus,tram,subway,train,ship,airplane";

            Utils.createAndDeleteFiles(fileName, header, Mod.transit_passengers, path);

        }
        else
        {
            if (previous_index == -1)
            {
                //Load existing data
                using (StreamReader reader = new StreamReader(fileName))
                {
                    int i = 1;
                    while (!reader.EndOfStream)
                    {
                        String line = reader.ReadLine();
                        if (i > 1)
                        {
                            string[] parts = line.Split(',');

                            TransitByHourInfo info = new TransitByHourInfo(Int32.Parse(parts[0]));
                            info.Bus = Int32.Parse(parts[1]);
                            info.Tram = Int32.Parse(parts[2]);
                            info.Subway = Int32.Parse(parts[3]);
                            info.Train = Int32.Parse(parts[4]);
                            info.Ship = Int32.Parse(parts[5]);
                            info.Airplane = Int32.Parse(parts[6]);
                            info.Total = info.Bus + info.Subway + info.Train + info.Tram + info.Airplane + info.Ship;
                            m_Results[Int32.Parse(parts[0])] = info;
                        }
                        i++;
                    }
                }
            }  
        }

        if (previous_index != index)
        {
            previous_index = index;
            int bus = 0;
            int subway = 0;
            int tram = 0;
            int train = 0;
            int airplane = 0;
            int ship = 0;

            foreach (var veh in results)
            {
                PrefabRef prefab;
                TransportLineData transportLineData;
                TransportLine transportLine;
                VehicleModel vehicleModel;
                PublicTransportVehicleData publicTransportVehicleData;

                transportLine = EntityManager.GetComponentData<TransportLine>(veh);
                prefab = EntityManager.GetComponentData<PrefabRef>(veh);

                if (EntityManager.TryGetComponent<TransportLineData>(prefab.m_Prefab, out transportLineData))
                {
                    if (EntityManager.TryGetComponent<VehicleModel>(veh, out vehicleModel))
                    {
                        if (EntityManager.TryGetComponent<PublicTransportVehicleData>(vehicleModel.m_PrimaryPrefab, out publicTransportVehicleData))
                        {
                            DynamicBuffer<RouteVehicle> vehicles = EntityManager.GetBuffer<RouteVehicle>(veh);

                            for (int i = 0; i < vehicles.Length; i++)
                            {
                                RouteVehicle vehicle = vehicles[i];

                                DynamicBuffer<Passenger> pax = EntityManager.GetBuffer<Passenger>(vehicle.m_Vehicle);
                                if (transportLineData.m_TransportType.Equals(TransportType.Bus))
                                {
                                    bus += pax.Length;
                                }
                                if (transportLineData.m_TransportType.Equals(TransportType.Subway))
                                {
                                    subway += pax.Length;
                                }
                                if (transportLineData.m_TransportType.Equals(TransportType.Tram))
                                {
                                    tram += pax.Length;
                                }
                                if (transportLineData.m_TransportType.Equals(TransportType.Train))
                                {
                                    train += pax.Length;
                                }
                                if (transportLineData.m_TransportType.Equals(TransportType.Airplane))
                                {
                                    airplane += pax.Length;
                                }
                                if (transportLineData.m_TransportType.Equals(TransportType.Ship))
                                {
                                    ship += pax.Length;
                                }
                            }
                        }
                    }
                }
            }

            TransitByHourInfo info = new TransitByHourInfo(index);
            info.Bus = bus;
            info.Subway = subway;
            info.Train = train;
            info.Tram = tram;
            info.Airplane = airplane;
            info.Ship = ship;
            info.Total = bus + subway + train + tram + airplane + ship;
            m_Results[index] = info;

            string line = $"{index},{bus},{tram},{subway},{train},{ship},{airplane}";

            using (StreamWriter sw = File.AppendText(fileName))
            {
                sw.WriteLine(line);
            }
        }

        m_uiResults.Update();
    }

    private void ResetResults()
    {
        for (int i = 0; i < m_Results.Length; i++)
        {
            m_Results[i] = new TransitByHourInfo(i);
        }
        //Plugin.Log("reset",true);
    }


    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void __AssignQueries(ref SystemState state)
    {
    }

    protected override void OnCreateForCompiler()
    {
        base.OnCreateForCompiler();
        __AssignQueries(ref base.CheckedStateRef);
    }

    //[Preserve]
    public TransitUISystem()
    {
    }

    public static void LogChunk(in ArchetypeChunk chunk)
    {
        var componentTypes = chunk.Archetype.GetComponentTypes();
        Mod.log.Info($"chunk: {chunk.Count}, {string.Join(", ", componentTypes.Select(ct => ct.GetType().GetTypeInfo().FullName))}");
    }

    public string[] ListEntityComponents(Entity entity)
    {
        var componentTypes = new List<ComponentType>();

        if (!EntityManager.Exists(entity))
            throw new ArgumentException("Entity does not exist.");

        //}

        NativeArray<ComponentType> NativeArray = EntityManager.GetComponentTypes(entity, Allocator.Temp);
        string[] ToReturn = NativeArray.Select(T => T.GetManagedType().Name).ToArray();
        NativeArray.Dispose();
        return ToReturn;

        //return componentTypes;
    }

    public void InspectComponentsInQuery(EntityQuery query)
    {
        Dictionary<string, int> CompDict = new Dictionary<string, int>();
        NativeArray<Entity> entities = m_TransitPassengersQuery.ToEntityArray(Allocator.Temp);
        for (int i = 0; i < entities.Length; i++)
        {
            Entity entity = entities[i];
            string[] comps = ListEntityComponents(entity);
            foreach (string comp in comps)
            {
                if (CompDict.ContainsKey(comp)) CompDict[comp]++;
                else CompDict.Add(comp, 1);
            }
        }
        entities.Dispose();
        // show the dictionary
        Mod.log.Info("=== Components in selected chunks ===");
        foreach (var pair in CompDict)
        {
            Mod.log.Info($"{pair.Key} {pair.Value}");
        }
    }
}
