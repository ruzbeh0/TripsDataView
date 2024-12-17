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

// This System calculates Commute time information
[CompilerGenerated]
public partial class CommuteTimeUISystem : ExtendedUISystemBase
{

    /// <summary>
    /// Holds info about commute time
    /// </summary>
    /// 
    private int previous_index = -1;
    private struct CommuteByTimeBinInfo
    {
        public double TimeBin;
        public int Total; // Total is a sum of the below parts
        public int Level1;
        public int Level2;
        public int Level3;
        public int Level4;
        public int Level5;
        public CommuteByTimeBinInfo(int _timeBin) { TimeBin = _timeBin; }
    }

    private static void WriteData(IJsonWriter writer, CommuteByTimeBinInfo info)
    {
        writer.TypeBegin("CommuteByTimeBinInfo");
        writer.PropertyName("timeBin");
        writer.Write(info.TimeBin);
        writer.PropertyName("total");
        writer.Write(info.Total);
        writer.PropertyName("level1");
        writer.Write(info.Level1);
        writer.PropertyName("level2");
        writer.Write(info.Level2);
        writer.PropertyName("level3");
        writer.Write(info.Level3);
        writer.PropertyName("level4");
        writer.Write(info.Level4);
        writer.PropertyName("level5");
        writer.Write(info.Level5);
        writer.TypeEnd();
    }

    private const string kGroup = "commuteTimeInfo";
    protected const string group = "commuteTime";

    private EntityQuery m_CommuteTimeQuery;

    private RawValueBinding m_uiResults;

    private NativeArray<CommuteByTimeBinInfo> m_Results; // final results, will be filled via jobs and then written as output

    // 240209 Set gameMode to avoid errors in the Editor
    public override GameMode gameMode => GameMode.Game;

    //[Preserve]
    protected override void OnCreate()
    {
        base.OnCreate();
        Setting setting = Mod.setting;
        //m_AgeCapUISetting = CreateBinding("AgeCap", setting.AgeCapSetting);

        m_CommuteTimeQuery = GetEntityQuery(new EntityQueryDesc
        {
            All = new[] {
                    ComponentType.ReadWrite<Worker>()
                }
        });
        RequireForUpdate(m_CommuteTimeQuery);

        AddBinding(m_uiResults = new RawValueBinding(kGroup, "commuteTimeDetails", delegate (IJsonWriter binder)
        {
            binder.ArrayBegin(m_Results.Length);
            for (int i = 0; i < m_Results.Length; i++)
            {
                WriteData(binder, m_Results[i]);
            }
            binder.ArrayEnd();
        }));

        m_Results = new NativeArray<CommuteByTimeBinInfo>(40, Allocator.Persistent); 
        Mod.log.Info("CommuteTimeUISystem created.");
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

        //ResetResults();

        var results = m_CommuteTimeQuery.ToEntityArray(Allocator.Temp);

        DateTime currentDateTime = this.World.GetExistingSystemManaged<TimeSystem>().GetCurrentDateTime();
        int index = currentDateTime.Hour;

        if (previous_index != index)
        {
            previous_index = index;

            int bin_size = 40;
            int[,] commute_10min_bins = new int[bin_size,5];
            float minutes_in_bin = 10f;

            foreach (var veh in results)
            {
                Worker data1;

                if (EntityManager.TryGetComponent<Worker>(veh, out data1))
                {
                    float commute = 24f * 60f * 60f * (data1.m_LastCommuteTime / TimeSystem.kTicksPerDay);

                    int b = (int)Math.Floor(commute / minutes_in_bin);
                    if (b > (bin_size - 1))
                    {
                        b = bin_size - 1;
                    }
                    commute_10min_bins[b,data1.m_Level]++;
                }
            }

            for (int i = 0; i < bin_size; i++)
            {
                CommuteByTimeBinInfo info = new CommuteByTimeBinInfo((int)(i*minutes_in_bin));
                info.Level1 = commute_10min_bins[i, 0];
                info.Level2 = commute_10min_bins[i, 1];
                info.Level3 = commute_10min_bins[i, 2];
                info.Level4 = commute_10min_bins[i, 3];
                info.Level5 = commute_10min_bins[i, 4];
                m_Results[i] = info;

                //string line = $"{i},{info.Level1},{info.Level2},{info.Level3},{info.Level4},{info.Level5}";
                //
                //using (StreamWriter sw = File.AppendText(fileName))
                //{
                //    sw.WriteLine(line);
                //}
            }
            
        }

        m_uiResults.Update();
    }

    private void ResetResults()
    {
        for (int i = 0; i < m_Results.Length; i++)
        {
            m_Results[i] = new CommuteByTimeBinInfo(i);
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
    public CommuteTimeUISystem()
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
        NativeArray<Entity> entities = m_CommuteTimeQuery.ToEntityArray(Allocator.Temp);
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
