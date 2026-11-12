using System;
using NUnit.Framework;
using UnityEngine;

namespace Vps.UnityClient.Tests.EditMode
{
    public sealed class DoraemonAssetTests
    {
        [Test]
        public void CompanionModelLoadsFromResources()
        {
            var prefab = Resources.Load<GameObject>("navigation");
            Assert.That(prefab, Is.Not.Null, "navigation prefab should load from Resources");
            var instance = UnityEngine.Object.Instantiate(prefab);
            try
            {
                var renderers = instance.GetComponentsInChildren<Renderer>();
                Assert.That(renderers.Length, Is.GreaterThan(0), "Companion should have renderers");
                var anim = instance.GetComponent<Animation>();
                Debug.Log($"[Companion Test] Has Animation: {anim != null}, clip count: {(anim != null ? anim.GetClipCount() : 0)}");
                if (anim != null)
                {
                    foreach (AnimationState state in anim)
                    {
                        Debug.Log($"[Companion Test] Clip: {state.name}, length: {state.length}");
                    }
                }
                Bounds b = renderers[0].bounds;
                for (int i = 1; i < renderers.Length; i++) b.Encapsulate(renderers[i].bounds);
                Debug.Log($"[Companion Test] Total Bounds center: {b.center}, size: {b.size}, min: {b.min}, max: {b.max}");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(instance);
            }
        }
    }
}
