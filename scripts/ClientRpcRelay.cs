using Godot;

public partial class ClientRpcRelay : Node
{
    [Rpc(MultiplayerApi.RpcMode.AnyPeer)] public void OnAuthResult(bool success, int assignedTeam) { }
    [Rpc(MultiplayerApi.RpcMode.AnyPeer)] public void SetPlayerTeam(int teamId) { }
    [Rpc(MultiplayerApi.RpcMode.AnyPeer)] public void AssignTeam(int teamId) { }
    [Rpc(MultiplayerApi.RpcMode.AnyPeer)] public void SyncTurn(int currentTurnId) { }
    [Rpc(MultiplayerApi.RpcMode.AnyPeer)] public void UpdateCastleHealth(string side, int health) { }
    [Rpc(MultiplayerApi.RpcMode.AnyPeer)] public void SyncRune(Vector2 pos, string name, int runeIdx) { }
    [Rpc(MultiplayerApi.RpcMode.AnyPeer)] public void RemoveRune(string runeName) { }
    [Rpc(MultiplayerApi.RpcMode.AnyPeer)] public void UpdateHealth(int newHealth) { }
    [Rpc(MultiplayerApi.RpcMode.AnyPeer)] public void SyncShieldAbsorbed(string ballName, int absorbedAmount) { }
    [Rpc(MultiplayerApi.RpcMode.AnyPeer)] public void SyncSpawnBall(string name, Vector2 pos, int textureIndex, int teamId, int health, int damage, string abilityJson) { }
    [Rpc(MultiplayerApi.RpcMode.AnyPeer)] public void SyncBallStats(string ballName, int health) { }
    [Rpc(MultiplayerApi.RpcMode.AnyPeer)] public void SyncBallStatsWithShield(string ballName, int health, int shield) { }
    [Rpc(MultiplayerApi.RpcMode.AnyPeer)] public void RemoveBall(string ballName) { }
    [Rpc(MultiplayerApi.RpcMode.AnyPeer)] public void UpdateBallPosition(string ballName, Vector2 newPos, float newRot) { }
    [Rpc(MultiplayerApi.RpcMode.AnyPeer)] public void SyncAbilityActivated(string ballName, string abilityType, int value) { }
    [Rpc(MultiplayerApi.RpcMode.AnyPeer)] public void SyncInvulnerable(string ballName) { }
    [Rpc(MultiplayerApi.RpcMode.AnyPeer)] public void SyncShieldUpdated(string ballName, int absorbedAmount, int newShield) { }
    [Rpc(MultiplayerApi.RpcMode.AnyPeer)] public void SyncBallDamage(string ballName, int damage) { }
    [Rpc(MultiplayerApi.RpcMode.AnyPeer)] public void SyncDiceEffect(string ballName, int effectType, int value) { }
    [Rpc(MultiplayerApi.RpcMode.AnyPeer)] public void PlayBallExplosion(string ballName) { }
    [Rpc(MultiplayerApi.RpcMode.AnyPeer)] public void OnGameOver(int winnerTeamId) { }
    [Rpc(MultiplayerApi.RpcMode.AnyPeer)] public void AuthenticateOnServer(string token, int teamFromClient) { }
    [Rpc(MultiplayerApi.RpcMode.AnyPeer)] public void RequestSpawnBall(Vector2 pos, Vector2 force, int textureIndex, int teamId, int health, int damage, string abilityJson) { }
    [Rpc(MultiplayerApi.RpcMode.AnyPeer)] public void RequestHitBall(string ballName, Vector2 force) { }
}
