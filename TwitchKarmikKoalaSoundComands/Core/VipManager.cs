using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using TwitchLib.Api;
using TwitchLib.Api.Helix.Models.ChannelPoints;
using TwitchLib.Api.Helix.Models.ChannelPoints.CreateCustomReward;
using TwitchLib.Api.Helix.Models.ChannelPoints.UpdateCustomReward;
using TwitchLib.Api.Helix.Models.Users;

public class VipManager {
    private TwitchAPI api;
    private string channelId;
    private BotSettings settings;
    private string vipListFile = "config/vip_list.json";
    private List<VipItem> vipList = new List<VipItem>();
    private Random random = new Random();

    // Фразы для краж
    private List<string> successfulStealMessages = new List<string>();
    private List<string> failedStealMessages = new List<string>();

    public string LastError { get; private set; } = "";
    private const int MAX_REWARDS = 50; // Twitch limit
    private const int MAX_VIP_LIMIT = 100; // Максимальный лимит VIP для Twitch

    public VipManager(TwitchAPI api, string channelId, BotSettings settings) {
        this.api = api;
        this.channelId = channelId;
        this.settings = settings;

        // Создаем директорию config если не существует
        if (!Directory.Exists("config")) {
            Directory.CreateDirectory("config");
        }

        LoadVipList();
        LoadStealMessages();
    }

    public async Task<bool> CreateVipRewards() {
        try {
            WriteDebug($"=== СОЗДАНИЕ VIP НАГРАД ===\n", ConsoleColor.Cyan);

            if (string.IsNullOrEmpty(channelId)) {
                LastError = "ChannelId не доступен";
                WriteColor($"❌ {LastError}\n", ConsoleColor.Red);
                return false;
            }

            var existingRewardsResponse = await api.Helix.ChannelPoints.GetCustomRewardAsync(channelId, new List<string>(), true);

            if (existingRewardsResponse == null || existingRewardsResponse.Data == null) {
                LastError = "Не удалось получить список существующих наград";
                WriteColor($"❌ {LastError}\n", ConsoleColor.Red);
                return false;
            }

            var existingRewards = existingRewardsResponse.Data;
            WriteDebug($"✅ Найдено существующих наград: {existingRewards.Length}\n", ConsoleColor.Green);

            // Проверяем количество слотов для наград
            int currentRewardsCount = existingRewards.Length;
            int neededSlots = 0;

            if (settings.EnableVipReward && !existingRewards.Any(r => r.Title == "Купить ВИП")) {
                neededSlots++;
            }
            if (settings.EnableVipStealReward && !existingRewards.Any(r => r.Title == "Украсть ВИП")) {
                neededSlots++;
            }

            if (currentRewardsCount + neededSlots > MAX_REWARDS) {
                LastError = $"Недостаточно слотов для наград. Доступно: {MAX_REWARDS - currentRewardsCount}, нужно: {neededSlots}";
                WriteColor($"❌ {LastError}\n", ConsoleColor.Red);
                WriteColor($"ℹ️ Удалите неиспользуемые награды в панели Twitch или деактивируйте некоторые награды в боте\n", ConsoleColor.Yellow);
                return false;
            }

            bool vipPurchaseCreated = false;
            bool vipStealCreated = false;

            await Task.Delay(500);
            // Создаем/обновляем награду "Купить ВИП"
            if (settings.EnableVipReward) {
                vipPurchaseCreated = await CreateOrUpdateVipReward(
                    "Купить VIP",
                    settings.VipRewardCost,
                    settings.VipCooldown * 60,
                    "#FFD700",
                    existingRewards
                );
            }

            await Task.Delay(500);
            // Создаем/обновляем награду "Украсть ВИП"
            if (settings.EnableVipStealReward) {
                vipStealCreated = await CreateOrUpdateVipReward(
                    "Украсть VIP",
                    settings.VipStealCost,
                    600, // 10 минут фиксированно
                    "#FF0000",
                    existingRewards
                );
            }

            WriteDebug($"\n=== РЕЗУЛЬТАТ СОЗДАНИЯ VIP НАГРАД ===\n", ConsoleColor.Cyan);
            if (settings.EnableVipReward) {
                WriteDebug($"Купить VIP: {(vipPurchaseCreated ? "✅" : "❌")}\n",
                          vipPurchaseCreated ? ConsoleColor.Green : ConsoleColor.Red);
            }
            if (settings.EnableVipStealReward) {
                WriteDebug($"Украсть VIP: {(vipStealCreated ? "✅" : "❌")}\n",
                          vipStealCreated ? ConsoleColor.Green : ConsoleColor.Red);
            }

            return (settings.EnableVipReward ? vipPurchaseCreated : true) &&
                   (settings.EnableVipStealReward ? vipStealCreated : true);

        } catch (Exception ex) {
            LastError = $"Критическая ошибка создания VIP наград: {ex.Message}";
            WriteDebug($"❌ {LastError}\n", ConsoleColor.Red);
            return false;
        }
    }

    public async Task<int> GetAvailableRewardSlots() {
        try {
            if (string.IsNullOrEmpty(channelId)) {
                return 0;
            }

            var existingRewardsResponse = await api.Helix.ChannelPoints.GetCustomRewardAsync(channelId, new List<string>(), true);

            if (existingRewardsResponse?.Data == null) {
                return 0;
            }

            int currentRewards = existingRewardsResponse.Data.Length;
            return Math.Max(0, MAX_REWARDS - currentRewards);
        } catch (Exception ex) {
            WriteDebug($"❌ Ошибка получения количества слотов наград: {ex.Message}\n", ConsoleColor.Red);
            return 0;
        }
    }

    public async Task<List<CustomReward>> GetAllRewards() {
        try {
            if (string.IsNullOrEmpty(channelId)) {
                return new List<CustomReward>();
            }

            var existingRewardsResponse = await api.Helix.ChannelPoints.GetCustomRewardAsync(channelId, new List<string>(), true);
            return existingRewardsResponse?.Data?.ToList() ?? new List<CustomReward>();
        } catch (Exception ex) {
            WriteDebug($"❌ Ошибка получения списка наград: {ex.Message}\n", ConsoleColor.Red);
            return new List<CustomReward>();
        }
    }

    private async Task<bool> CreateOrUpdateVipReward(string rewardTitle, int cost, int cooldownSeconds = 0, string color = "", CustomReward[] existingRewards = null) {
        try {
            WriteDebug($"🔍 Обрабатываем VIP награду: '{rewardTitle}'\n", ConsoleColor.Cyan);

            var existingReward = existingRewards?.FirstOrDefault(r =>
                r.Title.ToLower() == rewardTitle.ToLower());

            if (existingReward != null) {
                WriteDebug($"  ✅ Награда существует, обновляю...\n", ConsoleColor.Green);

                var updateRequest = new UpdateCustomRewardRequest {
                    Cost = cost,
                    IsEnabled = true
                };

                try {
                    var updatedReward = await api.Helix.ChannelPoints.UpdateCustomRewardAsync(
                        channelId, existingReward.Id, updateRequest);

                    if (updatedReward != null) {
                        WriteColor($"  ✅ Награда '{rewardTitle}' обновлена\n", ConsoleColor.Green);
                        return true;
                    }
                } catch (Exception updateEx) {
                    WriteColor($"  ❌ Ошибка обновления награды '{rewardTitle}': {updateEx.Message}\n", ConsoleColor.Red);
                    return await CreateNewVipReward(rewardTitle, cost, cooldownSeconds, color);
                }
            } else {
                WriteDebug($"  ➕ Создаю новую VIP награду...\n", ConsoleColor.Yellow);
                return await CreateNewVipReward(rewardTitle, cost, cooldownSeconds, color);
            }

            return false;
        } catch (Exception ex) {
            WriteColor($"  ❌ Критическая ошибка обработки награды '{rewardTitle}': {ex.Message}\n", ConsoleColor.Red);
            return false;
        }
    }

    private async Task<bool> CreateNewVipReward(string rewardTitle, int cost, int cooldownSeconds, string color) {
        try {
            var createRequest = new CreateCustomRewardsRequest {
                Title = rewardTitle,
                Cost = cost,
                IsEnabled = true,
                BackgroundColor = color,
                IsUserInputRequired = false
            };

            WriteDebug($"  📤 Отправляю запрос создания...\n", ConsoleColor.Yellow);

            var result = await api.Helix.ChannelPoints.CreateCustomRewardsAsync(channelId, createRequest);

            if (result != null && result.Data.Length > 0) {
                WriteColor($"  ✅ Награда '{rewardTitle}' создана\n", ConsoleColor.Green);
                return true;
            } else {
                WriteColor($"  ❌ Награда '{rewardTitle}' не создана - пустой ответ\n", ConsoleColor.Red);
            }
        } catch (Exception createEx) {
            WriteColor($"  ❌ Ошибка создания награды '{rewardTitle}': {createEx.Message}\n", ConsoleColor.Red);
        }

        return false;
    }

    public async Task DisableVipRewards() {
        try {
            if (string.IsNullOrEmpty(channelId)) {
                WriteColor("❌ ChannelId не доступен для отключения VIP наград\n", ConsoleColor.Red);
                return;
            }

            var rewards = await api.Helix.ChannelPoints.GetCustomRewardAsync(channelId, onlyManageableRewards: true);
            var vipRewards = rewards.Data.Where(r => r.Title == "Купить VIP" || r.Title == "Украсть VIP").ToList();

            foreach (var reward in vipRewards) {
                try {
                    var updateRequest = new UpdateCustomRewardRequest { IsEnabled = false };
                    await api.Helix.ChannelPoints.UpdateCustomRewardAsync(channelId, reward.Id, updateRequest);
                    WriteDebug($"Награда '{reward.Title}' отключена\n", ConsoleColor.Yellow);
                    await Task.Delay(200);
                } catch (Exception ex) {
                    WriteDebug($"Ошибка отключения награды '{reward.Title}': {ex.Message}\n", ConsoleColor.Red);
                }
            }
        } catch (Exception ex) {
            WriteDebug($"Ошибка при отключении VIP наград: {ex.Message}\n", ConsoleColor.Red);
        }
    }

    // УПРОЩЕННЫЙ ПОДХОД: Используем Chat API для проверки VIP статуса
    // Так как Helix API для VIP требует особых разрешений

    // НОВЫЙ МЕТОД: Упрощенная проверка VIP через доступные методы
    private async Task<List<string>> GetVipListFallback() {
        try {
            // Поскольку прямой API для получения VIP списка может быть недоступен,
            // используем упрощенный подход - считаем что все кто купил VIP через бота являются VIP
            // и добавляем известных VIP вручную если нужно

            var vipUsers = new List<string>();

            // Добавляем VIP из нашей базы данных (тех кто купил через бота)
            foreach (var vip in vipList.Where(v => !v.IsExpired)) {
                if (!vipUsers.Contains(vip.Username)) {
                    vipUsers.Add(vip.Username);
                }
            }

            // Можно добавить известных VIP вручную здесь если нужно
            // vipUsers.Add("известный_вип");

            WriteDebug($"✅ Используется локальный список VIP: {vipUsers.Count} пользователей\n", ConsoleColor.Green);
            return vipUsers;

        } catch (Exception ex) {
            WriteDebug($"❌ Ошибка получения локального списка VIP: {ex.Message}\n", ConsoleColor.Red);
            return new List<string>();
        }
    }

    // ОБНОВЛЕННЫЙ МЕТОД: Покупка VIP
    public async Task<bool> PurchaseVip(string username) {
        try {
            // Получаем текущий список VIP
            var currentVips = await GetVipListFallback();

            // Проверяем лимит VIP
            if (currentVips.Count >= MAX_VIP_LIMIT) {
                WriteColor($"❌ Достигнут лимит VIP пользователей на канале ({currentVips.Count}/{MAX_VIP_LIMIT})\n", ConsoleColor.Red);
                return false;
            }

            // Проверяем, не является ли пользователь уже VIP
            if (currentVips.Any(v => v.Equals(username, StringComparison.OrdinalIgnoreCase))) {
                WriteColor($"❌ {username} уже является VIP\n", ConsoleColor.Red);
                return false;
            }

            // Добавляем в нашу базу для отслеживания времени
            var existingVip = vipList.FirstOrDefault(v => v.Username.Equals(username, StringComparison.OrdinalIgnoreCase));
            if (existingVip != null) {
                // Обновляем существующую запись
                existingVip.GrantDate = DateTime.Now;
                existingVip.ExpiryDate = DateTime.Now.AddDays(settings.VipDurationDays);
                WriteColor($"✅ {username} продлил VIP на {settings.VipDurationDays} дней\n", ConsoleColor.Green);
            } else {
                // Добавляем нового VIP
                var vipItem = new VipItem(username, DateTime.Now, settings.VipDurationDays);
                vipList.Add(vipItem);
                WriteColor($"✅ {username} стал VIP на {settings.VipDurationDays} дней\n", ConsoleColor.Green);
            }

            SaveVipList();
            return true;

        } catch (Exception ex) {
            WriteColor($"❌ Ошибка при покупке VIP для {username}: {ex.Message}\n", ConsoleColor.Red);
            return false;
        }
    }

    // ОБНОВЛЕННЫЙ МЕТОД: Получение количества активных VIP
    public async Task<int> GetActiveVipCountAsync() {
        try {
            var vips = await GetVipListFallback();
            return vips.Count;
        } catch (Exception ex) {
            WriteDebug($"❌ Ошибка получения количества VIP: {ex.Message}\n", ConsoleColor.Red);
            return vipList.Count(v => !v.IsExpired);
        }
    }

    // Синхронная версия для обратной совместимости
    public int GetActiveVipCount() {
        try {
            var task = Task.Run(async () => await GetActiveVipCountAsync());
            task.Wait(TimeSpan.FromSeconds(3));
            return task.IsCompleted ? task.Result : vipList.Count(v => !v.IsExpired);
        } catch {
            return vipList.Count(v => !v.IsExpired);
        }
    }

    // ОБНОВЛЕННЫЙ МЕТОД: Получение списка VIP
    public async Task<List<string>> GetVipUsersAsync() {
        try {
            return await GetVipListFallback();
        } catch (Exception ex) {
            WriteDebug($"❌ Ошибка получения списка VIP: {ex.Message}\n", ConsoleColor.Red);
            return vipList.Where(v => !v.IsExpired).Select(v => v.Username).ToList();
        }
    }

    // Синхронная версия
    public List<string> GetVipUsers() {
        try {
            var task = Task.Run(async () => await GetVipUsersAsync());
            task.Wait(TimeSpan.FromSeconds(3));
            return task.IsCompleted ? task.Result : vipList.Where(v => !v.IsExpired).Select(v => v.Username).ToList();
        } catch {
            return vipList.Where(v => !v.IsExpired).Select(v => v.Username).ToList();
        }
    }

    // ОБНОВЛЕННЫЙ МЕТОД: Проверка VIP статуса
    public async Task<bool> IsVipAsync(string username) {
        try {
            var vips = await GetVipListFallback();
            return vips.Any(v => v.Equals(username, StringComparison.OrdinalIgnoreCase));
        } catch (Exception ex) {
            WriteDebug($"❌ Ошибка проверки VIP статуса: {ex.Message}\n", ConsoleColor.Red);
            return vipList.Any(v => v.Username.Equals(username, StringComparison.OrdinalIgnoreCase) && !v.IsExpired);
        }
    }

    // Синхронная версия
    public bool IsVip(string username) {
        try {
            var task = Task.Run(async () => await IsVipAsync(username));
            task.Wait(TimeSpan.FromSeconds(3));
            return task.IsCompleted ? task.Result : vipList.Any(v => v.Username.Equals(username, StringComparison.OrdinalIgnoreCase) && !v.IsExpired);
        } catch {
            return vipList.Any(v => v.Username.Equals(username, StringComparison.OrdinalIgnoreCase) && !v.IsExpired);
        }
    }

    // ОБНОВЛЕННЫЙ МЕТОД: Кража VIP
    public async Task<(bool success, string stolenFrom)> StealVipAsync(string thiefName) {
        try {
            // Проверяем шанс кражи
            if (random.Next(100) >= settings.VipStealChance) {
                WriteColor($"❌ {thiefName} не смог украсть VIP (шанс {settings.VipStealChance}%)\n", ConsoleColor.Red);
                return (false, null);
            }

            // Получаем текущий список VIP
            var currentVips = await GetVipListFallback();

            // Ищем доступных жертв (исключая самого вора)
            var availableVictims = currentVips
                .Where(v => !v.Equals(thiefName, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (availableVictims.Count == 0) {
                WriteColor($"❌ Нет подходящих жертв для кражи VIP\n", ConsoleColor.Red);
                return (false, null);
            }

            // Выбираем случайную жертву
            var stolenFrom = availableVictims[random.Next(availableVictims.Count)];

            // Обновляем нашу базу данных
            var victimVip = vipList.FirstOrDefault(v => v.Username.Equals(stolenFrom, StringComparison.OrdinalIgnoreCase));
            if (victimVip != null) {
                vipList.Remove(victimVip);
            }

            // Добавляем/обновляем VIP вору
            var existingThiefVip = vipList.FirstOrDefault(v => v.Username.Equals(thiefName, StringComparison.OrdinalIgnoreCase));
            if (existingThiefVip != null) {
                existingThiefVip.GrantDate = DateTime.Now;
                existingThiefVip.ExpiryDate = DateTime.Now.AddDays(settings.VipDurationDays);
            } else {
                var thiefVip = new VipItem(thiefName, DateTime.Now, settings.VipDurationDays);
                vipList.Add(thiefVip);
            }

            SaveVipList();

            WriteColor($"✅ {thiefName} украл VIP у {stolenFrom}\n", ConsoleColor.Green);
            return (true, stolenFrom);

        } catch (Exception ex) {
            WriteColor($"❌ Ошибка при краже VIP: {ex.Message}\n", ConsoleColor.Red);
            return (false, null);
        }
    }

    // Синхронная версия для обратной совместимости
    public (bool success, string stolenFrom) StealVip(string thiefName) {
        try {
            var task = Task.Run(async () => await StealVipAsync(thiefName));
            task.Wait(TimeSpan.FromSeconds(3));
            return task.IsCompleted ? task.Result : (false, null);
        } catch {
            return (false, null);
        }
    }

    public string GetRandomSuccessfulStealMessage(string thiefName, string preyName) {
        if (successfulStealMessages.Count == 0)
            return $"{thiefName} украл VIP у {preyName}!";

        var message = successfulStealMessages[random.Next(successfulStealMessages.Count)];
        return message.Replace("$thiefName", thiefName).Replace("$preyName", preyName);
    }

    public string GetRandomFailedStealMessage(string thiefName) {
        if (failedStealMessages.Count == 0)
            return $"{thiefName} попытался украсть VIP и был наказан!";

        var message = failedStealMessages[random.Next(failedStealMessages.Count)];
        return message.Replace("$thiefName", thiefName);
    }

    private void LoadVipList() {
        if (File.Exists(vipListFile)) {
            try {
                string json = File.ReadAllText(vipListFile);
                vipList = JsonSerializer.Deserialize<List<VipItem>>(json) ?? new List<VipItem>();
                WriteColor($"✅ Загружено VIP записей: {vipList.Count}\n", ConsoleColor.Green);
            } catch (Exception ex) {
                WriteColor($"❌ Ошибка загрузки списка VIP: {ex.Message}\n", ConsoleColor.Red);
                vipList = new List<VipItem>();
            }
        } else {
            WriteColor("ℹ️ Файл списка VIP не найден, создан новый список\n", ConsoleColor.Yellow);
            vipList = new List<VipItem>();
        }
    }

    private void SaveVipList() {
        try {
            string json = JsonSerializer.Serialize(vipList, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(vipListFile, json);
        } catch (Exception ex) {
            WriteColor($"❌ Ошибка сохранения списка VIP: {ex.Message}\n", ConsoleColor.Red);
        }
    }

    private void LoadStealMessages() {
        try {
            // Загружаем фразы для удачных краж
            if (File.Exists("config/successful_steal_messages.txt")) {
                successfulStealMessages = File.ReadAllLines("config/successful_steal_messages.txt")
                    .Where(line => !string.IsNullOrWhiteSpace(line) && !line.StartsWith("#"))
                    .ToList();
            } else {
                successfulStealMessages = new List<string>
                {
                    "$thiefName коварно украл VIP у $preyName!",
                    "VIP перешел от $preyName к $thiefName в результате дерзкой кражи!",
                    "$thiefName стащил VIP прямо из-под носа $preyName!",
                    "Невероятно! $thiefName украл VIP статус у $preyName!"
                };
                File.WriteAllLines("config/successful_steal_messages.txt", successfulStealMessages);
                WriteColor("✅ Создан файл фраз для удачных краж VIP\n", ConsoleColor.Green);
            }

            // Загружаем фразы для неудачных краж
            if (File.Exists("config/failed_steal_messages.txt")) {
                failedStealMessages = File.ReadAllLines("config/failed_steal_messages.txt")
                    .Where(line => !string.IsNullOrWhiteSpace(line) && !line.StartsWith("#"))
                    .ToList();
            } else {
                failedStealMessages = new List<string>
                {
                    "$thiefName попытался украсть VIP, но был пойман!",
                    "Кража VIP $thiefName провалилась!",
                    "$thiefName не смог украсть VIP и будет наказан!",
                    "Провал! $thiefName был замечен при попытке кражи VIP!"
                };
                File.WriteAllLines("config/failed_steal_messages.txt", failedStealMessages);
                WriteColor("✅ Создан файл фраз для неудачных краж VIP\n", ConsoleColor.Green);
            }
        } catch (Exception ex) {
            WriteColor($"❌ Ошибка загрузки фраз для краж: {ex.Message}\n", ConsoleColor.Red);
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