using Ashla.SDK;

namespace Ashla.Host;

internal sealed class ExternalProvider : IGameState
{
    public GameCapabilities Capabilities => GameCapabilities.CombatLog;

    public bool TryGetLocalPlayer(out PlayerSnapshot? player)
    {
        player = null;
        return false;
    }

    public bool TryGetCurrentTarget(out TargetSnapshot? target)
    {
        target = null;
        return false;
    }
}
