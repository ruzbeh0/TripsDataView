using Colossal.UI.Binding;
using Game;
using Game.Areas;
using Game.Common;
using Game.Net;
using Game.Pathfind;
using Game.Prefabs;
using Game.SceneFlow;
using Game.Simulation;
using Game.UI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using TripsDataView.Extensions;
using Unity.Collections;
using Unity.Entities;

namespace TripsDataView.Systems
{
    public partial class ODMatrixUISystem : ExtendedUISystemBase
    {
        public override GameMode gameMode => GameMode.Game;

        private const string kGroup = "odMatrixInfo";
        private const string kOutside = "Outside";
        private const string kOther = "Other";

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

        private readonly Dictionary<Entity, string> m_Cache = new();
        private readonly List<(string o, string d, int c)> m_Entries = new();
        private readonly Dictionary<string, int> mNameToIdx = new();
        private readonly Dictionary<int, string> mIdxToName = new();

        private int m_LastHour = -1;

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

            var nowHour = World.GetExistingSystemManaged<TimeSystem>().GetCurrentDateTime().Hour;
            if (m_LastHour == nowHour) return; // min 1/h
            m_LastHour = nowHour;

            m_Cache.Clear(); mNameToIdx.Clear(); mIdxToName.Clear(); m_Entries.Clear();

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


                var o = ResolveBucket(elems[0].m_Target);
                var d = ResolveBucket(elems[elems.Length - 1].m_Target);

                names.Add(o); names.Add(d);
            }

            // build ordered list (Other last)
            foreach (var name in names.Where(n => n != kOther).OrderBy(n => n))
            {
                int i = mNameToIdx.Count;
                mNameToIdx[name] = i; mIdxToName[i] = name;
            }
            if (!mNameToIdx.ContainsKey(kOther))
            {
                int i = mNameToIdx.Count;
                mNameToIdx[kOther] = i; mIdxToName[i] = kOther;
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


                var o = ResolveBucket(elems[0].m_Target);
                var d = ResolveBucket(elems[elems.Length - 1].m_Target);

                var row = mat[o];
                row[d] = row.TryGetValue(d, out var c) ? c + 1 : 1;
            }

            // flatten for UI
            foreach (var o in mNameToIdx.Keys.OrderBy(k => k == kOther ? "\uFFFF" : k))
            {
                foreach (var d in mNameToIdx.Keys.OrderBy(k => k == kOther ? "\uFFFF" : k))
                {
                    var count = (mat.TryGetValue(o, out var row) && row.TryGetValue(d, out var v)) ? v : 0;
                    m_Entries.Add((o, d, count));
                }
            }

            paths.Dispose();

            m_uiDistricts.Update();
            m_uiResults.Update();
        }

        // Inside ODMatrixUISystem
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



        // Replace your ResolveBucket/IsOutside logic with this simple version:
        private string ResolveBucket(Entity e)
        {
            // If entity (or an owner up the chain) belongs to a district, use its name; else "Other".
            Entity cur = e;
            for (int hop = 0; hop < 6 && cur != Entity.Null; hop++)
            {
                if (m_CurrentDistrictLookup.TryGetComponent(cur, out var cd) && cd.m_District != Entity.Null)
                {
                    var name = GetDistrictNameString(m_NameSystem.GetName(cd.m_District));
                    return string.IsNullOrEmpty(name) ? "Other" : name;
                }
                if (!m_OwnerLookup.TryGetComponent(cur, out var ow) || ow.m_Owner == cur)
                    break;
                cur = ow.m_Owner;
            }
            return "Other";
        }

    }
}
