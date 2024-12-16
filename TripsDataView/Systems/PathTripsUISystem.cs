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

    private static void WriteLinkedData(IJsonWriter writer, LinkedTripsInfo info)
    {
        writer.TypeBegin("LinkedTripsInfo");
        writer.PropertyName("mode");
        writer.Write(info.Mode);
        writer.PropertyName("trips");
        writer.Write(info.Trips);
        writer.TypeEnd();
    }

    private const string kGroup = "pathTripsInfo";
    protected const string group = "pathTrips";

    private EntityQuery m_PathTripsQuery;

    private RawValueBinding m_uiResults;

    private NativeArray<LinkedTripsInfo> m_Results; // final results, will be filled via jobs and then written as output

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
        
        m_Results = new NativeArray<LinkedTripsInfo>(3, Allocator.Persistent); 
        Mod.log.Info("PathTripsUISystem created.");
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
        return TimeSystem.kTicksPerDay / 32;
    }

    private bool getTransitWaypoints(Entity target, DynamicBuffer<PathElement> pathElements, int startIndex, out int endIndex, out Waypoint waypoint1, out Waypoint waypoint2)
    {
        if (EntityManager.TryGetComponent<Waypoint>(target, out waypoint1))
        {
            for (endIndex = startIndex + 1; endIndex < pathElements.Length; endIndex++)
            {
                if (EntityManager.TryGetComponent<Waypoint>(pathElements[endIndex].m_Target, out waypoint2))
                {
                    return true;
                }
            }
        }

        waypoint1 = default;
        waypoint2 = default;
        endIndex = startIndex;
        return false;
    }

    protected override void OnUpdate()
    {
        Mod.log.Info($"OnUpdate");
        base.OnUpdate();

        Setting setting = Mod.setting;

        //ResetResults();

        var results = m_PathTripsQuery.ToEntityArray(Allocator.Temp);

        DateTime currentDateTime = this.World.GetExistingSystemManaged<TimeSystem>().GetCurrentDateTime();
        int index = currentDateTime.Hour;

        Mod.log.Info($"OnUpdate:{results.Length}");
        if (previous_index != index)
        {
            previous_index = index;

            //Last one is vehicle
            int[,] countModes = new int[results.Length, Enum.GetNames(typeof(TransportType)).Length + 1];
            //0: Pedestrian, 1: Vehicle
            int[,] countRoadType = new int[results.Length, 2];
            int transitLinkedTrips = 0;
            int transitUnlinkedTrips = 0;
            int vehicleLinkedTrips = 0;
            int pedLinkedTrips = 0;
            int k = 0;
            foreach (var path in results)
            {
                PathOwner pathOwner;

                if (EntityManager.TryGetComponent<PathOwner>(path, out pathOwner))
                {
                    DynamicBuffer<PathElement> pathElements;
                    if(EntityManager.TryGetBuffer<PathElement>(path, true, out pathElements))
                    {
                        int totalModes = 0;
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
                                if (EntityManager.HasComponent<RouteLane>(element.m_Target) &&
                                    this.getTransitWaypoints(element.m_Target, pathElements, i, out i, out Waypoint waypoint1, out Waypoint waypoint2))
                                {
                                    //if (i >= pathOwner.m_ElementIndex)
                                    {

                                        PrefabRef prefab1;
                                        if (EntityManager.TryGetComponent<PrefabRef>(owner.m_Owner, out prefab1))
                                        {
                                            TransportLineData transportLineData;
                                            if (EntityManager.TryGetComponent<TransportLineData>(prefab1.m_Prefab, out transportLineData))
                                            {
                                                countModes[k, (int)transportLineData.m_TransportType] = 1;
                                            }
                                        }
                                    }
                                }
                            }
                        }

                        //Transit Modes
                        for (int j = 0; j < Enum.GetNames(typeof(TransportType)).Length; ++j)
                        {
                            totalModes += countModes[k, j];
                        }

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
            info.Trips = vehicleLinkedTrips;
            m_Results[(int)linkedMode.Vehicle] = info;
            info = new LinkedTripsInfo((int)linkedMode.Transit);
            info.Trips = transitLinkedTrips;
            m_Results[(int)linkedMode.Transit] = info;
            info = new LinkedTripsInfo((int)linkedMode.Pedestrian);
            info.Trips = pedLinkedTrips;
            m_Results[(int)linkedMode.Pedestrian] = info;
            Mod.log.Info($"Unlinked:{transitUnlinkedTrips}, LInked:{transitLinkedTrips}, VehicleLinked:{vehicleLinkedTrips}, PedLinked:{pedLinkedTrips}");
        }


        m_uiResults.Update();
    }

    private void ResetResults()
    {
        for (int i = 0; i < m_Results.Length; i++)
        {
            m_Results[i] = new LinkedTripsInfo(i);
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
