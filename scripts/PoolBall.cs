#nullable enable

using Godot;

public partial class PoolBall : RigidBody2D
{
    private static PoolBall? activeBall;
    private bool dragging = false;
    private Vector2 dragStart;
    private Line2D? aimLine;
    private const float MovingThreshold = 5.0f;

    // --- Görselleştirme ve Stat Değişkenleri ---
    private bool _isMyBall = false;
    private bool _isTextureLoaded = false; // Görsel yüklendi mi kontrolü

    // Yeni: Önceki canı tutmak için (popup hasar hesaplaması için)
    private int _previousHealth = -1;
    
    // Shield tracking
    private int _currentShield = 0;
    private Label? _shieldLabel;

    public IBallAbility? BallAbility { get; private set; }
    private bool _hasTriggeredStopAbility = false;
    private bool _hasUsedInvulnerable = false;
    private Label? _abilityLabel;

    public int TeamId = -1;
    private float _drawRadius = 25.3f;

    public override void _Ready()
    {
        aimLine = GetNodeOrNull<Line2D>("AimLine");
        _abilityLabel = GetNodeOrNull<Label>("HealthUI/AbilityLabel");
        _shieldLabel = GetNodeOrNull<Label>("HealthUI/ShieldLabel");
        GD.Print($"[BALL-CLIENT] Top hazır: {Name} | Global Pozisyon: {GlobalPosition} | Takım: {TeamId} | ShieldLabel Bulundu: {_shieldLabel != null}");
        if (aimLine != null) aimLine.Visible = false;

        InputPickable = true;
        AddToGroup("balls"); // Topların senkronizasyonu için gruba ekliyoruz
    }

    // YENİ: Sunucudan gelen görsel indeksini uygular
    [Rpc(MultiplayerApi.RpcMode.AnyPeer)]
    public void SetBallTexture(int templateId)
    {
        GD.Print($"[BALL-TEXTURE] {Name} için animasyon atanıyor: {templateId}");

        var animSprite = GetNodeOrNull<AnimatedSprite2D>("animatedBall");
        if (animSprite != null)
        {
            string animName = templateId.ToString(); // Animasyon isimleri sayı şeklinde
            if (animSprite.SpriteFrames.HasAnimation(animName))
            {
                animSprite.Play(animName);
                _isTextureLoaded = true;
                QueueRedraw();
            }
            else
            {
                GD.PrintErr($"[BALL-TEXTURE] Animasyon bulunamadı: {animName}");
            }
        }
    }


    public void SetAsMyBall(bool isMine)
    {
        _isMyBall = isMine;
        QueueRedraw();
    }

    public void SetAbility(string abilityJson)
    {
        BallAbility = AbilityFactory.CreateAbility(abilityJson);
        _hasTriggeredStopAbility = false;
        _hasUsedInvulnerable = false;
        if (BallAbility != null)
        {
            GD.Print($"[ABILITY] {Name} loaded ability: {BallAbility.GetType().Name}");
            // Yeteneklerin açıklamasını label'da göster
            if (_abilityLabel != null)
            {
                _abilityLabel.Text = GetAbilityDescription(BallAbility);
            }
        }
    }

    public void UpdateStats(int health, int damage, int shield = 0)
    {
        var healthLabel = GetNodeOrNull<Label>("HealthUI/HealthLabel");
        var damageLabel = GetNodeOrNull<Label>("HealthUI/DamageLabel");

        if (healthLabel != null) healthLabel.Text = health.ToString();
        if (damageLabel != null) damageLabel.Text = damage.ToString();
        
        _currentShield = shield;
        
        // Shield UI Update - Try to find label if not cached
        if (_shieldLabel == null)
            _shieldLabel = GetNodeOrNull<Label>("HealthUI/ShieldLabel");
            
        if (_shieldLabel != null)
        {
            _shieldLabel.Text = shield > 0 ? shield.ToString() : "";
            _shieldLabel.Visible = shield > 0;
        }

        var healthBar = GetNodeOrNull<ProgressBar>("HealthUI/HealthBar");
        if (healthBar != null)
        {
            healthBar.MaxValue = 20;
            healthBar.Value = health;
        }

        // --- GÜNCELLENEN KISIM: Hasar veya İyileşme Kontrolü ---
        // Sadece 'health < _previousHealth' yerine 'health != _previousHealth' kullanıyoruz
        if (_previousHealth >= 0 && health != _previousHealth)
        {
            int diff = health - _previousHealth;
            string sign = diff > 0 ? "+" : ""; // Pozitifse başına + koyar
            ShowHitPopup($"{sign}{diff}");
        }

        // Güncelle: bir sonraki karşılaştırma için önceki canı sakla
        _previousHealth = health;
    }
    
    public void UpdateShield(int newShield)
    {
        _currentShield = newShield;
        
        // Try to find label if not cached
        if (_shieldLabel == null)
            _shieldLabel = GetNodeOrNull<Label>("HealthUI/ShieldLabel");
            
        if (_shieldLabel != null)
        {
            _shieldLabel.Text = newShield > 0 ? newShield.ToString() : "";
            _shieldLabel.Visible = newShield > 0;
            GD.Print($"[SHIELD-UPDATE] {Name} shield güncellendi: {newShield}");
        }
        else
        {
            GD.PrintErr($"[SHIELD-ERROR] {Name} için ShieldLabel bulunamadı!");
        }
    }

    // Yeni: Hasar/pop-up gösterme metodu
    // Yeni: Hasar/pop-up gösterme metodu
    private void ShowHitPopup(string text)
    {
        // Popup için Label oluştur
        Label popup = new Label();
        popup.Text = text;
        popup.AddThemeFontSizeOverride("font_size", 30);

        // --- YENİ MANTIK: Pozitifse yeşil, negatifse kırmızı ---
        if (text.Contains("+"))
        {
            popup.AddThemeColorOverride("font_color", new Color(0.2f, 1, 0.2f)); // yeşilimsi
        }
        else
        {
            popup.AddThemeColorOverride("font_color", new Color(1, 0.2f, 0.2f)); // kırmızımsı
        }

        // Godot 4 API: HorizontalAlignment ve VerticalAlignment kullan
        popup.HorizontalAlignment = HorizontalAlignment.Center;
        popup.VerticalAlignment = VerticalAlignment.Center;

        popup.Modulate = new Color(1, 1, 1, 1);
        popup.ZIndex = 1000;

        // Başlangıç pozisyonu: topun üstü
        Vector2 startPos = new Vector2(0, -30);
        popup.Position = startPos;

        AddChild(popup);

        // Tween ile yukarı hareket ve fade out
        var tween = CreateTween();
        tween.TweenProperty(popup, "position", startPos + new Vector2(0, -40), 0.9f).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.Out);
        tween.TweenProperty(popup, "modulate:a", 0.0f, 0.9f).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.Out);
        tween.TweenCallback(Callable.From(() => popup.QueueFree()));
    }

    public void UpdateShieldFromServer(int newShield)
    {
        UpdateShield(newShield);
    }
    
    public void ShowShieldPopup(int amount)
    {
        Label popup = new Label();
        popup.Text = $"-{amount} Shield";
        popup.AddThemeFontSizeOverride("font_size", 24);
        popup.AddThemeColorOverride("font_color", new Color(0.5f, 0.5f, 1.0f)); // mavi
        popup.HorizontalAlignment = HorizontalAlignment.Center;
        popup.VerticalAlignment = VerticalAlignment.Center;
        popup.Modulate = new Color(1, 1, 1, 1);
        popup.ZIndex = 1000;

        Vector2 startPos = new Vector2(0, -40);
        popup.Position = startPos;

        AddChild(popup);

        var tween = CreateTween();
        tween.TweenProperty(popup, "position", startPos + new Vector2(0, -30), 1.0f).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.Out);
        tween.TweenProperty(popup, "modulate:a", 0.0f, 1.0f).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.Out);
        tween.TweenCallback(Callable.From(() => popup.QueueFree()));
    }

    public void ShowInvulnerablePopup()
    {
        Label popup = new Label();
        popup.Text = "Invulnerable!";
        popup.AddThemeFontSizeOverride("font_size", 20);
        popup.AddThemeColorOverride("font_color", new Color(1.0f, 1.0f, 0.0f)); // sarı
        popup.HorizontalAlignment = HorizontalAlignment.Center;
        popup.VerticalAlignment = VerticalAlignment.Center;
        popup.Modulate = new Color(1, 1, 1, 1);
        popup.ZIndex = 1000;

        Vector2 startPos = new Vector2(0, -50);
        popup.Position = startPos;

        AddChild(popup);

        var tween = CreateTween();
        tween.TweenProperty(popup, "position", startPos + new Vector2(0, -20), 1.2f).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.Out);
        tween.TweenProperty(popup, "modulate:a", 0.0f, 1.2f).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.Out);
        tween.TweenCallback(Callable.From(() => popup.QueueFree()));
    }

    // YENİ: Dice Rünü Pop-up Efektleri
    public void ShowDiceLoss(int healthLoss)
    {
        Label popup = new Label();
        popup.Text = $"ŞANSSIZ! -{healthLoss} HP";
        popup.AddThemeFontSizeOverride("font_size", 26);
        popup.AddThemeColorOverride("font_color", new Color(1.0f, 0.2f, 0.2f)); // kırmızı
        popup.HorizontalAlignment = HorizontalAlignment.Center;
        popup.VerticalAlignment = VerticalAlignment.Center;
        popup.Modulate = new Color(1, 1, 1, 1);
        popup.ZIndex = 1000;

        Vector2 startPos = new Vector2(0, -40);
        popup.Position = startPos;

        AddChild(popup);

        var tween = CreateTween();
        tween.TweenProperty(popup, "position", startPos + new Vector2(0, -50), 1.2f).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.Out);
        tween.TweenProperty(popup, "modulate:a", 0.0f, 1.2f).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.Out);
        tween.TweenCallback(Callable.From(() => popup.QueueFree()));
    }

    public void ShowDiceGain(int damageGain)
    {
        Label popup = new Label();
        popup.Text = $"ŞANSLI! +{damageGain} DMG";
        popup.AddThemeFontSizeOverride("font_size", 26);
        popup.AddThemeColorOverride("font_color", new Color(0.2f, 1.0f, 0.2f)); // yeşil
        popup.HorizontalAlignment = HorizontalAlignment.Center;
        popup.VerticalAlignment = VerticalAlignment.Center;
        popup.Modulate = new Color(1, 1, 1, 1);
        popup.ZIndex = 1000;

        Vector2 startPos = new Vector2(0, -40);
        popup.Position = startPos;

        AddChild(popup);

        var tween = CreateTween();
        tween.TweenProperty(popup, "position", startPos + new Vector2(0, -50), 1.2f).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.Out);
        tween.TweenProperty(popup, "modulate:a", 0.0f, 1.2f).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.Out);
        tween.TweenCallback(Callable.From(() => popup.QueueFree()));
    }

    public void ShowDiceJackpot(int healAmount)
    {
        Label popup = new Label();
        popup.Text = $"🎲 JACKPOT! +{healAmount} HP";
        popup.AddThemeFontSizeOverride("font_size", 28);
        popup.AddThemeColorOverride("font_color", new Color(1.0f, 0.84f, 0.0f)); // altın sarı
        popup.HorizontalAlignment = HorizontalAlignment.Center;
        popup.VerticalAlignment = VerticalAlignment.Center;
        popup.Modulate = new Color(1, 1, 1, 1);
        popup.ZIndex = 1000;

        Vector2 startPos = new Vector2(0, -40);
        popup.Position = startPos;

        AddChild(popup);

        var tween = CreateTween();
        tween.TweenProperty(popup, "position", startPos + new Vector2(0, -60), 1.5f).SetTrans(Tween.TransitionType.Bounce).SetEase(Tween.EaseType.Out);
        tween.TweenProperty(popup, "modulate:a", 0.0f, 1.5f).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.Out);
        tween.TweenCallback(Callable.From(() => popup.QueueFree()));
    }

    public override void _Draw()
    {
        // Eğer görsel yüklenemediyse yedek olarak bir daire çiz (Hata tespiti için)
        if (!_isTextureLoaded)
        {
            DrawCircle(Vector2.Zero, 14.0f, new Color(0.5f, 0.5f, 0.5f, 0.5f));
        }

        if (_isMyBall)
        {
            // Sadece benim olan topun etrafına halka çiz
            DrawArc(Vector2.Zero, _drawRadius, 0, Mathf.Tau, 64, new Color(1, 0.84f, 0, 0.8f), 3.0f);
        }
    }

    private bool AnyBallMoving()
    {
        foreach (Node n in GetTree().GetNodesInGroup("balls"))
        {
            if (n is RigidBody2D rb && (rb.LinearVelocity.Length() > MovingThreshold || !rb.Sleeping))
                return true;
        }
        return false;
    }

    public override void _InputEvent(Viewport viewport, InputEvent @event, int shapeIdx)
    {
        if (Multiplayer.IsServer()) return;

        if (@event is InputEventMouseButton mb && mb.ButtonIndex == MouseButton.Left && mb.Pressed)
        {
            // 1. Hareket kontrolü
            if (AnyBallMoving()) return;

            // 2. Sıra kontrolü (Client scriptinden sıranın bizde olup olmadığını al)
            var clientNode = GetTree().Root.FindChild("Client", true, false) as Node;
            if (clientNode != null) return;

            // 3. Sahiplik kontrolü (Sadece kendi topuna nişan alabilirsin)
            if (!_isMyBall) return;

            activeBall = this;
            dragging = true;
            dragStart = GetGlobalMousePosition();
            if (aimLine != null) aimLine.Visible = true;
        }
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (this != activeBall) return;

        if (@event is InputEventMouseButton mb && mb.ButtonIndex == MouseButton.Left && !mb.Pressed && dragging)
        {
            dragging = false;
            if (aimLine != null) aimLine.Visible = false;

            Vector2 force = (dragStart - GetGlobalMousePosition()) * 5f;

            var serverNode = GetTree().Root.FindChild("Server", true, false) as Server;
            if (serverNode != null)
            {
                serverNode.RpcId(1, "RequestHitBall", Name, force);
            }

            activeBall = null;
        }
    }

    public override void _Process(double delta)
    {
        if (dragging && this == activeBall && aimLine != null)
        {
            aimLine.Points = new Vector2[] { Vector2.Zero, ToLocal(GetGlobalMousePosition()) };
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        if (Multiplayer.IsServer() && BallAbility?.ActivateOnStop == true && !_hasTriggeredStopAbility)
        {
            if (LinearVelocity.Length() < 1.0f && Sleeping)
            {
                var clientNode = GetTree().Root.FindChild("Client", true, false) as Node;

                if (BallAbility is DefenseAbility def && def.Invulnerable && !_hasUsedInvulnerable)
                {
                    BallAbility.Execute(this);
                    _hasUsedInvulnerable = true;
                    clientNode?.Rpc("SyncAbilityActivated", Name, "defense", def.Shield);
                    clientNode?.Rpc("SyncInvulnerable", Name);
                }
                else if (!(BallAbility is DefenseAbility def2 && def2.Invulnerable))
                {
                    BallAbility.Execute(this);
                    if (BallAbility is DamageAbility dmg)
                        clientNode?.Rpc("SyncAbilityActivated", Name, "damage", dmg.Value);
                    else if (BallAbility is PhysicsAbility phy)
                        clientNode?.Rpc("SyncAbilityActivated", Name, "physics", (int)phy.Push);
                }
                _hasTriggeredStopAbility = true;
            }
        }
    }

    public void ShowAbilityPopup(string abilityType, int value)
    {
        string text = abilityType switch
        {
            "damage" => $"+{value} Damage",
            "defense" => $"+{value} Defense",
            "physics" => $"{value} Physics",
            _ => "Ability!"
        };

        Label popup = new Label();
        popup.Text = text;
        popup.AddThemeFontSizeOverride("font_size", 22);
        popup.AddThemeColorOverride("font_color", new Color(1.0f, 0.8f, 0.0f)); // altın
        popup.HorizontalAlignment = HorizontalAlignment.Center;
        popup.VerticalAlignment = VerticalAlignment.Center;
        popup.Modulate = new Color(1, 1, 1, 1);
        popup.ZIndex = 1000;

        Vector2 startPos = new Vector2(0, -60);
        popup.Position = startPos;

        AddChild(popup);

        var tween = CreateTween();
        tween.TweenProperty(popup, "position", startPos + new Vector2(0, -20), 1.5f).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.Out);
        tween.TweenProperty(popup, "modulate:a", 0.0f, 1.5f).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.Out);
        tween.TweenCallback(Callable.From(() => popup.QueueFree()));
    }

    private void PlayDeathEffect()
    {
        GD.Print($"[CLIENT] {Name} için patlama efekti oynatılıyor.");
        var explosionScene = GD.Load<PackedScene>("res://scene/BallExplosion.tscn");
        if (explosionScene == null) return;

        var explosion = (GpuParticles2D)explosionScene.Instantiate();

        // Pozisyonu nesneyi eklemeden ÖNCE yerel (Position) olarak veriyoruz
        // veya ekledikten hemen sonra GlobalPosition veriyoruz.
        explosion.Position = this.GlobalPosition;

        // Sahneye ekle
        GetParent().AddChild(explosion);

        // Eğer particles otomatik başlamazsa:
        explosion.Emitting = true;
    }

    private string GetAbilityDescription(IBallAbility ability)
    {
        if (ability is DamageAbility dmg)
        {
            string desc = $"+{dmg.Value} DMG";
            if (dmg.AreaOfEffect > 0) desc += $" AOE:{dmg.AreaOfEffect}";
            if (dmg.Duration > 0) desc += $" Dur:{dmg.Duration}";
            if (dmg.Pierce) desc += " Pierce";
            if (dmg.ActivateOnStop) desc += " (stop)";
            return desc;
        }
        else if (ability is DefenseAbility def)
        {
            if (def.Invulnerable) return def.ActivateOnStop ? "Invuln (stop)" : "Invuln";
            if (def.Reflect > 0f && def.Shield > 0) return $"+{def.Shield} Shd / {def.Reflect * 100:F0}% Refl";
            if (def.Reflect > 0f) return $"{def.Reflect * 100:F0}% Refl";
            if (def.Shield > 0) return $"+{def.Shield} Shd";
            return def.ActivateOnStop ? "Defense (stop)" : "Defense";
        }
        else if (ability is PhysicsAbility phy)
        {
            var parts = new System.Collections.Generic.List<string>();
            if (phy.Pull) parts.Add("Pull");
            if (phy.Push > 0) parts.Add($"+{(int)phy.Push} Push");
            if (phy.Bounce > 0) parts.Add($"+{phy.Bounce:F1} Bounce");
            if (phy.PushResist > 0) parts.Add($"+{(int)(phy.PushResist * 100)}% Res");
            if (phy.SoftImpact) parts.Add("Soft Impact");
            if (parts.Count > 0)
            {
                return string.Join(" ", parts);
            }
            return phy.ActivateOnStop ? "Physics (stop)" : "Physics";
        }
        return "Ability";
    }

}
