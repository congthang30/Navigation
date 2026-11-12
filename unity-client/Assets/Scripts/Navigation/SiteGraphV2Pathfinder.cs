using System;
using System.Collections.Generic;
using UnityEngine;
using Vps.UnityClient.Api;

namespace Vps.UnityClient.Navigation
{
    public sealed class SiteGraphV2Route
    {
        public Vector3[] AnchorLocalPoints { get; private set; }
        public string[] EdgeIds { get; private set; }
        public double WeightedCost { get; private set; }

        internal SiteGraphV2Route(Vector3[] points, string[] edgeIds, double weightedCost)
        {
            AnchorLocalPoints = points;
            EdgeIds = edgeIds;
            WeightedCost = weightedCost;
        }
    }

    /// <summary>Metric A* over published V2 edge geometry, beginning at a projected anchor-local position.</summary>
    public static class SiteGraphV2Pathfinder
    {
        private const float EndpointEpsilon = 0.0001f;
        private const int MaximumStartCandidates = 16;
        private const int MaximumRoutePoints = 20000;

        private sealed class Arc
        {
            public PublishedGraphEdgeV2 Edge;
            public bool Forward;
            public string From;
            public string To;
            public double Cost;
        }

        private sealed class Projection
        {
            public PublishedGraphEdgeV2 Edge;
            public int SegmentIndex;
            public float SegmentT;
            public float Distance;
            public Vector3 Point;
            public double DistanceFromStart;
            public double TotalLength;
        }

        private sealed class SearchPath
        {
            public string[] NodeIds;
            public Arc[] Arcs;
            public double Cost;
        }

        private sealed class QueueItem
        {
            public string NodeId;
            public double Cost;
            public double Priority;
        }

        public static bool TryFindRoute(
            PublishedSiteGraphManifest manifest,
            Vector3 anchorLocalStart,
            string destinationNodeId,
            float maximumSnapDistanceM,
            out SiteGraphV2Route route,
            out string error)
        {
            route = null;
            error = null;
            if (manifest == null || manifest.Graph == null || manifest.Graph.Nodes == null || manifest.Graph.Edges == null ||
                !IsFinite(anchorLocalStart) || !IsFinite(maximumSnapDistanceM) || maximumSnapDistanceM <= 0f ||
                string.IsNullOrWhiteSpace(destinationNodeId))
            {
                error = "A valid V2 graph, finite start point, destination, and snap distance are required.";
                return false;
            }

            var nodes = new Dictionary<string, PublishedGraphNodeV2>(StringComparer.Ordinal);
            foreach (PublishedGraphNodeV2 node in manifest.Graph.Nodes)
                if (node != null) nodes[node.Id] = node;
            if (!nodes.ContainsKey(destinationNodeId))
            {
                error = "The selected destination is not in the published graph.";
                return false;
            }

            var adjacency = new Dictionary<string, List<Arc>>(StringComparer.Ordinal);
            foreach (string id in nodes.Keys) adjacency.Add(id, new List<Arc>());
            float minimumMultiplier = float.MaxValue;
            foreach (PublishedGraphEdgeV2 edge in manifest.Graph.Edges)
            {
                if (edge == null || !edge.Enabled || !nodes.ContainsKey(edge.FromNodeId) || !nodes.ContainsKey(edge.ToNodeId)) continue;
                double measuredLength = PolylineLength(edge.Geometry);
                if (measuredLength <= 0d || !IsFinite((float)measuredLength) || edge.CostMultiplier <= 0f) continue;
                var forward = new Arc
                {
                    Edge = edge,
                    Forward = true,
                    From = edge.FromNodeId,
                    To = edge.ToNodeId,
                    Cost = measuredLength * edge.CostMultiplier
                };
                adjacency[forward.From].Add(forward);
                minimumMultiplier = Math.Min(minimumMultiplier, edge.CostMultiplier);
                if (edge.Bidirectional)
                {
                    var reverse = new Arc
                    {
                        Edge = edge,
                        Forward = false,
                        From = edge.ToNodeId,
                        To = edge.FromNodeId,
                        Cost = forward.Cost
                    };
                    adjacency[reverse.From].Add(reverse);
                }
            }
            if (minimumMultiplier == float.MaxValue)
            {
                error = "The published graph has no enabled traversable edges.";
                return false;
            }
            foreach (List<Arc> arcs in adjacency.Values)
                arcs.Sort((left, right) => string.CompareOrdinal(left.Edge.Id, right.Edge.Id));

            List<Projection> projections = ProjectStart(anchorLocalStart, manifest.Graph.Edges, maximumSnapDistanceM);
            if (projections.Count == 0)
            {
                error = "The current position is outside the enabled navigation graph.";
                return false;
            }
            projections.Sort((left, right) =>
            {
                int distance = left.Distance.CompareTo(right.Distance);
                if (distance != 0) return distance;
                return string.CompareOrdinal(left.Edge.Id, right.Edge.Id);
            });

            SiteGraphV2Route best = null;
            double bestCost = double.PositiveInfinity;
            float candidateLimit = Mathf.Min(maximumSnapDistanceM, projections[0].Distance + 0.2f);
            int evaluated = 0;
            for (int index = 0; index < projections.Count && evaluated < MaximumStartCandidates; index++)
            {
                Projection projection = projections[index];
                if (projection.Distance > candidateLimit) break;
                evaluated++;
                ConsiderDirection(projection, true, nodes, adjacency, minimumMultiplier, destinationNodeId, ref best, ref bestCost);
                if (projection.Edge.Bidirectional)
                    ConsiderDirection(projection, false, nodes, adjacency, minimumMultiplier, destinationNodeId, ref best, ref bestCost);
            }

            if (best == null)
            {
                error = "No permitted route from the current position reaches the selected destination.";
                return false;
            }
            route = best;
            return true;
        }

        private static void ConsiderDirection(
            Projection projection,
            bool towardToNode,
            Dictionary<string, PublishedGraphNodeV2> nodes,
            Dictionary<string, List<Arc>> adjacency,
            float minimumMultiplier,
            string destinationNodeId,
            ref SiteGraphV2Route best,
            ref double bestCost)
        {
            string endpointId = towardToNode ? projection.Edge.ToNodeId : projection.Edge.FromNodeId;
            if (!towardToNode && !projection.Edge.Bidirectional) return;
            double partialLength = towardToNode
                ? projection.TotalLength - projection.DistanceFromStart
                : projection.DistanceFromStart;
            if (partialLength < 0d) partialLength = 0d;
            double partialCost = partialLength * projection.Edge.CostMultiplier;
            SearchPath remainder = Search(endpointId, destinationNodeId, nodes, adjacency, minimumMultiplier);
            if (remainder == null) return;
            double totalCost = partialCost + remainder.Cost;
            if (totalCost > bestCost + 0.000001d) return;

            var points = new List<Vector3> { projection.Point };
            AppendPartialGeometry(points, projection, towardToNode);
            foreach (Arc arc in remainder.Arcs) AppendArcGeometry(points, arc);
            if (points.Count < 2 || points.Count > MaximumRoutePoints) return;
            var edgeIds = new List<string> { projection.Edge.Id };
            foreach (Arc arc in remainder.Arcs) edgeIds.Add(arc.Edge.Id);
            if (totalCost < bestCost - 0.000001d || best == null || CompareIds(edgeIds, best.EdgeIds) < 0)
            {
                bestCost = totalCost;
                best = new SiteGraphV2Route(points.ToArray(), edgeIds.ToArray(), totalCost);
            }
        }

        private static SearchPath Search(
            string start,
            string destination,
            Dictionary<string, PublishedGraphNodeV2> nodes,
            Dictionary<string, List<Arc>> adjacency,
            float minimumMultiplier)
        {
            if (start == destination) return new SearchPath { NodeIds = new[] { start }, Arcs = new Arc[0], Cost = 0d };
            var costs = new Dictionary<string, double>(StringComparer.Ordinal) { [start] = 0d };
            var previous = new Dictionary<string, Arc>(StringComparer.Ordinal);
            var open = new MinQueue();
            open.Push(new QueueItem { NodeId = start, Cost = 0d, Priority = Heuristic(nodes[start], nodes[destination], minimumMultiplier) });
            var closed = new HashSet<string>(StringComparer.Ordinal);
            while (open.Count > 0)
            {
                QueueItem current = open.Pop();
                double bestKnown;
                if (!costs.TryGetValue(current.NodeId, out bestKnown) || current.Cost > bestKnown + 0.000001d || !closed.Add(current.NodeId)) continue;
                if (current.NodeId == destination) break;
                foreach (Arc arc in adjacency[current.NodeId])
                {
                    double candidate = bestKnown + arc.Cost;
                    double oldCost;
                    if (costs.TryGetValue(arc.To, out oldCost) && candidate >= oldCost - 0.000001d) continue;
                    costs[arc.To] = candidate;
                    previous[arc.To] = arc;
                    open.Push(new QueueItem
                    {
                        NodeId = arc.To,
                        Cost = candidate,
                        Priority = candidate + Heuristic(nodes[arc.To], nodes[destination], minimumMultiplier)
                    });
                }
            }
            if (!costs.ContainsKey(destination)) return null;

            var reverseArcs = new List<Arc>();
            string cursor = destination;
            while (cursor != start)
            {
                Arc arc;
                if (!previous.TryGetValue(cursor, out arc)) return null;
                reverseArcs.Add(arc);
                cursor = arc.From;
            }
            reverseArcs.Reverse();
            var pathNodes = new List<string> { start };
            foreach (Arc arc in reverseArcs) pathNodes.Add(arc.To);
            return new SearchPath { NodeIds = pathNodes.ToArray(), Arcs = reverseArcs.ToArray(), Cost = costs[destination] };
        }

        private static List<Projection> ProjectStart(Vector3 point, PublishedGraphEdgeV2[] edges, float maxDistance)
        {
            var projections = new List<Projection>();
            foreach (PublishedGraphEdgeV2 edge in edges)
            {
                if (edge == null || !edge.Enabled || edge.Geometry == null || edge.Geometry.Length < 2) continue;
                Vector3[] geometry = new Vector3[edge.Geometry.Length];
                for (int index = 0; index < geometry.Length; index++) geometry[index] = edge.Geometry[index].ToVector3();
                double distanceFromStart = 0d;
                double totalLength = PolylineLength(edge.Geometry);
                if (totalLength <= 0d) continue;
                for (int index = 0; index < geometry.Length - 1; index++)
                {
                    Vector3 a = geometry[index];
                    Vector3 b = geometry[index + 1];
                    Vector3 delta = b - a;
                    float denominator = Vector3.Dot(delta, delta);
                    if (denominator <= 0.000001f) continue;
                    float t = Mathf.Clamp01(Vector3.Dot(point - a, delta) / denominator);
                    Vector3 projected = a + delta * t;
                    float distance = Vector3.Distance(point, projected);
                    if (distance <= maxDistance)
                    {
                        double along = distanceFromStart + Vector3.Distance(a, projected);
                        projections.Add(new Projection
                        {
                            Edge = edge,
                            SegmentIndex = index,
                            SegmentT = t,
                            Distance = distance,
                            Point = projected,
                            DistanceFromStart = along,
                            TotalLength = totalLength
                        });
                    }
                    distanceFromStart += Vector3.Distance(a, b);
                }
            }
            return projections;
        }

        private static void AppendPartialGeometry(List<Vector3> points, Projection projection, bool towardToNode)
        {
            AnchorLocalPointV2[] geometry = projection.Edge.Geometry;
            if (towardToNode)
            {
                AppendDistinct(points, geometry[projection.SegmentIndex + 1].ToVector3());
                for (int index = projection.SegmentIndex + 2; index < geometry.Length; index++)
                    AppendDistinct(points, geometry[index].ToVector3());
            }
            else
            {
                AppendDistinct(points, geometry[projection.SegmentIndex].ToVector3());
                for (int index = projection.SegmentIndex - 1; index >= 0; index--)
                    AppendDistinct(points, geometry[index].ToVector3());
            }
        }

        private static void AppendArcGeometry(List<Vector3> points, Arc arc)
        {
            AnchorLocalPointV2[] geometry = arc.Edge.Geometry;
            if (arc.Forward)
            {
                for (int index = 0; index < geometry.Length; index++) AppendDistinct(points, geometry[index].ToVector3());
            }
            else
            {
                for (int index = geometry.Length - 1; index >= 0; index--) AppendDistinct(points, geometry[index].ToVector3());
            }
        }

        private static void AppendDistinct(List<Vector3> points, Vector3 point)
        {
            if (points.Count == 0 || Vector3.Distance(points[points.Count - 1], point) > EndpointEpsilon) points.Add(point);
        }

        private static double Heuristic(PublishedGraphNodeV2 from, PublishedGraphNodeV2 to, float minimumMultiplier)
        {
            return Vector3.Distance(from.AnchorLocal.ToVector3(), to.AnchorLocal.ToVector3()) * minimumMultiplier;
        }

        private static double PolylineLength(AnchorLocalPointV2[] geometry)
        {
            if (geometry == null || geometry.Length < 2) return 0d;
            double result = 0d;
            for (int index = 1; index < geometry.Length; index++)
                result += Vector3.Distance(geometry[index - 1].ToVector3(), geometry[index].ToVector3());
            return result;
        }

        private static int CompareIds(IList<string> left, string[] right)
        {
            int count = Math.Min(left.Count, right.Length);
            for (int index = 0; index < count; index++)
            {
                int result = string.CompareOrdinal(left[index], right[index]);
                if (result != 0) return result;
            }
            return left.Count.CompareTo(right.Length);
        }

        private static bool IsFinite(Vector3 point) => IsFinite(point.x) && IsFinite(point.y) && IsFinite(point.z);
        private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

        private sealed class MinQueue
        {
            private readonly List<QueueItem> items = new List<QueueItem>();
            public int Count => items.Count;

            public void Push(QueueItem item)
            {
                items.Add(item);
                int index = items.Count - 1;
                while (index > 0)
                {
                    int parent = (index - 1) / 2;
                    if (Compare(items[parent], items[index]) <= 0) break;
                    Swap(parent, index);
                    index = parent;
                }
            }

            public QueueItem Pop()
            {
                QueueItem result = items[0];
                int last = items.Count - 1;
                items[0] = items[last];
                items.RemoveAt(last);
                int index = 0;
                while (index * 2 + 1 < items.Count)
                {
                    int child = index * 2 + 1;
                    if (child + 1 < items.Count && Compare(items[child + 1], items[child]) < 0) child++;
                    if (Compare(items[index], items[child]) <= 0) break;
                    Swap(index, child);
                    index = child;
                }
                return result;
            }

            private static int Compare(QueueItem left, QueueItem right)
            {
                int priority = left.Priority.CompareTo(right.Priority);
                if (priority != 0) return priority;
                return string.CompareOrdinal(left.NodeId, right.NodeId);
            }

            private void Swap(int a, int b)
            {
                QueueItem temporary = items[a];
                items[a] = items[b];
                items[b] = temporary;
            }
        }
    }
}
