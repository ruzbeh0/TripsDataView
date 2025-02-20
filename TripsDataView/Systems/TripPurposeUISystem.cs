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
using Newtonsoft.Json;

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
    private const int samples_per_hour = 3;
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
    private string jsonFileName;

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

        m_Results = new NativeArray<TripPurposeInfo>(24, Allocator.Persistent);
        Mod.log.Info("TripPurposeUISystem created.");
    }

    //[Preserve]
    protected override void OnDestroy()
    {
        m_Results.Dispose();
        base.OnDestroy();
    }

    public void SaveCimTravelPurposes()
    {
        if (_outputData.Keys.Count() > 0)
        {
            CityConfigurationSystem m_CityConfigurationSystem = this.World.GetOrCreateSystemManaged<CityConfigurationSystem>();

            DateTime currentDateTime = this.World.GetExistingSystemManaged<TimeSystem>().GetCurrentDateTime();

            string path = Path.Combine(Mod.outputPath, Mod.cim_travel_history);
            jsonFileName = path +
                "_" + m_CityConfigurationSystem.cityName + "_" + currentDateTime.DayOfYear + "_" + currentDateTime.Year + ".csv";

            var json = JsonConvert.SerializeObject(_outputData);
            Utils.createAndDeleteFiles(jsonFileName, json, Mod.cim_travel_history);
            Mod.log.Info($"Saved Json file with {_outputData.Keys.Count()} keys");
        } 
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

        var results = m_TripPurposeQuery.ToEntityArray(Allocator.Temp);

        CityConfigurationSystem m_CityConfigurationSystem = this.World.GetOrCreateSystemManaged<CityConfigurationSystem>();
        CitySystem m_CitySystem = this.World.GetOrCreateSystemManaged<CitySystem>();

        DateTime currentDateTime = this.World.GetExistingSystemManaged<TimeSystem>().GetCurrentDateTime();
        int hour = currentDateTime.Hour;
        int minute_part = (int)Math.Floor(((float)currentDateTime.Minute) / (60 / samples_per_hour));
        int index = minute_part % (60/samples_per_hour) + hour*samples_per_hour;

        //string path = Path.Combine(Mod.outputPath, Mod.trip_purpose_dir);
        //string fileName2 = path +
        //    "_" + m_CityConfigurationSystem.cityName + "_" + currentDateTime.DayOfYear + "_" + currentDateTime.Year + ".csv";
        //
        //if (!File.Exists(fileName2))
        //{
        //    string header = "hour,hw,wh,wo,ow,hsch,schh,scho,osch,ho,oh,oo"; ;
        //    Utils.createAndDeleteFiles(fileName2, header, Mod.trip_purpose);
        //}

        string path = Path.Combine(Mod.outputPath, Mod.trip_purpose);
        string fileName = path +
            "_" + m_CityConfigurationSystem.cityName + "_" + currentDateTime.DayOfYear + "_" + currentDateTime.Year + ".csv";

        path = Path.Combine(Mod.outputPath, Mod.cim_travel_history);
        jsonFileName = path +
            "_" + m_CityConfigurationSystem.cityName + "_" + currentDateTime.DayOfYear + "_" + currentDateTime.Year + ".csv";

        if (!File.Exists(fileName))
        {
            string header = "hour,hbw,hbo,hbsch,nhb";
            Utils.createAndDeleteFiles(fileName, header, Mod.trip_purpose);
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
                            string[] line_parts = line.Split(',');
                            
                            if(line_parts.Length > 0)
                            {
                                TripPurposeInfo info = new TripPurposeInfo(Int32.Parse(line_parts[0]));
                                info.Hbw = Int32.Parse(line_parts[1]);
                                info.Hbo = Int32.Parse(line_parts[2]);
                                info.Hbsch = Int32.Parse(line_parts[3]);
                                info.Nhb = Int32.Parse(line_parts[4]);
                                info.Total = info.Hbw + info.Hbo + info.Nhb + info.Hbsch;
                                m_Results[Int32.Parse(line_parts[0])] = info;
                            }
                        }
                        i++;
                    }
                }
            }  
        }

        int parts = 36 * samples_per_hour;

        if (previous_index != index)
        {
            previous_index = index;

            if(File.Exists(jsonFileName) && index > 0 && _outputData.Keys.Count() == 0)
            {
                using (StreamReader r = new StreamReader(jsonFileName))
                {
                    string jsonR = r.ReadToEnd();
                    _outputData = JsonConvert.DeserializeObject<Dictionary<int, Purpose[]>>(jsonR);

                    Mod.log.Info($"Loaded Json file with {_outputData.Keys.Count()} keys");
                }      
            } 

            //Citizen Purposes
            int[] cimpurp = new int[(int)Purpose.Count];
            int tourists = 0;

            int total_hbo_17 = 0;
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
                        // 0 - (parts/3 - 1) YESTERDAY, (parts/3) to (parts - 1) TODAY
                        purp = new Purpose[parts];
                        purp[index + (parts/3)] = data2.m_Purpose;
                        _outputData.Add(cim.Index, purp);
                    }
                    else
                    {
                        if (_outputData.TryGetValue(cim.Index, out purp))
                        {
                            if(purp.Length < parts)
                            {
                                purp = new Purpose[parts];
                            }

                            purp[index + (parts / 3)] = data2.m_Purpose;
                            _outputData[cim.Index] = purp;
                        }
                    }

                    if ((data1.m_State & CitizenFlags.Tourist) != 0)
                    {
                        tourists++;
                    }
                }
            }

            if (index == 0)
            {
                int[] hbw = new int[24];
                int[] hbsch = new int[24];
                int[] hbo = new int[24];
                int[] nhb = new int[24];

                int[] hw = new int[24];
                int[] wh = new int[24];
                int[] hsch = new int[24];
                int[] schh = new int[24];
                int[] ho = new int[24];
                int[] oh = new int[24];
                int[] oo = new int[24];
                int[] wo = new int[24];
                int[] ow = new int[24];
                int[] osch = new int[24];
                int[] scho = new int[24];

                for (int h = 0; h < 24; h++)
                {
                    hbw[h] = 0;
                    hbsch[h] = 0;
                    hbo[h] = 0;
                    nhb[h] = 0;
                }

                Mod.log.Info($"Calculating Trips with {_outputData.Keys.Count()} keys");

                foreach (var key in _outputData.Keys)
                {
                    Purpose[] purp = _outputData[key];
                    if(purp.Length < parts)
                    {
                        continue;
                    }
                    Purpose prev = Purpose.PathFailed;
                    int i = 0;
                    List<Purpose> tpurp = new List<Purpose>();
                    List<int> thour = new List<int>();
                    while (i < parts)
                    {
                        Purpose current = purp[i];

                        if (i < (parts - 1))
                        {
                            int j = i + 1;
                            Purpose next = purp[j];

                            while (current.Equals(next) && j < (parts - 1))
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
                                //if (!current.Equals(Purpose.None) && !current.Equals(prev))
                                if (!current.Equals(prev))
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
                            hour_diff += parts;
                        }
                        //Mod.log.Info($"{key},{h},{(h - (parts / 3)) /samples_per_hour},{current},{previous},{hour_diff}");
                        int hbw_trip = 0;
                        int hbo_trip = 0;
                        if (h > (parts/3 - 1) && (hour_diff) < 20*(samples_per_hour / 2))
                        {
                            if ((h == (parts/3) && previous.Equals(next) && (next_hour == (parts/3 + 1))) || previous.Equals(current))
                            {
                                continue;
                            }
                            // Going to Work
                            if (current.Equals(Purpose.GoingToWork))
                            {
                                if (h == (parts/3) && next_hour == (parts/3 + 1) && (next.Equals(Purpose.Sleeping) || (next.Equals(Purpose.GoingHome) && previous.Equals(Purpose.GoingHome))))
                                {
                                    continue;
                                }
                                if (previous.Equals(Purpose.Working))
                                {
                                    continue;
                                }
                                if (previous.Equals(Purpose.Sleeping) || previous.Equals(Purpose.None) || previous.Equals(Purpose.GoingHome))
                                {
                                    hbw[(h - (parts / 3)) / samples_per_hour]++;
                                    hw[(h - (parts / 3)) / samples_per_hour]++;
                                    hbw_trip++;
                                }
                                else
                                {
                                    nhb[(h - (parts / 3)) / samples_per_hour]++;
                                    ow[(h - (parts / 3)) / samples_per_hour]++;
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
                                        hbw[(h - (parts / 3)) / samples_per_hour]++;
                                        hw[(h - (parts / 3)) / samples_per_hour]++;
                                        hbw_trip++;
                                    }
                                    else
                                    {
                                        if (!previous.Equals(Purpose.GoingToWork))
                                        {
                                            nhb[(h - (parts / 3)) / samples_per_hour]++;
                                            ow[(h - (parts / 3)) / samples_per_hour]++;
                                        }
                                    }
                                }
                                else
                                {
                                    if (current.Equals(Purpose.GoingToSchool))
                                    {
                                        if (previous.Equals(Purpose.GoingHome) || previous.Equals(Purpose.Sleeping) || previous.Equals(Purpose.None))
                                        {
                                            hbsch[(h - (parts / 3)) / samples_per_hour]++;
                                            hsch[(h - (parts / 3)) / samples_per_hour]++;
                                        }
                                        else
                                        {
                                            nhb[(h - (parts / 3)) / samples_per_hour]++;
                                            osch[(h - (parts / 3)) / samples_per_hour]++;
                                        }
                                    }
                                    else
                                    {
                                        // For cases where status is "Studying", but there is no "GoingToSchool" status before between now and previous location
                                        if (current.Equals(Purpose.Studying))
                                        {
                                            if (previous.Equals(Purpose.Sleeping) || previous.Equals(Purpose.None))
                                            {
                                                hbsch[(h - (parts / 3)) / samples_per_hour]++;
                                                hsch[(h - (parts / 3)) / samples_per_hour]++;
                                            }
                                            else
                                            {
                                                if (!previous.Equals(Purpose.GoingToSchool))
                                                {
                                                    nhb[(h - (parts / 3)) / samples_per_hour]++;
                                                    osch[(h - (parts / 3)) / samples_per_hour]++;
                                                }

                                            }
                                        }
                                        else
                                        {
                                            //Going Home
                                            if (current.Equals(Purpose.GoingHome))
                                            {
                                                if (h == (parts / 3) && next_hour == (parts / 3 + 1) && (next.Equals(previous) && hour_diff < samples_per_hour))
                                                {
                                                    continue;
                                                }
                                                if (previous.Equals(Purpose.Working) || (previous.Equals(Purpose.GoingToWork)))
                                                {
                                                    hbw[(h - (parts / 3)) / samples_per_hour]++;
                                                    wh[(h - (parts / 3)) / samples_per_hour]++;
                                                    hbw_trip++;
                                                    //if ((next.Equals(Purpose.Sleeping) || next.Equals(Purpose.None) || next.Equals(Purpose.GoingToWork)))
                                                    ////if ((next.Equals(Purpose.Sleeping) || next.Equals(Purpose.None) || next.Equals(Purpose.GoingToWork)) || (Math.Abs(next_hour - h) > samples_per_hour / 2))
                                                    //{
                                                    //    hbw[(h - (parts / 3)) / samples_per_hour]++;
                                                    //    wh[(h - (parts / 3)) / samples_per_hour]++;
                                                    //}
                                                    //else
                                                    //{
                                                    //    nhb[(h - (parts / 3)) / samples_per_hour]++;
                                                    //    wo[(h - (parts / 3)) / samples_per_hour]++;
                                                    //}
                                                }
                                                else
                                                {
                                                    if (previous.Equals(Purpose.Studying) || previous.Equals(Purpose.GoingToSchool))
                                                    {
                                                        hbsch[(h - (parts / 3)) / samples_per_hour]++;
                                                        schh[(h - (parts / 3)) / samples_per_hour]++;
                                                        //if ((next.Equals(Purpose.Sleeping) || next.Equals(Purpose.None)) || (Math.Abs(next_hour - h) > samples_per_hour / 2))
                                                        //if ((next.Equals(Purpose.Sleeping) || next.Equals(Purpose.None)))
                                                        //{
                                                        //    hbsch[(h - (parts / 3)) / samples_per_hour]++;
                                                        //    schh[(h - (parts / 3)) / samples_per_hour]++;
                                                        //}
                                                        //else
                                                        //{
                                                        //    nhb[(h - (parts / 3)) / samples_per_hour]++;
                                                        //    scho[(h - (parts / 3)) / samples_per_hour]++;
                                                        //}
                                                    }
                                                    else
                                                    {
                                                        if (previous.Equals(Purpose.Sleeping))
                                                        {
                                                            continue;
                                                        }
                                                        if ((next.Equals(Purpose.Sleeping) || next.Equals(Purpose.None)) || (Math.Abs(next_hour - h) > samples_per_hour / 2))
                                                        {
                                                            hbo[(h - (parts / 3)) / samples_per_hour]++;
                                                            oh[(h - (parts / 3)) / samples_per_hour]++;
                                                            hbo_trip++;
                                                        }
                                                        else
                                                        {
                                                            if (!previous.Equals(Purpose.None) && !previous.Equals(Purpose.Sleeping) && !next.Equals(Purpose.GoingHome) && !next.Equals(Purpose.GoingToWork) && !next.Equals(Purpose.GoingToSchool))
                                                            {
                                                                if ((Math.Abs(next_hour - h) > samples_per_hour))
                                                                {
                                                                    hbo[(h - (parts / 3)) / samples_per_hour]++;
                                                                    oh[(h - (parts / 3)) / samples_per_hour]++;
                                                                    hbo_trip++;
                                                                }
                                                                else
                                                                {
                                                                    nhb[(h - (parts / 3)) / samples_per_hour]++;
                                                                    oo[(h - (parts / 3)) / samples_per_hour]++;
                                                                }
                                                            }
                                                        }
                                                    }
                                                }
                                            }
                                            else
                                            {
                                                if (current.Equals(Purpose.None) || next.Equals(Purpose.GoingHome) || next.Equals(Purpose.GoingToWork) || next.Equals(Purpose.GoingToSchool))
                                                {
                                                    continue;
                                                }
                                                if (previous.Equals(Purpose.Sleeping) || previous.Equals(Purpose.None) || previous.Equals(Purpose.GoingHome))
                                                {
                                                    if (!current.Equals(Purpose.Sleeping))
                                                    {
                                                        if (!(current.Equals(Purpose.GoingToSchool) || current.Equals(Purpose.GoingToWork) || current.Equals(Purpose.Studying) || current.Equals(Purpose.Working)))
                                                        {
                                                            hbo[(h - (parts / 3)) / samples_per_hour]++;
                                                            ho[(h - (parts / 3)) / samples_per_hour]++;
                                                            hbo_trip++;
                                                        }
                                                    }
                                                }
                                                else if (previous.Equals(Purpose.Working) || previous.Equals(Purpose.GoingToWork))
                                                {
                                                    if (!(current.Equals(Purpose.GoingToSchool) || current.Equals(Purpose.GoingToWork) || current.Equals(Purpose.Studying) || current.Equals(Purpose.Working)))
                                                    {
                                                        nhb[(h - (parts / 3)) / samples_per_hour]++;
                                                        wo[(h - (parts / 3)) / samples_per_hour]++;
                                                    }
                                                }
                                                else if (previous.Equals(Purpose.Studying) || previous.Equals(Purpose.GoingToSchool))
                                                {
                                                    if (!(current.Equals(Purpose.GoingToSchool) || current.Equals(Purpose.GoingToWork) || current.Equals(Purpose.Studying) || current.Equals(Purpose.Working)))
                                                    {
                                                        nhb[(h - (parts / 3)) / samples_per_hour]++;
                                                        scho[(h - (parts / 3)) / samples_per_hour]++;
                                                    }
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                        }
                        if((h - (parts/3))/samples_per_hour == 17)
                        {
                            total_hbo_17 += hbo_trip;
                        }
                        //Mod.log.Info($"{key},{h},{(h - (parts / 3)) / samples_per_hour},{current},{previous},hour_diff:{hour_diff},hbw:{hbw_trip},hbo:{hbo_trip}");
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
                        if (purp.Length < parts)
                        {
                            continue;
                        }
                        for (int h = 0; h < (parts/3); h++)
                        {
                            purp[h] = purp[h + (2/3)*parts];
                            purp[h + (parts/3)] = 0;
                            purp[h + (2 / 3) * parts] = 0;
                            _outputData[key] = purp;
                        }
                    }
                }

                float total_hbw = 0f;
                float total_hbo = 0f;
                float total_hbsch = 0f;
                float total_nhb = 0f;

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

                        total_hbw += hbw[h];
                        total_hbo += hbo[h];
                        total_hbsch += hbsch[h];
                        total_nhb += nhb[h];
                    }
                }

                Population population;
                if (this.EntityManager.TryGetComponent<Population>(m_CitySystem.City, out population))
                {
                    float total_pop = population.m_Population;
                    Mod.log.Info($"Trips per person: HBW: {total_hbw/ total_pop}, HBO: {total_hbo/ total_pop}, HBSCH: {total_hbsch/total_pop}, NHB: {total_nhb/total_pop}, TOTAL: {(total_hbw+total_hbo+total_hbsch+total_nhb)/ total_pop}");
                }

                //using (StreamWriter sw = File.AppendText(fileName2))
                //{
                //    for (int h = 0; h < 24; h++)
                //    {
                //        sw.WriteLine($"{h},{hw[h]},{wh[h]},{wo[h]},{ow[h]},{hsch[h]},{schh[h]},{scho[h]},{osch[h]},{ho[h]},{oh[h]},{oo[h]}");
                //    }
                //}
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
