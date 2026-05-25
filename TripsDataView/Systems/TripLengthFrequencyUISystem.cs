using System;
using System.Runtime.CompilerServices;
using Colossal.Entities;
using Colossal.UI.Binding;
using Game;
using Game.Agents;
using Game.Common;
using Game.Net;
using Game.Objects;
using Game.Pathfind;
using Game.Prefabs;
using Game.Routes;
using Game.Simulation;
using Game.UI;
using TripsDataView.Extensions;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace TripsDataView.Systems;

/// <summary>
/// Calculates a Trip Length Frequency Distribution (TLFD) from active path owners.
/// Distances follow the same basic idea used by Realistic JobSearch metrics:
/// resolve an origin and destination world position, compute straight-line XZ distance,
/// and place the trip into 1 km bins.
/// </summary>
[CompilerGenerated]
public partial class TripLengthFrequencyUISystem : ExtendedUISystemBase
{
    private const string kGroup = "tripLengthFrequencyInfo";
    private const int kNumBins = 31;        // 0-1 km ... 29-30 km, 30+ km
    private const float kMetersPerBin = 1000f;

    private int m_LastHour = -1;

    private EntityQuery m_PathTripsQuery;
    private RawValueBinding m_uiResults;
    private NativeArray<TripLengthBinInfo> m_Results;

    public override GameMode gameMode => GameMode.Game;

    private struct TripLengthBinInfo
    {
        public int DistanceBin; // lower edge in km; 30 means 30+ km
        public int Total;

        public TripLengthBinInfo(int distanceBin)
        {
            DistanceBin = distanceBin;
            Total = 0;
        }
    }

    private static void WriteTripLengthData(IJsonWriter writer, TripLengthBinInfo info)
    {
        writer.TypeBegin("TripLengthBinInfo");
        writer.PropertyName("distanceBin");
        writer.Write(info.DistanceBin);
        writer.PropertyName("total");
        writer.Write(info.Total);
        writer.TypeEnd();
    }

    protected override void OnCreate()
    {
        base.OnCreate();

        m_PathTripsQuery = GetEntityQuery(
            ComponentType.ReadOnly<PathOwner>(),
            ComponentType.ReadOnly<PathElement>());

        RequireForUpdate(m_PathTripsQuery);

        AddBinding(m_uiResults = new RawValueBinding(kGroup, "tripLengthFrequencyDetails", writer =>
        {
            writer.ArrayBegin(m_Results.Length);
            for (int i = 0; i < m_Results.Length; i++)
            {
                WriteTripLengthData(writer, m_Results[i]);
            }
            writer.ArrayEnd();
        }));

        m_Results = new NativeArray<TripLengthBinInfo>(kNumBins, Allocator.Persistent);
        ResetResults();

        Mod.log.Info("TripLengthFrequencyUISystem created.");
    }

    protected override void OnDestroy()
    {
        if (m_Results.IsCreated)
            m_Results.Dispose();

        base.OnDestroy();
    }

    public override int GetUpdateInterval(SystemUpdatePhase phase)
    {
        return TimeSystem.kTicksPerDay / 32;
    }

    protected override void OnUpdate()
    {
        base.OnUpdate();

        DateTime currentDateTime = World.GetExistingSystemManaged<TimeSystem>().GetCurrentDateTime();
        int hour = currentDateTime.Hour;

        // Same cadence used by several TripsView panels: rebuild once per in-game hour.
        if (m_LastHour == hour)
            return;

        m_LastHour = hour;
        ResetResults();

        NativeArray<Entity> paths = m_PathTripsQuery.ToEntityArray(Allocator.Temp);

        for (int i = 0; i < paths.Length; i++)
        {
            Entity path = paths[i];

            if (!EntityManager.TryGetBuffer<PathElement>(path, true, out DynamicBuffer<PathElement> pathElements))
                continue;

            if (pathElements.Length == 0)
                continue;

            if (!TryGetEndpointPositionFromPathBuffer(pathElements, true, out float3 origin))
                continue;

            if (!TryGetEndpointPositionFromPathBuffer(pathElements, false, out float3 destination))
                continue;

            float meters = math.distance(new float2(origin.x, origin.z), new float2(destination.x, destination.z));
            int binIndex = (int)math.floor(meters / kMetersPerBin);
            binIndex = math.clamp(binIndex, 0, kNumBins - 1);

            AddToBin(binIndex);
        }

        paths.Dispose();

        m_uiResults.Update();
    }

    private void ResetResults()
    {
        for (int i = 0; i < m_Results.Length; i++)
        {
            m_Results[i] = new TripLengthBinInfo(i);
        }
    }

    private void AddToBin(int binIndex)
    {
        TripLengthBinInfo row = m_Results[binIndex];
        row.Total++;

        m_Results[binIndex] = row;
    }

    private bool TryGetEndpointPositionFromPathBuffer(DynamicBuffer<PathElement> pathElements, bool origin, out float3 pos)
    {
        int steps = math.min(16, pathElements.Length);

        if (origin)
        {
            for (int i = 0; i < steps; i++)
            {
                if (TryGetPathElementWorldPos(pathElements[i], true, out pos))
                    return true;
            }
        }
        else
        {
            for (int k = 0; k < steps; k++)
            {
                int i = pathElements.Length - 1 - k;
                if (TryGetPathElementWorldPos(pathElements[i], false, out pos))
                    return true;
            }
        }

        pos = default;
        return false;
    }

    private bool TryGetPathElementWorldPos(PathElement element, bool startOfElement, out float3 pos)
    {
        Entity target = element.m_Target;

        // Curves are usually the most precise path elements. Use the requested end of the curve.
        if (EntityManager.TryGetComponent<Game.Net.Curve>(target, out var curve))
        {
            pos = startOfElement ? curve.m_Bezier.a : curve.m_Bezier.d;
            return true;
        }

        if (EntityManager.TryGetComponent<Position>(target, out var agentPosition))
        {
            pos = agentPosition.m_Position;
            return true;
        }

        if (EntityManager.TryGetComponent<Transform>(target, out var transform))
        {
            pos = transform.m_Position;
            return true;
        }

        return TryGetWorldPosDeep(target, out pos);
    }

    private bool TryGetWorldPosDeep(Entity entity, out float3 pos)
    {
        Entity current = entity;

        for (int depth = 0; depth < 8 && current != Entity.Null; depth++)
        {
            if (EntityManager.TryGetComponent<Game.Net.Curve>(current, out var curve))
            {
                // Owner-chain fallback: midpoint is safer when we no longer know which end is origin/destination.
                pos = (curve.m_Bezier.a + curve.m_Bezier.d) * 0.5f;
                return true;
            }

            if (EntityManager.TryGetComponent<Position>(current, out var agentPosition))
            {
                pos = agentPosition.m_Position;
                return true;
            }

            if (EntityManager.TryGetComponent<Transform>(current, out var transform))
            {
                pos = transform.m_Position;
                return true;
            }

            if (!EntityManager.TryGetComponent<Owner>(current, out Owner owner))
                break;

            if (owner.m_Owner == current)
                break;

            current = owner.m_Owner;
        }

        pos = default;
        return false;
    }
}
