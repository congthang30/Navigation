using System;
using UnityEngine;

namespace Vps.UnityClient.Navigation
{
    /// <summary>
    /// Controls the ground-aligned 3D route line and Doraemon companion guide in AR navigation.
    /// Doraemon walks ahead along the route line leading the user, stops and waits when the user stops,
    /// and resumes walking ahead when the user walks.
    /// </summary>
    public sealed class DoraemonCompanionGuide : MonoBehaviour
    {
        public const float DefaultScale = 0.25f;
        public const float DefaultGroundDrop = 0.08f;
        public const float DefaultLeadDistance = 1.8f;
        public const float DefaultWalkSpeed = 1.3f;
        public const float DefaultModelBrightness = 0.75f;
        public const float OffRouteRerouteThresholdM = 3.5f;
        private const float UserSpeedNoiseFloorMps = 0.16f;
        private const float UserMoveStartMps = 0.24f;
        private const float UserMoveStopMps = 0.10f;
        private const float WaitingReleaseLeadGapM = 0.42f;

        [SerializeField] private float groundDropMeters = DefaultGroundDrop;
        [SerializeField] private float companionScale = DefaultScale;
        [SerializeField] private float modelScaleMultiplier = 1f;
        [SerializeField] private float modelBrightness = DefaultModelBrightness;
        [SerializeField] private float leadDistance = DefaultLeadDistance;
        [SerializeField] private float walkSpeed = DefaultWalkSpeed;
        [SerializeField] private float modelYawOffset = 0f;

        private GameObject doraemonInstance;
        private Renderer primaryRenderer;
        private Transform trackedAnchor;
        private Camera arCamera;
        private Vector3[] rawRoutePoints = Array.Empty<Vector3>();
        private Vector3[] groundPoints = Array.Empty<Vector3>();
        private float[] segmentLengths = Array.Empty<float>();
        private float totalRouteLength;

        private float currentDistanceOnRoute;
        private float userDistanceOnRoute;
        private float minDistanceToRoute;
        private bool isWaitingForUser;
        private bool isAtDestination;
        private bool isVisible;
        private bool needsReroute;

        // User movement tracking
        private Vector2 lastUserPos2D;
        private bool hasLastUserPos;
        private float userSpeed;
        private float timeSinceStopped;
        private float walkAnimTime;
        private bool userIsMoving;
        private Quaternion waitingFacingRotation;
        private bool hasWaitingFacingRotation;
        private Vector3 baseModelScale = Vector3.one;

        private struct MaterialBrightnessState
        {
            public Material Material;
            public Color BaseColor;
            public bool HasBaseColor;
            public Color EmissionColor;
            public bool HadEmissionKeyword;
        }

        private readonly System.Collections.Generic.List<MaterialBrightnessState> brightnessMaterials =
            new System.Collections.Generic.List<MaterialBrightnessState>();

        public float GroundDropMeters
        {
            get => groundDropMeters;
            set
            {
                groundDropMeters = Mathf.Clamp(value, 0.0f, 2.5f);
                RebuildGroundPoints();
            }
        }

        public float LeadDistance
        {
            get => leadDistance;
            set => leadDistance = Mathf.Clamp(value, 1.2f, 3.0f);
        }

        /// <summary>Scale multiplier applied after the model is normalized to navigation height.</summary>
        public float ModelScaleMultiplier
        {
            get => modelScaleMultiplier;
            set
            {
                modelScaleMultiplier = Mathf.Clamp(value, 0.5f, 2.0f);
                ApplyModelScale();
            }
        }

        /// <summary>Emission strength used to keep the companion readable in dim camera environments.</summary>
        public float ModelBrightness
        {
            get => modelBrightness;
            set
            {
                modelBrightness = Mathf.Clamp(value, 0f, 2f);
                ApplyModelBrightness();
            }
        }

        public bool IsWaitingForUser => isWaitingForUser;
        public bool IsAtDestination => isAtDestination;
        public bool NeedsReroute => needsReroute;
        public float CurrentDistanceOnRoute => currentDistanceOnRoute;
        public float UserDistanceOnRoute => userDistanceOnRoute;
        public float UserSpeed => userSpeed;
        public float TotalRouteLength => totalRouteLength;
        public Vector3[] GroundPoints => groundPoints;
        public Renderer PrimaryRenderer => primaryRenderer;

        public string CompanionStatusMessage
        {
            get
            {
                if (isAtDestination) return "Robot: Đã tới đích!";
                if (isWaitingForUser) return "Robot: Đang đứng đợi bạn... Hãy bước tiếp!";
                return "Robot: Đang dẫn đường, đi theo tớ nhé!";
            }
        }

        public GameObject CompanionInstance => doraemonInstance;
        public Light CompanionLight => doraemonInstance != null ? doraemonInstance.GetComponentInChildren<Light>(true) : null;

        public void Initialize(Transform anchor, Camera camera)
        {
            trackedAnchor = anchor;
            arCamera = camera;
            EnsureDoraemonModel();
            if (doraemonInstance != null)
            {
                if (trackedAnchor != null && doraemonInstance.transform.parent != trackedAnchor)
                {
                    doraemonInstance.transform.SetParent(trackedAnchor, false);
                }
                else if (trackedAnchor == null && doraemonInstance.transform.parent == null)
                {
                    doraemonInstance.transform.SetParent(transform, false);
                }
            }
        }

        public void SetRoute(Vector3[] anchorLocalPoints)
        {
            Vector3 previousDoraemonPos = Vector3.zero;
            bool hadPreviousPos = false;
            // A valid previous route is enough: at the route start, distance is 0 but the model still has a live pose.
            if (doraemonInstance != null && groundPoints.Length >= 2)
            {
                previousDoraemonPos = doraemonInstance.transform.localPosition;
                hadPreviousPos = true;
            }

            rawRoutePoints = anchorLocalPoints ?? Array.Empty<Vector3>();
            needsReroute = false;
            RebuildGroundPoints();

            if (groundPoints.Length < 2)
            {
                currentDistanceOnRoute = 0f;
                userDistanceOnRoute = 0f;
                hasWaitingFacingRotation = false;
                return;
            }

            if (hadPreviousPos)
            {
                // Seamlessly project Doraemon's existing position onto the updated route without jumping
                currentDistanceOnRoute = Mathf.Clamp(
                    ProjectPointDistance2D(previousDoraemonPos, out _),
                    0f,
                    totalRouteLength
                );
            }
            else if (currentDistanceOnRoute <= 0.05f || currentDistanceOnRoute > totalRouteLength)
            {
                currentDistanceOnRoute = 0f;
            }

            if (doraemonInstance != null)
            {
                SamplePath(currentDistanceOnRoute, out Vector3 initialPos, out Vector3 initialTangent);
                doraemonInstance.transform.localPosition = initialPos;
                if (!hadPreviousPos)
                {
                    hasWaitingFacingRotation = false;
                    Vector3 forward = new Vector3(initialTangent.x, 0f, initialTangent.z).normalized;
                    if (forward.sqrMagnitude > 0.001f)
                    {
                        doraemonInstance.transform.localRotation = Quaternion.LookRotation(forward, Vector3.up) * Quaternion.Euler(0f, modelYawOffset, 0f);
                    }
                }
            }
        }

        public void SetVisible(bool visible)
        {
            isVisible = visible;
            if (doraemonInstance != null)
            {
                doraemonInstance.SetActive(visible && groundPoints.Length >= 2);
            }
            if (!visible)
            {
                hasLastUserPos = false;
                userSpeed = 0f;
                userIsMoving = false;
                timeSinceStopped = 0f;
                hasWaitingFacingRotation = false;
            }
        }

        public void ResetRerouteFlag()
        {
            needsReroute = false;
        }

        public void UpdateCompanion(Vector3 anchorLocalUserPos)
        {
            UpdateCompanion(anchorLocalUserPos, Time.deltaTime);
        }

        public void UpdateCompanion(Vector3 anchorLocalUserPos, float dt)
        {
            if (!isVisible || groundPoints.Length < 2 || totalRouteLength <= 0.001f)
            {
                if (doraemonInstance != null && doraemonInstance.activeSelf)
                {
                    doraemonInstance.SetActive(false);
                }
                return;
            }

            EnsureDoraemonModel();
            if (doraemonInstance != null && !doraemonInstance.activeSelf)
            {
                doraemonInstance.SetActive(true);
            }

            if (dt <= 0.00001f) dt = 0.016f;

            // 1. Track user horizontal movement speed in 2D (X, Z)
            Vector2 currUserPos2D = new Vector2(anchorLocalUserPos.x, anchorLocalUserPos.z);
            if (hasLastUserPos)
            {
                float distMoved = Vector2.Distance(currUserPos2D, lastUserPos2D);
                float instantSpeed = distMoved / dt;
                // Suppress small VPS pose jitter, then use hysteresis so speed noise cannot toggle wait/walk.
                float targetSpeed = instantSpeed >= UserSpeedNoiseFloorMps && instantSpeed < 4.0f
                    ? instantSpeed
                    : 0f;
                userSpeed = Mathf.Lerp(userSpeed, targetSpeed, dt * 8f);
            }
            lastUserPos2D = currUserPos2D;
            hasLastUserPos = true;

            if (!userIsMoving && userSpeed >= UserMoveStartMps)
                userIsMoving = true;
            else if (userIsMoving && userSpeed <= UserMoveStopMps)
                userIsMoving = false;

            // 2. Project user onto 2D horizontal route geometry
            userDistanceOnRoute = ProjectUserDistance2D(anchorLocalUserPos, out minDistanceToRoute);

            // Check if user has wandered off-route
            if (minDistanceToRoute > OffRouteRerouteThresholdM)
            {
                needsReroute = true;
            }

            // 3. Check if user reached destination
            if (userDistanceOnRoute >= totalRouteLength - 0.45f)
            {
                isAtDestination = true;
                isWaitingForUser = false;
                currentDistanceOnRoute = totalRouteLength;
                PlaceDoraemon(currentDistanceOnRoute, anchorLocalUserPos, CompanionState.Arrived, dt);
                return;
            }

            isAtDestination = false;

            // 4. Target distance along the route for Doraemon (always leading ahead)
            float targetDistance = Mathf.Clamp(userDistanceOnRoute + leadDistance, 0f, totalRouteLength);

            // 5. Determine if user is moving or stopped
            if (userIsMoving)
            {
                timeSinceStopped = 0f;
            }
            else
            {
                timeSinceStopped += dt;
            }

            bool isUserStationary = timeSinceStopped >= 0.4f;

            // 6. Doraemon advance & state decision
            float distanceToTarget = targetDistance - currentDistanceOnRoute;

            bool keepWaiting = isWaitingForUser && !userIsMoving && distanceToTarget <= WaitingReleaseLeadGapM;
            bool reachedLeadPosition = isUserStationary && distanceToTarget <= 0.15f;
            if (keepWaiting || reachedLeadPosition)
            {
                // User stopped and Doraemon reached lead position:
                // Doraemon stops and waits for the user!
                isWaitingForUser = true;
                PlaceDoraemon(currentDistanceOnRoute, anchorLocalUserPos, CompanionState.Waiting, dt);
            }
            else
            {
                // User is walking or Doraemon needs to advance ahead to lead position:
                // Doraemon walks ahead along the route!
                isWaitingForUser = false;

                // Adjust walking speed dynamically to keep smooth lead
                float speed = walkSpeed;
                if (userIsMoving && userSpeed > walkSpeed * 0.8f)
                {
                    speed = Mathf.Max(walkSpeed, userSpeed * 1.25f);
                }
                speed = Mathf.Clamp(speed, 0.9f, 2.2f);

                currentDistanceOnRoute = Mathf.MoveTowards(currentDistanceOnRoute, targetDistance, speed * dt);
                PlaceDoraemon(currentDistanceOnRoute, anchorLocalUserPos, CompanionState.Walking, dt);
            }
        }

        private enum CompanionState
        {
            Walking,
            Waiting,
            Arrived
        }

        private void PlaceDoraemon(float distance, Vector3 userPos, CompanionState state, float dt)
        {
            if (doraemonInstance == null) return;

            SamplePath(distance, out Vector3 position, out Vector3 tangent);

            // Ground contact offset: 0.005m higher than line so model feet cleanly sit on top of line
            position.y += 0.005f;

            Vector3 forward = new Vector3(tangent.x, 0f, tangent.z).normalized;
            if (forward.sqrMagnitude < 0.001f) forward = Vector3.forward;

            if (state == CompanionState.Waiting)
            {
                // Waiting: Stop, turn smoothly around to face the user, gentle breathing bob
                float idleBob = Mathf.Sin(Time.time * 2.5f) * 0.012f;
                position.y += idleBob;

                if (!hasWaitingFacingRotation)
                {
                    Vector3 toUser = userPos - position;
                    toUser.y = 0f;
                    waitingFacingRotation = toUser.sqrMagnitude > 0.04f
                        ? Quaternion.LookRotation(toUser.normalized, Vector3.up) * Quaternion.Euler(0f, modelYawOffset, 0f)
                        : Quaternion.LookRotation(forward, Vector3.up) * Quaternion.Euler(0f, modelYawOffset, 0f);
                    hasWaitingFacingRotation = true;
                }

                doraemonInstance.transform.localPosition = position;
                doraemonInstance.transform.localRotation = Quaternion.Slerp(
                    doraemonInstance.transform.localRotation, waitingFacingRotation, dt * 6f);
            }
            else if (state == CompanionState.Walking)
            {
                hasWaitingFacingRotation = false;
                // Walking: Face along the path, cheerful waddle tilt and step bobbing
                walkAnimTime += dt * 1.2f;
                float waddleTilt = Mathf.Sin(walkAnimTime * 8f) * 5.5f;
                float stepBob = Mathf.Abs(Mathf.Sin(walkAnimTime * 8f)) * 0.025f;
                position.y += stepBob;

                Quaternion pathRot = Quaternion.LookRotation(forward, Vector3.up) * Quaternion.Euler(0f, modelYawOffset, 0f);
                Quaternion waddledRot = pathRot * Quaternion.Euler(0f, 0f, waddleTilt);

                doraemonInstance.transform.localPosition = position;
                doraemonInstance.transform.localRotation = Quaternion.Slerp(
                    doraemonInstance.transform.localRotation, waddledRot, dt * 8f);
            }
            else // Arrived
            {
                hasWaitingFacingRotation = false;
                // Arrived at destination: Turn to user, celebratory happy hop
                float hopBob = Mathf.Abs(Mathf.Sin(Time.time * 6f)) * 0.04f;
                position.y += hopBob;

                Vector3 toUser = userPos - position;
                toUser.y = 0f;
                Quaternion targetRot = toUser.sqrMagnitude > 0.04f
                    ? Quaternion.LookRotation(toUser.normalized, Vector3.up) * Quaternion.Euler(0f, modelYawOffset, 0f)
                    : Quaternion.LookRotation(forward, Vector3.up) * Quaternion.Euler(0f, modelYawOffset, 0f);

                doraemonInstance.transform.localPosition = position;
                doraemonInstance.transform.localRotation = Quaternion.Slerp(
                    doraemonInstance.transform.localRotation, targetRot, dt * 6f);
            }
        }

        private void RebuildGroundPoints()
        {
            if (rawRoutePoints == null || rawRoutePoints.Length == 0)
            {
                groundPoints = Array.Empty<Vector3>();
                segmentLengths = Array.Empty<float>();
                totalRouteLength = 0f;
                return;
            }

            groundPoints = new Vector3[rawRoutePoints.Length];
            for (int i = 0; i < rawRoutePoints.Length; i++)
            {
                Vector3 p = rawRoutePoints[i];
                groundPoints[i] = new Vector3(p.x, p.y - groundDropMeters, p.z);
            }

            if (groundPoints.Length < 2)
            {
                segmentLengths = Array.Empty<float>();
                totalRouteLength = 0f;
                return;
            }

            segmentLengths = new float[groundPoints.Length - 1];
            totalRouteLength = 0f;
            for (int i = 0; i < segmentLengths.Length; i++)
            {
                float len = Vector3.Distance(groundPoints[i], groundPoints[i + 1]);
                segmentLengths[i] = len;
                totalRouteLength += len;
            }
        }

        public float ProjectPointDistance2D(Vector3 point, out float minDistanceToRoute)
        {
            return ProjectUserDistance2D(point, out minDistanceToRoute);
        }

        public float ProjectUserDistance2D(Vector3 userPos, out float minDistanceToRoute)
        {
            minDistanceToRoute = 0f;
            if (groundPoints.Length < 2 || totalRouteLength <= 0.001f) return 0f;

            Vector2 userPos2D = new Vector2(userPos.x, userPos.z);
            float closestDistanceSoFar = float.MaxValue;
            float bestDistanceAlongRoute = 0f;
            float accumulatedDistance = 0f;

            for (int i = 0; i < segmentLengths.Length; i++)
            {
                Vector2 a = new Vector2(groundPoints[i].x, groundPoints[i].z);
                Vector2 b = new Vector2(groundPoints[i + 1].x, groundPoints[i + 1].z);
                float segLen = segmentLengths[i];
                if (segLen <= 0.0001f) continue;

                Vector2 seg = b - a;
                float segLenSq = seg.sqrMagnitude;
                float t = segLenSq > 0.000001f
                    ? Mathf.Clamp01(Vector2.Dot(userPos2D - a, seg) / segLenSq)
                    : 0f;

                Vector2 proj = a + seg * t;
                float distToSeg = Vector2.Distance(userPos2D, proj);

                if (distToSeg < closestDistanceSoFar)
                {
                    closestDistanceSoFar = distToSeg;
                    bestDistanceAlongRoute = accumulatedDistance + t * segLen;
                }

                accumulatedDistance += segLen;
            }

            minDistanceToRoute = closestDistanceSoFar;
            return bestDistanceAlongRoute;
        }

        public void SamplePath(float distance, out Vector3 position, out Vector3 tangent)
        {
            if (groundPoints.Length == 0)
            {
                position = Vector3.zero;
                tangent = Vector3.forward;
                return;
            }

            if (groundPoints.Length == 1 || distance <= 0f)
            {
                position = groundPoints[0];
                tangent = groundPoints.Length > 1 ? (groundPoints[1] - groundPoints[0]).normalized : Vector3.forward;
                return;
            }

            if (distance >= totalRouteLength)
            {
                position = groundPoints[groundPoints.Length - 1];
                tangent = (groundPoints[groundPoints.Length - 1] - groundPoints[groundPoints.Length - 2]).normalized;
                return;
            }

            float running = 0f;
            for (int i = 0; i < segmentLengths.Length; i++)
            {
                float segLen = segmentLengths[i];
                if (distance <= running + segLen)
                {
                    float factor = segLen > 0.0001f ? (distance - running) / segLen : 0f;
                    position = Vector3.Lerp(groundPoints[i], groundPoints[i + 1], factor);
                    tangent = (groundPoints[i + 1] - groundPoints[i]).normalized;
                    if (tangent.sqrMagnitude < 0.001f) tangent = Vector3.forward;
                    return;
                }
                running += segLen;
            }

            position = groundPoints[groundPoints.Length - 1];
            tangent = Vector3.forward;
        }

        private void EnsureDoraemonModel()
        {
            if (doraemonInstance != null) return;

            var prefab = Resources.Load<GameObject>("navigation");
            if (prefab == null)
            {
                Debug.LogWarning("[CompanionGuide] Failed to load 'navigation' prefab from Resources.");
            }

            if (prefab != null)
            {
                doraemonInstance = Instantiate(prefab);
                doraemonInstance.name = "Navigation Companion";

                // Auto-scale to normalize companion height to ~0.42m (compact AR companion)
                Renderer[] rs = doraemonInstance.GetComponentsInChildren<Renderer>(true);
                if (rs.Length > 0)
                {
                    Bounds b = rs[0].bounds;
                    for (int i = 1; i < rs.Length; i++) b.Encapsulate(rs[i].bounds);
                    float currentHeight = b.size.y;
                    if (currentHeight > 0.05f)
                    {
                        float scaleFactor = 0.42f / currentHeight;
                        doraemonInstance.transform.localScale = Vector3.one * scaleFactor;
                    }
                    else
                    {
                        doraemonInstance.transform.localScale = Vector3.one * companionScale;
                    }
                }
                else
                {
                    doraemonInstance.transform.localScale = Vector3.one * companionScale;
                }

                baseModelScale = doraemonInstance.transform.localScale;
                ApplyModelScale();

                // Strip any imported camera or light from the 3D model asset to prevent overriding AR camera
                foreach (var cam in doraemonInstance.GetComponentsInChildren<Camera>(true))
                {
                    cam.enabled = false;
                    Destroy(cam);
                }
                foreach (var light in doraemonInstance.GetComponentsInChildren<Light>(true))
                {
                    Destroy(light);
                }
            }
            else
            {
                // Fallback procedural capsule if asset is missing
                doraemonInstance = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                doraemonInstance.name = "Companion (Fallback)";
                doraemonInstance.transform.localScale = new Vector3(0.3f, 0.5f, 0.3f);
                baseModelScale = doraemonInstance.transform.localScale;
                ApplyModelScale();
            }

            // Ensure dedicated soft directional light exists on companion in AR scene
            var existingLight = doraemonInstance.GetComponentInChildren<Light>(true);
            if (existingLight == null)
            {
                GameObject lightObj = new GameObject("Companion DirectLight");
                lightObj.transform.SetParent(doraemonInstance.transform, false);
                lightObj.transform.localPosition = new Vector3(0f, 1.5f, -1.0f);
                lightObj.transform.localRotation = Quaternion.Euler(45f, 20f, 0f);
                Light compLight = lightObj.AddComponent<Light>();
                compLight.type = LightType.Directional;
                compLight.color = new Color(1.0f, 0.98f, 0.95f, 1.0f);
                compLight.intensity = 0.85f;
            }

            if (trackedAnchor != null)
            {
                doraemonInstance.transform.SetParent(trackedAnchor, false);
            }
            else
            {
                doraemonInstance.transform.SetParent(transform, false);
            }

            ConfigureRenderersForDepthOcclusion();
            doraemonInstance.SetActive(false);
        }

        private void ConfigureRenderersForDepthOcclusion()
        {
            if (doraemonInstance == null) return;

            Renderer[] renderers = doraemonInstance.GetComponentsInChildren<Renderer>(true);
            float largestVolume = -1f;
            primaryRenderer = null;
            brightnessMaterials.Clear();

            foreach (Renderer r in renderers)
            {
                if (r == null) continue;
                Vector3 size = r.bounds.size;
                float vol = size.x * size.y * size.z;
                if (primaryRenderer == null || vol > largestVolume)
                {
                    largestVolume = vol;
                    primaryRenderer = r;
                }

                // Ensure world-space companion materials participate in hardware depth testing (ZTest LEqual, ZWrite On)
                // so real-world walls, pillars, chairs, and people accurately occlude the companion partially or fully.
                Material[] mats = Application.isPlaying ? r.materials : r.sharedMaterials;
                if (mats == null) continue;
                for (int i = 0; i < mats.Length; i++)
                {
                    Material mat = mats[i];
                    if (mat == null) continue;

                    // Fallback to Standard or Mobile shader if shader is missing/errored
                    if (mat.shader == null || mat.shader.name == "Hidden/InternalErrorShader")
                    {
                        Shader fallback = Shader.Find("Standard") ?? Shader.Find("Mobile/Diffuse") ?? Shader.Find("Diffuse");
                        if (fallback != null) mat.shader = fallback;
                    }

                    if (mat.HasProperty("_ZWrite")) mat.SetInt("_ZWrite", 1);
                    if (mat.HasProperty("_ZTest")) mat.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.LessEqual);
                    if (mat.renderQueue < 2000 || mat.renderQueue >= 3000)
                    {
                        mat.renderQueue = 2000;
                    }

                    if (Application.isPlaying) CaptureBrightnessState(mat);
                }
            }

            if (Application.isPlaying) ApplyModelBrightness();
        }

        private static readonly int BaseColorPropertyId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorPropertyId = Shader.PropertyToID("_Color");
        private static readonly int EmissionColorPropertyId = Shader.PropertyToID("_EmissionColor");

        private void CaptureBrightnessState(Material material)
        {
            if (material == null) return;

            bool hasBaseColor = material.HasProperty(BaseColorPropertyId) || material.HasProperty(ColorPropertyId);
            Color baseColor = material.HasProperty(BaseColorPropertyId)
                ? material.GetColor(BaseColorPropertyId)
                : (material.HasProperty(ColorPropertyId) ? material.GetColor(ColorPropertyId) : Color.white);
            bool hasEmissionColor = material.HasProperty(EmissionColorPropertyId);

            brightnessMaterials.Add(new MaterialBrightnessState
            {
                Material = material,
                BaseColor = baseColor,
                HasBaseColor = hasBaseColor,
                EmissionColor = hasEmissionColor ? material.GetColor(EmissionColorPropertyId) : Color.black,
                HadEmissionKeyword = material.IsKeywordEnabled("_EMISSION")
            });
        }

        private void ApplyModelScale()
        {
            if (doraemonInstance != null)
            {
                doraemonInstance.transform.localScale = baseModelScale * modelScaleMultiplier;
            }
        }

        private void ApplyModelBrightness()
        {
            float effectiveBrightness = Mathf.Max(0.20f, modelBrightness);
            for (int i = 0; i < brightnessMaterials.Count; i++)
            {
                MaterialBrightnessState state = brightnessMaterials[i];
                Material material = state.Material;
                if (material == null) continue;

                if (material.HasProperty(EmissionColorPropertyId))
                {
                    material.EnableKeyword("_EMISSION");
                    Color tint = state.HasBaseColor ? state.BaseColor : Color.white;
                    material.SetColor(EmissionColorPropertyId, state.EmissionColor + tint * effectiveBrightness);
                }
                else if (state.HasBaseColor)
                {
                    int propertyId = material.HasProperty(BaseColorPropertyId) ? BaseColorPropertyId : ColorPropertyId;
                    float lift = 1f + effectiveBrightness * 0.25f;
                    Color color = state.BaseColor * lift;
                    color.a = state.BaseColor.a;
                    material.SetColor(propertyId, color);
                }
            }
        }

        private void OnDestroy()
        {
            primaryRenderer = null;
            brightnessMaterials.Clear();
            if (doraemonInstance != null)
            {
                Destroy(doraemonInstance);
                doraemonInstance = null;
            }
        }
    }
}
