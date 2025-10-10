public class SoundCommand {
    public string SoundFile { get; set; }
    public int Cost { get; set; }
    public int Cooldown { get; set; }
    public string RewardTitle { get; set; }
    public bool ChatEnabled { get; set; }
    public bool RewardEnabled { get; set; }

    public SoundCommand(string soundFile, int cost, int cooldown, string rewardTitle, bool chatEnabled, bool rewardEnabled) {
        SoundFile = soundFile;
        Cost = cost;
        Cooldown = cooldown;
        RewardTitle = rewardTitle;
        ChatEnabled = chatEnabled;
        RewardEnabled = rewardEnabled;
    }
}
