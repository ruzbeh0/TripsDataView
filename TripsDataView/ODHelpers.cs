using Colossal.Mathematics;
using Game.Net;
using Game.Objects;
using Game.Pathfind;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Unity.Entities;
using Unity.Mathematics;

namespace TripsDataView
{
    static class ODHelpers
    {
        public enum ElementSample { Start, End, Midpoint }

        public static bool TryGetElementWorldPos(
            in PathElement elem,
            ElementSample sample,
            in ComponentLookup<Curve> curveData,
            in ComponentLookup<SpawnLocation> spawnLocData,
            in ComponentLookup<Transform> transformData,
            out float3 worldPos)
        {
            // t from the element's [from,to] deltas
            float t = sample switch
            {
                ElementSample.Start => elem.m_TargetDelta.x,
                ElementSample.End => elem.m_TargetDelta.y,
                _ /*Midpoint*/         => math.lerp(elem.m_TargetDelta.x, elem.m_TargetDelta.y, 0.5f),
            };
            return TryGetElementWorldPos(elem.m_Target, t, curveData, spawnLocData, transformData, out worldPos);
        }

        public static bool TryGetElementWorldPos(
            Entity target,
            float t,
            in ComponentLookup<Curve> curveData,
            in ComponentLookup<SpawnLocation> spawnLocData,
            in ComponentLookup<Transform> transformData,
            out float3 worldPos)
        {
            // Case 1: lane with a Curve → sample bezier
            if (curveData.HasComponent(target))
            {
                var curve = curveData[target];
                worldPos = MathUtils.Position(curve.m_Bezier, math.saturate(t));
                return true;
            }

            // Case 2: spawn location → jump to connected lane & sample at its curve pos
            if (spawnLocData.HasComponent(target))
            {
                var sl = spawnLocData[target];
                var lane = sl.m_ConnectedLane1;
                var laneT = sl.m_CurvePosition1;
                if (curveData.HasComponent(lane))
                {
                    var curve = curveData[lane];
                    worldPos = MathUtils.Position(curve.m_Bezier, math.saturate(laneT));
                    return true;
                }
            }

            // Case 3: plain transform (buildings, props, etc.)
            if (transformData.HasComponent(target))
            {
                worldPos = transformData[target].m_Position;
                return true;
            }

            worldPos = default;
            return false;
        }
    }
}
