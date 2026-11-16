using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using Vps.UnityClient.Api;

namespace Vps.UnityClient.Navigation
{
    public struct GraphSnapTarget
    {
        public bool IsValid;
        public float Distance;
        public Vector3 SnapPoint;
        public string NodeId;
        public string EdgeId;
        public int EdgeSegmentIndex;
        public float EdgeSegmentT;
    }

    /// <summary>
    /// Offline Site Graph V2 storage and authoring utilities.
    /// Allows adding, editing, and deleting destinations and walked routes locally without a backend,
    /// while preserving strict SiteGraphV2ContractParser and SiteGraphV2Pathfinder invariants.
    /// </summary>
    public static class LocalSiteGraphStore
    {
        public const string DefaultFileName = "LocalNavigationManifest.json";
        private const float NodeSnapThresholdMeters = 0.35f;
        private const float SameNodeReuseThresholdMeters = 0.20f;
        private const float DefaultWalkSampleSpacingMeters = 0.35f;
        private const float SimplifyToleranceMeters = 0.06f;

        public static readonly string[] SupportedCategories =
        {
            "room", "service", "entrance", "elevator", "stairs", "restroom", "other"
        };

        public static string DefaultManifestPath =>
            Path.Combine(Application.persistentDataPath, DefaultFileName);

        public static bool HasLocalOverride(string filePath = null)
        {
            string target = ResolvePath(filePath);
            return !string.IsNullOrEmpty(target) && File.Exists(target);
        }

        public static void DeleteLocalOverride(string filePath = null)
        {
            string target = ResolvePath(filePath);
            if (!string.IsNullOrEmpty(target) && File.Exists(target))
            {
                File.Delete(target);
            }
        }

        public static bool TryLoadManifest(
            string expectedFloorId,
            string expectedVersionId,
            out PublishedSiteGraphManifest manifest,
            out string error,
            string filePath = null)
        {
            manifest = null;
            string target = ResolvePath(filePath);
            if (string.IsNullOrEmpty(target) || !File.Exists(target))
            {
                error = "Local navigation manifest file does not exist.";
                return false;
            }

            string json;
            try
            {
                json = File.ReadAllText(target, Encoding.UTF8);
            }
            catch (Exception ex)
            {
                error = "Could not read local navigation manifest: " + ex.Message;
                return false;
            }

            return SiteGraphV2ContractParser.TryParseManifest(
                json, expectedFloorId, expectedVersionId, out manifest, out error, out _);
        }

        public static bool SaveManifest(
            PublishedSiteGraphManifest manifest,
            out string error,
            string filePath = null)
        {
            if (manifest == null)
            {
                error = "Manifest cannot be null.";
                return false;
            }

            string json = SerializeManifestJson(manifest, true);
            if (!SiteGraphV2ContractParser.TryParseManifest(
                    json, manifest.FloorId, manifest.VersionId, out _, out error, out _))
            {
                return false;
            }

            string target = ResolvePath(filePath);
            try
            {
                string directory = Path.GetDirectoryName(target);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                string tempPath = target + ".tmp";
                File.WriteAllText(tempPath, json, Encoding.UTF8);
                if (File.Exists(target))
                {
                    File.Delete(target);
                }
                File.Move(tempPath, target);
                error = null;
                return true;
            }
            catch (Exception ex)
            {
                error = "Failed to save local navigation manifest: " + ex.Message;
                return false;
            }
        }

        public static string SerializeManifestJson(PublishedSiteGraphManifest manifest, bool indented = true)
        {
            var nodesArray = new JArray();
            if (manifest.Graph != null && manifest.Graph.Nodes != null)
            {
                foreach (PublishedGraphNodeV2 node in manifest.Graph.Nodes)
                {
                    if (node == null) continue;
                    nodesArray.Add(new JObject
                    {
                        ["id"] = node.Id,
                        ["kind"] = node.Kind,
                        ["anchorLocal"] = PointToJson(node.AnchorLocal),
                        ["provenance"] = node.Provenance
                    });
                }
            }

            var edgesArray = new JArray();
            if (manifest.Graph != null && manifest.Graph.Edges != null)
            {
                foreach (PublishedGraphEdgeV2 edge in manifest.Graph.Edges)
                {
                    if (edge == null) continue;
                    var geometryArray = new JArray();
                    if (edge.Geometry != null)
                    {
                        foreach (AnchorLocalPointV2 pt in edge.Geometry)
                        {
                            geometryArray.Add(PointToJson(pt));
                        }
                    }

                    edgesArray.Add(new JObject
                    {
                        ["id"] = edge.Id,
                        ["fromNodeId"] = edge.FromNodeId,
                        ["toNodeId"] = edge.ToNodeId,
                        ["bidirectional"] = edge.Bidirectional,
                        ["enabled"] = edge.Enabled,
                        ["costMultiplier"] = edge.CostMultiplier,
                        ["lengthM"] = edge.LengthM,
                        ["geometry"] = geometryArray,
                        ["provenance"] = edge.Provenance
                    });
                }
            }

            var poisArray = new JArray();
            if (manifest.Graph != null && manifest.Graph.Pois != null)
            {
                foreach (PublishedGraphPoiV2 poi in manifest.Graph.Pois)
                {
                    if (poi == null) continue;
                    var poiObj = new JObject
                    {
                        ["id"] = poi.Id,
                        ["label"] = poi.Label,
                        ["category"] = poi.Category,
                        ["isActive"] = poi.IsActive,
                        ["nodeId"] = poi.NodeId,
                        ["anchorLocal"] = PointToJson(poi.AnchorLocal),
                        ["provenance"] = poi.Provenance
                    };
                    if (!string.IsNullOrWhiteSpace(poi.SourceMarkerId))
                    {
                        poiObj["sourceMarkerId"] = poi.SourceMarkerId;
                    }
                    if (!string.IsNullOrWhiteSpace(poi.Description))
                    {
                        poiObj["description"] = poi.Description.Trim();
                    }
                    poisArray.Add(poiObj);
                }
            }

            var root = new JObject
            {
                ["schemaVersion"] = manifest.SchemaVersion,
                ["versionId"] = manifest.VersionId,
                ["version"] = manifest.Version,
                ["floorId"] = manifest.FloorId,
                ["buildingId"] = manifest.BuildingId,
                ["siteBinding"] = new JObject
                {
                    ["siteId"] = manifest.SiteBinding != null ? manifest.SiteBinding.SiteId : string.Empty,
                    ["productionAssetId"] = manifest.SiteBinding != null ? manifest.SiteBinding.ProductionAssetId : string.Empty,
                    ["payloadSha256"] = manifest.SiteBinding != null ? manifest.SiteBinding.PayloadSha256 : string.Empty
                },
                ["sourceDraftRevision"] = manifest.SourceDraftRevision.HasValue
                    ? (JToken)manifest.SourceDraftRevision.Value
                    : JValue.CreateNull(),
                ["siteMeshSha256"] = manifest.SiteMeshSha256 != null
                    ? (JToken)manifest.SiteMeshSha256
                    : JValue.CreateNull(),
                ["graph"] = new JObject
                {
                    ["nodes"] = nodesArray,
                    ["edges"] = edgesArray,
                    ["pois"] = poisArray
                },
                ["publishedAt"] = string.IsNullOrWhiteSpace(manifest.PublishedAt)
                    ? DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture)
                    : manifest.PublishedAt
            };

            return root.ToString(indented ? Formatting.Indented : Formatting.None);
        }

        public static float EstimateFloorHeight(PublishedSiteGraphManifest manifest, Vector3 anchorLocalReference)
        {
            if (manifest == null || manifest.Graph == null || manifest.Graph.Nodes == null || manifest.Graph.Nodes.Length == 0)
            {
                return -1.0f;
            }

            float bestDistanceSq = float.MaxValue;
            float bestY = manifest.Graph.Nodes[0].AnchorLocal.Y;
            for (int i = 0; i < manifest.Graph.Nodes.Length; i++)
            {
                PublishedGraphNodeV2 node = manifest.Graph.Nodes[i];
                if (node == null || node.AnchorLocal == null) continue;
                float dx = node.AnchorLocal.X - anchorLocalReference.x;
                float dz = node.AnchorLocal.Z - anchorLocalReference.z;
                float distSq = dx * dx + dz * dz;
                if (distSq < bestDistanceSq)
                {
                    bestDistanceSq = distSq;
                    bestY = node.AnchorLocal.Y;
                }
            }
            return bestY;
        }

        public static GraphSnapTarget FindNearestGraphConnection(PublishedSiteGraphManifest manifest, Vector3 point)
        {
            var result = new GraphSnapTarget { Distance = float.MaxValue };
            if (manifest == null || manifest.Graph == null) return result;

            PublishedGraphNodeV2[] nodes = manifest.Graph.Nodes ?? Array.Empty<PublishedGraphNodeV2>();
            PublishedGraphEdgeV2[] edges = manifest.Graph.Edges ?? Array.Empty<PublishedGraphEdgeV2>();

            PublishedGraphNodeV2 nearestNode = null;
            float nearestNodeDist = float.MaxValue;
            for (int i = 0; i < nodes.Length; i++)
            {
                PublishedGraphNodeV2 node = nodes[i];
                if (node == null || node.AnchorLocal == null) continue;
                float d = Vector3.Distance(point, node.AnchorLocal.ToVector3());
                if (d < nearestNodeDist)
                {
                    nearestNodeDist = d;
                    nearestNode = node;
                }
            }

            PublishedGraphEdgeV2 nearestEdge = null;
            int nearestSegIndex = 0;
            float nearestSegT = 0f;
            Vector3 nearestEdgePoint = Vector3.zero;
            float nearestEdgeDist = float.MaxValue;

            for (int i = 0; i < edges.Length; i++)
            {
                PublishedGraphEdgeV2 edge = edges[i];
                if (edge == null || !edge.Enabled || edge.Geometry == null || edge.Geometry.Length < 2) continue;

                for (int s = 0; s < edge.Geometry.Length - 1; s++)
                {
                    Vector3 a = edge.Geometry[s].ToVector3();
                    Vector3 b = edge.Geometry[s + 1].ToVector3();
                    Vector3 delta = b - a;
                    float lenSq = delta.sqrMagnitude;
                    float t = lenSq > 1e-6f ? Mathf.Clamp01(Vector3.Dot(point - a, delta) / lenSq) : 0f;
                    Vector3 projected = a + delta * t;
                    float dist = Vector3.Distance(point, projected);
                    if (dist < nearestEdgeDist)
                    {
                        nearestEdgeDist = dist;
                        nearestEdge = edge;
                        nearestSegIndex = s;
                        nearestSegT = t;
                        nearestEdgePoint = projected;
                    }
                }
            }

            if (nearestNode == null && nearestEdge == null) return result;

            if (nearestEdge == null || nearestNodeDist <= nearestEdgeDist + 0.05f || nearestNodeDist <= NodeSnapThresholdMeters)
            {
                result.IsValid = true;
                result.Distance = nearestNodeDist;
                result.SnapPoint = nearestNode.AnchorLocal.ToVector3();
                result.NodeId = nearestNode.Id;
                return result;
            }

            // Check if projection is very close to one of the edge's endpoint nodes.
            PublishedGraphNodeV2 fromNode = FindNode(nodes, nearestEdge.FromNodeId);
            PublishedGraphNodeV2 toNode = FindNode(nodes, nearestEdge.ToNodeId);
            if (fromNode != null && Vector3.Distance(nearestEdgePoint, fromNode.AnchorLocal.ToVector3()) <= NodeSnapThresholdMeters)
            {
                result.IsValid = true;
                result.Distance = Vector3.Distance(point, fromNode.AnchorLocal.ToVector3());
                result.SnapPoint = fromNode.AnchorLocal.ToVector3();
                result.NodeId = fromNode.Id;
                return result;
            }
            if (toNode != null && Vector3.Distance(nearestEdgePoint, toNode.AnchorLocal.ToVector3()) <= NodeSnapThresholdMeters)
            {
                result.IsValid = true;
                result.Distance = Vector3.Distance(point, toNode.AnchorLocal.ToVector3());
                result.SnapPoint = toNode.AnchorLocal.ToVector3();
                result.NodeId = toNode.Id;
                return result;
            }

            result.IsValid = true;
            result.Distance = nearestEdgeDist;
            result.SnapPoint = nearestEdgePoint;
            result.EdgeId = nearestEdge.Id;
            result.EdgeSegmentIndex = nearestSegIndex;
            result.EdgeSegmentT = nearestSegT;
            return result;
        }

        public static Vector3[] BuildWalkPolyline(
            IReadOnlyList<Vector3> walkedPoints,
            Vector3 startAnchor,
            Vector3 endAnchor,
            float minSpacingMeters = DefaultWalkSampleSpacingMeters)
        {
            var raw = new List<Vector3> { startAnchor };
            if (walkedPoints != null)
            {
                for (int i = 0; i < walkedPoints.Count; i++)
                {
                    Vector3 pt = walkedPoints[i];
                    if (!IsFinite(pt)) continue;
                    if (Vector3.Distance(raw[raw.Count - 1], pt) >= minSpacingMeters &&
                        Vector3.Distance(pt, endAnchor) >= minSpacingMeters)
                    {
                        raw.Add(pt);
                    }
                }
            }

            if (Vector3.Distance(raw[raw.Count - 1], endAnchor) > 0.001f)
            {
                raw.Add(endAnchor);
            }

            if (raw.Count <= 2) return raw.ToArray();

            var simplified = new List<Vector3>();
            SimplifyPolyline(raw, 0, raw.Count - 1, SimplifyToleranceMeters, simplified);
            simplified.Add(raw[raw.Count - 1]);
            return simplified.ToArray();
        }

        public static bool AddPoiWithRoute(
            PublishedSiteGraphManifest source,
            string label,
            string category,
            Vector3 destinationFloorPoint,
            IReadOnlyList<Vector3> walkedPoints,
            out PublishedSiteGraphManifest updated,
            out PublishedGraphPoiV2 addedPoi,
            out string error)
        {
            return AddPoiWithRoute(
                source, label, category, null, destinationFloorPoint, walkedPoints,
                out updated, out addedPoi, out error);
        }

        public static bool AddPoiWithRoute(
            PublishedSiteGraphManifest source,
            string label,
            string category,
            string description,
            Vector3 destinationFloorPoint,
            IReadOnlyList<Vector3> walkedPoints,
            out PublishedSiteGraphManifest updated,
            out PublishedGraphPoiV2 addedPoi,
            out string error)
        {
            updated = null;
            addedPoi = null;

            if (source == null || source.Graph == null)
            {
                error = "Bản đồ hiện tại chưa sẵn sàng.";
                return false;
            }

            string trimmedLabel = label == null ? string.Empty : label.Trim();
            if (string.IsNullOrWhiteSpace(trimmedLabel) || trimmedLabel.Length > 100)
            {
                error = "Vui lòng nhập tên điểm đến (1–100 ký tự).";
                return false;
            }

            if (!TryNormalizeDescription(description, out string normalizedDescription, out error))
            {
                return false;
            }

            string normalizedCategory = NormalizeCategory(category);
            if (!IsFinite(destinationFloorPoint))
            {
                error = "Tọa độ điểm đến không hợp lệ.";
                return false;
            }

            PublishedSiteGraphManifest clone = CloneManifest(source);
            var nodes = new List<PublishedGraphNodeV2>(clone.Graph.Nodes ?? Array.Empty<PublishedGraphNodeV2>());
            var edges = new List<PublishedGraphEdgeV2>(clone.Graph.Edges ?? Array.Empty<PublishedGraphEdgeV2>());
            var pois = new List<PublishedGraphPoiV2>(clone.Graph.Pois ?? Array.Empty<PublishedGraphPoiV2>());

            Vector3 connectionProbe = walkedPoints != null && walkedPoints.Count > 0
                ? walkedPoints[0]
                : destinationFloorPoint;
            GraphSnapTarget snap = FindNearestGraphConnection(clone, connectionProbe);
            if (!snap.IsValid)
            {
                error = "Không tìm thấy điểm nối hợp lệ trên đồ thị hiện tại.";
                return false;
            }

            string startNodeId;
            Vector3 startNodePos;
            if (!string.IsNullOrEmpty(snap.NodeId))
            {
                startNodeId = snap.NodeId;
                startNodePos = snap.SnapPoint;
            }
            else
            {
                if (!SplitEdgeAtProjection(nodes, edges, snap, out PublishedGraphNodeV2 junctionNode, out error))
                {
                    return false;
                }
                startNodeId = junctionNode.Id;
                startNodePos = junctionNode.AnchorLocal.ToVector3();
            }

            string destinationNodeId;
            if (Vector3.Distance(startNodePos, destinationFloorPoint) < SameNodeReuseThresholdMeters)
            {
                // Standing directly on an existing node: attach the new POI directly without creating a zero-length self edge.
                destinationNodeId = startNodeId;
                destinationFloorPoint = startNodePos;
            }
            else
            {
                var destNode = new PublishedGraphNodeV2
                {
                    Id = NewStableId("n"),
                    Kind = "waypoint",
                    AnchorLocal = ToPoint(destinationFloorPoint),
                    Provenance = "admin"
                };
                nodes.Add(destNode);
                destinationNodeId = destNode.Id;

                Vector3[] polyline = BuildWalkPolyline(walkedPoints, startNodePos, destinationFloorPoint);
                AnchorLocalPointV2[] geometry = ToPoints(polyline);
                float lengthM = (float)ComputePolylineLength(geometry);
                if (lengthM <= 0.01f)
                {
                    error = "Khoảng cách đường nối quá ngắn.";
                    return false;
                }

                edges.Add(new PublishedGraphEdgeV2
                {
                    Id = NewStableId("e"),
                    FromNodeId = startNodeId,
                    ToNodeId = destinationNodeId,
                    Bidirectional = true,
                    Enabled = true,
                    CostMultiplier = 1.0f,
                    LengthM = lengthM,
                    Geometry = geometry,
                    Provenance = "admin"
                });
            }

            addedPoi = new PublishedGraphPoiV2
            {
                Id = NewStableId("p"),
                Label = trimmedLabel,
                Category = normalizedCategory,
                IsActive = true,
                NodeId = destinationNodeId,
                AnchorLocal = ToPoint(destinationFloorPoint),
                Provenance = "admin",
                SourceMarkerId = null,
                Description = normalizedDescription
            };
            pois.Add(addedPoi);

            clone.Graph.Nodes = nodes.ToArray();
            clone.Graph.Edges = edges.ToArray();
            clone.Graph.Pois = pois.ToArray();
            clone.PublishedAt = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);

            if (!ValidateManifestAndRouting(clone, startNodePos, addedPoi.NodeId, out error))
            {
                addedPoi = null;
                return false;
            }

            updated = clone;
            return true;
        }

        public static bool UpdatePoi(
            PublishedSiteGraphManifest source,
            string poiId,
            string newLabel,
            string newCategory,
            out PublishedSiteGraphManifest updated,
            out string error)
        {
            return UpdatePoiInternal(source, poiId, newLabel, newCategory, null, false, out updated, out error);
        }

        public static bool UpdatePoi(
            PublishedSiteGraphManifest source,
            string poiId,
            string newLabel,
            string newCategory,
            string newDescription,
            out PublishedSiteGraphManifest updated,
            out string error)
        {
            return UpdatePoiInternal(source, poiId, newLabel, newCategory, newDescription, true, out updated, out error);
        }

        private static bool UpdatePoiInternal(
            PublishedSiteGraphManifest source,
            string poiId,
            string newLabel,
            string newCategory,
            string newDescription,
            bool updateDescription,
            out PublishedSiteGraphManifest updated,
            out string error)
        {
            updated = null;
            if (source == null || source.Graph == null || source.Graph.Pois == null)
            {
                error = "Bản đồ hiện tại chưa sẵn sàng.";
                return false;
            }

            string trimmedLabel = newLabel == null ? string.Empty : newLabel.Trim();
            if (string.IsNullOrWhiteSpace(trimmedLabel) || trimmedLabel.Length > 100)
            {
                error = "Vui lòng nhập tên điểm đến (1–100 ký tự).";
                return false;
            }

            string normalizedDescription = null;
            if (updateDescription && !TryNormalizeDescription(newDescription, out normalizedDescription, out error))
            {
                return false;
            }

            PublishedSiteGraphManifest clone = CloneManifest(source);
            bool found = false;
            for (int i = 0; i < clone.Graph.Pois.Length; i++)
            {
                PublishedGraphPoiV2 poi = clone.Graph.Pois[i];
                if (poi != null && string.Equals(poi.Id, poiId, StringComparison.Ordinal))
                {
                    poi.Label = trimmedLabel;
                    poi.Category = NormalizeCategory(newCategory);
                    if (updateDescription) poi.Description = normalizedDescription;
                    found = true;
                    break;
                }
            }

            if (!found)
            {
                error = "Không tìm thấy điểm đến cần sửa.";
                return false;
            }

            clone.PublishedAt = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);
            if (!ValidateManifest(clone, out error))
            {
                return false;
            }

            updated = clone;
            return true;
        }

        private static bool TryNormalizeDescription(string description, out string normalized, out string error)
        {
            normalized = null;
            if (!string.IsNullOrWhiteSpace(description))
            {
                string trimmed = description.Trim();
                if (trimmed.Length > 500)
                {
                    error = "Mô tả điểm đến tối đa 500 ký tự.";
                    return false;
                }
                normalized = trimmed;
            }
            error = null;
            return true;
        }

        public static bool DeletePoi(
            PublishedSiteGraphManifest source,
            string poiId,
            out PublishedSiteGraphManifest updated,
            out string error)
        {
            updated = null;
            if (source == null || source.Graph == null || source.Graph.Pois == null)
            {
                error = "Bản đồ hiện tại chưa sẵn sàng.";
                return false;
            }

            PublishedSiteGraphManifest clone = CloneManifest(source);
            var nodes = new List<PublishedGraphNodeV2>(clone.Graph.Nodes ?? Array.Empty<PublishedGraphNodeV2>());
            var edges = new List<PublishedGraphEdgeV2>(clone.Graph.Edges ?? Array.Empty<PublishedGraphEdgeV2>());
            var pois = new List<PublishedGraphPoiV2>(clone.Graph.Pois ?? Array.Empty<PublishedGraphPoiV2>());

            int removeIndex = -1;
            int activeCount = 0;
            for (int i = 0; i < pois.Count; i++)
            {
                PublishedGraphPoiV2 p = pois[i];
                if (p != null && p.IsActive) activeCount++;
                if (p != null && string.Equals(p.Id, poiId, StringComparison.Ordinal))
                {
                    removeIndex = i;
                }
            }

            if (removeIndex < 0)
            {
                error = "Không tìm thấy điểm đến cần xóa.";
                return false;
            }

            PublishedGraphPoiV2 targetPoi = pois[removeIndex];
            if (targetPoi.IsActive && activeCount <= 1)
            {
                error = "Cần giữ lại ít nhất 1 điểm đến trên bản đồ.";
                return false;
            }

            pois.RemoveAt(removeIndex);

            // Prune dangling custom leaf node/edge chain starting ONLY at the deleted POI's node.
            PruneOrphanedLeafChain(targetPoi.NodeId, nodes, edges, pois);

            clone.Graph.Nodes = nodes.ToArray();
            clone.Graph.Edges = edges.ToArray();
            clone.Graph.Pois = pois.ToArray();
            clone.PublishedAt = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);

            if (!ValidateManifest(clone, out error))
            {
                return false;
            }

            updated = clone;
            return true;
        }

        public static string NormalizeCategory(string category)
        {
            if (!string.IsNullOrWhiteSpace(category))
            {
                string trimmed = category.Trim().ToLowerInvariant();
                for (int i = 0; i < SupportedCategories.Length; i++)
                {
                    if (string.Equals(SupportedCategories[i], trimmed, StringComparison.Ordinal))
                    {
                        return SupportedCategories[i];
                    }
                }
            }
            return "room";
        }

        private static bool SplitEdgeAtProjection(
            List<PublishedGraphNodeV2> nodes,
            List<PublishedGraphEdgeV2> edges,
            GraphSnapTarget snap,
            out PublishedGraphNodeV2 junctionNode,
            out string error)
        {
            junctionNode = null;
            int edgeIndex = edges.FindIndex(e => e != null && string.Equals(e.Id, snap.EdgeId, StringComparison.Ordinal));
            if (edgeIndex < 0)
            {
                error = "Không tìm thấy cạnh để tách điểm nối.";
                return false;
            }

            PublishedGraphEdgeV2 original = edges[edgeIndex];
            if (original.Geometry == null || original.Geometry.Length < 2)
            {
                error = "Dữ liệu hình học của cạnh không hợp lệ.";
                return false;
            }

            junctionNode = new PublishedGraphNodeV2
            {
                Id = NewStableId("n"),
                Kind = "junction",
                AnchorLocal = ToPoint(snap.SnapPoint),
                Provenance = "admin"
            };
            nodes.Add(junctionNode);

            var firstPoints = new List<AnchorLocalPointV2>();
            for (int i = 0; i <= snap.EdgeSegmentIndex && i < original.Geometry.Length; i++)
            {
                firstPoints.Add(ClonePoint(original.Geometry[i]));
            }
            if (firstPoints.Count == 0 || Vector3.Distance(firstPoints[firstPoints.Count - 1].ToVector3(), snap.SnapPoint) > 0.001f)
            {
                firstPoints.Add(ToPoint(snap.SnapPoint));
            }
            if (firstPoints.Count < 2)
            {
                firstPoints.Add(ToPoint(snap.SnapPoint));
            }

            var secondPoints = new List<AnchorLocalPointV2> { ToPoint(snap.SnapPoint) };
            for (int i = snap.EdgeSegmentIndex + 1; i < original.Geometry.Length; i++)
            {
                AnchorLocalPointV2 pt = ClonePoint(original.Geometry[i]);
                if (i == original.Geometry.Length - 1 || Vector3.Distance(secondPoints[secondPoints.Count - 1].ToVector3(), pt.ToVector3()) > 0.001f)
                {
                    secondPoints.Add(pt);
                }
            }
            if (secondPoints.Count < 2)
            {
                secondPoints.Add(ClonePoint(original.Geometry[original.Geometry.Length - 1]));
            }

            AnchorLocalPointV2[] firstGeo = firstPoints.ToArray();
            AnchorLocalPointV2[] secondGeo = secondPoints.ToArray();

            var firstEdge = new PublishedGraphEdgeV2
            {
                Id = NewStableId("e"),
                FromNodeId = original.FromNodeId,
                ToNodeId = junctionNode.Id,
                Bidirectional = original.Bidirectional,
                Enabled = original.Enabled,
                CostMultiplier = original.CostMultiplier,
                LengthM = (float)ComputePolylineLength(firstGeo),
                Geometry = firstGeo,
                Provenance = original.Provenance
            };

            var secondEdge = new PublishedGraphEdgeV2
            {
                Id = NewStableId("e"),
                FromNodeId = junctionNode.Id,
                ToNodeId = original.ToNodeId,
                Bidirectional = original.Bidirectional,
                Enabled = original.Enabled,
                CostMultiplier = original.CostMultiplier,
                LengthM = (float)ComputePolylineLength(secondGeo),
                Geometry = secondGeo,
                Provenance = original.Provenance
            };

            edges.RemoveAt(edgeIndex);
            edges.Add(firstEdge);
            edges.Add(secondEdge);
            error = null;
            return true;
        }

        private static void PruneOrphanedLeafChain(
            string startNodeId,
            List<PublishedGraphNodeV2> nodes,
            List<PublishedGraphEdgeV2> edges,
            List<PublishedGraphPoiV2> pois)
        {
            string cursorNodeId = startNodeId;
            while (!string.IsNullOrEmpty(cursorNodeId) && nodes.Count > 2 && edges.Count > 1)
            {
                bool hasPoi = false;
                for (int i = 0; i < pois.Count; i++)
                {
                    if (pois[i] != null && string.Equals(pois[i].NodeId, cursorNodeId, StringComparison.Ordinal))
                    {
                        hasPoi = true;
                        break;
                    }
                }
                if (hasPoi) break;

                int connectedEdgeIndex = -1;
                int degree = 0;
                for (int i = 0; i < edges.Count; i++)
                {
                    PublishedGraphEdgeV2 e = edges[i];
                    if (e == null) continue;
                    if (string.Equals(e.FromNodeId, cursorNodeId, StringComparison.Ordinal) ||
                        string.Equals(e.ToNodeId, cursorNodeId, StringComparison.Ordinal))
                    {
                        degree++;
                        connectedEdgeIndex = i;
                    }
                }

                if (degree != 1 || connectedEdgeIndex < 0) break;

                PublishedGraphEdgeV2 removedEdge = edges[connectedEdgeIndex];
                // Only auto-prune user-authored edges so base map geometry stays intact unless it's the deleted leaf itself.
                if (!string.Equals(removedEdge.Provenance, "admin", StringComparison.Ordinal) &&
                    !string.Equals(cursorNodeId, startNodeId, StringComparison.Ordinal))
                {
                    break;
                }

                string nextNodeId = string.Equals(removedEdge.FromNodeId, cursorNodeId, StringComparison.Ordinal)
                    ? removedEdge.ToNodeId
                    : removedEdge.FromNodeId;

                edges.RemoveAt(connectedEdgeIndex);
                nodes.RemoveAll(n => n != null && string.Equals(n.Id, cursorNodeId, StringComparison.Ordinal));
                cursorNodeId = nextNodeId;
            }
        }

        private static bool ValidateManifest(PublishedSiteGraphManifest manifest, out string error)
        {
            string json = SerializeManifestJson(manifest, false);
            return SiteGraphV2ContractParser.TryParseManifest(
                json, manifest.FloorId, manifest.VersionId, out _, out error, out _);
        }

        private static bool ValidateManifestAndRouting(
            PublishedSiteGraphManifest manifest,
            Vector3 startPos,
            string destinationNodeId,
            out string error)
        {
            if (!ValidateManifest(manifest, out error))
            {
                return false;
            }

            if (!SiteGraphV2Pathfinder.TryFindRoute(manifest, startPos, destinationNodeId, 2.5f, out _, out error))
            {
                error = "Không thể kiểm chứng đường dẫn tới điểm mới: " + error;
                return false;
            }

            return true;
        }

        private static PublishedSiteGraphManifest CloneManifest(PublishedSiteGraphManifest source)
        {
            var clonedNodes = new List<PublishedGraphNodeV2>();
            if (source.Graph != null && source.Graph.Nodes != null)
            {
                foreach (PublishedGraphNodeV2 n in source.Graph.Nodes)
                {
                    if (n == null) continue;
                    clonedNodes.Add(new PublishedGraphNodeV2
                    {
                        Id = n.Id,
                        Kind = n.Kind,
                        AnchorLocal = ClonePoint(n.AnchorLocal),
                        Provenance = n.Provenance
                    });
                }
            }

            var clonedEdges = new List<PublishedGraphEdgeV2>();
            if (source.Graph != null && source.Graph.Edges != null)
            {
                foreach (PublishedGraphEdgeV2 e in source.Graph.Edges)
                {
                    if (e == null) continue;
                    var geo = new AnchorLocalPointV2[e.Geometry != null ? e.Geometry.Length : 0];
                    for (int i = 0; i < geo.Length; i++) geo[i] = ClonePoint(e.Geometry[i]);
                    clonedEdges.Add(new PublishedGraphEdgeV2
                    {
                        Id = e.Id,
                        FromNodeId = e.FromNodeId,
                        ToNodeId = e.ToNodeId,
                        Bidirectional = e.Bidirectional,
                        Enabled = e.Enabled,
                        CostMultiplier = e.CostMultiplier,
                        LengthM = e.LengthM,
                        Geometry = geo,
                        Provenance = e.Provenance
                    });
                }
            }

            var clonedPois = new List<PublishedGraphPoiV2>();
            if (source.Graph != null && source.Graph.Pois != null)
            {
                foreach (PublishedGraphPoiV2 p in source.Graph.Pois)
                {
                    if (p == null) continue;
                    clonedPois.Add(new PublishedGraphPoiV2
                    {
                        Id = p.Id,
                        Label = p.Label,
                        Category = p.Category,
                        IsActive = p.IsActive,
                        NodeId = p.NodeId,
                        AnchorLocal = ClonePoint(p.AnchorLocal),
                        Provenance = p.Provenance,
                        SourceMarkerId = p.SourceMarkerId,
                        Description = p.Description
                    });
                }
            }

            return new PublishedSiteGraphManifest
            {
                SchemaVersion = source.SchemaVersion,
                VersionId = source.VersionId,
                Version = source.Version,
                FloorId = source.FloorId,
                BuildingId = source.BuildingId,
                SiteBinding = source.SiteBinding == null
                    ? new SiteBindingManifestV2()
                    : new SiteBindingManifestV2
                    {
                        SiteId = source.SiteBinding.SiteId,
                        ProductionAssetId = source.SiteBinding.ProductionAssetId,
                        PayloadSha256 = source.SiteBinding.PayloadSha256
                    },
                SourceDraftRevision = source.SourceDraftRevision,
                SiteMeshSha256 = source.SiteMeshSha256,
                Graph = new PublishedNavigationGraphV2
                {
                    Nodes = clonedNodes.ToArray(),
                    Edges = clonedEdges.ToArray(),
                    Pois = clonedPois.ToArray()
                },
                PublishedAt = source.PublishedAt
            };
        }

        private static void SimplifyPolyline(
            List<Vector3> points,
            int startIndex,
            int endIndex,
            float tolerance,
            List<Vector3> output)
        {
            if (endIndex <= startIndex + 1)
            {
                output.Add(points[startIndex]);
                return;
            }

            Vector3 a = points[startIndex];
            Vector3 b = points[endIndex];
            float maxDist = -1f;
            int splitIndex = -1;

            for (int i = startIndex + 1; i < endIndex; i++)
            {
                float d = DistancePointToSegment(points[i], a, b);
                if (d > maxDist)
                {
                    maxDist = d;
                    splitIndex = i;
                }
            }

            if (maxDist > tolerance && splitIndex > startIndex)
            {
                SimplifyPolyline(points, startIndex, splitIndex, tolerance, output);
                SimplifyPolyline(points, splitIndex, endIndex, tolerance, output);
            }
            else
            {
                output.Add(points[startIndex]);
            }
        }

        private static float DistancePointToSegment(Vector3 p, Vector3 a, Vector3 b)
        {
            Vector3 ab = b - a;
            float lenSq = ab.sqrMagnitude;
            if (lenSq <= 1e-8f) return Vector3.Distance(p, a);
            float t = Mathf.Clamp01(Vector3.Dot(p - a, ab) / lenSq);
            return Vector3.Distance(p, a + ab * t);
        }

        private static double ComputePolylineLength(AnchorLocalPointV2[] points)
        {
            if (points == null || points.Length < 2) return 0d;
            double total = 0d;
            for (int i = 1; i < points.Length; i++)
            {
                total += Vector3.Distance(points[i - 1].ToVector3(), points[i].ToVector3());
            }
            return total;
        }

        private static PublishedGraphNodeV2 FindNode(PublishedGraphNodeV2[] nodes, string id)
        {
            if (nodes == null || string.IsNullOrEmpty(id)) return null;
            for (int i = 0; i < nodes.Length; i++)
            {
                if (nodes[i] != null && string.Equals(nodes[i].Id, id, StringComparison.Ordinal))
                {
                    return nodes[i];
                }
            }
            return null;
        }

        private static string NewStableId(string prefix)
        {
            return prefix + "-" + Guid.NewGuid().ToString("N").Substring(0, 18);
        }

        private static JObject PointToJson(AnchorLocalPointV2 point)
        {
            return new JObject
            {
                ["x"] = point != null ? (double)point.X : 0d,
                ["y"] = point != null ? (double)point.Y : 0d,
                ["z"] = point != null ? (double)point.Z : 0d
            };
        }

        private static AnchorLocalPointV2 ToPoint(Vector3 v) =>
            new AnchorLocalPointV2 { X = v.x, Y = v.y, Z = v.z };

        private static AnchorLocalPointV2[] ToPoints(Vector3[] vectors)
        {
            var result = new AnchorLocalPointV2[vectors.Length];
            for (int i = 0; i < vectors.Length; i++) result[i] = ToPoint(vectors[i]);
            return result;
        }

        private static AnchorLocalPointV2 ClonePoint(AnchorLocalPointV2 p) =>
            p == null ? new AnchorLocalPointV2() : new AnchorLocalPointV2 { X = p.X, Y = p.Y, Z = p.Z };

        private static bool IsFinite(Vector3 v) =>
            !float.IsNaN(v.x) && !float.IsInfinity(v.x) &&
            !float.IsNaN(v.y) && !float.IsInfinity(v.y) &&
            !float.IsNaN(v.z) && !float.IsInfinity(v.z);

        private static string ResolvePath(string filePath) =>
            string.IsNullOrWhiteSpace(filePath) ? DefaultManifestPath : filePath;
    }
}
