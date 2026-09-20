using Godot;
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;

public partial class Main : Node
{
    [Export] public PackedScene ballScene;
    [Export] public PackedScene runeScene; // YENİ
    public List<Texture2D> ball_images = new List<Texture2D>();
    public List<Texture2D> rune_images = new List<Texture2D>(); // YENİ
    private RandomNumberGenerator rng = new RandomNumberGenerator();
    private Rect2 playArea = new Rect2(180, 90, 960, 550);
    private float ballRadius = 12f;
    private float runeRadius = 25f;


    [Export] private Label _p1NameLabel;
    [Export] private Label _p1MmrLabel;
    [Export] private Label _p2NameLabel;
    [Export] private Label _p2MmrLabel;

    // Yasaklı Bölgeler (Kaleler)
    private List<Rect2> forbiddenAreas = new List<Rect2>()
    {
        new Rect2(0, 250, 250, 350),
        new Rect2(1050, 250, 250, 350)
    };

    public override void _Ready()
    {
        load_images();

        CallDeferred(nameof(SetPlayerLabels));

        var gData = GetNode<GlobalData>("/root/GlobalData");

        if (RuntimeMode.IsServer)
        {
            GD.Print("[MAIN] Server modu aktif.");
            ParseCommandLineArgs(gData);

            if (!string.IsNullOrEmpty(gData.CurrentMatchId))
            {
                GD.Print($"[MAIN] MatchID yakalandı: {gData.CurrentMatchId}. Detaylar API'den isteniyor...");
                RequestMatchDetails(gData.CurrentMatchId);
            }
            else
            {
                GD.PrintErr("[ERROR] MatchID bulunamadı! Varsayılan oyun başlatılıyor.");
            }
        }
    }

    private void ParseCommandLineArgs(GlobalData gData)
    {
        var args = OS.GetCmdlineArgs();
        foreach (string arg in args)
        {
            if (arg.StartsWith("--match_id="))
                gData.CurrentMatchId = arg.Split("=")[1];

            if (arg.StartsWith("--port="))
                GD.Print("[MAIN] Port Parametresi: " + arg.Split("=")[1]);
        }
    }

    private void RequestMatchDetails(string matchId)
    {
        var httpRequest = new HttpRequest();
        AddChild(httpRequest);
        httpRequest.RequestCompleted += OnMatchDetailsLoaded;

        // Backend URL'niz
        string url = $"http://host.docker.internal:8080/api/match/check-session?matchId={matchId}";

        httpRequest.Request(url);
    }

    private void OnMatchDetailsLoaded(long result, long responseCode, string[] headers, byte[] body)
    {
        // HTTP isteğinin genel sonucunu ve dönen HTTP durum kodunu detaylıca logluyoruz
        GD.Print($"[MAIN] API İstek Sonucu - Result: {result}, Response Code: {responseCode}");

        if (responseCode == 200)
        {
            try
            {
                string json = Encoding.UTF8.GetString(body);
                var matchData = JsonSerializer.Deserialize<MatchSessionData>(json);
                GlobalData.Instance.ActiveMatch = matchData;

                var serverNode = GetNodeOrNull<Server>("Server");
                if (RuntimeMode.IsServer && serverNode != null)
                {
                    serverNode.StartServer(matchData.serverPort);
                    serverNode.ProcessPendingAuthentications();
                }

                GD.Print("[MAIN] API Verisi Başarıyla Alındı. Oyun Başlatılıyor...");
                CallDeferred(nameof(newGame));
            }
            catch (Exception e)
            {
                GD.PrintErr("[MAIN] JSON Parse Hatası: " + e.Message);
                GD.Print("[MAIN] JSON parse edilemediği için varsayılan oyun başlatılıyor...");
                StartDefaultGame(); // Varsayılan oyun başlatma metodumuz
            }
        }
        else
        {
            // Hata durumunda dönen yanıtın gövdesini (body) incelemek için ekliyoruz (varsa)
            string errorDetails = body != null ? Encoding.UTF8.GetString(body) : "Boş yanıt gövdesi";

            GD.PrintErr($"[MAIN] API Hatası! Kod: {responseCode}. Detay: {errorDetails}");
            GD.Print("[MAIN] Sunucu yanıtı başarısız (200 dönmedi). Varsayılan oyun başlatılıyor...");

            StartDefaultGame(); // Varsayılan oyun başlatma metodumuz
        }
    }

    private void StartDefaultGame()
    {
        GD.Print("[MAIN] -> Varsayılan oyun modu aktif ediliyor. Yerel ayarlarla devam ediliyor.");
        // Varsayılan oyun başlatma işlemlerini buraya yazabilirsiniz
        CallDeferred(nameof(newGame));
    }
    public void load_images()
    {
        ball_images.Clear();
        for (int i = 1; i <= 20; i++) // API'deki templateId'leri desteklemek için menzili genişlettim
        {
            string path = $"res://assets/Maingame/ball_{i}.png";
            if (FileAccess.FileExists(path))
                ball_images.Add(GD.Load<Texture2D>(path));
        }

        rune_images.Clear();
        Dictionary<int, string> RuneMap = new Dictionary<int, string> {
            {0, "gold"}, {1, "health"}, {2, "lighting"}, {3, "trap"},
            {4, "water"}, {5, "banana"}, {6, "dice"}, {7, "key"}
        };

        for (int i = 0; i < RuneMap.Count; i++)
        {
            string path = $"res://assets/Maingame/runes/{RuneMap[i]}.png";
            if (FileAccess.FileExists(path))
                rune_images.Add(GD.Load<Texture2D>(path));
        }
    }

    public void newGame()
    {
        foreach (Node n in GetTree().GetNodesInGroup("balls")) n.QueueFree();
        foreach (Node n in GetTree().GetNodesInGroup("runes")) n.QueueFree();

        generateBalls();
        generateRunes(6);
    }

    public void generateBalls()
    {
        var serverNode = GetNodeOrNull<Server>("Server");
        if (serverNode == null) return;

        var match = GlobalData.Instance.ActiveMatch;

        // EĞER API'DEN VERİ GELDİYSE (Normal Akış)
        if (match != null && match.player1Deck != null && match.player2Deck != null)
        {

           

            // Player 1 Topları
            foreach (var item in match.player1Deck.items)
            {
                Vector2 pos = GetValidRandomPosition(ballRadius);
                string abilityJson = item.ability.ValueKind == JsonValueKind.Object ? item.ability.GetRawText() : "{}";
                serverNode.RequestSpawnBall(pos, Vector2.Zero, item.ballTemplateId, 0, item.health, item.attackPower, abilityJson);

            }

            // Player 2 Topları
            foreach (var item in match.player2Deck.items)
            {
                Vector2 pos = GetValidRandomPosition(ballRadius);
                string abilityJson = item.ability.ValueKind == JsonValueKind.Object ? item.ability.GetRawText() : "{}";
                serverNode.RequestSpawnBall(pos, Vector2.Zero, item.ballTemplateId, 1, item.health, item.attackPower, abilityJson);
            }
        }
        else // API'DEN VERİ GELMEZSE (Bug/Fallback Durumu)
        {
            GD.Print("[MAIN] API verisi boş, default toplar üretilemiyor...");
          
        }
    }

    private void generateRunes(int count)
    {
        if (rune_images.Count == 0) return;
        List<int> availableIndices = new List<int>();
        for (int j = 0; j < rune_images.Count; j++) availableIndices.Add(j);

        for (int i = 0; i < count; i++)
        {
            if (availableIndices.Count == 0) break;
            Vector2 pos = GetValidRandomPosition(runeRadius);

            int listIdx = rng.RandiRange(0, availableIndices.Count - 1);
            int runeIdx = availableIndices[listIdx];
            availableIndices.RemoveAt(listIdx);

            var serverNode = GetNodeOrNull<Server>("Server");
            if (serverNode != null) serverNode.SpawnRuneAtPosition(pos, runeIdx);
        }
    }

    // Güncellendi: Artık hem top hem rün için radius alıyor ve kaleleri kontrol ediyor
    private Vector2 GetValidRandomPosition(float radius)
    {
        int attempts = 0;
        while (attempts < 100)
        {
            float x = rng.RandfRange(playArea.Position.X + radius, playArea.End.X - radius);
            float y = rng.RandfRange(playArea.Position.Y + radius, playArea.End.Y - radius);
            Vector2 pos = new Vector2(x, y);

            bool isInvalid = false;

            // Kale kontrolü
            foreach (Rect2 area in forbiddenAreas)
                if (area.HasPoint(pos)) { isInvalid = true; break; }

            if (isInvalid) { attempts++; continue; }

            // Üst üste binme kontrolü (Gruptaki her şeyle)
            foreach (Node n in GetTree().GetNodesInGroup("balls"))
                if (n is Node2D b && b.GlobalPosition.DistanceTo(pos) < radius * 2.5f) { isInvalid = true; break; }

            if (isInvalid) { attempts++; continue; }

            foreach (Node n in GetTree().GetNodesInGroup("runes"))
                if (n is Node2D r && r.GlobalPosition.DistanceTo(pos) < radius * 2.5f) { isInvalid = true; break; }

            if (!isInvalid) return pos;
            attempts++;
        }
        return new Vector2(500, 400);
    }
    private void SetPlayerLabels()
    {
        var gData = GetNode<GlobalData>("/root/GlobalData");

        // Maç verisi kontrolü
        if (gData.ActiveMatch != null)
        {
            // P1 Takımı Ben miyim kontrolü (myTeam 0 ise P1 benim demektir)
            if (gData.ActiveMatch.myTeam == 0)
            {
                // Kendi bilgilerim (P1 / Radiant)
                string p1Name = !string.IsNullOrEmpty(gData.ActiveMatch.player1Name) ? gData.ActiveMatch.player1Name : gData.UserName;
                int p1Elo = gData.ActiveMatch.player1Elo > 0 ? gData.ActiveMatch.player1Elo : gData.eloRating;

                if (_p1NameLabel != null) _p1NameLabel.Text = p1Name;
                if (_p1MmrLabel != null) _p1MmrLabel.Text = $"MMR: {p1Elo}";

                // Rakip bilgileri (P2 / Dire)
                if (_p2NameLabel != null) _p2NameLabel.Text = gData.ActiveMatch.player2Name ?? "Rakip";
                if (_p2MmrLabel != null) _p2MmrLabel.Text = $"MMR: {gData.ActiveMatch.player2Elo}";

                GD.Print($"[P1-INFO] P1 Name Set: {p1Name}");
                GD.Print($"[P1-INFO] P1 MMR Set: {p1Elo}");
                GD.Print($"[P2-INFO] P2 Name Set: {gData.ActiveMatch.player2Name}");
                GD.Print($"[P2-INFO] P2 MMR Set: {gData.ActiveMatch.player2Elo}");
            }
            // Eğer P2 benimsem
            else if (gData.ActiveMatch.myTeam == 1)
            {
                string p2Name = !string.IsNullOrEmpty(gData.ActiveMatch.player2Name) ? gData.ActiveMatch.player2Name : gData.UserName;
                int p2Elo = gData.ActiveMatch.player2Elo > 0 ? gData.ActiveMatch.player2Elo : gData.eloRating;

                if (_p2NameLabel != null) _p2NameLabel.Text = p2Name;
                if (_p2MmrLabel != null) _p2MmrLabel.Text = $"MMR: {p2Elo}";

                if (_p1NameLabel != null) _p1NameLabel.Text = gData.ActiveMatch.player1Name ?? "Rakip";
                if (_p1MmrLabel != null) _p1MmrLabel.Text = $"MMR: {gData.ActiveMatch.player1Elo}";

                GD.Print($"[P1-INFO] P1 Name Set: {gData.ActiveMatch.player1Name}");
                GD.Print($"[P1-INFO] P1 MMR Set: {gData.ActiveMatch.player1Elo}");
                GD.Print($"[P2-INFO] P2 Name Set: {p2Name}");
                GD.Print($"[P2-INFO] P2 MMR Set: {p2Elo}");
            }
        }
    }
}