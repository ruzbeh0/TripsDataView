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
using UnityEngine.Rendering;
using Unity.Entities.UniversalDelegates;
using static TripsDataView.Systems.PathTripsUISystem;

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
    float standard_ticksPerDay = 262144f;
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

    private struct WaitingTimeBinInfo
    {
        public double TimeBin;
        public int Total; // Total is a sum of the below parts
        public int Bus;
        public int Tram;
        public int Subway;
        public int Train;
        public int Ship;
        public int Airplane;
        public WaitingTimeBinInfo(int _timeBin) { TimeBin = _timeBin; }
    }

    public enum unlinkedMode
    {
        Bus,
        Tram,
        Subway,
        Train,
        Ship,
        Airplane
    }

    private struct UnlinkedTripsInfo
    {
        public int Mode;
        public int Trips; // Total is a sum of the below parts

        public UnlinkedTripsInfo(int _mode) { Mode = _mode; }
    }

    private static void WriteTransitUnlinkedData(IJsonWriter writer, UnlinkedTripsInfo info)
    {
        writer.TypeBegin("UnlinkedTripsInfo");
        writer.PropertyName("mode");
        writer.Write(info.Mode);
        writer.PropertyName("trips");
        writer.Write(info.Trips);
        writer.TypeEnd();
    }

    private static void WriteTransitPaxData(IJsonWriter writer, TransitByHourInfo info)
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

    private static void WriteTransitWaitingData(IJsonWriter writer, WaitingTimeBinInfo info)
    {
        writer.TypeBegin("waitingTimeBinInfo");
        writer.PropertyName("timeBin");
        writer.Write(info.TimeBin);
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

    private const string kGroup = "transit";
    protected const string group = "transit";

    private EntityQuery m_TransitQuery;

    private RawValueBinding m_uiTransitPaxResults;
    private RawValueBinding m_uiTransitWaitingResults;
    private RawValueBinding m_uiTransitUnlinkedResults;

    private NativeArray<TransitByHourInfo> m_TransitPaxResults; // final results, will be filled via jobs and then written as output
    private NativeArray<WaitingTimeBinInfo> m_TransitWaitingResults;
    private NativeArray<UnlinkedTripsInfo> m_TransitUnlinkedResults;

    // 240209 Set gameMode to avoid errors in the Editor
    public override GameMode gameMode => GameMode.Game;

    //[Preserve]
    protected override void OnCreate()
    {
        base.OnCreate();
        Setting setting = Mod.setting;
        //m_AgeCapUISetting = CreateBinding("AgeCap", setting.AgeCapSetting);

        m_TransitQuery = GetEntityQuery(new EntityQueryDesc
        {
            All = new[] {
                    ComponentType.ReadOnly<TransportLine>(),
                    ComponentType.ReadOnly<VehicleModel>(),
                    ComponentType.ReadOnly<RouteNumber>(),
                    ComponentType.ReadOnly<PrefabRef>(),
                }
        });
        RequireForUpdate(m_TransitQuery);

        AddBinding(m_uiTransitPaxResults = new RawValueBinding(kGroup, "transitPaxDetails", delegate (IJsonWriter binder)
        {
            binder.ArrayBegin(m_TransitPaxResults.Length);
            for (int i = 0; i < m_TransitPaxResults.Length; i++)
            {
                WriteTransitPaxData(binder, m_TransitPaxResults[i]);
            }
            binder.ArrayEnd();
        }));

        AddBinding(m_uiTransitWaitingResults = new RawValueBinding(kGroup, "transitWaitingDetails", delegate (IJsonWriter binder)
        {
            binder.ArrayBegin(m_TransitWaitingResults.Length);
            for (int i = 0; i < m_TransitWaitingResults.Length; i++)
            {
                WriteTransitWaitingData(binder, m_TransitWaitingResults[i]);
            }
            binder.ArrayEnd();
        }));

        AddBinding(m_uiTransitUnlinkedResults = new RawValueBinding(kGroup, "transitUnlinkedDetails", delegate (IJsonWriter binder)
        {
            binder.ArrayBegin(m_TransitUnlinkedResults.Length);
            for (int i = 0; i < m_TransitUnlinkedResults.Length; i++)
            {
                WriteTransitUnlinkedData(binder, m_TransitUnlinkedResults[i]);
            }
            binder.ArrayEnd();
        }));

        m_TransitPaxResults = new NativeArray<TransitByHourInfo>(24, Allocator.Persistent);
        m_TransitWaitingResults = new NativeArray<WaitingTimeBinInfo>(60, Allocator.Persistent);
        m_TransitUnlinkedResults = new NativeArray<UnlinkedTripsInfo>(6, Allocator.Persistent);
        Mod.log.Info("TransitUISystem created.");
    }

    //[Preserve]
    protected override void OnDestroy()
    {
        m_TransitPaxResults.Dispose();
        base.OnDestroy();
    }

    public override int GetUpdateInterval(SystemUpdatePhase phase)
    {
        // One day (or month) in-game is '262144' ticks
        return TimeSystem.kTicksPerDay / 32;
    }

    protected override void OnUpdate()
    {
        base.OnUpdate();

        Setting setting = Mod.setting;

        //ResetResults();

        var results = m_TransitQuery.ToEntityArray(Allocator.Temp);

        CityConfigurationSystem m_CityConfigurationSystem = this.World.GetOrCreateSystemManaged<CityConfigurationSystem>();

        DateTime currentDateTime = this.World.GetExistingSystemManaged<TimeSystem>().GetCurrentDateTime();
        int index = currentDateTime.Hour;

        //string path = Path.Combine(Mod.outputPath, Mod.transit_waiting);
        //string fileNameTransitWaiting = path +
        //    "_" + m_CityConfigurationSystem.cityName + "_" + currentDateTime.DayOfYear + "_" + currentDateTime.Year + ".csv";
        //
        //if (!File.Exists(fileNameTransitWaiting))
        //{
        //    string header = "timeBin,bus,tram,subway,train,ship,airplane"; ;
        //    Utils.createAndDeleteFiles(fileNameTransitWaiting, header, Mod.transit_waiting, path);
        //}

        string path = Path.Combine(Mod.outputPath, Mod.transit_passengers);
        string fileNameTransitPax = path +
            "_" + m_CityConfigurationSystem.cityName + "_" + currentDateTime.DayOfYear + "_" + currentDateTime.Year + ".csv";

        if (!File.Exists(fileNameTransitPax))
        {
            string header = "hour,bus,tram,subway,train,ship,airplane";

            Utils.createAndDeleteFiles(fileNameTransitPax, header, Mod.transit_passengers, path);

        }
        else
        {
            if (previous_index == -1)
            {
                //Load existing data
                using (StreamReader reader = new StreamReader(fileNameTransitPax))
                {
                    int i = 1;
                    while (!reader.EndOfStream)
                    {
                        String line = reader.ReadLine();
                        if (i > 1)
                        {
                            string[] parts = line.Split(',');

                            if (parts.Length > 0)
                            {
                                TransitByHourInfo info = new TransitByHourInfo(Int32.Parse(parts[0]));
                                info.Bus = Int32.Parse(parts[1]);
                                info.Tram = Int32.Parse(parts[2]);
                                info.Subway = Int32.Parse(parts[3]);
                                info.Train = Int32.Parse(parts[4]);
                                info.Ship = Int32.Parse(parts[5]);
                                info.Airplane = Int32.Parse(parts[6]);
                                info.Total = info.Bus + info.Subway + info.Train + info.Tram + info.Airplane + info.Ship;
                                m_TransitPaxResults[Int32.Parse(parts[0])] = info;
                            }
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

            //Modes in order: bus, tram, subway, train, ship, airplane
            int[] waiting = new int[6];
            float[] waiting_time = new float[6];

            int bin_size = 30;
            int[,] waiting_bins = new int[bin_size, 6];
            float minutes_in_bin = 5f;

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
                    DynamicBuffer<RouteWaypoint> waypoints = EntityManager.GetBuffer<RouteWaypoint>(veh);

                    int mode = 0;
                    if (transportLineData.m_TransportType.Equals(TransportType.Tram))
                    {
                        mode = 1;
                    }
                    if (transportLineData.m_TransportType.Equals(TransportType.Subway))
                    {
                        mode = 2;
                    }
                    if (transportLineData.m_TransportType.Equals(TransportType.Train))
                    {
                        mode = 3;
                    }
                    if (transportLineData.m_TransportType.Equals(TransportType.Ship))
                    {
                        mode = 4;
                    }
                    if (transportLineData.m_TransportType.Equals(TransportType.Airplane))
                    {
                        mode = 5;
                    }

                    for (int i = 0; i < waypoints.Length; i++)
                    {
                        RouteWaypoint waypoint = waypoints[i];
                        WaitingPassengers waitingPax;
                        if (EntityManager.TryGetComponent<WaitingPassengers>(waypoint.m_Waypoint, out waitingPax))
                        {
                            int b = (int)Math.Floor((TimeSystem.kTicksPerDay/standard_ticksPerDay)*(waitingPax.m_AverageWaitingTime / 60f) / minutes_in_bin);
                            if (b > (bin_size - 1))
                            {
                                b = bin_size - 1;
                            }
                            waiting_bins[b, mode] += waitingPax.m_Count;
                        }
                    }

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
            m_TransitPaxResults[index] = info;

            string line = $"{index},{bus},{tram},{subway},{train},{ship},{airplane}";

            using (StreamWriter sw = File.AppendText(fileNameTransitPax))
            {
                sw.WriteLine(line);
            }

            //Write Mode Shares for Unlined Trips
            UnlinkedTripsInfo info2 = new UnlinkedTripsInfo((int)unlinkedMode.Bus);
            info2.Trips = (int)(10*Math.Round(100 * (bus / (float)(bus + subway + train + tram + airplane + ship)), 1));
            m_TransitUnlinkedResults[(int)unlinkedMode.Bus] = info2;
            info2 = new UnlinkedTripsInfo((int)unlinkedMode.Subway);
            info2.Trips = (int)(10 * Math.Round(100 * (subway / (float)(bus + subway + train + tram + airplane + ship)), 1));
            m_TransitUnlinkedResults[(int)unlinkedMode.Subway] = info2;
            info2 = new UnlinkedTripsInfo((int)unlinkedMode.Tram);
            info2.Trips = (int)(10 * Math.Round(100 * (tram / (float)(bus + subway + train + tram + airplane + ship)), 1));
            m_TransitUnlinkedResults[(int)unlinkedMode.Tram] = info2;
            info2 = new UnlinkedTripsInfo((int)unlinkedMode.Train);
            info2.Trips = (int)(10 * Math.Round(100 * (train / (float)(bus + subway + train + tram + airplane + ship)), 1));
            m_TransitUnlinkedResults[(int)unlinkedMode.Train] = info2;
            info2 = new UnlinkedTripsInfo((int)unlinkedMode.Ship);
            info2.Trips = (int)(10 * Math.Round(100 * (ship / (float)(bus + subway + train + tram + airplane + ship)), 1));
            m_TransitUnlinkedResults[(int)unlinkedMode.Ship] = info2;
            info2 = new UnlinkedTripsInfo((int)unlinkedMode.Airplane);
            info2.Trips = (int)(10 * Math.Round(100 * (airplane / (float)(bus + subway + train + tram + airplane + ship)), 1));
            m_TransitUnlinkedResults[(int)unlinkedMode.Airplane] = info2;

            for (int i = 0; i < bin_size; i++)
            {
                WaitingTimeBinInfo infoW = new WaitingTimeBinInfo((int)(i * minutes_in_bin));
                infoW.Bus = waiting_bins[i, 0];
                infoW.Tram = waiting_bins[i, 1];
                infoW.Subway = waiting_bins[i, 2];
                infoW.Train = waiting_bins[i, 3];
                infoW.Ship = waiting_bins[i, 4];
                infoW.Airplane = waiting_bins[i, 5];
                m_TransitWaitingResults[i] = infoW;
            }
        }

        m_uiTransitPaxResults.Update();
        m_uiTransitWaitingResults.Update();
    }

    private void ResetResults()
    {
        for (int i = 0; i < m_TransitPaxResults.Length; i++)
        {
            m_TransitPaxResults[i] = new TransitByHourInfo(i);
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
        NativeArray<Entity> entities = m_TransitQuery.ToEntityArray(Allocator.Temp);
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
