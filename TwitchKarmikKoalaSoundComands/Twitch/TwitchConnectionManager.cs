using TwitchLib.Api;
using TwitchLib.Client;
using TwitchLib.Client.Events;
using TwitchLib.Client.Models;
using TwitchLib.Communication.Clients;
using TwitchLib.Communication.Models;
using TwitchLib.PubSub;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

public class TwitchConnectionManager {
    private TwitchClient client;
    private TwitchAPI api;
    private TwitchPubSub pubSub;
    private BotSettings settings;
    private string channelId;

    public event EventHandler<(string username, string message)> OnChatCommand;
    public event EventHandler<(string command, string username)> OnRewardRedeemed;
    public event EventHandler<(string rewardId, string rewardTitle)> OnRewardMappingUpdated;

    private Dictionary<string, string> rewardIdToCommandMap;
    private List<string> createdRewardIds = new List<string>();

    // Добавляем свойства для доступа к приватным полям
    public TwitchAPI Api => api;
    public string ChannelId => channelId;

    public TwitchConnectionManager(BotSettings settings) {
        this.settings = settings;
        rewardIdToCommandMap = new Dictionary<string, string>();
    }

    public async Task<(bool authOk, string authError, bool rewardsOk, string rewardsError, bool chatOk, string chatError)> Connect() {
        api = new TwitchAPI();
        api.Settings.ClientId = settings.ClientId;

        string apiToken = settings.OAuthToken;
        if (apiToken.StartsWith("oauth:")) {
            apiToken = apiToken.Substring(6);
        }
        api.Settings.AccessToken = apiToken;

        string authError = "";
        try {
            var users = await api.Helix.Users.GetUsersAsync(logins: new List<string> { settings.ChannelName });
            if (users.Users.Length > 0) {
                channelId = users.Users[0].Id;
                if (settings.DebugMode) {
                    WriteDebug($"✅ Успешно! Получен channelId: {channelId} для канала {settings.ChannelName}\n", ConsoleColor.Green);
                }
            } else {
                authError = $"Канал {settings.ChannelName} не найден";
                return (false, authError, false, "", false, "");
            }
        } catch (Exception ex) {
            authError = $"Ошибка получения channelId: {ex.Message}";
            WriteDebug($"❌ Ошибка: {authError}\n", ConsoleColor.Red);
            return (false, authError, false, "", false, "");
        }

        bool chatOk = false;
        bool rewardsOk = false;
        string chatError = "";
        string rewardsError = "";

        if (settings.ChatEnabled) {
            try {
                var credentials = new ConnectionCredentials(settings.BotUsername, settings.OAuthToken);
                var clientOptions = new ClientOptions {
                    MessagesAllowedInPeriod = 750,
                    ThrottlingPeriod = TimeSpan.FromSeconds(30)
                };

                var customClient = new WebSocketClient(clientOptions);
                client = new TwitchClient(customClient);

                client.Initialize(credentials, settings.ChannelName);
                client.OnJoinedChannel += OnJoinedChannel;
                client.OnMessageReceived += OnMessageReceived;

                client.Connect();
                chatOk = await WaitForChatConnection();
                if (!chatOk) {
                    chatError = "Таймаут подключения к чату";
                } else {
                    WriteDebug("✅ Чат подключен успешно\n", ConsoleColor.Green);
                }
            } catch (Exception ex) {
                chatError = $"Ошибка чата: {ex.Message}";
            }
        }

        if (settings.RewardsEnabled) {
            // Награды будут создаваться позже через RewardManager
            rewardsOk = true;
        }

        if (settings.RewardsEnabled && rewardsOk) {
            try {
                InitializePubSub();
                WriteDebug("✅ PubSub для наград инициализирован\n", ConsoleColor.Green);
            } catch (Exception ex) {
                WriteDebug($"❌ Ошибка инициализации PubSub: {ex.Message}\n", ConsoleColor.Red);
            }
        }

        return (true, "", rewardsOk, rewardsError, chatOk, chatError);
    }

    private async Task<bool> WaitForChatConnection() {
        for (int i = 0; i < 10; i++) {
            if (client?.IsConnected == true)
                return true;
            await Task.Delay(1000);
        }
        return client?.IsConnected == true;
    }

    private void OnJoinedChannel(object sender, OnJoinedChannelArgs e) {
        //client.SendMessage(e.Channel, "Бот звуков подключен! Используйте !звуки для списка команд.");
    }

    private void OnMessageReceived(object sender, OnMessageReceivedArgs e) {
        if (!settings.ChatEnabled)
            return;

        var message = e.ChatMessage.Message.ToLower();
        var username = e.ChatMessage.Username;
        OnChatCommand?.Invoke(this, (username, message));
    }

    private void InitializePubSub() {
        pubSub = new TwitchPubSub();
        pubSub.OnRewardRedeemed += OnRewardRedeemedHandler;
        pubSub.OnPubSubServiceConnected += OnPubSubServiceConnected;
        pubSub.OnListenResponse += OnListenResponse;

        pubSub.Connect();

        Task.Run(async () => {
            await Task.Delay(2000);
            pubSub.ListenToRewards(channelId);
            pubSub.SendTopics(settings.OAuthToken.Replace("oauth:", ""));
        });
    }

    private void OnRewardRedeemedHandler(object sender, TwitchLib.PubSub.Events.OnRewardRedeemedArgs e) {
        if (!settings.RewardsEnabled)
            return;

        if (settings.DebugMode) {
            WriteDebug($"🎁 Активирована награда: {e.RewardTitle} пользователем {e.DisplayName}\n", ConsoleColor.Magenta);
        }

        if (rewardIdToCommandMap.TryGetValue(e.RewardId.ToString(), out string command)) {
            OnRewardRedeemed?.Invoke(this, (command, e.DisplayName));
        } else {
            // Если не нашли по ID, попробуем найти по названию
            OnRewardMappingUpdated?.Invoke(this, (e.RewardId.ToString(), e.RewardTitle));
        }
    }

    private void OnPubSubServiceConnected(object sender, EventArgs e) {
        WriteDebug("✅ PubSub подключен\n", ConsoleColor.Green);
    }

    private void OnListenResponse(object sender, TwitchLib.PubSub.Events.OnListenResponseArgs e) {
        if (!e.Successful) {
            WriteDebug($"❌ Ошибка подписки на тему: {e.Topic}\n", ConsoleColor.Red);
        } else {
            WriteDebug($"✅ Успешная подписка на тему: {e.Topic}\n", ConsoleColor.Green);
        }
    }

    public async Task Disconnect(bool disableRewards = true) {
        client?.Disconnect();
        pubSub?.Disconnect();
        WriteDebug("Соединение закрыто\n", ConsoleColor.Yellow);
    }

    // Метод для добавления сопоставления наград
    public void AddRewardMapping(string rewardId, string command) {
        rewardIdToCommandMap[rewardId] = command;
        if (settings.DebugMode) {
            WriteDebug($"✅ Сопоставлена награда ID '{rewardId}' -> команда '{command}'\n", ConsoleColor.Green);
        }
    }

    private void WriteDebug(string text, ConsoleColor color) {
        if (settings.DebugMode) {
            WriteColor(text, color);
        }
    }

    private void WriteColor(string text, ConsoleColor color) {
        var originalColor = Console.ForegroundColor;
        Console.ForegroundColor = color;
        Console.Write(text);
        Console.ForegroundColor = originalColor;
    }
}