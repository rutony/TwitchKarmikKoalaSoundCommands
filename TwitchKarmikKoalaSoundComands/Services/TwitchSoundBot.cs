using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

public class TwitchSoundBot {
    private readonly SettingsManager settingsManager;
    private readonly CommandManager commandManager;
    private readonly TwitchConnectionManager connectionManager;
    private readonly StatisticsService statisticsService;
    private readonly AudioPlayer audioPlayer;
    private readonly FileManager fileManager;

    private RewardManager rewardManager;
    private Dictionary<string, string> rewardIdToCommandMap;
    private string lastAuthError = "";
    private string lastRewardsError = "";
    private string lastChatError = "";

    public TwitchSoundBot() {
        settingsManager = new SettingsManager();
        commandManager = new CommandManager(settingsManager.Settings);
        connectionManager = new TwitchConnectionManager(settingsManager.Settings);
        statisticsService = new StatisticsService(commandManager);
        audioPlayer = new AudioPlayer(settingsManager.Settings);
        fileManager = new FileManager(settingsManager.Settings.SoundsDirectory);

        rewardIdToCommandMap = new Dictionary<string, string>();

        // Подписка на события
        connectionManager.OnChatCommand += HandleChatCommand;
        connectionManager.OnRewardRedeemed += HandleRewardCommand;
        connectionManager.OnRewardMappingUpdated += HandleRewardMapping;
    }

    public async Task<(bool authOk, string authError, bool rewardsOk, string rewardsError, bool chatOk, string chatError)> Connect() {
        var result = await connectionManager.Connect();

        // Инициализируем RewardManager после получения api и channelId
        rewardManager = new RewardManager(connectionManager.Api, connectionManager.ChannelId, settingsManager.Settings);

        if (settingsManager.Settings.RewardsEnabled && result.rewardsOk) {
            try {
                var rewardTitleToIdMap = await rewardManager.CreateCustomRewards(commandManager.GetAllCommands());

                // Строим mapping ID наград к командам
                foreach (var command in commandManager.GetAllCommands()) {
                    if (command.Value.RewardEnabled && rewardTitleToIdMap.ContainsKey(command.Value.RewardTitle)) {
                        var rewardId = rewardTitleToIdMap[command.Value.RewardTitle];
                        connectionManager.AddRewardMapping(rewardId, command.Key);
                        rewardIdToCommandMap[rewardId] = command.Key;

                        if (settingsManager.Settings.DebugMode) {
                            WriteColor($"✅ Сопоставлено: {command.Value.RewardTitle} -> {command.Key}\n", ConsoleColor.Green);
                        }
                    }
                }

                if (rewardTitleToIdMap.Count > 0) {
                    result.rewardsOk = true;
                    WriteColor($"✅ Награды настроены: {rewardTitleToIdMap.Count} наград\n", ConsoleColor.Green);
                } else {
                    result.rewardsOk = false;
                    lastRewardsError = "Не удалось создать или найти награды";
                }
            } catch (Exception ex) {
                lastRewardsError = $"Ошибка создания наград: {ex.Message}";
                result.rewardsOk = false;
            }
        }

        fileManager.CheckSoundFiles(commandManager.GetAllCommands());
        return result;
    }

    private void HandleChatCommand(object sender, (string username, string message) args) {
        if (!settingsManager.Settings.ChatEnabled)
            return;

        var message = args.message.ToLower();
        var username = args.username;

        // Обрабатываем специальные команды
        if (message == "!звуки" || message == "!sounds") {
            // Показываем список команд (можно добавить логику)
            return;
        }

        if (commandManager.ProcessChatCommand(message, username)) {
            var command = commandManager.GetCommand(message);
            if (command != null) {
                if (settingsManager.Settings.DebugMode) {
                    WriteColor($"🔊 Активирована команда чата: {message} пользователем {username}\n", ConsoleColor.Cyan);
                }
                audioPlayer.PlaySound(command.SoundFile, username, message);
            }
        }
    }

    private void HandleRewardCommand(object sender, (string command, string username) args) {
        if (!settingsManager.Settings.RewardsEnabled)
            return;

        if (commandManager.ProcessRewardCommand(args.command, args.username)) {
            var soundCommand = commandManager.GetCommand(args.command);
            if (soundCommand != null) {
                if (settingsManager.Settings.DebugMode) {
                    WriteColor($"🎁 Активирована награда: {args.command} пользователем {args.username}\n", ConsoleColor.Magenta);
                }
                audioPlayer.PlaySound(soundCommand.SoundFile, args.username, args.command);
            }
        } else {
            if (settingsManager.Settings.DebugMode) {
                WriteColor($"⏳ Cooldown для команды {args.command} пользователя {args.username}\n", ConsoleColor.Yellow);
            }
        }
    }

    private void HandleRewardMapping(object sender, (string rewardId, string rewardTitle) args) {
        // Ищем команду по названию награды
        var command = commandManager.GetAllCommands()
            .FirstOrDefault(c => c.Value.RewardTitle == args.rewardTitle && c.Value.RewardEnabled);

        if (command.Key != null) {
            connectionManager.AddRewardMapping(args.rewardId, command.Key);
            rewardIdToCommandMap[args.rewardId] = command.Key;

            if (settingsManager.Settings.DebugMode) {
                WriteColor($"🔍 Сопоставлена награда по названию: '{args.rewardTitle}' -> '{command.Key}'\n", ConsoleColor.Green);
            }
        } else {
            if (settingsManager.Settings.DebugMode) {
                WriteColor($"❌ Не найдена команда для награды: {args.rewardTitle}\n", ConsoleColor.Red);
            }
        }
    }

    // Публичные методы для UI (остаются без изменений)
    public string GetChannelName() => settingsManager.Settings.ChannelName;
    public int GetTotalCommands() => commandManager.GetAllCommands().Count;
    public int GetTotalUsage() => commandManager.TotalUsage;
    public Dictionary<string, int> GetCommandUsage() => commandManager.GetCommandUsage();
    public List<string> GetMissingFiles() => fileManager.GetMissingFiles();
    public string GetLastAuthError() => lastAuthError;
    public string GetLastRewardsError() => lastRewardsError;
    public string GetLastChatError() => lastChatError;
    public bool ChatEnabled => settingsManager.Settings.ChatEnabled;
    public bool RewardsEnabled => settingsManager.Settings.RewardsEnabled;
    public int GetChatEnabledCount() => commandManager.ChatEnabledCount;
    public int GetRewardEnabledCount() => commandManager.RewardEnabledCount;
    public bool IsDebugMode => settingsManager.Settings.DebugMode;
    public int GetVolume => settingsManager.Settings.Volume;

    public void ToggleChat() {
        settingsManager.Settings.ChatEnabled = !settingsManager.Settings.ChatEnabled;
        settingsManager.SaveSettings();
        WriteColor($"Команды чата: {(settingsManager.Settings.ChatEnabled ? "ВКЛ" : "ВЫКЛ")}\n",
                   settingsManager.Settings.ChatEnabled ? ConsoleColor.Green : ConsoleColor.Red);
    }

    public void ToggleRewards() {
        settingsManager.Settings.RewardsEnabled = !settingsManager.Settings.RewardsEnabled;
        settingsManager.SaveSettings();
        WriteColor($"Награды: {(settingsManager.Settings.RewardsEnabled ? "ВКЛ" : "ВЫКЛ")}\n",
                   settingsManager.Settings.RewardsEnabled ? ConsoleColor.Green : ConsoleColor.Red);
    }

    public void ToggleDebugMode() {
        settingsManager.Settings.DebugMode = !settingsManager.Settings.DebugMode;
        settingsManager.SaveSettings();
        WriteColor($"Режим отладки: {(settingsManager.Settings.DebugMode ? "ВКЛ" : "ВЫКЛ")}\n",
                   settingsManager.Settings.DebugMode ? ConsoleColor.Green : ConsoleColor.Red);
    }

    public void ChangeVolume() {
        settingsManager.Settings.Volume += 10;
        if (settingsManager.Settings.Volume > 100) {
            settingsManager.Settings.Volume = 0;
        }
        settingsManager.SaveSettings();
        WriteColor($"Громкость установлена: {settingsManager.Settings.Volume}%\n", ConsoleColor.Cyan);
    }

    public void ShowStatistics() => statisticsService.ShowStatistics();

    public void ShowPreferences() {
        Console.Clear();
        WriteColor("=== НАСТРОЙКИ ===\n", ConsoleColor.Cyan);
        Console.WriteLine();

        Console.Write("1 - Команды в чате: ");
        WriteColor(settingsManager.Settings.ChatEnabled ? "ВКЛ" : "ВЫКЛ",
                   settingsManager.Settings.ChatEnabled ? ConsoleColor.Green : ConsoleColor.Red);
        Console.WriteLine();

        Console.Write("2 - Награды Channel Points: ");
        WriteColor(settingsManager.Settings.RewardsEnabled ? "ВКЛ" : "ВЫКЛ",
                   settingsManager.Settings.RewardsEnabled ? ConsoleColor.Green : ConsoleColor.Red);
        Console.WriteLine();

        Console.Write("t - Режим отладки: ");
        WriteColor(settingsManager.Settings.DebugMode ? "ВКЛ" : "ВЫКЛ",
                   settingsManager.Settings.DebugMode ? ConsoleColor.Green : ConsoleColor.Red);
        Console.WriteLine();

        Console.Write("v - Громкость: ");
        WriteColor($"{settingsManager.Settings.Volume}%\n", ConsoleColor.Cyan);

        Console.WriteLine();
        WriteColor("a - Открыть ссылку авторизации в браузере\n", ConsoleColor.Blue);
        Console.WriteLine();
        WriteColor("b - Назад\n", ConsoleColor.Gray);
        Console.WriteLine();
    }

    public async Task Disconnect(bool disableRewards = true) {
        if (disableRewards && rewardManager != null) {
            await rewardManager.DisableCustomRewards();
        }
        await connectionManager.Disconnect(disableRewards);
    }

    public string GetAuthUrl() {
        var scopes = "channel:manage:redemptions chat:edit chat:read";
        var encodedScopes = Uri.EscapeDataString(scopes);
        return $"https://id.twitch.tv/oauth2/authorize?client_id={settingsManager.Settings.ClientId}&redirect_uri=http://localhost&response_type=token&scope={encodedScopes}";
    }

    public async Task CheckTokenScopes() {
        try {
            var cleanToken = settingsManager.Settings.OAuthToken.Replace("oauth:", "");
            var validated = await connectionManager.Api.Auth.ValidateAccessTokenAsync(cleanToken);

            if (validated != null) {
                WriteColor($"Токен действителен для пользователя: {validated.UserId}\n", ConsoleColor.Green);
                WriteColor($"Scopes токена: {string.Join(", ", validated.Scopes)}\n", ConsoleColor.Yellow);

                var requiredScopes = new[] { "channel:manage:redemptions" };
                var missingScopes = requiredScopes.Where(scope => !validated.Scopes.Contains(scope)).ToList();

                if (missingScopes.Any()) {
                    WriteColor($"Не хватает scopes для наград: {string.Join(", ", missingScopes)}\n", ConsoleColor.Red);
                } else {
                    WriteColor("Все необходимые scopes присутствуют\n", ConsoleColor.Green);
                }
            }
        } catch (Exception ex) {
            WriteColor($"Ошибка проверки токена: {ex.Message}\n", ConsoleColor.Red);
        }
    }

    private void WriteColor(string text, ConsoleColor color) {
        var originalColor = Console.ForegroundColor;
        Console.ForegroundColor = color;
        foreach (var ch in text) {
            try {
                Console.Write(ch);
            } catch {
                Console.Write('?');
            }
        }
        Console.ForegroundColor = originalColor;
    }
}