# Unity client — Site Graph V2

This dedicated Unity project implements Site selection, VPS2 anchor tracking, mobile route recording, and AR navigation from the published V2 graph. The only runtime coordinate frame is metres relative to the bound Site anchor. It has no floor-plan image, pixel graph, calibration, or V1 manifest fallback.

The project pins Unity Editor `6000.0.74f1`, Niantic Spatial SDK `4.1.0-26051913`, and the resolved AR Foundation/ARCore/ARKit packages in `Packages/manifest.json` and `Packages/packages-lock.json`. The route recorder uses the installed NSDK Sites/asset APIs, `ARVps2Manager.TryTrackAnchor`, `ARVps2Anchor.trackingState`, and Site mesh API. Site mesh preview and route traces are captured in the same tracked anchor-local frame. A tracking interruption ends a segment; it never creates a connecting edge.

Route capture is not publication. Unity submits captures and marked POIs to the authenticated mobile API; an Admin reviews the generated graph on the private Site mesh, approves the exact revision, and explicitly publishes it. Unity navigation reads only the active published V2 manifest, verifies Site/asset identity, computes a metric path, and hides the route when tracking is not stable.

Local EditMode and PlayMode results are recorded in `docs/implementation-plan/TASK_CHECKLIST.md`. Tests skipped for real credentials, service access or physical devices remain external evidence gates.

---

## AR depth occlusion

### Runtime stack

| Layer | Component | Notes |
|---|---|---|
| Depth source | `NsdkOcclusionSubsystem` (NSDK ML depth) | Enabled by `_useNsdkDepth: 1` in `Assets/XR/Settings/NSDK Settings.asset`. Not ARCore hardware depth. |
| Depth rendering | `NsdkOcclusionExtension` → `Nsdk/ZBufferOcclusion` | Writes the measured depth into the camera z-buffer. No depth bias is applied by the SDK. |
| Depth warp | `XRDisplayContext.OccludeeEyeDepth` | Back projection plane used to reproject the depth image onto the current camera pose. |
| Stabilization | `ARMeshManager` + `NsdkMeshingExtension` → `FEATURE_STABILIZATION` | Mesh blended depth. **Not supported on Vulkan** (`OcclusionComponentDeployment.cs`: "Occlusion stabilization is not yet supported on Vulkan devices."). |
| Suppression | `ARSceneSegmentationManager` → `FEATURE_SUPPRESSION` | Optional, opt-in, device dependent. |

Android graphics APIs are `[OpenGLES3, Vulkan]` (`ProjectSettings/ProjectSettings.asset` → `m_BuildTargetGraphicsAPIs`), so the device runs **OpenGLES3** and the Vulkan limitation does not apply.

### Quality tiers

| Mode | Depth mode | Depth rate | Stabilization | Notes |
|---|---|---|---|---|
| `Off` | – | – | – | Everything disabled. |
| `Fallback` | `EnvironmentDepthMode.Fastest` | – | – | AR Foundation only, no NSDK post-processing. |
| `Balanced` | `EnvironmentDepthMode.Medium` | 20 Hz | no | Default. 20 Hz is the NSDK maximum recommended rate (`NsdkOcclusionSubsystem.MaxRecommendedFrameRate`). |
| `HighQuality` | `EnvironmentDepthMode.Best` | 20 Hz | yes | Adds mesh fused stabilization. |

The effective mode is re-resolved continuously (every 0.5 s). When it differs from the requested mode the reason is reported in `ArOcclusionController.FallbackReason` and shown in the settings panel, so a downgrade is never silent.

### Depth priority for virtual content

The NSDK writes the measured depth into the z-buffer without any bias, so virtual content that sits on a real surface competes with depth noise (a few centimetres on floors).

* **Route ribbon** (`Assets/Shaders/ARRouteLine.shader`) — the bias is applied along the view axis only (`viewPos.z -= _DepthBiasMeters`, default **3 cm**). It must not scale the whole view vector: that also moves the vertices laterally and distorts the route. The ribbon does not write depth.
* **Companion** (`Assets/Shaders/ARCompanionOcclusion.shader`) — the built-in Standard shader exposes no depth offset property, so the companion uses a dedicated shader with a **contact only** bias (default 2 cm over the bottom 6 cm of the model). The feet win against floor noise, the body keeps exact occlusion against walls and furniture.
* **Ground placement** — the navigation prefab pivot sits on the mesh base, so the companion is only sunk **1.5 cm** below the route plane (`DoraemonCompanionGuide.DefaultGroundDrop`). The ribbon is drawn on the raw route plane.

### Occlusion reference distance

`OcclusionDistanceStabilizer` replaces the NSDK default for `XRDisplayContext.OccludeeEyeDepth`. The SDK uses the **closest** depth sample of the field of view (`OcclusionExtensionUtils.SampleImageSubregion` keeps the minimum), which collapses towards the camera as soon as anything close enters the frame and misaligns the whole depth buffer. The app instead uses a median over a sparse grid plus temporal smoothing with hysteresis. It can be toggled at runtime ("Ổn định khoảng cách che khuất") for A/B testing on device.

### Diagnostics

* Settings panel → "DEPTH OCCLUSION": graphics API, requested/effective mode, depth mode, configured and estimated depth rate, depth resolution, reference plane, distance mode, mesh fusion count, fallback reason.
* `Assets/Editor/VpsOcclusionAudit.cs` — batch mode audit of the graphics APIs, render pipeline, shader availability, model pivot and AR subsystem descriptors.

### On-device validation checklist

1. Companion standing on the floor — feet must not be clipped.
2. Companion behind a wall / a table — must be occluded.
3. Companion close and far from the camera.
4. A real object entering the frame close to the camera — occlusion must not jump.
5. Fast camera rotation and lateral movement — occlusion must stay aligned.
6. Route line across a wall base, a doorway and a low step — must stay on the floor and not pass through obstacles.
7. Tracking loss and recovery.
8. Switching Balanced / HighQuality repeatedly.
