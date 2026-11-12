using NUnit.Framework;
using Vps.UnityClient.Navigation;

namespace Vps.UnityClient.Tests.PlayMode
{
    public sealed class SiteGraphV2NavigationGatePlayModeTests
    {
        [Test]
        public void TrackingLossOrBindingMismatchHidesTheActiveRoute()
        {
            Assert.That(SiteGraphV2NavigationGate.CanRender(true, true, true), Is.True);
            Assert.That(SiteGraphV2NavigationGate.CanRender(false, true, true), Is.False);
            Assert.That(SiteGraphV2NavigationGate.CanRender(true, false, true), Is.False);
            Assert.That(SiteGraphV2NavigationGate.CanRender(true, true, false), Is.False);
        }
    }
}
