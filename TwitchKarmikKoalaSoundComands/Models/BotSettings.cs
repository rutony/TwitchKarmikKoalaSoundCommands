public class BotSettings {
    public string BotUsername { get; set; } = "";
    public string OAuthToken { get; set; } = "";
    public string ClientId { get; set; } = "";
    public string ChannelName { get; set; } = "";
    public string SoundsDirectory { get; set; } = "sounds";
    public int DefaultCooldown { get; set; } = 30;
    public bool ChatEnabled { get; set; } = true;
    public bool RewardsEnabled { get; set; } = true;
    public bool DebugMode { get; set; } = false;
    public int Volume { get; set; } = 50;

    // Настройки VIP
    public bool EnableVipReward { get; set; } = false;
    public int VipRewardCost { get; set; } = 150000;
    public int VipCooldown { get; set; } = 10; // в минутах
    public int VipDurationDays { get; set; } = 30;

    // Настройки кражи VIP
    public bool EnableVipStealReward { get; set; } = false;
    public int VipStealCost { get; set; } = 50000;
    public int VipStealChance { get; set; } = 5; // в процентах
    public int VipStealBanTime { get; set; } = 180; // в минутах

    public string MusicCommandKeywords { get; set; } = "!music;!музыка;!song;!track;!трек";
    public string MusicResponseTemplate { get; set; } = "$name, сейчас играет: $trackName $trackLink";
    public string NoMusicResponseTemplate { get; set; } = "$name, сейчас ничего не играет";
    public bool MusicTrackerEnabled { get; set; } = true;
    public int MusicTrackerPort { get; set; } = 8080;
}

