using UnityEngine;

namespace Vps.UnityClient.Vps
{
    /// <summary>Allows only one app workflow to own the shared VPS2 anchor at a time.</summary>
    public static class VpsWorkflowCoordinator
    {
        private static MonoBehaviour owner;

        public static bool TryAcquire(MonoBehaviour requester)
        {
            if (requester == null) return false;
            if (owner == null || owner == requester)
            {
                owner = requester;
                return true;
            }
            return false;
        }

        public static void Release(MonoBehaviour requester)
        {
            if (owner == requester) owner = null;
        }

        public static bool IsOwnedByOther(MonoBehaviour requester)
        {
            return owner != null && owner != requester;
        }
    }
}
