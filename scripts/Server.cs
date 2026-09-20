using Godot;
using System;
using System.Collections.Generic;
using System.Text.Json;

public partial class Server : Node
{
    private ENetMultiplayerPeer _peer = new ENetMultiplayerPeer();
    private Node2D _tableInstance;
    private List<long> _playerIds = new List<long>();
    private Dictionary<long, int> _authenticatedPlayers = new Dictionary<long, int>(); // PeerID -> TeamID
    private Dictionary<long, int> _pendingAuthentications = new Dictionary<long, int>();
    private int _currentTurnPlayerId = 0;
    private bool _isGameOver = false; // Oyunun bittiğini takip etmek için

    // KRİTİK: Vuruşu başlatan topun adını burada tutuyoruz
    private string _currentAttackerBallName = "";

    // GÜVENLIK: Per-turn vuruş hakkı ve network tick rate
    private Dictionary<long, bool> _hasShotThisTurn = new Dictionary<long, bool>();
    private float _positionUpdateTimer = 0f;
    private const float POSITION_UPDATE_INTERVAL = 1f / 20f; // 20 Hz (50ms)

    // KALE CANLARI VE COOLDOWN
    private int _radiantHealth = 100;
    private int _direHealth = 100;
    private List<string> _castleHitCooldown = new List<string>();

    // Bu turda zaten yetenek kullanmış olan toplar
    private HashSet<string> _abilityUsedThisTurn = new HashSet<string>();
    private bool _serverStarted;

    private Dictionary<int, string> runeMap = new Dictionary<int, string> {
        {0, "gold"},
        {1, "health"},
        {2, "lighting"},
        {3, "trap"},
        {4, "water"},
        {5, "banana"},
        {6, "dice"},
        {7, "key"}
    };

    [Rpc(MultiplayerApi.RpcMode.AnyPeer)]
    public void AuthenticateOnServer(string token, int teamFromClient)
    {
        long senderId = Multiplayer.GetRemoteSenderId();
        if (string.IsNullOrEmpty(token)) return;

        if (GlobalData.Instance.ActiveMatch == null)
        {
            _pendingAuthentications[senderId] = teamFromClient;
            GD.Print($"[SERVER-AUTH] Peer {senderId} match verisini bekliyor.");
            return;
        }

        AuthenticatePeer(senderId, teamFromClient);
    }

    public void ProcessPendingAuthentications()
    {
        if (GlobalData.Instance.ActiveMatch == null) return;

        foreach (var pending in _pendingAuthentications)
            AuthenticatePeer(pending.Key, pending.Value);

        _pendingAuthentications.Clear();
    }

    private void AuthenticatePeer(long peerId, int teamId)
    {
        if (teamId < 0 || teamId > 1)
        {
            GD.PrintErr($"[SERVER-AUTH] Gecersiz takim: {teamId}");
            return;
        }

        if (!_authenticatedPlayers.ContainsKey(peerId))
        {
            _authenticatedPlayers[peerId] = teamId;
            _hasShotThisTurn[peerId] = false;
            GD.Print($"[SERVER-AUTH] Peer {peerId} takım {teamId} olarak doğrulandı.");

            var clientNodeAuth = GetParent().GetNodeOrNull<Node>("Client");
            if (clientNodeAuth != null)
            {
                clientNodeAuth.RpcId(peerId, "SetPlayerTeam", teamId);
                clientNodeAuth.RpcId(peerId, "SyncTurn", _currentTurnPlayerId);
            }
        }
    }

    private int ValidateTokenWithBackend(string token)
    {
        // TODO: HttpRequest ile Java backend'e token doğrulaması yap
        // Backend şu şekilde yanıt vermeli: { "teamId": 0 veya 1, "userId": "...", "valid": true }
        // Şimdilik: Oyunun başında toplar zaten yaratıldığından, bunu mock'layabiliriz.
        // Gerçek implementasyon: GlobalData.Instance.ActiveMatch.myTeam kullan
        
        if (GlobalData.Instance.ActiveMatch != null)
        {
            int teamId = GlobalData.Instance.ActiveMatch.myTeam;
            GD.Print($"[SERVER-AUTH] Mock: TeamID={teamId} (GlobalData'dan alındı)");
            return teamId;
        }
        
        GD.PrintErr("[SERVER-AUTH] ActiveMatch verisi yok!");
        return -1;
    }

    public override void _Ready()
    {
        if (!RuntimeMode.IsServer) return;
        _tableInstance = GetParent().GetNodeOrNull<Node2D>("Table");

        if (RuntimeMode.TryGetServerPort(out int port))
        {
            StartServer(port);
        }
        else
        {
            GD.Print("[SERVER] Port check-session cevabi bekleniyor.");
        }
    }

    public bool StartServer(int port)
    {
        if (_serverStarted) return true;
        if (port <= 0 || port > 65535)
        {
            GD.PrintErr($"[SERVER] Gecersiz port: {port}");
            return false;
        }

        var error = _peer.CreateServer(port, 10);
        if (error == Error.Ok)
        {
            _serverStarted = true;
            Multiplayer.MultiplayerPeer = _peer;
            GD.Print($"[SERVER] Port {port} hazir ve dinlemede.");
            Multiplayer.PeerConnected += OnPeerConnected;

            // Kale Alanlarını Dinle
            if (_tableInstance != null)
            {
                var area2D = _tableInstance.GetNodeOrNull<Area2D>("Area2D");
                if (area2D != null)
                {
                    area2D.BodyEntered += OnCastleBodyEntered;
                }
            }
        }
        else
        {
            GD.PrintErr("[SERVER] Port acilamadi! Hata kodu: " + error);
        }

        return error == Error.Ok;
    }

    private void OnPeerConnected(long id)
    {
        if (!_playerIds.Contains(id)) _playerIds.Add(id);

        // DÜZELTME: assignedTeam atamasını buradan sildik çünkü AuthenticateOnServer içinde Java'dan alıyoruz.

        var clientNodePeer = GetParent().GetNode("Client");
        if (clientNodePeer != null)
        {
            // Yeni oyuncuya sadece sırayı ve kale canlarını bildiriyoruz (Takım bilgisini Auth metodunda bildireceğiz)
            clientNodePeer.RpcId(id, "SyncTurn", _currentTurnPlayerId);

            // Mevcut kale canlarını yeni gelene bildir
            clientNodePeer.RpcId(id, "UpdateCastleHealth", "Radiant", _radiantHealth);
            clientNodePeer.RpcId(id, "UpdateCastleHealth", "Dire", _direHealth);

            // Mevcut rünleri yeni bağlanana bildir - YENİ EKLEME
            foreach (Node node in GetTree().GetNodesInGroup("runes"))
            {
                if (node is Area2D rune)
                {
                    int rIdx = rune.HasMeta("rune_idx") ? (int)rune.GetMeta("rune_idx") : 0;
                    clientNodePeer.RpcId(id, "SyncRune", rune.GlobalPosition, rune.Name, rIdx);
                }
            }
        }
        foreach (Node node in GetTree().GetNodesInGroup("balls"))
        {
            if (node is RigidBody2D ball)
            {
                int tIdx = (int)ball.GetMeta("texture_idx");
                int team = (int)ball.GetMeta("team_id");
                int hp = (int)ball.GetMeta("health");
                int dmg = (int)ball.GetMeta("damage");
                string abilityJson = ball.HasMeta("ability_json") ? (string)ball.GetMeta("ability_json") : "{}";
                clientNodePeer.RpcId(id, "SyncSpawnBall", ball.Name, ball.Position, tIdx, team, hp, dmg, abilityJson);
            }
        }
    }

    // KRİTİK: Checksum hatasını önlemek ve rünleri senkronize etmek için gereken metotlar
    [Rpc(MultiplayerApi.RpcMode.Authority)]
    public void SyncRune(Vector2 pos, string name, int runeIdx) { }



    public void SpawnRuneAtPosition(Vector2 pos, int runeIdx)
    {
        var mainNode = GetParent<Main>();
        if (mainNode.runeScene == null) return;

        var rune = mainNode.runeScene.Instantiate<Area2D>();
        rune.GlobalPosition = pos;

        // Benzersiz bir isim veriyoruz
        string runeName = "Rune_" + Guid.NewGuid().ToString().Substring(0, 8);
        rune.Name = runeName;
        rune.AddToGroup("runes");

        // SUNUCUDA ÇARPIŞMAYI AKTİF ET
        rune.Monitoring = true;

        // KRİTİK: İsim "rune_idx" olarak belirlendi
        rune.SetMeta("rune_idx", runeIdx);

        AddChild(rune);

        // Sinyal bağlantısı
        rune.BodyEntered += (body) => OnRuneBodyEntered(body, rune);

        // O an oyunda olan herkese rünü bildir
                var clientNodeRune = GetParent().GetNodeOrNull<Node>("Client");
        if (clientNodeRune != null)
        {
            clientNodeRune.Rpc("SyncRune", pos, runeName, runeIdx);
        }
    }

    private void OnRuneBodyEntered(Node body, Area2D rune)
    {
        if (body is RigidBody2D ball && ball.IsInGroup("balls"))
        {
            // 1. Rünün üzerindeki meta veriyi al (0, 1, 2 vb.)
            int runeIdx = (int)rune.GetMeta("rune_idx", -1);
            var clientNodeRuneEffect = GetParent().GetNodeOrNull<Node>("Client");

            // 2. Senin oluşturduğun runeMap'e sor: "Bu sayı ne anlama geliyor?"
            if (runeMap.TryGetValue(runeIdx, out string runeTypeString))
            {
                // Artik runeTypeString "gold", "health", "lighting" gibi bir kelime.
                GD.Print($"[SERVER] {ball.Name} rün aldı: {runeTypeString}");

                switch (runeTypeString)
                {
                    case "health":
                        // CAN VERME MANTIĞI (Eski kodundaki 0'ın yaptığı işi artık bu yapıyor)
                        var rng = new RandomNumberGenerator();
                        rng.Randomize();
                        int healAmount = rng.RandiRange(5, 10);
                        int newHealth = (int)ball.GetMeta("health", 0) + healAmount;
                        ball.SetMeta("health", newHealth);
                        clientNodeRuneEffect?.Rpc("SyncBallStats", ball.Name, newHealth);
                        break;

                    case "gold":
                        // Burada Java backend'e gold ekleme isteği atılabilir veya maç sonu bonusu olarak işaretlenebilir
                        GD.Print("[SERVER] Altın rünü toplandı! (Maç sonu işlenecek)");
                        break;

                    case "lighting":
                        GD.Print($"[SERVER] {ball.Name} yıldırım hızı kazandı!");
                        // Topun mevcut hızını anlık olarak 2 katına çıkar
                        ball.ApplyCentralImpulse(ball.LinearVelocity * 1.2f);
                        break;

                    case "trap":
                        GD.Print($"[SERVER] {ball.Name} tuzağa bastı! Hasar alıyor.");
                        int trapDamage = 5;
                        int healthAfterTrap = (int)ball.GetMeta("health", 0) - trapDamage;
                        ball.SetMeta("health", healthAfterTrap);
                        clientNodeRuneEffect?.Rpc("SyncBallStats", ball.Name, healthAfterTrap);

                        if (healthAfterTrap <= 0)
                        {
                            ball.QueueFree();
                            clientNodeRuneEffect?.Rpc("RemoveBall", ball.Name);
                        }
                        break;

                    case "dice":
                        int roll = new Random().Next(1, 7);
                        int currentDmg = ball.HasMeta("damage") ? (int)ball.GetMeta("damage") : 5;
                        int currentHp = ball.HasMeta("health") ? (int)ball.GetMeta("health") : 20;

                        GD.Print($"[DICE] {ball.Name} zar attı: {roll}");

                        if (roll <= 2)
                        {
                            int newHp = currentHp - 5;
                            ball.SetMeta("health", newHp);
                            GD.Print($"[DICE] ŞANSSIZ! {ball.Name} can kaybetti: {currentHp} → {newHp}");
                            clientNodeRuneEffect?.Rpc("SyncBallStats", ball.Name, newHp);
                            clientNodeRuneEffect?.Rpc("SyncDiceEffect", ball.Name, 1, 5); // Type 1 = HP Loss

                            if (newHp <= 0)
                            {
                                ball.QueueFree();
                                clientNodeRuneEffect?.Rpc("RemoveBall", ball.Name);
                                clientNodeRuneEffect?.Rpc("PlayBallExplosion", ball.Name);
                                CallDeferred(nameof(CheckWinCondition));
                            }
                        }
                        else if (roll >= 3 && roll <= 5)
                        {
                            int newDmg = currentDmg + 2;
                            ball.SetMeta("damage", newDmg);
                            GD.Print($"[DICE] ŞANSLI! {ball.Name} hasar kazandı: {currentDmg} → {newDmg}");
                            clientNodeRuneEffect?.Rpc("SyncBallDamage", ball.Name, newDmg);
                            clientNodeRuneEffect?.Rpc("SyncDiceEffect", ball.Name, 2, 2); // Type 2 = Damage Gain
                        }
                        else if (roll == 6)
                        {
                            int diceHealAmount = 20 - currentHp;
                            ball.SetMeta("health", 20);
                            GD.Print($"[DICE] JACKPOT! {ball.Name} dapat full HP iyileşme: {currentHp} → 20");
                            clientNodeRuneEffect?.Rpc("SyncBallStats", ball.Name, 20);
                            clientNodeRuneEffect?.Rpc("SyncDiceEffect", ball.Name, 3, diceHealAmount); // Type 3 = Jackpot
                        }
                        break;

                    case "water":
                        ball.LinearDamp = 3.0f;
                        GetTree().CreateTimer(2.0f).Timeout += () => {
                            if (IsInstanceValid(ball)) ball.LinearDamp = 0.1f;
                        };
                        break;

                    case "banana":
                        ball.LinearDamp = 0.0f;
                        GetTree().CreateTimer(4.0f).Timeout += () => {
                            if (IsInstanceValid(ball)) ball.LinearDamp = 0.1f;
                        };
                        break;

                    case "key":
                        GD.Print("[SERVER] Anahtar toplandı! Özel yetenek aktif edilebilir.");
                        break;
                }
            }
            ProcessRunePickup(rune);
        }
    }
    // Rünü hem sunucudan hem istemciden silen yardımcı fonksiyon
    private void ProcessRunePickup(Area2D rune)
    {
        string rName = rune.Name;

        // 1. Sunucudan sil
        rune.QueueFree();

        // 2. İstemcilere silme emri gönder
        var clientNodeRemoveRune = GetParent().GetNodeOrNull<Node>("Client");
        if (clientNodeRemoveRune != null)
        {
            clientNodeRemoveRune.Rpc("RemoveRune", rName);
        }
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer)]
    public void RequestSpawnBall(Vector2 pos, Vector2 force, int textureIndex, int teamId, int health, int damage, string abilityJson)
    {
        // GÜVENLİK: Sıra kontrolü (oyunun başından sonra spawn yok, ama güvenlik için kontrol edelim)
        long senderId = Multiplayer.GetRemoteSenderId();
        int senderTeam;
        if (senderId == 0)
        {
            senderTeam = teamId;
        }
        else if (!_authenticatedPlayers.TryGetValue(senderId, out senderTeam))
        {
            GD.PrintErr($"[SERVER] Yetkisiz spawn isteği! Peer {senderId} doğrulanmamış.");
            return;
        }

        teamId = senderTeam;
        GD.Print($"[SERVER] Top olusturuluyor: Pozisyon {pos}, Takim {teamId}");
        var main = GetParent<Main>();

        if (main.ballScene == null) return;

        GD.Print($"[SERVER] Top yaratma isteği alındı! Takım: {teamId}, Dokü: {textureIndex}, Pozisyon: {pos}");

        var match = GlobalData.Instance.ActiveMatch;
        if (match == null) return;

        // Takıma göre doğru deck'i bul
        DeckResponse deck = (teamId == 0) ? match.player1Deck : match.player2Deck;

        // textureIndex aslında ballTemplateId - 1 olarak geliyor
        BallItem item = deck.items.Find(i => i.ballTemplateId == textureIndex);

        if (item == null) return;

        int serverHealth = item.health;
        int serverDamage = item.attackPower;
        string abilityJsonString = string.IsNullOrWhiteSpace(abilityJson) ? "{}" : abilityJson;
        if (item.ability.ValueKind == JsonValueKind.Object)
            abilityJsonString = item.ability.GetRawText();

        var ball = (RigidBody2D)main.ballScene.Instantiate();
        ball.Name = "Ball_" + Guid.NewGuid().ToString().Substring(0, 8);

        // Loglarını koruyorum, ama gerçek değer serverHealth/serverDamage
        GD.Print($"[SERVER] Top sahnede oluşturuldu: {ball.Name} | HP: {serverHealth}");

        ball.SetMeta("team_id", teamId);
        ball.SetMeta("texture_idx", textureIndex);
        ball.SetMeta("ability_json", abilityJsonString);

        // Client’tan gelen health/damage yerine API’den gelen değerleri set ediyoruz
        ball.SetMeta("health", serverHealth);
        ball.SetMeta("damage", serverDamage);

        if (ball is PoolBall poolBall)
        {
            poolBall.SetAbility(abilityJsonString);
            poolBall.TeamId = teamId;

            // SPAWN SIRASINDAN DEFENSE ABILITY'NI UYGULA
            if (poolBall.BallAbility is DefenseAbility defenseAbility && !defenseAbility.ActivateOnStop)
            {
                if (defenseAbility.Invulnerable)
                {
                    ball.SetMeta("invulnerable", 1);
                    GD.Print($"[SPAWN] {ball.Name} invulnerable bayrağı set edildi.");
                }
                if (defenseAbility.Shield > 0)
                {
                    ball.SetMeta("shield", defenseAbility.Shield);
                    GD.Print($"[SPAWN] {ball.Name} shield set edildi: {defenseAbility.Shield}");
                }
                if (defenseAbility.Reflect > 0f)
                {
                    ball.SetMeta("reflect", defenseAbility.Reflect);
                    GD.Print($"[SPAWN] {ball.Name} reflect set edildi: {defenseAbility.Reflect}");
                }
            }
        }

        ball.ContactMonitor = true;
        ball.MaxContactsReported = 5;

        // Çarpışma sinyalini merkezi metoda bağlıyoruz
        ball.BodyEntered += (body) => OnBallCollision(ball, body);

        if (_tableInstance != null)
        {
            _tableInstance.AddChild(ball);
            ball.GlobalPosition = pos;
            ball.AddToGroup("balls");

            if (force != Vector2.Zero)
            {
                // Force clamp
                float MaxAllowedForce = 2000f;
                if (force.Length() > MaxAllowedForce)
                    force = force.Normalized() * MaxAllowedForce;

                ball.ApplyCentralImpulse(force);
            }

            var clientNode = GetParent().GetNode("Client");
            // Client’e de serverHealth/serverDamage ve yetenek JSON'u gönderiyoruz
            clientNode.Rpc("SyncSpawnBall", ball.Name, pos, textureIndex, teamId, serverHealth, serverDamage, abilityJsonString);
        }
    }


    // --- HASAR ALMAYI ENGELLEYEN MANTIK ---
    private void OnBallCollision(Node bodyA, Node bodyB)
    {
        if (!Multiplayer.IsServer()) return;
        if (!(bodyA is RigidBody2D ballA) || !(bodyB is RigidBody2D ballB)) return;

        // 1. Kendi topuna vurduysa veya çarpan top vuruş yapılan top değilse dur
        if (ballA.Name != _currentAttackerBallName) return;

        if (ballA is PoolBall attacker && ballB is PoolBall defender)
        {
            // YETENEKLİ VURUŞ: Bu turda henüz bu top tarafından yetenek kullanılmadıysa
            if (!_abilityUsedThisTurn.Contains(attacker.Name))
            {
                if (attacker.BallAbility is DamageAbility damageAbility && !damageAbility.ActivateOnStop)
                {
                    var targetsInAOE = GetBallsInArea(attacker.GlobalPosition, damageAbility.AreaOfEffect);
                    foreach (var target in targetsInAOE)
                    {
                        if (target == attacker) continue;
                        int targetTeam = (int)target.GetMeta("team_id");
                        int attackerTeam = (int)attacker.GetMeta("team_id");
                        if (targetTeam == attackerTeam) continue;

                        damageAbility.Execute(attacker, target);
                    }
                    _abilityUsedThisTurn.Add(attacker.Name);

                    var clientNodeAbility = GetParent().GetNodeOrNull<Node>("Client");
                    clientNodeAbility?.Rpc("SyncAbilityActivated", attacker.Name, "damage", damageAbility.Value);
                }
                else if (attacker.BallAbility is PhysicsAbility physicsAbility && !physicsAbility.ActivateOnStop)
                {
                    attacker.BallAbility?.Execute(attacker, defender);
                    _abilityUsedThisTurn.Add(attacker.Name);

                    var clientNodeAbility = GetParent().GetNodeOrNull<Node>("Client");
                    clientNodeAbility?.Rpc("SyncAbilityActivated", attacker.Name, "physics", 0);
                }
            }
        }

        // 2. Takım Kontrolü (Dost Ateşi Kapalı)
        int teamA = (int)ballA.GetMeta("team_id");
        int teamB = (int)ballB.GetMeta("team_id");
        if (teamA == teamB) return;

        // 3. Hasar Uygulama
        int damageValue = (int)ballA.GetMeta("damage");
        int targetHealth = (int)ballB.GetMeta("health");

        // Invulnerable kontrolü (Tek seferlik koruma - ilk darbeyi emer)
        if (ballB.HasMeta("invulnerable") && (int)ballB.GetMeta("invulnerable") > 0)
        {
            ballB.SetMeta("invulnerable", 0);
            GD.Print($"[HIT] {ballA.Name} vurdu -> {ballB.Name} invulnerable, hasar alınmadı. Koruma harcanmış.");
            var clientNodeInvul = GetParent().GetNodeOrNull<Node>("Client");
            clientNodeInvul?.Rpc("SyncInvulnerable", ballB.Name);
            return;
        }

        if (ballB.HasMeta("bounce_override") && (int)ballB.GetMeta("bounce_override") == 1)
        {
            damageValue = (int)Math.Ceiling(damageValue * 0.5f);
            ballB.SetMeta("bounce_override", 0);
            GD.Print($"[HIT] {ballA.Name} vurdu -> {ballB.Name} soft impact sayesinde hasar yarılandı: {damageValue}");
        }

        // Shield kontrolü
        if (ballB.HasMeta("shield") && (int)ballB.GetMeta("shield") > 0)
        {
            int shield = (int)ballB.GetMeta("shield");
            if (shield >= damageValue)
            {
                ballB.SetMeta("shield", shield - damageValue);
                int newShield = shield - damageValue;
                GD.Print($"[HIT] {ballA.Name} vurdu -> {ballB.Name} shield absorbe etti. Kalan shield: {newShield}");
                var clientNodeShield = GetParent().GetNodeOrNull<Node>("Client");
                clientNodeShield?.Rpc("SyncShieldUpdated", ballB.Name, damageValue, newShield);
                return;
            }
            else
            {
                damageValue -= shield;
                ballB.SetMeta("shield", 0);
                GD.Print($"[HIT] {ballA.Name} vurdu -> {ballB.Name} shield kırıldı, kalan hasar: {damageValue}");
                var clientNodeShieldBreak = GetParent().GetNodeOrNull<Node>("Client");
                clientNodeShieldBreak?.Rpc("SyncShieldUpdated", ballB.Name, shield, 0);
            }
        }

        targetHealth -= damageValue;
        ballB.SetMeta("health", targetHealth);

        // Reflect
        if (ballB is PoolBall reflector && reflector.BallAbility is DefenseAbility defenseAbility && defenseAbility.Reflect > 0f)
        {
            int reflectedDamage = (int)Math.Ceiling(damageValue * defenseAbility.Reflect);
            int attackerHealth = (int)ballA.GetMeta("health");
            ballA.SetMeta("health", attackerHealth - reflectedDamage);
            GD.Print($"[REFLECT] {ballB.Name} reflected {reflectedDamage} damage back to {ballA.Name}.");

            var clientNodeReflect = GetParent().GetNodeOrNull<Node>("Client");
            if (clientNodeReflect != null)
            {
                int attackerShield = ballA.HasMeta("shield") ? (int)ballA.GetMeta("shield") : 0;
                clientNodeReflect.Rpc("SyncBallStatsWithShield", ballA.Name, attackerHealth - reflectedDamage, attackerShield);
                if (attackerHealth - reflectedDamage <= 0)
                {
                    ballA.QueueFree();
                    clientNodeReflect.Rpc("RemoveBall", ballA.Name);
                    clientNodeReflect.Rpc("PlayBallExplosion", ballA.Name);
                }
            }
        }

        int currentShield = ballB.HasMeta("shield") ? (int)ballB.GetMeta("shield") : 0;
        var clientNodeDamage = GetParent().GetNode("Client");
        clientNodeDamage.Rpc("SyncBallStatsWithShield", ballB.Name, targetHealth, currentShield);

        GD.Print($"[HIT] {ballA.Name} vurdu -> {ballB.Name} hasar aldi. Yeni Can: {targetHealth}");

        if (targetHealth <= 0)
        {
            ballB.QueueFree();
            clientNodeDamage.Rpc("RemoveBall", ballB.Name);
            clientNodeDamage.Rpc("PlayBallExplosion", ballB.Name);

            CallDeferred(nameof(CheckWinCondition));
        }
    }

    private void OnCastleBodyEntered(Node body)
    {
        if (!Multiplayer.IsServer()) return;
        if (body is RigidBody2D ball)
        {
            if (_castleHitCooldown.Contains(ball.Name)) return;

            int ballTeam = (int)ball.GetMeta("team_id");
            int damageValue = ball.HasMeta("damage") ? (int)ball.GetMeta("damage") : 10;

            // Radiant Kalesi Kontrolü (Sol taraf - Genelde Team 0 Radiant, Team 1 Dire kabul edilir)
            if (ball.GlobalPosition.X < 300)
            {
                // Eğer giren top Dire'ın (Team 1) topuysa Radiant hasar alır
                if (ballTeam == 1)
                {
                    _radiantHealth -= damageValue;
                    GD.Print($"[CASTLE HIT] Radiant hasar aldi! Yeni Can: {_radiantHealth}");
                    UpdateCastleOnClients("Radiant", _radiantHealth);
                }
            }
            // Dire Kalesi Kontrolü (Sağ taraf)
            else if (ball.GlobalPosition.X > 900)
            {
                // Eğer giren top Radiant'ın (Team 0) topuysa Dire hasar alır
                if (ballTeam == 0)
                {
                    _direHealth -= damageValue;
                    GD.Print($"[CASTLE HIT] Dire hasar aldi! Yeni Can: {_direHealth}");
                    UpdateCastleOnClients("Dire", _direHealth);
                }
            }

            _castleHitCooldown.Add(ball.Name);
            GetTree().CreateTimer(0.5f).Timeout += () => _castleHitCooldown.Remove(ball.Name);
        }
    }

    private List<PoolBall> GetBallsInArea(Vector2 position, int radius)
    {
        var ballsInArea = new List<PoolBall>();
        var ballsGroup = GetTree().GetNodesInGroup("balls");
        foreach (var ball in ballsGroup)
        {
            if (ball is PoolBall poolBall)
            {
                float distance = poolBall.GlobalPosition.DistanceTo(position);
                if (distance <= radius)
                {
                    ballsInArea.Add(poolBall);
                }
            }
        }
        return ballsInArea;
    }

    private void OnTurnEnd()
    {
    }

    private void UpdateCastleOnClients(string side, int health)
    {
        var clientNodeCastle = GetParent().GetNodeOrNull<Node>("Client");
        if (clientNodeCastle != null)
        {
            clientNodeCastle.Rpc("UpdateCastleHealth", side, health);

            // Can 0 veya altına düştü mü?
            if (health <= 0 && !_isGameOver)
            {
                _isGameOver = true;
                // Eğer Radiant (Sol) canı bittiyse Dire (1) kazandı, 
                // Eğer Dire (Sağ) canı bittiyse Radiant (0) kazandı.
                int winnerTeam = (side == "Radiant") ? 1 : 0;

                GD.Print($"[SERVER] OYUN BİTTİ! Kazanan Takım: {winnerTeam}");
                clientNodeCastle.Rpc("OnGameOver", winnerTeam);
            }
        }
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer)]
    public void RequestHitBall(string ballName, Vector2 force)
    {
        if (!Multiplayer.IsServer()) return;

        long senderId = Multiplayer.GetRemoteSenderId();

        // GÜVENLİK 1: Oyuncu doğrulandı mı?
        if (!_authenticatedPlayers.TryGetValue(senderId, out int senderIndex))
        {
            GD.PrintErr($"[SERVER] Yetkisiz vuruş denemesi! Peer: {senderId}");
            return;
        }

        // GÜVENLİK 2: Sıra kontrolü
        if (senderIndex != _currentTurnPlayerId)
        {
            GD.Print($"[SERVER] Sıra dışı vuruş! Sıra: {_currentTurnPlayerId}, Gönderen: {senderIndex}");
            return;
        }

        // GÜVENLİK 3: Tüm toplar tamamen mi durdu? (Rapid-Fire engellemesi)
        if (AnyBallMoving())
        {
            GD.Print($"[SERVER] Toplar hâlâ hareket ediyor. Yeni vuruş engellendi.");
            return;
        }
    

        // GÜVENLİK 4: Bu turda zaten vurdu mu? (Per-turn shot limiti)
        if (_hasShotThisTurn.ContainsKey(senderId) && _hasShotThisTurn[senderId])
        {
            GD.PrintErr($"[SERVER] Oyuncu {senderId} bu turda zaten vurmuş! Tekrar vuramaz.");
            return;
        }

        // GÜVENLİK 5: Force clamp
        float maxAllowedForce = 1200f;
        if (force.Length() > maxAllowedForce)
        {
            force = force.Normalized() * maxAllowedForce;
            GD.Print($"[SERVER] {ballName} için aşırı güç algılandı, limitlendi.");
        }

        var ball = _tableInstance.GetNodeOrNull<RigidBody2D>(ballName);
        if (ball != null)
        {
            // Top sahipliğini kontrol et
            if (!IsBallOwnedByTeam(ball, senderIndex))
            {
                GD.Print($"[SERVER] Oyuncu {senderIndex} bu topa vuramaz: {ballName}");
                return;
            }

            // Vuran topu "Aktif Saldırgan" olarak işaretle
            _currentAttackerBallName = ball.Name;
            _abilityUsedThisTurn.Clear();

            ball.Sleeping = false;
            ball.ApplyCentralImpulse(force);

            // Bu turda bu oyuncu vurdu olarak işaretle (Rapid-Fire engelleme)
            _hasShotThisTurn[senderId] = true;

            ChangeTurn();
        }
    }

    private bool IsBallOwnedByTeam(Node ball, int teamId)
    {
        if (!ball.HasMeta("team_id"))
            return false;

        return (int)ball.GetMeta("team_id") == teamId;
    }

    private bool AnyBallMoving()
    {
        const float linearSpeedThreshold = 1.0f;
        const float angularSpeedThreshold = 0.05f;

        foreach (Node n in GetTree().GetNodesInGroup("balls"))
        {
            if (n is RigidBody2D rb &&
                (rb.LinearVelocity.Length() > linearSpeedThreshold ||
                 Mathf.Abs(rb.AngularVelocity) > angularSpeedThreshold))
                return true;
        }

        return false;
    }


    private void ChangeTurn()
    {
        _currentTurnPlayerId = (_currentTurnPlayerId == 0) ? 1 : 0;

        // Yeni tur: tüm oyuncuların vuruş hakkını sıfırla
        foreach (var key in _hasShotThisTurn.Keys)
        {
            _hasShotThisTurn[key] = false;
        }

        // Client düğümünü bul ve RPC gönder
        var clientNode = GetParent().GetNodeOrNull("Client");
        if (clientNode != null)
        {
            clientNode.Rpc("SyncTurn", _currentTurnPlayerId);
        }
    }

    public override void _Process(double delta)
    {
        if (DisplayServer.GetName() != "headless") return;
        var clientNode = GetParent().GetNodeOrNull("Client");
        if (clientNode == null) return;

        // GÜVENLİK: Tick rate sınırlaması (20 Hz = 50ms) - Network Flooding engelleme
        _positionUpdateTimer += (float)delta;
        if (_positionUpdateTimer < POSITION_UPDATE_INTERVAL)
            return;

        _positionUpdateTimer = 0f;

        foreach (Node node in GetTree().GetNodesInGroup("balls"))
        {
            if (node is RigidBody2D ball && (!ball.Sleeping || ball.LinearVelocity.Length() > 0.1f))
            {
                clientNode.Rpc("UpdateBallPosition", ball.Name, ball.GlobalPosition, ball.Rotation);
            }
        }
    }

    // Server.cs içindeki CheckWinCondition metodunu şu şekilde revize et:
    private void CheckWinCondition()
    {
        var allBalls = GetTree().GetNodesInGroup("balls");
        int radiantBalls = 0;
        int direBalls = 0;

        foreach (Node node in allBalls)
        {
            if (node is RigidBody2D ball && IsInstanceValid(ball) && !ball.IsQueuedForDeletion())
            {
                int team = ball.HasMeta("team_id") ? (int)ball.GetMeta("team_id") : -1;
                if (team == 0) radiantBalls++;
                else if (team == 1) direBalls++;
            }
        }

        GD.Print($"[SERVER] Kalan Toplar -> Radiant: {radiantBalls}, Dire: {direBalls}");

        if (!_isGameOver)
        {
            var clientNode = GetParent().GetNodeOrNull<Node>("Client");

            if (radiantBalls == 0)
            {
                _isGameOver = true;
                GD.Print("[SERVER] Radiant topları bitti! Dire kazandı.");
                clientNode?.Rpc("OnGameOver", 1); // Dire kazandı
            }
            else if (direBalls == 0)
            {
                _isGameOver = true;
                GD.Print("[SERVER] Dire topları bitti! Radiant kazandı.");
                clientNode?.Rpc("OnGameOver", 0); // Radiant kazandı
            }
        }
    }


    public void SpawnBallAtPosition(Vector2 pos, int textureIdx, int teamId)
    {
        var rng = new RandomNumberGenerator();
        rng.Randomize();
        RequestSpawnBall(pos, Vector2.Zero, textureIdx, teamId, rng.RandiRange(15, 25), rng.RandiRange(5, 10), "{}");
    }
    [Rpc(MultiplayerApi.RpcMode.Authority)] public void OnGameOver(int winnerTeamId) { }

    [Rpc(MultiplayerApi.RpcMode.Authority)]
    public void SyncBallDamage(string ballName, int damage) { }
}