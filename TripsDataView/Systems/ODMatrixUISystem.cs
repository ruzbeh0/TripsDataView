using Colossal.Entities;
using Colossal.UI.Binding;
using Game;
using Game.Areas;
using Game.Common;
using Game.Net;
using Game.Objects;
using Game.Pathfind;
using Game.Prefabs;
using Game.Routes;
using Game.SceneFlow;
using Game.Simulation;
using Game.UI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Runtime.CompilerServices;
using TripsDataView.Extensions;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using static System.Net.Mime.MediaTypeNames;

namespace TripsDataView.Systems
{
    public partial class ODMatrixUISystem : ExtendedUISystemBase
    {
        public override GameMode gameMode => GameMode.Game;

        private const string kGroup = "odMatrixInfo";
        private const string kOutside = "Outside";
        private const string kOther = "Other";
        private const int kMapSize = (int)(14336); //We are assuming outside connections origin/destination have coordinates that are in the border of the map. So we will reduce the map size to flag them


        private RawValueBinding m_uiResults;
        private RawValueBinding m_uiDistricts;

        private EntityQuery m_PathTripsQuery;
        private EntityQuery m_DistrictQuery;

        private NameSystem m_NameSystem;
        private ComponentLookup<CurrentDistrict> m_CurrentDistrictLookup;
        private ComponentLookup<Game.Objects.OutsideConnection> m_OutsideConnLookup;
        private ComponentLookup<Owner> m_OwnerLookup;
        private ComponentLookup<PrefabRef> m_PrefabRefLookup;
        private ComponentLookup<OutsideConnectionData> m_OutsideConnDataLookup;
        private ComponentLookup<Game.Net.Curve> m_CurveLookup;
        private ComponentLookup<Game.Objects.SpawnLocation> m_SpawnLocLookup;
        private ComponentLookup<Transform> m_TransformLookup;


        private readonly Dictionary<Entity, string> m_Cache = new();
        private readonly List<(string o, string d, int c)> m_Entries = new();
        private readonly Dictionary<string, int> mNameToIdx = new();
        private readonly Dictionary<int, string> mIdxToName = new();

        private int m_LastHour = -1;

        // --- Geometric district cache ---
        private struct DistrictPoly
        {
            public string Name;
            public float2[] Points;  // XZ polygon
            public float2 Min;       // AABB min
            public float2 Max;       // AABB max
        }
        private readonly List<DistrictPoly> m_DistrictPolys = new();


        protected override void OnCreate()
        {
            base.OnCreate();

            m_PathTripsQuery = GetEntityQuery(ComponentType.ReadOnly<PathOwner>(), ComponentType.ReadOnly<PathElement>());
            m_DistrictQuery = GetEntityQuery(ComponentType.ReadOnly<District>());

            m_NameSystem = World.GetOrCreateSystemManaged<NameSystem>();
            m_CurrentDistrictLookup = GetComponentLookup<CurrentDistrict>(true);
            m_OwnerLookup = GetComponentLookup<Owner>(true);
            m_OutsideConnLookup = GetComponentLookup<Game.Objects.OutsideConnection>(true);
            m_PrefabRefLookup = GetComponentLookup<PrefabRef>(true);
            m_OutsideConnDataLookup = GetComponentLookup<OutsideConnectionData>(true);
            m_CurveLookup = GetComponentLookup<Game.Net.Curve>(true);
            m_SpawnLocLookup = GetComponentLookup<Game.Objects.SpawnLocation>(true);
            m_TransformLookup = GetComponentLookup<Transform>(true);


            RequireForUpdate(m_PathTripsQuery);

            AddBinding(m_uiResults = new RawValueBinding(kGroup, "odMatrixDetails", w => {
                w.ArrayBegin(m_Entries.Count);
                foreach (var e in m_Entries)
                {
                    w.TypeBegin("ODMatrixEntry");
                    w.PropertyName("originDistrict"); w.Write(e.o);
                    w.PropertyName("destinationDistrict"); w.Write(e.d);
                    w.PropertyName("count"); w.Write(e.c);
                    w.TypeEnd();
                }
                w.ArrayEnd();
            }));

            AddBinding(m_uiDistricts = new RawValueBinding(kGroup, "districtList", w => {
                var list = mIdxToName.Values.OrderBy(x => x == kOther ? "\uFFFF" : x).ToArray();
                w.ArrayBegin(list.Length);
                for (int i = 0; i < list.Length; i++) w.Write(list[i]);
                w.ArrayEnd();
            }));
        }

        public override int GetUpdateInterval(SystemUpdatePhase phase) => TimeSystem.kTicksPerDay / 32;

        protected override void OnUpdate()
        {
            base.OnUpdate();

            // refresh lookups
            m_CurrentDistrictLookup.Update(ref CheckedStateRef);
            m_OwnerLookup.Update(ref CheckedStateRef);
            m_OutsideConnLookup.Update(ref CheckedStateRef);
            m_PrefabRefLookup.Update(ref CheckedStateRef);
            m_OutsideConnDataLookup.Update(ref CheckedStateRef);
            m_CurveLookup.Update(ref CheckedStateRef);
            m_SpawnLocLookup.Update(ref CheckedStateRef);
            m_TransformLookup.Update(ref CheckedStateRef);

            var nowHour = World.GetExistingSystemManaged<TimeSystem>().GetCurrentDateTime().Hour;
            if (m_LastHour == nowHour) return; // min 1/h
            m_LastHour = nowHour;

            m_Cache.Clear(); mNameToIdx.Clear(); mIdxToName.Clear(); m_Entries.Clear();

            BuildDistrictPolygonCache();

            var paths = m_PathTripsQuery.ToEntityArray(Allocator.Temp);

            // collect names
            HashSet<string> names = new();

            foreach (var e in paths)
            {
                if (!EntityManager.HasBuffer<PathElement>(e))
                    continue;
                var elems = EntityManager.GetBuffer<PathElement>(e, true);
                if (elems.Length == 0)
                    continue;

                Unity.Mathematics.float3 oPos, dPos;
                bool okO = TryGetEndpointPositionFromPathBuffer(elems, true, out oPos);
                bool okD = TryGetEndpointPositionFromPathBuffer(elems, false, out dPos);

                var o = okO ? ResolveBucket(oPos, "origin") : kOther;
                var d = okD ? ResolveBucket(dPos, "dest") : kOther;

                names.Add(o); names.Add(d);

            }

            // Build ordered list: normal names (A→Z), then "Other", then "Outside"
            foreach (var name in names
                .Where(n => n != kOther && n != kOutside)
                .OrderBy(n => n, StringComparer.Ordinal))
            {
                int i = mNameToIdx.Count;
                mNameToIdx[name] = i;
                mIdxToName[i] = name;
            }
            if (!mNameToIdx.ContainsKey(kOther))
            {
                int i = mNameToIdx.Count;
                mNameToIdx[kOther] = i;
                mIdxToName[i] = kOther;
            }
            if (!mNameToIdx.ContainsKey(kOutside))
            {
                int i = mNameToIdx.Count;
                mNameToIdx[kOutside] = i;
                mIdxToName[i] = kOutside;
            }


            // aggregate
            var mat = new Dictionary<string, Dictionary<string, int>>();
            foreach (var n in mNameToIdx.Keys) mat[n] = new Dictionary<string, int>();

            foreach (var e in paths)
            {
                if (!EntityManager.HasBuffer<PathElement>(e))
                    continue;
                var elems = EntityManager.GetBuffer<PathElement>(e, true);
                if (elems.Length == 0)
                    continue;


                Unity.Mathematics.float3 oPos, dPos;
                bool okO = TryGetEndpointPositionFromPathBuffer(elems, true, out oPos);
                bool okD = TryGetEndpointPositionFromPathBuffer(elems, false, out dPos);

                var o = okO ? ResolveBucket(oPos, "origin") : kOther;
                var d = okD ? ResolveBucket(dPos, "dest") : kOther;

                var row = mat[o];
                row[d] = row.TryGetValue(d, out var c) ? c + 1 : 1;
            }

            // flatten for UI
            IEnumerable<string> OrderedKeys(IEnumerable<string> keys) => keys.OrderBy(k =>
            {
                if (k == kOther) return "~~~~~1"; // later than normal names
                if (k == kOutside) return "~~~~~2"; // last
                return k;
            }, StringComparer.Ordinal);

            foreach (var o in OrderedKeys(mNameToIdx.Keys))
            {
                foreach (var d in OrderedKeys(mNameToIdx.Keys))
                {
                    var count = (mat.TryGetValue(o, out var row) && row.TryGetValue(d, out var v)) ? v : 0;
                    m_Entries.Add((o, d, count));
                }
            }


            paths.Dispose();

            m_uiDistricts.Update();
            m_uiResults.Update();
        }

        // Skip controller/line tool entities; sample lane/stop/building instead.
        bool IsLikelyLineController(Entity e)
        {
            try
            {
                var n = m_NameSystem.GetName(e);
                var s = GetDistrictNameString(n) ?? "";
                s = s.ToLowerInvariant();
                return s.Contains(" line ") || s.Contains(" line tool");
            }
            catch { return false; }
        }

        // Scan from one end of the path buffer to find the first element that yields a concrete world pos.
        // start=true  → look from the beginning (origin); start=false → from the end (destination).
        bool TryGetEndpointPositionFromPathBuffer(DynamicBuffer<PathElement> elems, bool start, out Unity.Mathematics.float3 pos)
        {
            if (elems.Length == 0) { pos = default; return false; }

            int steps = math.min(16, elems.Length); // don’t scan huge paths
            if (start)
            {
                for (int i = 0; i < steps; i++)
                {
                    var pe = elems[i];
                    if (IsLikelyLineController(pe.m_Target)) continue;
                    if (ODHelpers.TryGetElementWorldPos(pe, ODHelpers.ElementSample.Start, m_CurveLookup, m_SpawnLocLookup, m_TransformLookup, out pos))
                        return true;
                }
            }
            else
            {
                for (int k = 0; k < steps; k++)
                {
                    int i = elems.Length - 1 - k;
                    var pe = elems[i];
                    if (IsLikelyLineController(pe.m_Target)) continue;
                    if (ODHelpers.TryGetElementWorldPos(pe, ODHelpers.ElementSample.End, m_CurveLookup, m_SpawnLocLookup, m_TransformLookup, out pos))
                        return true;
                }
            }

            // Last-ditch: allow transform from very first/last if it’s not a controller
            if (start && !IsLikelyLineController(elems[0].m_Target) &&
                ODHelpers.TryGetElementWorldPos(elems[0], ODHelpers.ElementSample.Start, m_CurveLookup, m_SpawnLocLookup, m_TransformLookup, out pos))
                return true;

            if (!start && !IsLikelyLineController(elems[elems.Length - 1].m_Target) &&
                ODHelpers.TryGetElementWorldPos(elems[elems.Length - 1], ODHelpers.ElementSample.End, m_CurveLookup, m_SpawnLocLookup, m_TransformLookup, out pos))
                return true;

            pos = default;
            return false;
        }


        void BuildDistrictPolygonCache()
        {
            m_DistrictPolys.Clear();

            using var districts = m_DistrictQuery.ToEntityArray(Unity.Collections.Allocator.Temp);
            foreach (var dist in districts)
            {
                var d = dist;
                // Name (use your existing resolver so localization stays consistent)
                var name = GetDistrictNameString(m_NameSystem.GetName(d));
                if (string.IsNullOrWhiteSpace(name))
                    name = "District";

                // We expect district area nodes as a buffer on the district entity.
                // Typical type is Game.Areas.Node with a world-space position (float3).
                if (!EntityManager.HasBuffer<Game.Areas.Node>(d))
                {
                    // Try walking to the owned area entity for geometry
                    if (EntityManager.TryGetComponent<Owner>(d, out var ow)
                        && ow.m_Owner != Entity.Null
                        && EntityManager.HasBuffer<Game.Areas.Node>(ow.m_Owner))
                    {
                        d = ow.m_Owner; // switch to area entity that has the nodes
                    }
                }
                if (!EntityManager.HasBuffer<Game.Areas.Node>(d))
                    continue; // still no geometry → skip

                var nodes = EntityManager.GetBuffer<Game.Areas.Node>(d);
                if (nodes.Length < 3)
                    continue;

                var pts = new float2[nodes.Length];
                SortLoopInPlace(pts);
                float2 mn = new float2(float.PositiveInfinity);
                float2 mx = new float2(float.NegativeInfinity);
                for (int i = 0; i < nodes.Length; i++)
                {
                    // Use XZ for 2D tests
                    var wp = nodes[i].m_Position; // adapt if your node type uses a different field name
                    var p = new float2(wp.x, wp.z);
                    pts[i] = p;
                    mn = math.min(mn, p);
                    mx = math.max(mx, p);
                }

                m_DistrictPolys.Add(new DistrictPoly { Name = name, Points = pts, Min = mn, Max = mx });
            }
        }

        static void SortLoopInPlace(float2[] pts)
        {
            float2 c = default;
            for (int i = 0; i < pts.Length; i++) c += pts[i];
            c /= pts.Length;
            System.Array.Sort(pts, (a, b) => {
                float aa = math.atan2(a.y - c.y, a.x - c.x);
                float bb = math.atan2(b.y - c.y, b.x - c.x);
                return aa.CompareTo(bb);
            });
        }

        // Fast AABB test
        static bool AabbContains(in float2 p, in float2 min, in float2 max)
            => p.x >= min.x && p.x <= max.x && p.y >= min.y && p.y <= max.y;

        // Ray-cast point-in-polygon in XZ plane
        static bool PointInPolygon(float2 p, float2[] poly)
        {
            bool inside = false;
            for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
            {
                float2 a = poly[j], b = poly[i];
                bool cross = ((a.y > p.y) != (b.y > p.y)) &&
                             (p.x < (b.x - a.x) * (p.y - a.y) / (b.y - a.y + 1e-12f) + a.x);
                if (cross) inside = !inside;
            }
            return inside;
        }


        static bool IsInsideMap(float3 pos)
        {
            // World square centered at (0,0), XZ plane
            float half = kMapSize * 0.5f;                     // 7168
            bool result = pos.x >= -half && pos.x <= half
                && pos.z >= -half && pos.z <= half;
            return result;
        }

        bool TryGetWorldPosDeep(Entity e, out float3 pos)
        {
            // Walk Owner → ... until we find a Position
            Entity cur = e;
            for (int hop = 0; hop < 8 && cur != Entity.Null; hop++)
            {
                if (EntityManager.TryGetComponent<Position>(cur, out var p))
                { pos = p.m_Position; return true; }

                if (!m_OwnerLookup.TryGetComponent(cur, out var ow) || ow.m_Owner == cur)
                    break;
                cur = ow.m_Owner;
            }
            pos = default;
            return false;
        }
        private string GetDistrictNameString(NameSystem.Name name)
        {
            // 1) Some builds return the user-visible label here (for renamed districts).
            // Only accept if it's not the type name.
            try
            {
                var s = name.ToString();
                if (!string.IsNullOrWhiteSpace(s) && !s.Contains("NameSystem+Name"))
                    return s;
            }
            catch { /* ignore */ }

            // 2) Pull the localization key (e.g., "Assets.DISTRICT_NAME:151").
            string key = null;
            try
            {
                var f = typeof(NameSystem.Name).GetField("m_NameID", BindingFlags.NonPublic | BindingFlags.Instance);
                if (f != null) key = f.GetValue(name) as string;
            }
            catch { /* ignore */ }

            if (!string.IsNullOrEmpty(key))
            {
                // 2a) Translate via the active localization dictionary.
                // This mirrors UILocalizationManager.Translate(), which uses activeDictionary.TryGetValue(key, out text) :contentReference[oaicite:2]{index=2}
                try
                {
                    var mgr = GameManager.instance?.localizationManager; // Colossal.Localization.LocalizationManager
                    if (mgr?.activeDictionary != null && mgr.activeDictionary.TryGetValue(key, out var text) && !string.IsNullOrWhiteSpace(text))
                        return text;
                }
                catch { /* fall through */ }

                // 2b) Friendly fallback from key: "District 151" instead of just "151".
                int colon = key.LastIndexOf(':');
                if (colon >= 0 && colon < key.Length - 1)
                {
                    var idPart = key.Substring(colon + 1);
                    if (int.TryParse(idPart, out var id))
                        return $"District {id}";
                }

                // If no number, use the last token after '.' with basic title casing.
                int dot = key.LastIndexOf('.');
                if (dot >= 0 && dot < key.Length - 1)
                {
                    var token = key.Substring(dot + 1).Replace('_', ' ');
                    if (token.Length > 0)
                        return char.ToUpper(token[0]) + (token.Length > 1 ? token.Substring(1).ToLowerInvariant() : "");
                }

                return key; // absolute last resort: show the key
            }

            return "Other";
        }

        private string ResolveBucket(Unity.Mathematics.float3 pos, string debugTag)
        {
            if (!IsInsideMap(pos)) return kOutside;

            var p = new Unity.Mathematics.float2(pos.x, pos.z);
            for (int i = 0; i < m_DistrictPolys.Count; i++)
            {
                var poly = m_DistrictPolys[i];
                if (p.x < poly.Min.x || p.x > poly.Max.x || p.y < poly.Min.y || p.y > poly.Max.y) continue;
                if (PointInPolygon(p, poly.Points)) return poly.Name;
            }

            // Detailed miss log (you already added this style earlier)
            //Mod.log.Info($"[OD] No district ({debugTag}) pos=({pos.x:F2},{pos.y:F2},{pos.z:F2}); PIP miss after lane/pos scan.");
            return kOther;
        }


        // Try to get a user-facing name for any entity (entity name → prefab name).
        private string TryGetAnyDisplayName(Entity e)
        {
            try
            {
                var nm = m_NameSystem.GetName(e);
                var s = GetDistrictNameString(nm);
                if (!string.IsNullOrWhiteSpace(s)) return s;
            }
            catch { /* ignore */ }

            if (EntityManager.TryGetComponent<PrefabRef>(e, out var pr))
            {
                try
                {
                    var pn = m_NameSystem.GetName(pr.m_Prefab);
                    var sp = GetDistrictNameString(pn);
                    if (!string.IsNullOrWhiteSpace(sp)) return sp;
                }
                catch { /* ignore */ }
            }
            return null;
        }

        // Dump a compact owner chain and the current-district association.
        private void LogNoDistrictDebug(Entity endpoint, in Unity.Mathematics.float3 pos, string tag)
        {
            var sb = new System.Text.StringBuilder(256);
            sb.Append($"[OD] No district ({tag}) pos=({pos.x:F2},{pos.y:F2},{pos.z:F2}); ");

            // CurrentDistrict on the endpoint (if present)
            if (m_CurrentDistrictLookup.TryGetComponent(endpoint, out var cd) && cd.m_District != Entity.Null)
            {
                var dn = GetDistrictNameString(m_NameSystem.GetName(cd.m_District));
                sb.Append($"CurrentDistrict={dn}; ");
            }
            else
            {
                sb.Append("CurrentDistrict=<none>; ");
            }

            // Owner chain names (entity / prefab)
            sb.Append("chain: ");
            var cur = endpoint;
            for (int hop = 0; hop < 8 && cur != Entity.Null; hop++)
            {
                var n = TryGetAnyDisplayName(cur) ?? "<unnamed>";
                sb.Append(hop == 0 ? "" : " -> ");
                sb.Append($"[{hop}:{n}]");

                if (!m_OwnerLookup.TryGetComponent(cur, out var ow) || ow.m_Owner == cur)
                    break;
                cur = ow.m_Owner;
            }

            Mod.log.Info(sb.ToString());
        }

    }
}
