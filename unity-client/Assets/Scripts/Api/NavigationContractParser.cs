using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Vps.UnityClient.Api
{
    [Serializable]
    public sealed class PublicNavigationConfigV2
    {
        public int SchemaVersion;
        public string ActiveFloorId;
        public string ActiveVersionId;
        public int ConfigRevision;
    }

    [Serializable]
    public sealed class AnchorLocalPointV2
    {
        public float X;
        public float Y;
        public float Z;

        public Vector3 ToVector3() { return new Vector3(X, Y, Z); }
    }

    [Serializable]
    public sealed class PublishedGraphNodeV2
    {
        public string Id;
        public string Kind;
        public AnchorLocalPointV2 AnchorLocal;
        public string Provenance;
    }

    [Serializable]
    public sealed class PublishedGraphEdgeV2
    {
        public string Id;
        public string FromNodeId;
        public string ToNodeId;
        public bool Bidirectional;
        public bool Enabled;
        public float CostMultiplier;
        public float LengthM;
        public AnchorLocalPointV2[] Geometry;
        public string Provenance;
    }

    [Serializable]
    public sealed class PublishedGraphPoiV2
    {
        public string Id;
        public string Label;
        public string Category;
        public bool IsActive;
        public string NodeId;
        public AnchorLocalPointV2 AnchorLocal;
        public string Provenance;
        public string SourceMarkerId;
    }

    [Serializable]
    public sealed class PublishedNavigationGraphV2
    {
        public PublishedGraphNodeV2[] Nodes;
        public PublishedGraphEdgeV2[] Edges;
        public PublishedGraphPoiV2[] Pois;
    }

    [Serializable]
    public sealed class PublishedSiteGraphManifest
    {
        public int SchemaVersion;
        public string VersionId;
        public int Version;
        public string FloorId;
        public string BuildingId;
        public SiteBindingManifestV2 SiteBinding;
        public int? SourceDraftRevision;
        public string SiteMeshSha256;
        public PublishedNavigationGraphV2 Graph;
        public string PublishedAt;
    }

    [Serializable]
    public sealed class SiteBindingManifestV2
    {
        public string SiteId;
        public string ProductionAssetId;
        public string PayloadSha256;
    }

    /// <summary>Strict, fail-closed parser for the Site Graph V2 public contract.</summary>
    public static class SiteGraphV2ContractParser
    {
        private static readonly Regex StableId = new Regex("^[A-Za-z0-9_-]{1,100}$", RegexOptions.CultureInvariant);
        private static readonly Regex Sha256 = new Regex("^[a-f0-9]{64}$", RegexOptions.CultureInvariant);

        public static bool TryParseConfig(string json, out PublicNavigationConfigV2 config, out string error)
        {
            config = null;
            JObject root;
            if (!TryObject(json, "config", out root, out error)) return false;
            if (!Exact(root, "config", out error, "schemaVersion", "activeFloorId", "activeVersionId", "configRevision")) return false;
            int schemaVersion, revision;
            if (!TryInt(root["schemaVersion"], "config.schemaVersion", 2, 2, out schemaVersion, out error) ||
                !TryInt(root["configRevision"], "config.configRevision", 0, int.MaxValue, out revision, out error)) return false;
            string floorId, versionId;
            if (!TryNullableString(root["activeFloorId"], "config.activeFloorId", out floorId, out error) ||
                !TryNullableString(root["activeVersionId"], "config.activeVersionId", out versionId, out error)) return false;
            if ((floorId == null) != (versionId == null))
            {
                error = "config activeFloorId and activeVersionId must both be null or both be set.";
                return false;
            }
            config = new PublicNavigationConfigV2
            {
                SchemaVersion = schemaVersion,
                ActiveFloorId = floorId,
                ActiveVersionId = versionId,
                ConfigRevision = revision
            };
            error = null;
            return true;
        }

        public static bool TryParseManifest(
            string json,
            string expectedFloorId,
            string expectedVersionId,
            out PublishedSiteGraphManifest manifest,
            out string error,
            out bool activePointerMismatch)
        {
            manifest = null;
            activePointerMismatch = false;
            JObject root;
            if (!TryObject(json, "manifest", out root, out error)) return false;
            if (!Exact(root, "manifest", out error, "schemaVersion", "versionId", "version", "floorId", "buildingId",
                    "siteBinding", "sourceDraftRevision", "siteMeshSha256", "graph", "publishedAt")) return false;

            int schemaVersion, version;
            string versionId, floorId, buildingId, publishedAt;
            if (!TryInt(root["schemaVersion"], "manifest.schemaVersion", 2, 2, out schemaVersion, out error) ||
                !TryInt(root["version"], "manifest.version", 1, int.MaxValue, out version, out error) ||
                !TryRequiredString(root["versionId"], "manifest.versionId", out versionId, out error) ||
                !TryRequiredString(root["floorId"], "manifest.floorId", out floorId, out error) ||
                !TryRequiredString(root["buildingId"], "manifest.buildingId", out buildingId, out error) ||
                !TryRequiredString(root["publishedAt"], "manifest.publishedAt", out publishedAt, out error)) return false;
            DateTimeOffset parsedPublishedAt;
            if (!DateTimeOffset.TryParse(publishedAt, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out parsedPublishedAt))
            {
                error = "manifest.publishedAt must be an ISO-8601 timestamp.";
                return false;
            }

            SiteBindingManifestV2 binding;
            if (!TryParseBinding(root["siteBinding"], out binding, out error)) return false;
            int? sourceDraftRevision;
            if (!TryNullableInt(root["sourceDraftRevision"], "manifest.sourceDraftRevision", out sourceDraftRevision, out error)) return false;
            string meshSha;
            if (!TryNullableSha(root["siteMeshSha256"], "manifest.siteMeshSha256", out meshSha, out error)) return false;
            PublishedNavigationGraphV2 graph;
            if (!TryParseGraph(root["graph"], out graph, out error)) return false;

            if (string.IsNullOrWhiteSpace(expectedFloorId) || !string.Equals(floorId, expectedFloorId, StringComparison.Ordinal))
            {
                activePointerMismatch = true;
                error = "manifest floorId does not match the active config.";
                return false;
            }
            if (string.IsNullOrWhiteSpace(expectedVersionId) || !string.Equals(versionId, expectedVersionId, StringComparison.Ordinal))
            {
                activePointerMismatch = true;
                error = "manifest versionId does not match the active config.";
                return false;
            }

            manifest = new PublishedSiteGraphManifest
            {
                SchemaVersion = schemaVersion,
                VersionId = versionId,
                Version = version,
                FloorId = floorId,
                BuildingId = buildingId,
                SiteBinding = binding,
                SourceDraftRevision = sourceDraftRevision,
                SiteMeshSha256 = meshSha,
                Graph = graph,
                PublishedAt = publishedAt
            };
            error = null;
            return true;
        }

        private static bool TryParseBinding(JToken token, out SiteBindingManifestV2 binding, out string error)
        {
            binding = null;
            JObject value;
            if (!TryObjectToken(token, "manifest.siteBinding", out value, out error) ||
                !Exact(value, "manifest.siteBinding", out error, "siteId", "productionAssetId", "payloadSha256")) return false;
            string siteId, assetId, fingerprint;
            if (!TryRequiredString(value["siteId"], "manifest.siteBinding.siteId", out siteId, out error) ||
                !TryRequiredString(value["productionAssetId"], "manifest.siteBinding.productionAssetId", out assetId, out error) ||
                !TryRequiredString(value["payloadSha256"], "manifest.siteBinding.payloadSha256", out fingerprint, out error)) return false;
            if (!Sha256.IsMatch(fingerprint))
            {
                error = "manifest.siteBinding.payloadSha256 must be a lowercase SHA-256 digest.";
                return false;
            }
            binding = new SiteBindingManifestV2 { SiteId = siteId, ProductionAssetId = assetId, PayloadSha256 = fingerprint };
            error = null;
            return true;
        }

        private static bool TryParseGraph(JToken token, out PublishedNavigationGraphV2 graph, out string error)
        {
            graph = null;
            JObject value;
            if (!TryObjectToken(token, "manifest.graph", out value, out error) ||
                !Exact(value, "manifest.graph", out error, "nodes", "edges", "pois")) return false;
            JArray nodesToken, edgesToken, poisToken;
            if (!TryArray(value["nodes"], "manifest.graph.nodes", 2, 5000, out nodesToken, out error) ||
                !TryArray(value["edges"], "manifest.graph.edges", 1, 10000, out edgesToken, out error) ||
                !TryArray(value["pois"], "manifest.graph.pois", 1, 1000, out poisToken, out error)) return false;

            var nodes = new PublishedGraphNodeV2[nodesToken.Count];
            var nodeIds = new HashSet<string>(StringComparer.Ordinal);
            for (int index = 0; index < nodes.Length; index++)
            {
                if (!TryParseNode(nodesToken[index], index, out nodes[index], out error)) return false;
                if (!nodeIds.Add(nodes[index].Id))
                {
                    error = "manifest.graph.nodes contains a duplicate identifier.";
                    return false;
                }
            }

            var edges = new PublishedGraphEdgeV2[edgesToken.Count];
            var edgeIds = new HashSet<string>(StringComparer.Ordinal);
            bool hasEnabledEdge = false;
            for (int index = 0; index < edges.Length; index++)
            {
                if (!TryParseEdge(edgesToken[index], index, out edges[index], out error)) return false;
                if (!edgeIds.Add(edges[index].Id) || !nodeIds.Contains(edges[index].FromNodeId) || !nodeIds.Contains(edges[index].ToNodeId))
                {
                    error = "manifest.graph.edges contains duplicate IDs or missing node references.";
                    return false;
                }
                if (edges[index].FromNodeId == edges[index].ToNodeId)
                {
                    error = "manifest.graph.edges cannot contain self edges.";
                    return false;
                }
                Vector3 from = FindNode(nodes, edges[index].FromNodeId).AnchorLocal.ToVector3();
                Vector3 to = FindNode(nodes, edges[index].ToNodeId).AnchorLocal.ToVector3();
                Vector3[] geometry = ToVectors(edges[index].Geometry);
                if (Vector3.Distance(geometry[0], from) > 0.1f || Vector3.Distance(geometry[geometry.Length - 1], to) > 0.1f)
                {
                    error = "manifest.graph.edges geometry endpoints do not match their graph nodes.";
                    return false;
                }
                double geometryLength = PolylineLength(geometry);
                if (Math.Abs(geometryLength - edges[index].LengthM) > Math.Max(0.05d, geometryLength * 0.01d))
                {
                    error = "manifest.graph.edges lengthM does not match its anchor-local geometry.";
                    return false;
                }
                hasEnabledEdge |= edges[index].Enabled;
            }

            var pois = new PublishedGraphPoiV2[poisToken.Count];
            var poiIds = new HashSet<string>(StringComparer.Ordinal);
            bool hasActivePoi = false;
            for (int index = 0; index < pois.Length; index++)
            {
                if (!TryParsePoi(poisToken[index], index, out pois[index], out error)) return false;
                PublishedGraphNodeV2 node = FindNode(nodes, pois[index].NodeId);
                if (!poiIds.Add(pois[index].Id) || node == null ||
                    Vector3.Distance(node.AnchorLocal.ToVector3(), pois[index].AnchorLocal.ToVector3()) > 0.25f)
                {
                    error = "manifest.graph.pois contains a duplicate ID or invalid node position.";
                    return false;
                }
                hasActivePoi |= pois[index].IsActive;
            }
            if (!hasEnabledEdge || !hasActivePoi)
            {
                error = "manifest.graph requires an enabled edge and active point of interest.";
                return false;
            }
            graph = new PublishedNavigationGraphV2 { Nodes = nodes, Edges = edges, Pois = pois };
            error = null;
            return true;
        }

        private static bool TryParseNode(JToken token, int index, out PublishedGraphNodeV2 node, out string error)
        {
            node = null;
            string path = "manifest.graph.nodes[" + index + "]";
            JObject value;
            if (!TryObjectToken(token, path, out value, out error) ||
                !Exact(value, path, out error, "id", "kind", "anchorLocal", "provenance")) return false;
            string id, kind, provenance;
            AnchorLocalPointV2 point;
            if (!TryId(value["id"], path + ".id", out id, out error) ||
                !TryEnum(value["kind"], path + ".kind", out kind, out error, "waypoint", "junction", "entrance", "exit", "vertical_transition") ||
                !TryPoint(value["anchorLocal"], path + ".anchorLocal", out point, out error) ||
                !TryEnum(value["provenance"], path + ".provenance", out provenance, out error, "generated", "admin"))
            {
                if (string.IsNullOrEmpty(error)) error = path + " contains an unsupported node kind or provenance.";
                return false;
            }
            node = new PublishedGraphNodeV2 { Id = id, Kind = kind, AnchorLocal = point, Provenance = provenance };
            error = null;
            return true;
        }

        private static bool TryParseEdge(JToken token, int index, out PublishedGraphEdgeV2 edge, out string error)
        {
            edge = null;
            string path = "manifest.graph.edges[" + index + "]";
            JObject value;
            if (!TryObjectToken(token, path, out value, out error) ||
                !Exact(value, path, out error, "id", "fromNodeId", "toNodeId", "bidirectional", "enabled", "costMultiplier", "lengthM", "geometry", "provenance")) return false;
            string id, from, to, provenance;
            bool bidirectional, enabled;
            double multiplier, length;
            if (!TryId(value["id"], path + ".id", out id, out error) ||
                !TryId(value["fromNodeId"], path + ".fromNodeId", out from, out error) ||
                !TryId(value["toNodeId"], path + ".toNodeId", out to, out error) ||
                !TryBool(value["bidirectional"], path + ".bidirectional", out bidirectional, out error) ||
                !TryBool(value["enabled"], path + ".enabled", out enabled, out error) ||
                !TryNumber(value["costMultiplier"], path + ".costMultiplier", out multiplier, out error) ||
                !TryNumber(value["lengthM"], path + ".lengthM", out length, out error) ||
                !TryEnum(value["provenance"], path + ".provenance", out provenance, out error, "generated", "admin")) return false;
            if (multiplier <= 0d || multiplier > 10d || length <= 0d || length > float.MaxValue)
            {
                error = path + " has an invalid edge cost or length.";
                return false;
            }
            JArray geometry;
            if (!TryArray(value["geometry"], path + ".geometry", 2, 2000, out geometry, out error)) return false;
            var points = new AnchorLocalPointV2[geometry.Count];
            for (int pointIndex = 0; pointIndex < points.Length; pointIndex++)
                if (!TryPoint(geometry[pointIndex], path + ".geometry[" + pointIndex + "]", out points[pointIndex], out error)) return false;
            edge = new PublishedGraphEdgeV2
            {
                Id = id, FromNodeId = from, ToNodeId = to,
                Bidirectional = bidirectional, Enabled = enabled,
                CostMultiplier = (float)multiplier, LengthM = (float)length,
                Geometry = points, Provenance = provenance
            };
            error = null;
            return true;
        }

        private static bool TryParsePoi(JToken token, int index, out PublishedGraphPoiV2 poi, out string error)
        {
            poi = null;
            string path = "manifest.graph.pois[" + index + "]";
            JObject value;
            if (!TryObjectToken(token, path, out value, out error)) return false;
            string[] allowed = value.Property("sourceMarkerId") == null
                ? new[] { "id", "label", "category", "isActive", "nodeId", "anchorLocal", "provenance" }
                : new[] { "id", "label", "category", "isActive", "nodeId", "anchorLocal", "provenance", "sourceMarkerId" };
            if (!Exact(value, path, out error, allowed)) return false;
            string id, label, category, nodeId, provenance, sourceMarkerId = null;
            bool active;
            AnchorLocalPointV2 point;
            if (!TryId(value["id"], path + ".id", out id, out error) ||
                !TryRequiredString(value["label"], path + ".label", out label, out error) || label.Length > 100 ||
                !TryEnum(value["category"], path + ".category", out category, out error, "room", "service", "entrance", "elevator", "stairs", "restroom", "other") ||
                !TryBool(value["isActive"], path + ".isActive", out active, out error) ||
                !TryId(value["nodeId"], path + ".nodeId", out nodeId, out error) ||
                !TryPoint(value["anchorLocal"], path + ".anchorLocal", out point, out error) ||
                !TryEnum(value["provenance"], path + ".provenance", out provenance, out error, "captured", "generated", "admin")) return false;
            if (value.Property("sourceMarkerId") != null &&
                !TryUuid(value["sourceMarkerId"], path + ".sourceMarkerId", out sourceMarkerId, out error)) return false;
            poi = new PublishedGraphPoiV2
            {
                Id = id, Label = label, Category = category, IsActive = active,
                NodeId = nodeId, AnchorLocal = point, Provenance = provenance, SourceMarkerId = sourceMarkerId
            };
            error = null;
            return true;
        }

        private static bool TryPoint(JToken token, string path, out AnchorLocalPointV2 point, out string error)
        {
            point = null;
            JObject value;
            if (!TryObjectToken(token, path, out value, out error) || !Exact(value, path, out error, "x", "y", "z")) return false;
            double x, y, z;
            if (!TryNumber(value["x"], path + ".x", out x, out error) ||
                !TryNumber(value["y"], path + ".y", out y, out error) ||
                !TryNumber(value["z"], path + ".z", out z, out error) ||
                Math.Abs(x) > float.MaxValue || Math.Abs(y) > float.MaxValue || Math.Abs(z) > float.MaxValue)
            {
                if (string.IsNullOrEmpty(error)) error = path + " coordinates must be representable finite numbers.";
                return false;
            }
            point = new AnchorLocalPointV2 { X = (float)x, Y = (float)y, Z = (float)z };
            error = null;
            return true;
        }

        private static bool TryObject(string json, string path, out JObject value, out string error)
        {
            value = null;
            error = null;
            try
            {
                if (json == null)
                {
                    error = path + " must be valid JSON.";
                    return false;
                }
                using (var source = new StringReader(json))
                using (var reader = new JsonTextReader(source) { DateParseHandling = DateParseHandling.None })
                {
                    JToken parsed = JToken.ReadFrom(reader);
                    if (reader.Read())
                    {
                        error = path + " must contain one JSON object.";
                        return false;
                    }
                    if (!(parsed is JObject))
                    {
                        error = path + " must be a JSON object.";
                        return false;
                    }
                    value = (JObject)parsed;
                    return true;
                }
            }
            catch (JsonException)
            {
                error = path + " must be valid JSON.";
                return false;
            }
        }

        private static bool TryObjectToken(JToken token, string path, out JObject value, out string error)
        {
            value = token as JObject;
            error = value == null ? path + " must be an object." : null;
            return value != null;
        }

        private static bool Exact(JObject value, string path, out string error, params string[] expected)
        {
            var names = new HashSet<string>(expected, StringComparer.Ordinal);
            foreach (JProperty property in value.Properties())
            {
                if (!names.Remove(property.Name))
                {
                    error = path + " contains an unsupported field: " + property.Name + ".";
                    return false;
                }
            }
            if (names.Count > 0)
            {
                error = path + " is missing required fields: " + string.Join(", ", names) + ".";
                return false;
            }
            error = null;
            return true;
        }

        private static bool TryArray(JToken token, string path, int min, int max, out JArray array, out string error)
        {
            array = token as JArray;
            if (array == null || array.Count < min || array.Count > max)
            {
                error = path + " has an invalid item count.";
                return false;
            }
            error = null;
            return true;
        }

        private static bool TryRequiredString(JToken token, string path, out string value, out string error)
        {
            value = token != null && token.Type == JTokenType.String ? (string)token : null;
            if (string.IsNullOrWhiteSpace(value))
            {
                error = path + " must be a non-empty string.";
                return false;
            }
            error = null;
            return true;
        }

        private static bool TryNullableString(JToken token, string path, out string value, out string error)
        {
            value = null;
            if (token == null)
            {
                error = path + " is required.";
                return false;
            }
            if (token.Type == JTokenType.Null) { error = null; return true; }
            return TryRequiredString(token, path, out value, out error);
        }

        private static bool TryNullableInt(JToken token, string path, out int? value, out string error)
        {
            value = null;
            if (token == null) { error = path + " is required."; return false; }
            if (token.Type == JTokenType.Null) { error = null; return true; }
            int parsed;
            if (!TryInt(token, path, 0, int.MaxValue, out parsed, out error)) return false;
            value = parsed;
            return true;
        }

        private static bool TryInt(JToken token, string path, int min, int max, out int value, out string error)
        {
            value = 0;
            if (token == null || token.Type != JTokenType.Integer)
            {
                error = path + " must be an integer.";
                return false;
            }
            long parsed = (long)token;
            if (parsed < min || parsed > max)
            {
                error = path + " is outside its permitted range.";
                return false;
            }
            value = (int)parsed;
            error = null;
            return true;
        }

        private static bool TryNumber(JToken token, string path, out double value, out string error)
        {
            value = 0d;
            if (token == null || (token.Type != JTokenType.Integer && token.Type != JTokenType.Float))
            {
                error = path + " must be a finite number.";
                return false;
            }
            value = (double)token;
            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                error = path + " must be a finite number.";
                return false;
            }
            error = null;
            return true;
        }

        private static bool TryBool(JToken token, string path, out bool value, out string error)
        {
            value = false;
            if (token == null || token.Type != JTokenType.Boolean)
            {
                error = path + " must be a boolean.";
                return false;
            }
            value = (bool)token;
            error = null;
            return true;
        }

        private static bool TryEnum(JToken token, string path, out string value, out string error, params string[] allowed)
        {
            value = token != null && token.Type == JTokenType.String ? (string)token : null;
            for (int index = 0; index < allowed.Length; index++)
                if (string.Equals(value, allowed[index], StringComparison.Ordinal)) { error = null; return true; }
            return Fail(path + " has an unsupported value.", out error);
        }

        private static bool TryId(JToken token, string path, out string value, out string error)
        {
            if (!TryRequiredString(token, path, out value, out error)) return false;
            if (!StableId.IsMatch(value))
            {
                error = path + " contains an invalid stable identifier.";
                return false;
            }
            return true;
        }

        private static bool TryUuid(JToken token, string path, out string value, out string error)
        {
            if (!TryRequiredString(token, path, out value, out error)) return false;
            Guid parsed;
            if (!Guid.TryParse(value, out parsed))
            {
                error = path + " must be a UUID.";
                return false;
            }
            error = null;
            return true;
        }

        private static bool TryNullableSha(JToken token, string path, out string value, out string error)
        {
            value = null;
            if (token == null) { error = path + " is required."; return false; }
            if (token.Type == JTokenType.Null) { error = null; return true; }
            if (!TryRequiredString(token, path, out value, out error)) return false;
            if (!Sha256.IsMatch(value))
            {
                error = path + " must be a lowercase SHA-256 digest or null.";
                return false;
            }
            return true;
        }

        private static PublishedGraphNodeV2 FindNode(PublishedGraphNodeV2[] nodes, string id)
        {
            for (int index = 0; index < nodes.Length; index++)
                if (nodes[index].Id == id) return nodes[index];
            return null;
        }

        private static Vector3[] ToVectors(AnchorLocalPointV2[] points)
        {
            var result = new Vector3[points.Length];
            for (int index = 0; index < points.Length; index++) result[index] = points[index].ToVector3();
            return result;
        }

        private static double PolylineLength(Vector3[] points)
        {
            double total = 0d;
            for (int index = 1; index < points.Length; index++) total += Vector3.Distance(points[index - 1], points[index]);
            return total;
        }

        private static bool Fail(string message, out string error)
        {
            error = message;
            return false;
        }
    }
}
