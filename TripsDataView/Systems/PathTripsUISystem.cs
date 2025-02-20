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
using Game.Pathfind;
using Game.Net;
using static HarmonyLib.Code;
using Unity.Mathematics;
using Unity.Entities.UniversalDelegates;
using Game.Input;
using Game.UI.InGame;
using Colossal.Mathematics;
using static Game.Rendering.Debug.RenderPrefabRenderer;
using System.Security.Cryptography;

namespace TripsDataView.Systems;

// This System calculates Commute time information
[CompilerGenerated]
public partial class PathTripsUISystem : ExtendedUISystemBase
{

    /// <summary>
    /// Holds info about linked and unlinked trips calculated from the Paths
    /// </summary>
    /// 
    private int previous_index = -1;

    public enum linkedMode
    {
        Vehicle,
        Transit,
        Pedestrian
    }

    private struct LinkedTripsInfo
    {
        public int Mode;
        public int Trips; // Total is a sum of the below parts

        public LinkedTripsInfo(int _mode) { Mode = _mode; }
    }

    private struct PKTInfo
    {
        public int Mode;
        public int PKT; // Total is a sum of the below parts

        public PKTInfo(int _mode) { Mode = _mode; }
    }

    private struct TransferInfo
    {
        public int Index;
        public int Trips;

        public TransferInfo(int _index) { Index = _index; }
    }

    private static void WriteLinkedData(IJsonWriter writer, LinkedTripsInfo info)
    {
        writer.TypeBegin("LinkedTripsInfo");
        writer.PropertyName("mode");
        writer.Write(info.Mode);
        writer.PropertyName("trips");
        writer.Write(info.Trips);
        writer.TypeEnd();
    }

    private static void WritePKTData(IJsonWriter writer, PKTInfo info)
    {
        writer.TypeBegin("PKTTripsInfo");
        writer.PropertyName("mode");
        writer.Write(info.Mode);
        writer.PropertyName("pkt");
        writer.Write(info.PKT);
        writer.TypeEnd();
    }

    private static void WriteTransfersData(IJsonWriter writer, TransferInfo info)
    {
        writer.TypeBegin("TransferInfo");
        writer.PropertyName("index");
        writer.Write(info.Index);
        writer.PropertyName("trips");
        writer.Write(info.Trips);
        writer.TypeEnd();
    }

    private const string kGroup = "pathTripsInfo";
    protected const string group = "pathTrips";

    private EntityQuery m_PathTripsQuery;

    private RawValueBinding m_uiResults;
    private RawValueBinding m_uiPKTResults;
    private RawValueBinding m_uiTransfersResults;


    private NativeArray<LinkedTripsInfo> m_Results; // final results, will be filled via jobs and then written as output
    private NativeArray<PKTInfo> m_PKTResults; // final results, will be filled via jobs and then written as output
    private NativeArray<TransferInfo> m_TransfersResults;

    // 240209 Set gameMode to avoid errors in the Editor
    public override GameMode gameMode => GameMode.Game;

    //[Preserve]
    protected override void OnCreate()
    {
        base.OnCreate();
        Setting setting = Mod.setting;

        m_PathTripsQuery = GetEntityQuery(new EntityQueryDesc
        {
            All = new[] {
                    ComponentType.ReadOnly<PathOwner>()
                }
        });
        RequireForUpdate(m_PathTripsQuery);

        AddBinding(m_uiResults = new RawValueBinding(kGroup, "pathTripsDetails", delegate (IJsonWriter binder)
        {
            binder.ArrayBegin(m_Results.Length);
            for (int i = 0; i < m_Results.Length; i++)
            {
                WriteLinkedData(binder, m_Results[i]);
            }
            binder.ArrayEnd();
        }));

        AddBinding(m_uiPKTResults = new RawValueBinding(kGroup, "pktDetails", delegate (IJsonWriter binder)
        {
            binder.ArrayBegin(m_PKTResults.Length);
            for (int i = 0; i < m_PKTResults.Length; i++)
            {
                WritePKTData(binder, m_PKTResults[i]);
            }
            binder.ArrayEnd();
        }));

        AddBinding(m_uiTransfersResults = new RawValueBinding(kGroup, "transfersDetails", delegate (IJsonWriter binder)
        {
            binder.ArrayBegin(m_TransfersResults.Length);
            for (int i = 0; i < m_TransfersResults.Length; i++)
            {
                WriteTransfersData(binder, m_TransfersResults[i]);
            }
            binder.ArrayEnd();
        }));

        m_Results = new NativeArray<LinkedTripsInfo>(3, Allocator.Persistent);
        m_PKTResults = new NativeArray<PKTInfo>(9, Allocator.Persistent);
        m_TransfersResults = new NativeArray<TransferInfo>(1, Allocator.Persistent);
        Mod.log.Info("PathTripsUISystem created.");
    }

    //[Preserve]
    protected override void OnDestroy()
    {
        m_Results.Dispose();
        m_TransfersResults.Dispose();
        m_PKTResults.Dispose();
        base.OnDestroy();
    }

    public override int GetUpdateInterval(SystemUpdatePhase phase)
    {
        // One day (or month) in-game is '262144' ticks
        return TimeSystem.kTicksPerDay / 32;
    }

    private int getLastTransitIndex(Entity target, DynamicBuffer<PathElement> pathElements, int startIndex, out int endIndex, out int waypoint1_index, out int waypoint2_index)
    {
        Waypoint waypoint1 = default;
        Waypoint waypoint2 = default;
        waypoint1_index = 0;
        waypoint2_index = 0;
        if (EntityManager.TryGetComponent<Waypoint>(target, out waypoint1))
        {
            waypoint1_index = waypoint1.m_Index;
            for (endIndex = startIndex + 1; endIndex < pathElements.Length; endIndex++)
            {
                if (EntityManager.TryGetComponent<Waypoint>(pathElements[endIndex].m_Target, out waypoint2))
                {
                    waypoint2_index = waypoint2.m_Index;
                } else
                {
                    return endIndex;
                }
            }
        }

        endIndex = startIndex;
        return endIndex;
    }

    private double calculateStraightDistance(float3 a, float3 b)
    {
        return Math.Sqrt(Math.Pow(a.x - b.x, 2f) + Math.Pow(a.y - b.y, 2f) + Math.Pow(a.z - b.z, 2f));
    }
 
    protected override void OnUpdate()
    {
        base.OnUpdate();

        Setting setting = Mod.setting;

        //ResetResults();

        var results = m_PathTripsQuery.ToEntityArray(Allocator.Temp);

        DateTime currentDateTime = this.World.GetExistingSystemManaged<TimeSystem>().GetCurrentDateTime();
        int index = currentDateTime.Hour;

        if (previous_index != index)
        {
            previous_index = index;

            //Last one is vehicle
            int[,] countModes = new int[results.Length, Enum.GetNames(typeof(TransportType)).Length + 1];
            //0: Pedestrian, 1: Vehicle
            int[,] countRoadType = new int[results.Length, 2];
            //O: Pedestrian, 1: Vehicle, 2: Other transit
            int3 countAccess = 0;
            int transitLinkedTrips = 0;
            int transitUnlinkedTrips = 0;
            int vehicleLinkedTrips = 0;
            int pedLinkedTrips = 0;
            int[] unlinkedModeTrips = new int[Enum.GetNames(typeof(TransportType)).Length - 1];
            float[] modePKT = new float[Enum.GetNames(typeof(TransportType)).Length - 1];
            int[,] transferMatrix = new int[Enum.GetNames(typeof(TransportType)).Length - 1, Enum.GetNames(typeof(TransportType)).Length - 1];

            int k = 0;
            foreach (var path in results)
            {
                TransportType currentMode = TransportType.None;
                TransportType previousMode = TransportType.None;
                int currentRoute = 0;
                int previousRoute = 0;
                float3 currentPosition = -1f;
                float3 previousPosition = -1f;

                PathOwner pathOwner;
                HashSet<int> routesHashSet = new HashSet<int>();

                if (EntityManager.TryGetComponent<PathOwner>(path, out pathOwner))
                {
                    DynamicBuffer<PathElement> pathElements;
                    if(EntityManager.TryGetBuffer<PathElement>(path, true, out pathElements))
                    {
                        int totalModes = 0;
                        float distance = 0;
                        int waypoint1_index;
                        int waypoint2_index;
                        TransportType transportType = TransportType.None;

                        for (int i = 0; i < pathElements.Length; ++i)
                        {
                            PathElement element = pathElements[i];

                            Game.Net.ConnectionLane connectionLane;
                            if (EntityManager.TryGetComponent<Game.Net.ConnectionLane>(element.m_Target, out connectionLane))
                            {
                                if ((connectionLane.m_Flags & ConnectionLaneFlags.Pedestrian) != (ConnectionLaneFlags)0)
                                {
                                    countRoadType[k, 0] = 1;
                                }
                                if ((connectionLane.m_Flags & ConnectionLaneFlags.Road) != (ConnectionLaneFlags)0)
                                {
                                    countRoadType[k, 1] = 1;
                                }
                            }
                            
                            if (EntityManager.TryGetComponent(element.m_Target, out Owner owner))
                            {

                                if (EntityManager.HasComponent<RouteLane>(element.m_Target))
                                {
                                    i = getLastTransitIndex(element.m_Target, pathElements, i, out i, out waypoint1_index, out waypoint2_index);

                                    RouteNumber routeNumber;
                                    if (EntityManager.TryGetComponent<RouteNumber>(owner.m_Owner, out routeNumber))
                                    {
                                        PrefabRef prefab1;
                                        if (EntityManager.TryGetComponent<PrefabRef>(owner.m_Owner, out prefab1))
                                        {
                                            TransportLineData transportLineData;
                                            if (EntityManager.TryGetComponent<TransportLineData>(prefab1.m_Prefab, out transportLineData))
                                            {
                                                if (routeNumber.m_Number != currentRoute)
                                                {
                                                    if (!currentMode.Equals(TransportType.None) && distance > 0)
                                                    {
                                                        //Estimate adjustment factor because straight line distance between waypoints is shorter than real distance
                                                        //Factor will take into account: mode, type of trip, number of waypoints
                                                        //factor for air is zero
                                                        float factor = 0;

                                                        //distance thresholds by mode (bus, tram, subway, train, ship) 
                                                        //distance categories: urban, suburban, long-distance
                                                        int[] distance_cat_1 = [500, 600, 1500, 500, 200];
                                                        int[] distance_cat_2 = [2500, 2000, 4000, 15000, 10000];
                                                        float[] urban_factors = [0.25f, 0.2f, 0.15f, 0.15f, 0.25f];
                                                        float[] suburban_factors = [0.15f, 0.15f, 0.1f, 0.075f, 0.15f];
                                                        float[] long_factors = [0.35f, 0.25f, 0.1f, 0.075f, 0.075f];
                                                        int mode_category = 0; //zero is bus

                                                        if((currentMode.Equals(TransportType.Tram)))
                                                        {
                                                            mode_category = 1;
                                                        } 
                                                        else if((currentMode.Equals(TransportType.Subway)))
                                                        {
                                                            mode_category = 2;
                                                        }
                                                        else if ((currentMode.Equals(TransportType.Train)))
                                                        {
                                                            mode_category = 3;
                                                        }
                                                        else if ((currentMode.Equals(TransportType.Ship)))
                                                        {
                                                            mode_category = 4;
                                                        }

                                                        if(!(currentMode.Equals(TransportType.Airplane)))
                                                        {
                                                            factor = urban_factors[mode_category];
                                                            if (distance > distance_cat_1[mode_category] && distance <= distance_cat_2[mode_category])
                                                            {
                                                                factor = suburban_factors[mode_category]; ;
                                                            }
                                                            else if ( distance > distance_cat_2[mode_category])
                                                            {
                                                                factor = long_factors[mode_category]; ;
                                                            }
                                                        }

                                                        if(waypoint2_index == waypoint1_index)
                                                        {
                                                            waypoint2_index++;
                                                        }
                                                        float factor2 = (float)(1f + factor / Math.Sqrt(Math.Abs(waypoint2_index - waypoint1_index)));
                                                        
                                                        modePKT[(int)currentMode] += (distance*factor2)/1000f;
                                                        //Mod.log.Info($"k:{k},i:{i},distance:{distance},mode_category:{mode_category},factor:{factor},factor2:{factor2},sqrt:{Math.Sqrt((double)(waypoint2_index - waypoint1_index))},mode:{currentMode},transportType:{transportType},waypoint1_index:{waypoint1_index},waypoint2_index:{waypoint2_index}");
                                                    }

                                                    previousRoute = currentRoute;
                                                    currentRoute = routeNumber.m_Number;

                                                    previousMode = currentMode;
                                                    currentMode = transportLineData.m_TransportType;
                                                    
                                                    if (!previousMode.Equals(TransportType.None) && !currentMode.Equals(TransportType.None))
                                                    {
                                                        transferMatrix[(int)previousMode, (int)currentMode]++;
                                                        //Mod.log.Info($"Transfer from: {previousMode} to {currentMode}, transferMatrix:{transferMatrix[(int)previousMode, (int)currentMode]}");
                                                    }
                                                    unlinkedModeTrips[(int)transportLineData.m_TransportType]++;
                                                    countModes[k, (int)transportLineData.m_TransportType] = 1;
                                                    routesHashSet.Add(((int)transportLineData.m_TransportType) * 1000 + routeNumber.m_Number);
                                                    transportType = transportLineData.m_TransportType;
                                                    distance = 0;
                                                    currentPosition = -1f;
                                                }

                                                
                                            }
                                        }

                                        if (EntityManager.TryGetBuffer<RouteWaypoint>(owner.m_Owner, true, out DynamicBuffer<RouteWaypoint> routeWaypoints))
                                        {
                                            for (int j = 0; j < routeWaypoints.Length; j++)
                                            {
                                                RouteWaypoint routeWaypoint = routeWaypoints[j];
                                                Waypoint waypoint;
                                                if (EntityManager.TryGetComponent<Waypoint>(routeWaypoint.m_Waypoint, out waypoint))
                                                {
                                                    if (waypoint.m_Index >= waypoint1_index && waypoint.m_Index <= waypoint2_index)
                                                    {
                                                        Position position;
                                                        if (EntityManager.TryGetComponent<Position>(routeWaypoint.m_Waypoint, out position))
                                                        {
                                                            previousPosition = currentPosition;
                                                            currentPosition = position.m_Position;

                                                            if (previousPosition.x != -1f)
                                                            {
                                                                distance += (float)calculateStraightDistance(previousPosition, currentPosition);
                                                            }

                                                            //Mod.log.Info($"k:{k},i:{i},j:{j},{distance},routeWaypoints:{routeWaypoints.Length},previousPosition:{previousPosition},currentPosition:{currentPosition},waypoint.m_Index:{waypoint.m_Index},waypoint1.m_Index:{waypoint1_index},waypoint2.m_Index:{waypoint2_index},mode:{currentMode},transportType:{transportType}");
                                                        }
                                                    }
                                                }
                                            }
                                        }
                                    }
                                }

                                
                            }
                        }

                        //Transit Modes
                        totalModes += routesHashSet.Count();

                        if (totalModes == 0)
                        {
                            if (countRoadType[k, 1] == 1)
                            {
                                vehicleLinkedTrips++;
                            }
                            else if (countRoadType[k, 0] == 1 && countRoadType[k, 1] == 0)
                            {
                                pedLinkedTrips++;
                            }
                        }

                        if(countRoadType[k, 1] == 1 && routesHashSet.Count() >= 1)
                        {
                            countAccess.y++;
                        }
                        else if(routesHashSet.Count() > 1)
                        {
                            countAccess.z++;
                        }
                        else if(routesHashSet.Count() == 1)
                        {
                            countAccess.x++;
                        }

                        if (totalModes >= 1)
                        {
                            transitLinkedTrips++;
                            transitUnlinkedTrips += totalModes;
                        }
                    }
                }
                k++;
            }


            LinkedTripsInfo info = new LinkedTripsInfo((int)linkedMode.Vehicle);
            info.Trips = (int)(10 * Math.Round((100 * (vehicleLinkedTrips / (float)(vehicleLinkedTrips + transitLinkedTrips + pedLinkedTrips))), 1));
            m_Results[(int)linkedMode.Vehicle] = info;
            info = new LinkedTripsInfo((int)linkedMode.Transit);
            info.Trips = (int)(10 * Math.Round((100 * (transitLinkedTrips / (float)(vehicleLinkedTrips + transitLinkedTrips + pedLinkedTrips))), 1));
            m_Results[(int)linkedMode.Transit] = info;
            info = new LinkedTripsInfo((int)linkedMode.Pedestrian);
            info.Trips = (int)(10 * Math.Round((100 * (pedLinkedTrips / (float)(vehicleLinkedTrips + transitLinkedTrips + pedLinkedTrips))), 1));
            m_Results[(int)linkedMode.Pedestrian] = info;


            List<int> transports = new() { (int)TransportType.Bus, (int)TransportType.Tram, (int)TransportType.Subway, (int)TransportType.Train, (int)TransportType.Ship, (int)TransportType.Airplane };
            PKTInfo pktInfo;
            foreach (int t in transports)
            {
                int pkt = (int)Math.Round(modePKT[t]);
                if (pkt > 0)
                {
                    pktInfo = new PKTInfo(t);
                    pktInfo.PKT = pkt;
                    m_PKTResults[t] = pktInfo;
                }
            }


            TransferInfo info2 = new TransferInfo(0);
            if(transitLinkedTrips > 0)
            {
                info2.Trips = (int)(100 * Math.Round(((float)transitUnlinkedTrips) / transitLinkedTrips, 2)) - 100;
            } else
            {
                info2.Trips = 0;
            }
            
            m_TransfersResults[0] = info2;
            //for (int t = 0; t < Enum.GetNames(typeof(TransportType)).Length - 1; t++)
            //{
            //    Mod.log.Info($"t:{(TransportType)t},Linked: {linkedModeTrips[t]}, Unlinked:{unlinkedModeTrips[t]}, trasnfer rate:{unlinkedModeTrips[t]/(float)linkedModeTrips[t] - 1f}");
            //}
            //Mod.log.Info($"Unlinked:{transitUnlinkedTrips}, LInked:{transitLinkedTrips}, transfers:{info2.Trips}, VehicleLinked:{vehicleLinkedTrips}, PedLinked:{pedLinkedTrips}");
        }

        m_uiResults.Update();
        m_uiTransfersResults.Update();
        m_uiPKTResults.Update();
    }

    private void ResetResults()
    {
        for (int i = 0; i < m_PKTResults.Length; i++)
        {
            m_PKTResults[i] = new PKTInfo(i);
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
    public PathTripsUISystem()
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
        NativeArray<Entity> entities = m_PathTripsQuery.ToEntityArray(Allocator.Temp);
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
