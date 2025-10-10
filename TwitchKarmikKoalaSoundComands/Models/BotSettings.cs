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
}

