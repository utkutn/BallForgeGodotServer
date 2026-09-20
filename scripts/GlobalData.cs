using Godot;
using System;
using System.Collections.Generic;
using System.Text.Json;

public partial class GlobalData : Node
{
    public static GlobalData Instance { get; private set; }

    // Kullanıcının oturum bilgileri
    public string AuthToken { get; set; } = "";
    public string UserName { get; set; } = "";
    public string UserId { get; set; } = ""; // 

    public string CurrentMatchId { get; set; } = "";

    public int eloRating { get; set; } = 0;



    // Java Backend'den gelen maçın tüm detayları
    public MatchSessionData ActiveMatch { get; set; }

    public override void _Ready()
    {
        Instance = this;
    }
}

public class UserData
{
    public string userId { get; set; }
    public string displayName { get; set; }
    public string avatarUrl { get; set; }
    public int eloRating { get; set; }
    public int gold { get; set; }
    public int level { get; set; }
    public int xp { get; set; }
    public string currentRankCode { get; set; }
}

public class MatchSessionData
{
    public string matchId { get; set; }
    public string player1Id { get; set; }
    public string player2Id { get; set; }
    public string serverIp { get; set; }
    public int serverPort { get; set; }
    public string status { get; set; }
    public int myTeam { get; set; } // 0 veya 1


    public string player1Name { get; set; }
    public int player1Elo { get; set; }
    public string player2Name { get; set; }
    public int player2Elo { get; set; }

    // API'den gelen isimlendirmelerle eşleşmesi için:
    public DeckResponse player1Deck { get; set; }
    public DeckResponse player2Deck { get; set; }

    // Yardımcı propertyler (Kodun diğer yerlerinde myDeck/enemyDeck kullanıyorsan kalabilir)
    public DeckResponse myDeck { get; set; }
    public DeckResponse enemyDeck { get; set; }
}

public class DeckResponse
{
    public int deckId { get; set; }
    public string name { get; set; }
    public string userId { get; set; }
    public List<BallItem> items { get; set; }
    public int teamId { get; set; } // Dashboard'daki karşılaştırma için eklendi
}

public class BallItem
{
    public int ballTemplateId { get; set; }
    public string ballName { get; set; }
    public int attackPower { get; set; } // API'den gelen Damage
    public int health { get; set; }      // API'den gelen HP
    public JsonElement ability { get; set; }
    public string rarity { get; set; }
    public int quantity { get; set; }
}

public class UserItemResponse
{
    public string userItemId { get; set; }
    public int ballTemplateId { get; set; }
    public int quantity { get; set; }
}

