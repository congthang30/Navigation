# Unity client — Site Graph V2

This dedicated Unity project implements Site selection, VPS2 anchor tracking, mobile route recording, and AR navigation from the published V2 graph. The only runtime coordinate frame is metres relative to the bound Site anchor. It has no floor-plan image, pixel graph, calibration, or V1 manifest fallback.

The project pins Unity Editor `6000.0.74f1`, Niantic Spatial SDK `4.1.0-26051913`, and the resolved AR Foundation/ARCore/ARKit packages in `Packages/manifest.json` and `Packages/packages-lock.json`. The route recorder uses the installed NSDK Sites/asset APIs, `ARVps2Manager.TryTrackAnchor`, `ARVps2Anchor.trackingState`, and Site mesh API. Site mesh preview and route traces are captured in the same tracked anchor-local frame. A tracking interruption ends a segment; it never creates a connecting edge.

Route capture is not publication. Unity submits captures and marked POIs to the authenticated mobile API; an Admin reviews the generated graph on the private Site mesh, approves the exact revision, and explicitly publishes it. Unity navigation reads only the active published V2 manifest, verifies Site/asset identity, computes a metric path, and hides the route when tracking is not stable.

Local EditMode and PlayMode results are recorded in `docs/implementation-plan/TASK_CHECKLIST.md`. Tests skipped for real credentials, service access or physical devices remain external evidence gates.
