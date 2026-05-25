using System;
using System.Collections.Generic;
using System.Linq;
using Colossal.Entities;
using Colossal.Mathematics;
using Colossal.UI.Binding;
using Game;
using Game.Areas;
using Game.Common;
using Game.Net;
using Game.Objects;
using Game.Pathfind;
using Game.Rendering;
using Game.SceneFlow;
using Game.Simulation;
using Game.UI;
using TripsDataView.Extensions;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Color = UnityEngine.Color;

namespace TripsDataView.Systems;

[UpdateBefore(typeof(OverlayRenderSystem))]
public partial class ODDesireLinesOverlaySystem : ExtendedUISystemBase
{
    private const string kGroup = "odDesireLinesInfo";
    private const string kOutside = "Outside";
    private const string kOther = "Other";
    private const int kMapSize = 14336;
    private const float kBoundaryWidth = 2.5f;
    private const float kMinFlowWidth = 6f;
    private const float kMaxFlowWidth = 34f;

    private readonly List<DistrictShape> m_Districts = new();
    private readonly Dictionary<string, DistrictShape> m_DistrictsByName = new();
    private readonly Dictionary<FlowKey, int> m_FlowCounts = new();
    private readonly List<FlowInfo> m_Flows = new();

    private EntityQuery m_PathTripsQuery;
    private EntityQuery m_DistrictQuery;
    private NameSystem m_NameSystem;
    private OverlayRenderSystem m_OverlayRenderSystem;
    private ComponentLookup<Game.Net.Curve> m_CurveLookup;
    private ComponentLookup<SpawnLocation> m_SpawnLocLookup;
    private ComponentLookup<Game.Objects.Transform> m_TransformLookup;

    private RawValueBinding m_uiSummary;
    private bool m_OverlayEnabled;
    private bool m_DataDirty = true;
    private int m_LastHour = -1;
    private int m_MaxFlow;
    private int m_TotalInterDistrictTrips;

    public override GameMode gameMode => GameMode.Game;

    private struct DistrictShape
    {
        public string Name;
        public float2[] Points;
        public float2 Min;
        public float2 Max;
        public float3 Center;
    }

    private readonly struct FlowKey : IEquatable<FlowKey>
    {
        public readonly string A;
        public readonly string B;

        public FlowKey(string origin, string destination)
        {
            if (string.Compare(origin, destination, StringComparison.Ordinal) <= 0)
            {
                A = origin;
                B = destination;
            }
            else
            {
                A = destination;
                B = origin;
            }
        }

        public bool Equals(FlowKey other) => A == other.A && B == other.B;
        public override bool Equals(object obj) => obj is FlowKey other && Equals(other);
        public override int GetHashCode() => (A?.GetHashCode() ?? 0) * 397 ^ (B?.GetHashCode() ?? 0);
    }

    private struct FlowInfo
    {
        public DistrictShape Origin;
        public DistrictShape Destination;
        public int Count;
    }

    protected override void OnCreate()
    {
        base.OnCreate();

        m_PathTripsQuery = GetEntityQuery(ComponentType.ReadOnly<PathOwner>(), ComponentType.ReadOnly<PathElement>());
        m_DistrictQuery = GetEntityQuery(ComponentType.ReadOnly<District>());
        m_NameSystem = World.GetOrCreateSystemManaged<NameSystem>();
        m_OverlayRenderSystem = World.GetOrCreateSystemManaged<OverlayRenderSystem>();
        m_CurveLookup = GetComponentLookup<Game.Net.Curve>(true);
        m_SpawnLocLookup = GetComponentLookup<SpawnLocation>(true);
        m_TransformLookup = GetComponentLookup<Game.Objects.Transform>(true);

        RequireForUpdate(m_PathTripsQuery);

        AddBinding(m_uiSummary = new RawValueBinding(kGroup, "summary", WriteSummary));
        AddBinding(new TriggerBinding<bool>(kGroup, "setOverlayEnabled", SetOverlayEnabled, new GenericUIReader<bool>()));

        Mod.log.Info("ODDesireLinesOverlaySystem created.");
    }

    protected override void OnUpdate()
    {
        base.OnUpdate();

        DateTime now = World.GetExistingSystemManaged<TimeSystem>().GetCurrentDateTime();
        if (m_LastHour != now.Hour)
        {
            m_LastHour = now.Hour;
            m_DataDirty = true;
        }

        if (m_DataDirty)
        {
            RebuildOverlayData();
            m_DataDirty = false;
            m_uiSummary.Update();
        }

        if (!m_OverlayEnabled || m_Districts.Count == 0)
            return;

        DrawOverlay();
    }

    private void SetOverlayEnabled(bool enabled)
    {
        m_OverlayEnabled = enabled;
        if (enabled)
            m_DataDirty = true;

        m_uiSummary.Update();
    }

    private void WriteSummary(IJsonWriter writer)
    {
        writer.TypeBegin("ODDesireLinesSummary");
        writer.PropertyName("enabled");
        writer.Write(m_OverlayEnabled);
        writer.PropertyName("districts");
        writer.Write(m_Districts.Count);
        writer.PropertyName("flows");
        writer.Write(m_Flows.Count);
        writer.PropertyName("trips");
        writer.Write(m_TotalInterDistrictTrips);
        writer.PropertyName("maxFlow");
        writer.Write(m_MaxFlow);
        writer.TypeEnd();
    }

    private void RebuildOverlayData()
    {
        m_CurveLookup.Update(ref CheckedStateRef);
        m_SpawnLocLookup.Update(ref CheckedStateRef);
        m_TransformLookup.Update(ref CheckedStateRef);

        BuildDistrictShapes();
        BuildDesireLineFlows();
    }

    private void BuildDistrictShapes()
    {
        m_Districts.Clear();
        m_DistrictsByName.Clear();

        using NativeArray<Entity> districts = m_DistrictQuery.ToEntityArray(Allocator.Temp);
        foreach (Entity district in districts)
        {
            Entity geometryEntity = district;
            if (!EntityManager.HasBuffer<Game.Areas.Node>(geometryEntity) &&
                EntityManager.TryGetComponent<Owner>(geometryEntity, out Owner owner) &&
                owner.m_Owner != Entity.Null &&
                EntityManager.HasBuffer<Game.Areas.Node>(owner.m_Owner))
            {
                geometryEntity = owner.m_Owner;
            }

            if (!EntityManager.HasBuffer<Game.Areas.Node>(geometryEntity))
                continue;

            DynamicBuffer<Game.Areas.Node> nodes = EntityManager.GetBuffer<Game.Areas.Node>(geometryEntity, true);
            if (nodes.Length < 3)
                continue;

            float2[] points = new float2[nodes.Length];
            float2 min = new(float.PositiveInfinity);
            float2 max = new(float.NegativeInfinity);
            float3 center = default;

            for (int i = 0; i < nodes.Length; i++)
            {
                float3 position = nodes[i].m_Position;
                float2 point = new(position.x, position.z);
                points[i] = point;
                min = math.min(min, point);
                max = math.max(max, point);
                center += position;
            }

            center /= nodes.Length;
            string name = GetDistrictNameString(m_NameSystem.GetName(district));
            if (string.IsNullOrWhiteSpace(name))
                name = "District";

            DistrictShape shape = new()
            {
                Name = name,
                Points = points,
                Min = min,
                Max = max,
                Center = center
            };

            m_Districts.Add(shape);
            if (!m_DistrictsByName.ContainsKey(name))
                m_DistrictsByName.Add(name, shape);
        }
    }

    private void BuildDesireLineFlows()
    {
        m_FlowCounts.Clear();
        m_Flows.Clear();
        m_MaxFlow = 0;
        m_TotalInterDistrictTrips = 0;

        using NativeArray<Entity> paths = m_PathTripsQuery.ToEntityArray(Allocator.Temp);
        foreach (Entity path in paths)
        {
            if (!EntityManager.HasBuffer<PathElement>(path))
                continue;

            DynamicBuffer<PathElement> elements = EntityManager.GetBuffer<PathElement>(path, true);
            if (elements.Length == 0)
                continue;

            if (!TryGetEndpointPositionFromPathBuffer(elements, true, out float3 originPos) ||
                !TryGetEndpointPositionFromPathBuffer(elements, false, out float3 destinationPos))
            {
                continue;
            }

            string origin = ResolveDistrict(originPos);
            string destination = ResolveDistrict(destinationPos);
            if (origin == kOther || origin == kOutside || destination == kOther || destination == kOutside || origin == destination)
                continue;

            FlowKey key = new(origin, destination);
            m_FlowCounts[key] = m_FlowCounts.TryGetValue(key, out int count) ? count + 1 : 1;
            m_TotalInterDistrictTrips++;
        }

        foreach (KeyValuePair<FlowKey, int> pair in m_FlowCounts)
        {
            if (!m_DistrictsByName.TryGetValue(pair.Key.A, out DistrictShape origin) ||
                !m_DistrictsByName.TryGetValue(pair.Key.B, out DistrictShape destination))
            {
                continue;
            }

            m_MaxFlow = math.max(m_MaxFlow, pair.Value);
            m_Flows.Add(new FlowInfo
            {
                Origin = origin,
                Destination = destination,
                Count = pair.Value
            });
        }

        m_Flows.Sort((a, b) => a.Count.CompareTo(b.Count));
    }

    private void DrawOverlay()
    {
        JobHandle dependencies = default;
        OverlayRenderSystem.Buffer buffer = m_OverlayRenderSystem.GetBuffer(out dependencies);
        dependencies.Complete();

        OverlayRenderSystem.StyleFlags projected = OverlayRenderSystem.StyleFlags.Projected | OverlayRenderSystem.StyleFlags.DepthFadeBelow;
        Color boundaryFill = new(1f, 1f, 1f, 0.55f);
        Color boundaryOutline = new(0f, 0f, 0f, 0.35f);
        Color centroidFill = new(1f, 1f, 1f, 0.45f);
        Color centroidOutline = new(0f, 0f, 0f, 0.5f);

        for (int i = 0; i < m_Districts.Count; i++)
        {
            DistrictShape district = m_Districts[i];
            for (int p = 0; p < district.Points.Length; p++)
            {
                float2 a = district.Points[p];
                float2 b = district.Points[(p + 1) % district.Points.Length];
                buffer.DrawLine(
                    boundaryOutline,
                    boundaryFill,
                    1.25f,
                    projected,
                    new Line3.Segment(new float3(a.x, 0f, a.y), new float3(b.x, 0f, b.y)),
                    kBoundaryWidth,
                    new float2(1f));
            }

            buffer.DrawCircle(
                centroidOutline,
                centroidFill,
                1.2f,
                projected,
                new float2(0f, 1f),
                new float3(district.Center.x, 0f, district.Center.z),
                32f);
        }

        if (m_MaxFlow <= 0)
            return;

        Color flowOutline = new(0.02f, 0.04f, 0.05f, 0.75f);
        for (int i = 0; i < m_Flows.Count; i++)
        {
            FlowInfo flow = m_Flows[i];
            float ratio = math.sqrt(flow.Count / (float)m_MaxFlow);
            float width = math.lerp(kMinFlowWidth, kMaxFlowWidth, ratio);
            Color flowFill = new(0.05f, 0.75f, 1f, math.lerp(0.35f, 0.85f, ratio));

            float3 a = new(flow.Origin.Center.x, 0f, flow.Origin.Center.z);
            float3 b = new(flow.Destination.Center.x, 0f, flow.Destination.Center.z);
            buffer.DrawLine(
                flowOutline,
                flowFill,
                2.4f,
                projected,
                new Line3.Segment(a, b),
                width,
                new float2(1f));
        }
    }

    private bool TryGetEndpointPositionFromPathBuffer(DynamicBuffer<PathElement> elements, bool start, out float3 position)
    {
        int steps = math.min(16, elements.Length);
        if (start)
        {
            for (int i = 0; i < steps; i++)
            {
                if (ODHelpers.TryGetElementWorldPos(elements[i], ODHelpers.ElementSample.Start, m_CurveLookup, m_SpawnLocLookup, m_TransformLookup, out position))
                    return true;
            }
        }
        else
        {
            for (int k = 0; k < steps; k++)
            {
                int i = elements.Length - 1 - k;
                if (ODHelpers.TryGetElementWorldPos(elements[i], ODHelpers.ElementSample.End, m_CurveLookup, m_SpawnLocLookup, m_TransformLookup, out position))
                    return true;
            }
        }

        position = default;
        return false;
    }

    private string ResolveDistrict(float3 position)
    {
        if (!IsInsideMap(position))
            return kOutside;

        float2 point = new(position.x, position.z);
        for (int i = 0; i < m_Districts.Count; i++)
        {
            DistrictShape district = m_Districts[i];
            if (point.x < district.Min.x || point.x > district.Max.x || point.y < district.Min.y || point.y > district.Max.y)
                continue;

            if (PointInPolygon(point, district.Points))
                return district.Name;
        }

        return kOther;
    }

    private static bool IsInsideMap(float3 position)
    {
        float half = kMapSize * 0.5f;
        return position.x >= -half && position.x <= half && position.z >= -half && position.z <= half;
    }

    private static bool PointInPolygon(float2 point, float2[] polygon)
    {
        bool inside = false;
        for (int i = 0, j = polygon.Length - 1; i < polygon.Length; j = i++)
        {
            float2 a = polygon[j];
            float2 b = polygon[i];
            bool crosses = ((a.y > point.y) != (b.y > point.y)) &&
                           (point.x < (b.x - a.x) * (point.y - a.y) / (b.y - a.y + 1e-12f) + a.x);
            if (crosses)
                inside = !inside;
        }

        return inside;
    }

    private string GetDistrictNameString(NameSystem.Name name)
    {
        try
        {
            string value = name.ToString();
            if (!string.IsNullOrWhiteSpace(value) && !value.Contains("NameSystem+Name"))
                return value;
        }
        catch
        {
        }

        string key = null;
        try
        {
            System.Reflection.FieldInfo field = typeof(NameSystem.Name).GetField("m_NameID", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (field != null)
                key = field.GetValue(name) as string;
        }
        catch
        {
        }

        if (string.IsNullOrEmpty(key))
            return "District";

        try
        {
            Colossal.Localization.LocalizationManager manager = GameManager.instance?.localizationManager;
            if (manager?.activeDictionary != null && manager.activeDictionary.TryGetValue(key, out string text) && !string.IsNullOrWhiteSpace(text))
                return text;
        }
        catch
        {
        }

        int colon = key.LastIndexOf(':');
        if (colon >= 0 && colon < key.Length - 1 && int.TryParse(key.Substring(colon + 1), out int id))
            return $"District {id}";

        int dot = key.LastIndexOf('.');
        if (dot >= 0 && dot < key.Length - 1)
        {
            string token = key.Substring(dot + 1).Replace('_', ' ');
            if (token.Length > 0)
                return char.ToUpper(token[0]) + (token.Length > 1 ? token.Substring(1).ToLowerInvariant() : "");
        }

        return key;
    }
}
