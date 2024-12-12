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
public partial class TripPurposeUISystem : ExtendedUISystemBase
{

    /// <summary>
    /// Holds info about trips purpose by hour
    /// </summary>
    /// 
    private int previous_index = -1;
    private Dictionary<Entity, TravelPurpose> _CitizenToData = new Dictionary<Entity, TravelPurpose>();
    private Dictionary<int, Purpose[]> _outputData = new Dictionary<int, Purpose[]>();
    private struct TripPurposeInfo
    {
        public double Hour;
        public int Total; // Total is a sum of the below parts
        public int Hbw;
        public int Hbo;
        public int Hbsch;
        public int Nhb;
        public TripPurposeInfo(int _hour) { Hour = _hour; }
    }

    private static void WriteData(IJsonWriter writer, TripPurposeInfo info)
    {
        writer.TypeBegin("tripPurposeInfo");
        writer.PropertyName("hour");
        writer.Write(info.Hour);
        writer.PropertyName("total");
        writer.Write(info.Total);
        writer.PropertyName("hbw");
        writer.Write(info.Hbw);
        writer.PropertyName("hbo");
        writer.Write(info.Hbo);
        writer.PropertyName("hbsch");
        writer.Write(info.Hbsch);
        writer.PropertyName("nhb");
        writer.Write(info.Nhb);
        writer.TypeEnd();
    }

    private const string kGroup = "tripPurposeInfo";
    protected const string group = "tripPurpose";

    private EntityQuery m_TripPurposeQuery;

    private RawValueBinding m_uiResults;

    private NativeArray<TripPurposeInfo> m_Results; // final results, will be filled via jobs and then written as output

    // 240209 Set gameMode to avoid errors in the Editor
    public override GameMode gameMode => GameMode.Game;

    //[Preserve]
    protected override void OnCreate()
    {
        base.OnCreate();
        Setting setting = Mod.setting;

        m_TripPurposeQuery = GetEntityQuery(new EntityQueryDesc
        {
            All = new[] {
                    ComponentType.ReadOnly<TravelPurpose>(),
                }
        });
        RequireForUpdate(m_TripPurposeQuery);

        AddBinding(m_uiResults = new RawValueBinding(kGroup, "tripPurposeDetails", delegate (IJsonWriter binder)
        {
            binder.ArrayBegin(m_Results.Length);
            for (int i = 0; i < m_Results.Length; i++)
            {
                WriteData(binder, m_Results[i]);
            }
            binder.ArrayEnd();
        }));

        //AddBinding(m_uiResults = new RawValueBinding(kGroup, "tripPurposeDirDetails", delegate (IJsonWriter binder)
        //{
        //    binder.ArrayBegin(m_Results.Length);
        //    for (int i = 0; i < m_Results.Length; i++)
        //    {
        //        WriteData(binder, m_Results[i]);
        //    }
        //    binder.ArrayEnd();
        //}));

        m_Results = new NativeArray<TripPurposeInfo>(24, Allocator.Persistent);
        Mod.log.Info("TripPurposeUISystem created.");
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

        var results = m_TripPurposeQuery.ToEntityArray(Allocator.Temp);

        CityConfigurationSystem m_CityConfigurationSystem = this.World.GetOrCreateSystemManaged<CityConfigurationSystem>();

        DateTime currentDateTime = this.World.GetExistingSystemManaged<TimeSystem>().GetCurrentDateTime();
        int index = currentDateTime.Hour;

        string path = Path.Combine(Mod.outputPath, Mod.trip_purpose_dir);
        string fileName2 = path +
            "_" + m_CityConfigurationSystem.cityName + "_" + currentDateTime.DayOfYear + "_" + currentDateTime.Year + ".csv";

        if (!File.Exists(fileName2))
        {
            string header = "hour,hw,wh,wo,ow,hsch,schh,scho,osch,ho,oh,oo"; ;
            Utils.createAndDeleteFiles(fileName2, header, Mod.trip_purpose, path);
        }

        path = Path.Combine(Mod.outputPath, Mod.trip_purpose);
        string fileName = path +
            "_" + m_CityConfigurationSystem.cityName + "_" + currentDateTime.DayOfYear + "_" + currentDateTime.Year + ".csv";

        if (!File.Exists(fileName))
        {
            string header = "hour,hbw,hbo,hbsch,nhb";
            Utils.createAndDeleteFiles(fileName, header, Mod.trip_purpose, path);
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

                            TripPurposeInfo info = new TripPurposeInfo(Int32.Parse(parts[0]));
                            info.Hbw = Int32.Parse(parts[1]);
                            info.Hbo = Int32.Parse(parts[2]);
                            info.Hbsch = Int32.Parse(parts[3]);
                            info.Nhb = Int32.Parse(parts[4]);
                            info.Total = info.Hbw + info.Hbo + info.Nhb + info.Hbsch;
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

            //Citizen Purposes
            int[] cimpurp = new int[(int)Purpose.Count];
            int tourists = 0;

            foreach (var cim in results)
            {
                Citizen data1;
                TravelPurpose data2;

                data2 = EntityManager.GetComponentData<TravelPurpose>(cim);
                if (EntityManager.TryGetComponent<Citizen>(cim, out data1))
                {
                    Purpose[] purp;
                    if (!_outputData.ContainsKey(cim.Index))
                    {
                        // 0 - 11 YESTERDAY, 12 to 35 TODAY
                        purp = new Purpose[36];
                        purp[index + 12] = data2.m_Purpose;
                        _outputData.Add(cim.Index, purp);
                    }
                    else
                    {
                        if (_outputData.TryGetValue(cim.Index, out purp))
                        {
                            purp[index + 12] = data2.m_Purpose;
                            _outputData[cim.Index] = purp;
                        }
                    }

                    if ((data1.m_State & CitizenFlags.Tourist) != 0)
                    {
                        tourists++;
                    }
                }
            }

            if (currentDateTime.Hour == 0)
            {
                int[] hbw = new int[48];
                int[] hbsch = new int[48];
                int[] hbo = new int[48];
                int[] nhb = new int[48];

                int[] hw = new int[48];
                int[] wh = new int[48];
                int[] hsch = new int[48];
                int[] schh = new int[48];
                int[] ho = new int[48];
                int[] oh = new int[48];
                int[] oo = new int[48];
                int[] wo = new int[48];
                int[] ow = new int[48];
                int[] osch = new int[48];
                int[] scho = new int[48];

                for (int h = 0; h < 48; h++)
                {
                    hbw[h] = 0;
                    hbsch[h] = 0;
                    hbo[h] = 0;
                    nhb[h] = 0;
                }

                foreach (var key in _outputData.Keys)
                {
                    Purpose[] purp = _outputData[key];
                    Purpose prev = Purpose.PathFailed;
                    int i = 0;
                    List<Purpose> tpurp = new List<Purpose>();
                    List<int> thour = new List<int>();
                    while (i < 36)
                    {
                        Purpose current = purp[i];

                        if (i < 35)
                        {
                            int j = i + 1;
                            Purpose next = purp[j];

                            while (current.Equals(next) && j < 35)
                            {
                                j++;
                                next = purp[j];
                            }

                            if (!current.Equals(next))
                            {
                                if (!current.Equals(Purpose.None) && !current.Equals(prev))
                                {
                                    tpurp.Add(current);
                                    thour.Add(i);
                                    prev = current;
                                }
                            }

                            i = j;
                        }
                        else
                        {
                            if (!current.Equals(purp[i - 1]))
                            {
                                if (!current.Equals(Purpose.None) && !current.Equals(prev))
                                {
                                    tpurp.Add(current);
                                    thour.Add(i);
                                    prev = current;
                                }
                            }
                            i++;
                        }
                    }

                    if (tpurp.Count <= 2)
                    {
                        continue;
                    }

                    for (int k = 0; k < tpurp.Count; k++)
                    {
                        Purpose current = tpurp[k];
                        Purpose previous;
                        Purpose next;

                        int previous_hour;
                        int next_hour;

                        if (k == 0)
                        {
                            previous = tpurp[tpurp.Count - 1];
                            previous_hour = thour[thour.Count - 1];
                        }
                        else
                        {
                            previous = tpurp[k - 1];
                            previous_hour = thour[k - 1];
                        }

                        if (k == (tpurp.Count - 1))
                        {
                            next = tpurp[0];
                            next_hour = thour[0];
                        }
                        else
                        {
                            next = tpurp[k + 1];
                            next_hour = thour[k + 1];
                        }

                        int h = thour[k];
                        int hour_diff = h - previous_hour;
                        if (previous_hour > h)
                        {
                            hour_diff += 36;
                        }
                        if (h > 11 && (hour_diff) < 10)
                        {
                            if ((h == 12 && previous.Equals(next) && (next_hour == 13)) || previous.Equals(current))
                            {
                                continue;
                            }
                            // Going to Work
                            if (current.Equals(Purpose.GoingToWork))
                            {
                                if (h == 12 && next_hour == 13 && (next.Equals(Purpose.Sleeping) || (next.Equals(Purpose.GoingHome) && previous.Equals(Purpose.GoingHome))))
                                {
                                    continue;
                                }
                                if (previous.Equals(Purpose.Working))
                                {
                                    continue;
                                }
                                if (previous.Equals(Purpose.Sleeping) || previous.Equals(Purpose.None) || previous.Equals(Purpose.GoingHome))
                                {
                                    hbw[h - 12]++;
                                    hw[h - 12]++;
                                }
                                else
                                {
                                    nhb[h - 12]++;
                                    ow[h - 12]++;
                                }
                            }
                            else
                            {
                                // For cases where status is "Working", but there is no "GoingToWork" status before between now and previous location
                                if (current.Equals(Purpose.Working))
                                {
                                    if (next.Equals(Purpose.GoingToWork))
                                    {
                                        continue;
                                    }
                                    if (previous.Equals(Purpose.GoingHome) || previous.Equals(Purpose.Sleeping) || previous.Equals(Purpose.None))
                                    {
                                        hbw[h - 12]++;
                                        hw[h - 12]++;
                                    }
                                    else
                                    {
                                        if (!previous.Equals(Purpose.GoingToWork))
                                        {
                                            nhb[h - 12]++;
                                            ow[h - 12]++;
                                        }
                                    }
                                }
                                else
                                {
                                    if (current.Equals(Purpose.GoingToSchool))
                                    {
                                        if (previous.Equals(Purpose.GoingHome) || previous.Equals(Purpose.Sleeping) || previous.Equals(Purpose.None))
                                        {
                                            hbsch[h - 12]++;
                                            hsch[h - 12]++;
                                        }
                                        else
                                        {
                                            nhb[h - 12]++;
                                            osch[h - 12]++;
                                        }
                                    }
                                    else
                                    {
                                        // For cases where status is "Studying", but there is no "GoingToSchool" status before between now and previous location
                                        if (current.Equals(Purpose.Studying))
                                        {
                                            if (previous.Equals(Purpose.Sleeping) || previous.Equals(Purpose.None))
                                            {
                                                hbsch[h - 12]++;
                                                hsch[h - 12]++;
                                            }
                                            else
                                            {
                                                if (!previous.Equals(Purpose.GoingToSchool))
                                                {
                                                    nhb[h - 12]++;
                                                    osch[h - 12]++;
                                                }

                                            }
                                        }
                                        else
                                        {
                                            //Going Home
                                            if (current.Equals(Purpose.GoingHome))
                                            {
                                                if (h == 12 && next_hour == 13 && next.Equals(Purpose.Working) && previous.Equals(Purpose.Working))
                                                {
                                                    continue;
                                                }
                                                if (previous.Equals(Purpose.Working) || (previous.Equals(Purpose.GoingToWork)))
                                                {
                                                    if ((next.Equals(Purpose.Sleeping) || next.Equals(Purpose.None) || next.Equals(Purpose.GoingToWork)) || (Math.Abs(next_hour - h) > 1))
                                                    {
                                                        hbw[h - 12]++;
                                                        wh[h - 12]++;
                                                    }
                                                    else
                                                    {
                                                        nhb[h - 12]++;
                                                        wo[h - 12]++;
                                                    }
                                                }
                                                else
                                                {
                                                    if (previous.Equals(Purpose.Studying) || previous.Equals(Purpose.GoingToSchool))
                                                    {
                                                        if ((next.Equals(Purpose.Sleeping) || next.Equals(Purpose.None)) || (Math.Abs(next_hour - h) > 1))
                                                        {
                                                            hbsch[h - 12]++;
                                                            schh[h - 12]++;
                                                        }
                                                        else
                                                        {
                                                            nhb[h - 12]++;
                                                            scho[h - 12]++;
                                                        }
                                                    }
                                                    else
                                                    {
                                                        if (previous.Equals(Purpose.Sleeping))
                                                        {
                                                            continue;
                                                        }
                                                        if ((next.Equals(Purpose.Sleeping) || next.Equals(Purpose.None)) || (Math.Abs(next_hour - h) > 1))
                                                        {
                                                            hbo[h - 12]++;
                                                            oh[h - 12]++;
                                                        }
                                                        else
                                                        {
                                                            if (!previous.Equals(Purpose.None) && !previous.Equals(Purpose.Sleeping))
                                                            {
                                                                if ((Math.Abs(next_hour - h) > 2))
                                                                {
                                                                    hbo[h - 12]++;
                                                                    oh[h - 12]++;
                                                                }
                                                                else
                                                                {
                                                                    nhb[h - 12]++;
                                                                    oo[h - 12]++;
                                                                }
                                                            }
                                                        }
                                                    }
                                                }
                                            }
                                            else
                                            {
                                                if (current.Equals(Purpose.None))
                                                {
                                                    continue;
                                                }
                                                if (previous.Equals(Purpose.Sleeping) || previous.Equals(Purpose.None) || previous.Equals(Purpose.GoingHome))
                                                {
                                                    if (!current.Equals(Purpose.Sleeping))
                                                    {
                                                        if (!(current.Equals(Purpose.GoingToSchool) || current.Equals(Purpose.GoingToWork) || current.Equals(Purpose.Studying) || current.Equals(Purpose.Working)))
                                                        {
                                                            hbo[h - 12]++;
                                                            ho[h - 12]++;
                                                        }
                                                        else
                                                        {
                                                            nhb[h - 12]++;
                                                            oo[h - 12]++;
                                                        }
                                                    }
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                    thour = null;
                    tpurp = null;
                }

                Dictionary<int, Purpose[]> _outputDataTemp = new Dictionary<int, Purpose[]>();
                int[] keys = _outputData.Keys.ToArray<int>();
                foreach (var key in keys)
                {
                    Purpose[] purp;
                    if (_outputData.TryGetValue(key, out purp))
                    {
                        for (int h = 0; h < 12; h++)
                        {
                            purp[h] = purp[h + 24];
                            purp[h + 12] = 0;
                            purp[h + 24] = 0;
                            _outputData[key] = purp;
                        }
                    }
                }

                using (StreamWriter sw = File.AppendText(fileName))
                {
                    for (int h = 0; h < 24; h++)
                    {
                        sw.WriteLine($"{h},{hbw[h]},{hbo[h]},{hbsch[h]},{nhb[h]}");

                        TripPurposeInfo info = new TripPurposeInfo(h);
                        info.Hbw = hbw[h];
                        info.Hbo = hbo[h];
                        info.Hbsch = hbsch[h];
                        info.Nhb = nhb[h];
                        info.Total = hbw[h] + hbo[h] + hbsch[h] + nhb[h];
                        m_Results[h] = info;
                    }
                }

                using (StreamWriter sw = File.AppendText(fileName2))
                {
                    for (int h = 0; h < 24; h++)
                    {
                        sw.WriteLine($"{h},{hw[h]},{wh[h]},{wo[h]},{ow[h]},{hsch[h]},{schh[h]},{scho[h]},{osch[h]},{ho[h]},{oh[h]},{oo[h]}");
                    }
                }
            }
        }

        m_uiResults.Update();
    }

    private void ResetResults()
    {
        for (int i = 0; i < m_Results.Length; i++)
        {
            m_Results[i] = new TripPurposeInfo(i);
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
    public TripPurposeUISystem()
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
        NativeArray<Entity> entities = m_TripPurposeQuery.ToEntityArray(Allocator.Temp);
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
