using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

public class TwitchBot {
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

    private VipManager vipManager;

    private readonly MusicTrackerService musicTracker;
    private readonly List<string> musicKeywords;

    private StatisticsDisplay statisticsDisplay;

    public TwitchBot() {
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

        vipManager = null;
        statisticsDisplay = null;

        musicTracker = new MusicTrackerService(settingsManager.Settings);
        musicKeywords = new List<string>();
        LoadMusicKeywords();

    }

    private void InitializeApi() {
        if (connectionManager?.Api != null) {
            connectionManager.Api.Settings.ClientId = settingsManager.Settings.ClientId;

            string apiToken = settingsManager.Settings.OAuthToken;
            if (apiToken.StartsWith("oauth:")) {
                apiToken = apiToken.Substring(6);
            }
            connectionManager.Api.Settings.AccessToken = apiToken;
        }
    }


    public async Task<(bool authOk, string authError, bool rewardsOk, string rewardsError, bool chatOk, string chatError)> Connect() {
        var result = await connectionManager.Connect();

        InitializeApi();

        // Инициализируем RewardManager после получения api и channelId
        if (rewardManager == null) {
            rewardManager = new RewardManager(connectionManager.Api, connectionManager.ChannelId, settingsManager.Settings);
        }

        if (vipManager == null) {
            vipManager = new VipManager(connectionManager.Api, connectionManager.ChannelId, settingsManager.Settings);
        }

        if (statisticsDisplay == null) {
            statisticsDisplay = new StatisticsDisplay(this, vipManager, commandManager, settingsManager.Settings);
        }

        statisticsDisplay.Start();

        if (settingsManager.Settings.MusicTrackerEnabled) {
            musicTracker.Start();
        }

        try {
            // Проверяем валидность токена
            var cleanToken = settingsManager.Settings.OAuthToken.Replace("oauth:", "");
            var validation = await connectionManager.Api.Auth.ValidateAccessTokenAsync(cleanToken);

            if (validation == null) {
                WriteColor("❌ Токен невалиден\n", ConsoleColor.Red);
                return result;
            }

            WriteColor($"✅ Токен валиден для пользователя: {validation.UserId}\n", ConsoleColor.Green);
        } catch (Exception ex) {
            WriteColor($"❌ Ошибка проверки токена: {ex.Message}\n", ConsoleColor.Red);
            return result;
        }

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

        if (settingsManager.Settings.EnableVipReward || settingsManager.Settings.EnableVipStealReward) {
            WriteDebug($"🔍 Проверка доступности API: ChannelId={connectionManager.ChannelId}\n", ConsoleColor.Cyan);

            try {
                // Проверяем, что можем получить список наград
                var testRewards = await connectionManager.Api.Helix.ChannelPoints.GetCustomRewardAsync(
                    connectionManager.ChannelId, onlyManageableRewards: true);
                WriteDebug($"✅ API доступно, найдено наград: {testRewards.Data.Length}\n", ConsoleColor.Green);
            } catch (Exception ex) {
                WriteColor($"❌ Ошибка доступа к API наград: {ex.Message}\n", ConsoleColor.Red);
            }
        }

        if (settingsManager.Settings.EnableVipReward || settingsManager.Settings.EnableVipStealReward) {
            try {
                bool vipRewardsCreated = await vipManager.CreateVipRewards();

                if (vipRewardsCreated) {
                    WriteColor($"✅ VIP награды настроены успешно\n", ConsoleColor.Green);
                } else {
                    WriteColor($"❌ Ошибка создания VIP наград: {vipManager.LastError}\n", ConsoleColor.Red);
                }
            } catch (Exception ex) {
                WriteColor($"❌ Ошибка создания VIP наград: {ex.Message}\n", ConsoleColor.Red);
            }
        }

        fileManager.CheckSoundFiles(commandManager.GetAllCommands());

        statisticsDisplay.Start();

        return result;
    }

    private void LoadMusicKeywords() {
        musicKeywords.Clear();
        var keywords = settingsManager.Settings.MusicCommandKeywords
            .Split(new[] { ';', ',', ' ' }, StringSplitOptions.RemoveEmptyEntries);

        foreach (var keyword in keywords) {
            var trimmedKeyword = keyword.Trim().ToLower();
            if (!string.IsNullOrEmpty(trimmedKeyword)) {
                musicKeywords.Add(trimmedKeyword);
            }
        }

        if (settingsManager.Settings.DebugMode) {
            WriteColor($"🎵 Загружено музыкальных ключевых слов: {musicKeywords.Count}\n", ConsoleColor.Cyan);
        }
    }

    private async void HandleChatCommand(object sender, (string username, string message) args) {
        if (!settingsManager.Settings.ChatEnabled)
            return;

        var message = args.message.ToLower();
        var username = args.username;

        // Проверяем музыкальные команды
        if (musicKeywords.Contains(message)) {
            await HandleMusicCommand(username);
            return;
        }

        if (commandManager.ProcessChatCommand(message, username)) {
            var command = commandManager.GetCommand(message);
            if (command != null) {
                if (settingsManager.Settings.DebugMode) {
                    WriteColor($"🔊 Активирована команда чата: {message} пользователем {username}\n", ConsoleColor.Cyan);
                }

                // Записываем статистику
                statisticsDisplay.RecordSoundActivation(username, message, 0); // Для чата стоимость 0

                audioPlayer.PlaySound(command.SoundFile, username, message);

                // Обновляем отображение статистики
                UpdateStatisticsDisplay();
            }
        }
    }

    private async Task HandleMusicCommand(string username) {
        try {
            var currentTrack = musicTracker.GetCurrentTrack();
            string response;

            if (currentTrack != null && !string.IsNullOrEmpty(currentTrack.Name)) {
                response = settingsManager.Settings.MusicResponseTemplate
                    .Replace("$name", username)
                    .Replace("$trackName", currentTrack.Name)
                    .Replace("$trackLink", string.IsNullOrEmpty(currentTrack.Link) ? "" : currentTrack.Link);
            } else {
                response = settingsManager.Settings.NoMusicResponseTemplate
                    .Replace("$name", username);
            }

            connectionManager.SendMessage(response);

            if (settingsManager.Settings.DebugMode) {
                WriteColor($"🎵 Обработана музыкальная команда для {username}: {response}\n", ConsoleColor.Cyan);
            }
        } catch (Exception ex) {
            if (settingsManager.Settings.DebugMode) {
                WriteColor($"❌ Ошибка обработки музыкальной команды: {ex.Message}\n", ConsoleColor.Red);
            }
        }
    }

    private async void HandleRewardCommand(object sender, (string command, string username) args) {
        if (!settingsManager.Settings.RewardsEnabled)
            return;

        // Обработка VIP наград по названию команды
        if (args.command == "VIP_PURCHASE" || args.command.Contains("Купить VIP")) {
            HandleVipPurchase(args.username);
            return;
        } else if (args.command == "VIP_STEAL" || args.command.Contains("Украсть VIP")) {
            HandleVipSteal(args.username);
            return;
        }

        // Обработка обычных звуковых команд
        if (commandManager.ProcessRewardCommand(args.command, args.username)) {
            var soundCommand = commandManager.GetCommand(args.command);
            if (soundCommand != null) {
                if (settingsManager.Settings.DebugMode) {
                    WriteColor($"🎁 Активирована награда: {args.command} пользователем {args.username}\n", ConsoleColor.Magenta);
                }

                // Записываем статистику с стоимостью
                statisticsDisplay.RecordSoundActivation(args.username, args.command, soundCommand.Cost);

                audioPlayer.PlaySound(soundCommand.SoundFile, args.username, args.command);

                // Обновляем отображение статистики
                UpdateStatisticsDisplay();
            }
        } else {
            if (settingsManager.Settings.DebugMode) {
                WriteColor($"⏳ Cooldown для команды {args.command} пользователя {args.username}\n", ConsoleColor.Yellow);
            }
        }
    }

    private async void HandleVipPurchase(string username) {
        if (await vipManager.PurchaseVip(username)) {
            // Записываем статистику
            statisticsDisplay.RecordVipPurchase(username);

            // Отправляем сообщение в чат
            connectionManager.SendMessage($"🎉 Поздравляем, {username}! Вы стали VIP на {settingsManager.Settings.VipDurationDays} дней!");

            // Обновляем отображение статистики
            UpdateStatisticsDisplay();
        } else {
            connectionManager.SendMessage($"❌ {username}, невозможно выдать VIP. Возможно, нет свободных слотов или вы уже VIP.");
        }
    }

    private async void HandleVipSteal(string thiefName) {
        var result = vipManager.StealVip(thiefName);

        if (result.success) {
            // Записываем статистику успешной кражи
            statisticsDisplay.RecordStealAttempt(thiefName, true, result.stolenFrom);

            string message = vipManager.GetRandomSuccessfulStealMessage(thiefName, result.stolenFrom);
            connectionManager.SendMessage(message);

            // Обновляем отображение статистики
            UpdateStatisticsDisplay();
        } else {
            // Записываем статистику неудачной кражи
            statisticsDisplay.RecordStealAttempt(thiefName, false);

            string message = vipManager.GetRandomFailedStealMessage(thiefName);
            connectionManager.SendMessage(message);

            // Бан на указанное время
            await connectionManager.BanUser(thiefName, settingsManager.Settings.VipStealBanTime);

            // Обновляем отображение статистики
            UpdateStatisticsDisplay();
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

    private void UpdateStatisticsDisplay() {
        // Этот метод будет вызывать обновление интерфейса
        // В реальной реализации нужно обновить консоль
        if (Program.IsInterfaceActive) {
            Program.RequestInterfaceUpdate();
        }
    }

    public BotSettings GetCurrentSettings() {
        return settingsManager.Settings;
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
    public bool VipRewardEnabled => settingsManager.Settings.EnableVipReward;
    public bool VipStealEnabled => settingsManager.Settings.EnableVipStealReward;

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

    public void ToggleVipReward() {
        settingsManager.Settings.EnableVipReward = !settingsManager.Settings.EnableVipReward;
        settingsManager.SaveSettings();
        WriteColor($"Покупка VIP: {(settingsManager.Settings.EnableVipReward ? "ВКЛ" : "ВЫКЛ")}\n",
                   settingsManager.Settings.EnableVipReward ? ConsoleColor.Green : ConsoleColor.Red);
    }

    public void ToggleVipStealReward() {
        settingsManager.Settings.EnableVipStealReward = !settingsManager.Settings.EnableVipStealReward;
        settingsManager.SaveSettings();
        WriteColor($"Воровство VIP: {(settingsManager.Settings.EnableVipStealReward ? "ВКЛ" : "ВЫКЛ")}\n",
                   settingsManager.Settings.EnableVipStealReward ? ConsoleColor.Green : ConsoleColor.Red);
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

        // Добавляем настройки VIP
        Console.Write("3 - Покупка VIP: ");
        WriteColor(settingsManager.Settings.EnableVipReward ? "ВКЛ" : "ВЫКЛ",
                   settingsManager.Settings.EnableVipReward ? ConsoleColor.Green : ConsoleColor.Red);
        Console.WriteLine();

        Console.Write("4 - Воровство VIP: ");
        WriteColor(settingsManager.Settings.EnableVipStealReward ? "ВКЛ" : "ВЫКЛ",
                   settingsManager.Settings.EnableVipStealReward ? ConsoleColor.Green : ConsoleColor.Red);
        Console.WriteLine();
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

        statisticsDisplay.Stop();

        if (disableRewards) {
            if (settingsManager.Settings.EnableVipReward || settingsManager.Settings.EnableVipStealReward) {
                await vipManager.DisableVipRewards();
            }

            if (rewardManager != null) {
                await rewardManager.DisableCustomRewards();
            }
        }

        musicTracker.Stop();

        await connectionManager.Disconnect(false);
    }

    public string GetAuthUrl() {
        var scopes = "channel:manage:redemptions chat:edit chat:read moderator:manage:banned_users channel:read:redemptions channel:manage:vips";
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

                // Проверяем все необходимые scope
                var requiredScopes = new[] {
                "channel:manage:redemptions",
                "moderator:manage:banned_users",
                "channel:read:redemptions",
                "channel:manage:vips"
            };

                var missingScopes = requiredScopes.Where(scope => !validated.Scopes.Contains(scope)).ToList();

                if (missingScopes.Any()) {
                    WriteColor($"Не хватает scopes: {string.Join(", ", missingScopes)}\n", ConsoleColor.Red);

                    if (missingScopes.Contains("moderator:manage:banned_users")) {
                        WriteColor("⚠️  Отсутствует scope для бана пользователей. Кража VIP не будет работать!\n", ConsoleColor.Red);
                    }
                    if (missingScopes.Contains("channel:manage:redemptions")) {
                        WriteColor("⚠️  Отсутствует scope для управления наградами. Награды не будут работать!\n", ConsoleColor.Red);
                    }
                    if (missingScopes.Contains("channel:read:redemptions")) {
                        WriteColor("⚠️  Отсутствует scope для чтения наград. Награды могут работать некорректно!\n", ConsoleColor.Red);
                    }
                    if (missingScopes.Contains("channel:manage:vips")) {
                        WriteColor("⚠️  Отсутствует scope для управления VIP. Функции VIP не будут работать!\n", ConsoleColor.Red);
                    }
                } else {
                    WriteColor("✅ Все необходимые scopes присутствуют\n", ConsoleColor.Green);
                }
            }
        } catch (Exception ex) {
            WriteColor($"❌ Ошибка проверки токена: {ex.Message}\n", ConsoleColor.Red);
        }
    }

    public void DisplayLiveStatistics(int _left, int _top) {
        statisticsDisplay?.DisplayStatistics(_left, _top);
    }

    public void StopStatistics() {
        statisticsDisplay?.Stop();
    }

    private void WriteDebug(string text, ConsoleColor color) {
        if (true) {
            WriteColor(text, color);
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