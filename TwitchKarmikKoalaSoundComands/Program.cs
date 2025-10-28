using System;
using System.Threading.Tasks;

class Program {
    private static TwitchBot bot;
    private static bool running = true;
    private static bool alreadyDisconnected = false;

    private static Timer interfaceUpdateTimer;
    public static bool IsInterfaceActive { get; private set; } = true;
    private static bool interfaceUpdateRequested = false;

    static async Task Main(string[] args) {
        Console.Title = "Twitch Sound Bot (с) RUTONY 2025";

        Console.OutputEncoding = System.Text.Encoding.UTF8;

        try {
            Console.InputEncoding = System.Text.Encoding.UTF8;
        } catch {
        }

        try {
            bot = new TwitchBot();

            WriteColor("Подключение...\n", ConsoleColor.Yellow);

            var (authOk, authError, rewardsOk, rewardsError, chatOk, chatError) = await bot.Connect();

            if (bot.IsDebugMode) {
                await bot.CheckTokenScopes();
                Console.WriteLine("Нажмите любую клавишу чтобы продолжить...");
                Console.ReadKey();
            }

            // Запускаем таймер для обновления интерфейса
            StartInterfaceUpdateTimer();

            Console.Clear();
            DisplayStatus(authOk, authError, rewardsOk, rewardsError, chatOk, chatError);

            while (running) {
                var key = Console.ReadKey(true);

                switch (key.KeyChar) {
                    case 's':
                    case 'S':
                    case 'ы':
                    case 'Ы':
                        IsInterfaceActive = false;
                        bot.ShowStatistics();
                        WaitForBack();
                        IsInterfaceActive = true;
                        Console.Clear();
                        DisplayStatus(authOk, authError, rewardsOk, rewardsError, chatOk, chatError);
                        break;

                    case 'p':
                    case 'P':
                    case 'з':
                    case 'З':
                        IsInterfaceActive = false;
                        HandlePreferences();
                        IsInterfaceActive = true;
                        Console.Clear();
                        DisplayStatus(authOk, authError, rewardsOk, rewardsError, chatOk, chatError);
                        break;

                    case 'r':
                    case 'R':
                    case 'к':
                    case 'К':
                        IsInterfaceActive = false;
                        Console.Clear();
                        WriteColor("Перезагрузка...\n", ConsoleColor.Yellow);

                        try {
                            // Останавливаем таймер
                            interfaceUpdateTimer?.Dispose();

                            // Останавливаем статистику перед перезагрузкой
                            bot.StopStatistics();

                            // Отключаем без деактивации наград
                            await bot.Disconnect(false);

                            // Небольшая пауза для очистки
                            await Task.Delay(1000);

                            // Создаем полностью новый экземпляр
                            bot = new TwitchBot();

                            // Переподключаем
                            (authOk, authError, rewardsOk, rewardsError, chatOk, chatError) = await bot.Connect();

                            // Перезапускаем таймер
                            StartInterfaceUpdateTimer();

                            IsInterfaceActive = true;
                            Console.Clear();
                            DisplayStatus(authOk, authError, rewardsOk, rewardsError, chatOk, chatError);
                        } catch (Exception ex) {
                            WriteColor($"❌ Ошибка перезагрузки: {ex.Message}\n", ConsoleColor.Red);
                            WriteColor("Нажмите любую клавишу для продолжения...\n", ConsoleColor.Yellow);
                            Console.ReadKey();
                        }
                        break;

                    case 'q':
                    case 'Q':
                    case 'й':
                    case 'Й':
                        if (!alreadyDisconnected) {
                            IsInterfaceActive = false;
                            alreadyDisconnected = true;
                            running = false;
                            interfaceUpdateTimer?.Dispose();
                            WriteColor("Выход без отключения наград...\n", ConsoleColor.Yellow);
                            await bot.Disconnect(false);
                        }
                        break;

                    case 'w':
                    case 'W':
                    case 'ц':
                    case 'Ц':
                        if (!alreadyDisconnected) {
                            IsInterfaceActive = false;
                            alreadyDisconnected = true;
                            running = false;
                            interfaceUpdateTimer?.Dispose();
                            WriteColor("Выход с отключением наград...\n", ConsoleColor.Yellow);
                            await bot.Disconnect(true);
                        }
                        break;
                    default:
                        // При нажатии любой другой клавиши обновляем отображение
                        Console.Clear();
                        DisplayStatus(authOk, authError, rewardsOk, rewardsError, chatOk, chatError);
                        break;
                }
            }

            interfaceUpdateTimer?.Dispose();
            WriteColor("Программа завершена.\n", ConsoleColor.Yellow);
        } catch (Exception ex) {
            WriteColor($"❌ Ошибка: {ex.Message}\n", ConsoleColor.Red);
            WriteColor("Нажмите любую клавишу...\n", ConsoleColor.Gray);
            Console.ReadKey();
        }
    }

    private static void StartInterfaceUpdateTimer() {
        interfaceUpdateTimer = new Timer(_ => {
            if (interfaceUpdateRequested && IsInterfaceActive) {
                interfaceUpdateRequested = false;
                // В консольных приложениях сложно обновить интерфейс без перерисовки
                // Поэтому просто перерисовываем весь интерфейс при событиях
                if (bot != null) {
                    var settings = bot.GetCurrentSettings();
                    var (authOk, authError, rewardsOk, rewardsError, chatOk, chatError) =
                        (true, "", true, "", true, ""); // Упрощенные значения для перерисовки

                    Console.Clear();
                    DisplayStatus(authOk, authError, rewardsOk, rewardsError, chatOk, chatError);
                }
            }
        }, null, 0, 1000); // Проверяем каждую секунду
    }

    public static void RequestInterfaceUpdate() {
        interfaceUpdateRequested = true;
    }

    static void DisplayStatus(bool authOk, string authError, bool rewardsOk, string rewardsError, bool chatOk, string chatError) {
        Console.Clear(); // Полностью очищаем консоль

        int currentLine = 0;

        // Выводим основную информацию
        Console.WriteLine($"Канал: {bot.GetChannelName()}");
        currentLine++;

        Console.Write("Авторизация: ");
        if (authOk) {
            WriteColor("✅ OK\n", ConsoleColor.Green);
        } else {
            WriteColor("❌ Ошибка\n", ConsoleColor.Red);
            if (!string.IsNullOrEmpty(authError)) {
                WriteColor($"  {authError}\n", ConsoleColor.Yellow);
            }
        }
        currentLine++;

        Console.WriteLine($"Количество звуковых команд: {bot.GetTotalCommands()}");
        Console.WriteLine($"  - Для чата: {bot.GetChatEnabledCount()}");
        Console.WriteLine($"  - Для наград: {bot.GetRewardEnabledCount()}");
        currentLine += 3;

        var missingFiles = bot.GetMissingFiles();
        if (missingFiles.Count > 0) {
            WriteColor($"Отсутствуют файлы: {missingFiles.Count}\n", ConsoleColor.Yellow);
            foreach (var file in missingFiles) {
                WriteColor($"  {file}\n", ConsoleColor.Yellow);
                currentLine++;
            }
            currentLine++;
        }

        Console.Write("Награды: ");
        if (bot.RewardsEnabled) {
            WriteColor(rewardsOk ? "✅ OK" : "❌ Ошибка", rewardsOk ? ConsoleColor.Green : ConsoleColor.Red);
            Console.WriteLine($" ({(bot.RewardsEnabled ? "ВКЛ" : "ВЫКЛ")})");
            if (!rewardsOk) {
                string errorDetails = bot.GetLastRewardsError();
                if (!string.IsNullOrEmpty(errorDetails)) {
                    WriteColor($"  {errorDetails}\n", ConsoleColor.Yellow);
                    currentLine++;
                } else {
                    WriteColor($"  Неизвестная ошибка при создании/обновлении наград\n", ConsoleColor.Yellow);
                    currentLine++;
                }
            }
        } else {
            WriteColor("ВЫКЛ\n", ConsoleColor.Gray);
        }
        currentLine++;

        Console.Write("Команды в чате: ");
        if (bot.ChatEnabled) {
            WriteColor(chatOk ? "✅ OK" : "❌ Ошибка", chatOk ? ConsoleColor.Green : ConsoleColor.Red);
            Console.WriteLine($" ({(bot.ChatEnabled ? "ВКЛ" : "ВЫКЛ")})");
            if (!chatOk && !string.IsNullOrEmpty(chatError)) {
                WriteColor($"  {chatError}\n", ConsoleColor.Yellow);
                currentLine++;
            }
        } else {
            WriteColor("ВЫКЛ\n", ConsoleColor.Gray);
        }
        currentLine++;

        // Статус VIP наград
        Console.Write("Покупка VIP: ");
        WriteColor(bot.VipRewardEnabled ? "ВКЛ" : "ВЫКЛ", bot.VipRewardEnabled ? ConsoleColor.Green : ConsoleColor.Gray);
        Console.WriteLine();
        currentLine++;

        Console.Write("Воровство VIP: ");
        WriteColor(bot.VipStealEnabled ? "ВКЛ" : "ВЫКЛ", bot.VipStealEnabled ? ConsoleColor.Green : ConsoleColor.Gray);
        Console.WriteLine();
        currentLine++;

        // ПУСТАЯ СТРОКА ДЛЯ РАЗДЕЛЕНИЯ
        Console.WriteLine();
        currentLine++;

        // ВЫВОД СТАТИСТИКИ В ПРАВИЛЬНОМ МЕСТЕ
        currentLine++;

        // Вызываем статистику с правильной позицией
        bot.DisplayLiveStatistics(0, currentLine);
        currentLine += 6; // Примерно 6 строк для статистики

        // ПУСТАЯ СТРОКА ДЛЯ РАЗДЕЛЕНИЯ
        Console.SetCursorPosition(0, currentLine);
        Console.WriteLine();
        currentLine++;

        // ПОДСКАЗКИ ПО КЛАВИШАМ
        Console.WriteLine();
        WriteColor("s - Статистика по командам\n", ConsoleColor.DarkGray);
        WriteColor("p - Настройки\n", ConsoleColor.DarkGray);
        WriteColor("r - Перезагрузить\n", ConsoleColor.DarkGray);
        WriteColor("w - Выход с отключением наград\n", ConsoleColor.DarkGray);
        WriteColor("q - Выход\n", ConsoleColor.DarkGray);
    }

    static void HandlePreferences() {
        bot.ShowPreferences();

        while (true) {
            var key = Console.ReadKey(true);
            switch (key.KeyChar) {
                case '1':
                    bot.ToggleChat();
                    bot.ShowPreferences();
                    break;

                case '2':
                    bot.ToggleRewards();
                    bot.ShowPreferences();
                    break;

                case '3':
                    bot.ToggleVipReward();
                    bot.ShowPreferences();
                    break;

                case '4':
                    bot.ToggleVipStealReward();
                    bot.ShowPreferences();
                    break;

                case 't':
                case 'T':
                case 'е':
                case 'Е':
                    bot.ToggleDebugMode();
                    bot.ShowPreferences();
                    break;

                case 'v':
                case 'V':
                case 'м':
                case 'М':
                    bot.ChangeVolume();
                    bot.ShowPreferences();
                    break;

                case 'a':
                case 'A':
                case 'ф':
                case 'Ф':
                    OpenAuthInBrowser();
                    bot.ShowPreferences();
                    break;

                case 'b':
                case 'B':
                case 'и':
                case 'И':
                    return;
            }
        }
    }

    static void OpenAuthInBrowser() {
        try {
            var authUrl = bot.GetAuthUrl();
            WriteColor($"Открываю ссылку авторизации в браузере...\n", ConsoleColor.Yellow);
            WriteColor($"Ссылка: {authUrl}\n", ConsoleColor.Cyan);

            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo {
                FileName = authUrl,
                UseShellExecute = true
            });

            WriteColor("\nИнструкция:\n", ConsoleColor.White);
            WriteColor("1. Авторизуйтесь в Twitch\n", ConsoleColor.Gray);
            WriteColor("2. Скопируйте токен из адресной строки (часть после 'access_token=')\n", ConsoleColor.Gray);
            WriteColor("3. Обновите файл bot_settings.txt с новым токеном\n", ConsoleColor.Gray);
            WriteColor("4. Перезапустите бота (клавиша 'r')\n", ConsoleColor.Gray);
            WriteColor("\nНажмите любую клавишу чтобы продолжить...\n", ConsoleColor.Yellow);
            Console.ReadKey();
        } catch (Exception ex) {
            WriteColor($"Ошибка открытия браузера: {ex.Message}\n", ConsoleColor.Red);
            WriteColor($"Скопируйте ссылку вручную: {bot.GetAuthUrl()}\n", ConsoleColor.Cyan);
            WriteColor("Нажмите любую клавишу чтобы продолжить...\n", ConsoleColor.Yellow);
            Console.ReadKey();
        }
    }

    static void WaitForBack() {
        while (true) {
            var key = Console.ReadKey(true);
            if (key.KeyChar == 'b' || key.KeyChar == 'B' || key.KeyChar == 'и' || key.KeyChar == 'И')
                break;
        }
    }

    static void WriteColor(string text, ConsoleColor color) {
        var originalColor = Console.ForegroundColor;
        Console.ForegroundColor = color;
        Console.Write(text);
        Console.ForegroundColor = originalColor;
    }
}