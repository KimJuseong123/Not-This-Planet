using Mirror;

namespace Metagame.AIExperiment
{
    public sealed class CompanionTransform : NetworkTransformReliable
    {
        // Vendored Mirror's ResetState reads target during AddComponent in the Editor.
        public override void Reset()
        {
            target = transform;
            base.Reset();
            syncDirection = SyncDirection.ServerToClient;
        }
    }
}
