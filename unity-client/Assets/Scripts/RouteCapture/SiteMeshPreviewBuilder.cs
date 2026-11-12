using System;
using System.Collections.Generic;
using UnityEngine;
using Vps.UnityClient.RouteCapture;

namespace Vps.UnityClient.Authoring
{
    /// <summary>Builds a bounded, texture-free anchor-local mesh preview for Admin graph review.</summary>
    public static class SiteMeshPreviewBuilder
    {
        public const int MaximumPreviewTriangles = 12000;
        public const int MaximumPreviewVertices = 50000;

        public static bool TryBuild(
            GameObject meshRoot,
            Transform siteAnchor,
            string siteId,
            string productionAssetId,
            string payloadSha256,
            string nsdkVersion,
            DateTime capturedAtUtc,
            out SiteMeshUploadDocument preview,
            out string error)
        {
            preview = null;
            error = null;
            if (meshRoot == null || siteAnchor == null || string.IsNullOrWhiteSpace(siteId) ||
                string.IsNullOrWhiteSpace(productionAssetId) || string.IsNullOrWhiteSpace(payloadSha256) ||
                string.IsNullOrWhiteSpace(nsdkVersion))
            {
                error = "MESH_PREVIEW_INPUT_INVALID";
                return false;
            }

            MeshFilter[] filters = meshRoot.GetComponentsInChildren<MeshFilter>(true);
            int totalTriangleCount = 0;
            for (int index = 0; index < filters.Length; index++)
            {
                if (filters[index].sharedMesh != null) totalTriangleCount += filters[index].sharedMesh.triangles.Length / 3;
            }
            if (totalTriangleCount == 0)
            {
                error = "MESH_PREVIEW_EMPTY";
                return false;
            }

            int stride = Mathf.Max(1, Mathf.CeilToInt((float)totalTriangleCount / MaximumPreviewTriangles));
            var output = new SiteMeshUploadDocument
            {
                siteId = siteId,
                productionAssetId = productionAssetId,
                payloadSha256 = payloadSha256,
                nsdkVersion = nsdkVersion,
                capturedAt = capturedAtUtc.ToUniversalTime().ToString("O", System.Globalization.CultureInfo.InvariantCulture)
            };
            int globalTriangleIndex = 0;

            for (int filterIndex = 0; filterIndex < filters.Length; filterIndex++)
            {
                MeshFilter filter = filters[filterIndex];
                Mesh mesh = filter.sharedMesh;
                if (mesh == null) continue;
                Vector3[] sourceVertices = mesh.vertices;
                int[] sourceTriangles = mesh.triangles;
                var remap = new Dictionary<int, int>();
                for (int offset = 0; offset + 2 < sourceTriangles.Length; offset += 3, globalTriangleIndex++)
                {
                    if (globalTriangleIndex % stride != 0 || output.triangles.Count >= MaximumPreviewTriangles) continue;
                    int a, b, c;
                    if (!TryMapVertex(sourceTriangles[offset], sourceVertices, filter, siteAnchor, remap, output.vertices, out a) ||
                        !TryMapVertex(sourceTriangles[offset + 1], sourceVertices, filter, siteAnchor, remap, output.vertices, out b) ||
                        !TryMapVertex(sourceTriangles[offset + 2], sourceVertices, filter, siteAnchor, remap, output.vertices, out c))
                    {
                        error = "MESH_PREVIEW_VERTEX_LIMIT";
                        return false;
                    }
                    if (a == b || b == c || c == a) continue;
                    output.triangles.Add(new[] { a, b, c });
                }
            }

            if (output.vertices.Count < 3 || output.triangles.Count == 0)
            {
                error = "MESH_PREVIEW_EMPTY";
                return false;
            }
            preview = output;
            return true;
        }

        private static bool TryMapVertex(
            int sourceIndex,
            Vector3[] sourceVertices,
            MeshFilter filter,
            Transform siteAnchor,
            IDictionary<int, int> remap,
            IList<AnchorLocalPointDocument> output,
            out int mappedIndex)
        {
            if (remap.TryGetValue(sourceIndex, out mappedIndex)) return true;
            if (sourceIndex < 0 || sourceIndex >= sourceVertices.Length || output.Count >= MaximumPreviewVertices) return false;
            Vector3 world = filter.transform.TransformPoint(sourceVertices[sourceIndex]);
            Vector3 local = siteAnchor.InverseTransformPoint(world);
            if (!IsFinite(local.x) || !IsFinite(local.y) || !IsFinite(local.z)) return false;
            mappedIndex = output.Count;
            output.Add(new AnchorLocalPointDocument(local.x, local.y, local.z));
            remap.Add(sourceIndex, mappedIndex);
            return true;
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
