using System;
using NUnit.Framework;
using UnityEngine;
using Vps.UnityClient.Navigation;

namespace Vps.UnityClient.Tests.EditMode
{
    public sealed class DoraemonCompanionGuideTests
    {
        [Test]
        public void GroundPointsApplyGroundDropOffset()
        {
            var go = new GameObject("TestCompanion");
            try
            {
                var guide = go.AddComponent<DoraemonCompanionGuide>();
                guide.GroundDropMeters = 0.08f;

                Vector3[] input = new[]
                {
                    new Vector3(1f, -1.02f, 2f),
                    new Vector3(1f, -1.02f, 6f)
                };

                guide.SetRoute(input);

                Vector3[] ground = guide.GroundPoints;
                Assert.That(ground.Length, Is.EqualTo(2));
                Assert.That(ground[0].y, Is.EqualTo(-1.10f).Within(0.001f));
                Assert.That(ground[1].y, Is.EqualTo(-1.10f).Within(0.001f));
                Assert.That(guide.TotalRouteLength, Is.EqualTo(4.0f).Within(0.001f));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void ProjectUserDistance2DProjectsHorizontalCoordinatesIgnoringCameraHeight()
        {
            var go = new GameObject("TestCompanion");
            try
            {
                var guide = go.AddComponent<DoraemonCompanionGuide>();
                guide.GroundDropMeters = 0.08f;

                Vector3[] input = new[]
                {
                    new Vector3(0f, -1.02f, 0f),
                    new Vector3(0f, -1.02f, 10f)
                };

                guide.SetRoute(input);

                // User camera is at height y = +0.5m, positioned at z = 3.5m
                Vector3 cameraPos = new Vector3(0f, 0.5f, 3.5f);
                float dist = guide.ProjectUserDistance2D(cameraPos, out float minDistanceToRoute);

                Assert.That(dist, Is.EqualTo(3.5f).Within(0.01f));
                Assert.That(minDistanceToRoute, Is.EqualTo(0f).Within(0.01f));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void OffRouteSetsNeedsReroute()
        {
            var go = new GameObject("TestCompanion");
            try
            {
                var guide = go.AddComponent<DoraemonCompanionGuide>();
                Vector3[] input = new[]
                {
                    new Vector3(0f, -1.0f, 0f),
                    new Vector3(0f, -1.0f, 10f)
                };

                guide.SetRoute(input);
                guide.SetVisible(true);

                // User wanders 4 meters off to the side (x = 4.0m)
                guide.UpdateCompanion(new Vector3(4.0f, 0.5f, 5.0f), 0.1f);

                Assert.That(guide.NeedsReroute, Is.True, "NeedsReroute should be true when off-route > 2.5m");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void DoraemonWaitsWhenUserIsStationaryAndResumesWhenUserWalks()
        {
            var go = new GameObject("TestCompanion");
            try
            {
                var guide = go.AddComponent<DoraemonCompanionGuide>();
                guide.LeadDistance = 1.4f;
                Vector3[] input = new[]
                {
                    new Vector3(0f, -1.0f, 0f),
                    new Vector3(0f, -1.0f, 20f)
                };

                guide.SetRoute(input);
                guide.SetVisible(true);

                // Simulate user standing still at z = 2.0m for 3.5 seconds (35 frames of 0.1s)
                Vector3 stationaryUser = new Vector3(0f, 0.5f, 2.0f);
                for (int i = 0; i < 35; i++)
                {
                    guide.UpdateCompanion(stationaryUser, 0.1f);
                }

                // Doraemon should advance to lead distance (2.0 + 1.4 = 3.4m) and wait
                Assert.That(guide.CurrentDistanceOnRoute, Is.EqualTo(3.4f).Within(0.15f));
                Assert.That(guide.IsWaitingForUser, Is.True, "Doraemon should be waiting for stationary user");
                Assert.That(guide.CompanionStatusMessage, Does.Contain("đứng đợi bạn"));

                // Now simulate user walking forward from z = 2.0m to z = 5.0m (speed ~ 1.0 m/s)
                for (int i = 1; i <= 30; i++)
                {
                    Vector3 walkingUser = new Vector3(0f, 0.5f, 2.0f + i * 0.1f);
                    guide.UpdateCompanion(walkingUser, 0.1f);
                }

                // Doraemon should have resumed walking ahead
                Assert.That(guide.IsWaitingForUser, Is.False, "Doraemon should be walking ahead while user is moving");
                Assert.That(guide.CompanionStatusMessage, Does.Contain("Đang dẫn đường"));
                Assert.That(guide.CurrentDistanceOnRoute, Is.GreaterThan(5.0f), "Doraemon should stay ahead of user");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void DoraemonReachesDestinationWhenUserArrives()
        {
            var go = new GameObject("TestCompanion");
            try
            {
                var guide = go.AddComponent<DoraemonCompanionGuide>();
                Vector3[] input = new[]
                {
                    new Vector3(0f, -1.0f, 0f),
                    new Vector3(0f, -1.0f, 5.0f)
                };

                guide.SetRoute(input);
                guide.SetVisible(true);

                // User reaches near end of route (z = 4.8m on a 5.0m route)
                guide.UpdateCompanion(new Vector3(0f, 0.5f, 4.8f), 0.1f);

                Assert.That(guide.IsAtDestination, Is.True, "Doraemon should flag reached destination");
                Assert.That(guide.CompanionStatusMessage, Does.Contain("Đã tới đích"));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void DoraemonMaintainsTwoMeterLeadAndLocksAtDestinationWithoutSliding()
        {
            var go = new GameObject("TestCompanionTwoMeterLock");
            try
            {
                var guide = go.AddComponent<DoraemonCompanionGuide>();
                Assert.That(DoraemonCompanionGuide.DefaultLeadDistance, Is.EqualTo(2.0f).Within(0.001f));
                Assert.That(guide.LeadDistance, Is.EqualTo(2.0f).Within(0.001f));

                Vector3[] input = new[]
                {
                    new Vector3(0f, -1.0f, 0f),
                    new Vector3(0f, -1.0f, 10.0f)
                };

                guide.SetRoute(input);
                guide.SetVisible(true);

                // User arrives 2.0m before destination (z = 8.0m) -> robot stops cleanly at exact destination (0, *, 10.0)
                guide.UpdateCompanion(new Vector3(0f, 0.5f, 8.0f), 0.1f);
                Assert.That(guide.IsAtDestination, Is.True);
                Vector3 arrivedPos = guide.CompanionInstance.transform.localPosition;
                Assert.That(arrivedPos.x, Is.EqualTo(0f).Within(0.01f));
                Assert.That(arrivedPos.z, Is.EqualTo(10.0f).Within(0.01f));

                // Even if SetRoute receives a shortened/re-projected stub or user moves within 3.5m, robot must not slide
                guide.SetRoute(new[]
                {
                    new Vector3(0.4f, -1.0f, 9.8f),
                    new Vector3(0f, -1.0f, 10.0f)
                });
                guide.UpdateCompanion(new Vector3(1.2f, 0.5f, 11.0f), 0.1f);
                guide.UpdateCompanion(new Vector3(-1.0f, 0.5f, 8.5f), 0.1f);

                Assert.That(guide.IsAtDestination, Is.True);
                Vector3 lockedPos = guide.CompanionInstance.transform.localPosition;
                Assert.That(lockedPos.x, Is.EqualTo(0f).Within(0.01f));
                Assert.That(lockedPos.z, Is.EqualTo(10.0f).Within(0.01f));

                // Walking > 3.5m away unlocks destination state so guidance can resume
                guide.UpdateCompanion(new Vector3(0f, 0.5f, 5.0f), 0.1f);
                Assert.That(guide.IsAtDestination, Is.False);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }
    }
}
