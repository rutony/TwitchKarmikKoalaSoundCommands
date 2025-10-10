using System;
using System.Threading.Tasks;

class Program {
    private static TwitchSoundBot bot;
    private static bool running = true;
    private static bool alreadyDisconnected = false;

    static async Task Main(string[] args) {
        Console.Title = "Twitch Sound Bot (с) RUTONY 2025";

        try {
            bot = new TwitchSoundBot();

            WriteColor("=== Twitch Sound Bot with Rewards ===\n", ConsoleColor.Cyan);
            WriteColor("Подключение...\n", ConsoleColor.Yellow);

            var (authOk, authError, rewardsOk, rewardsError, chatOk, chatError) = await bot.Connect();

            if (bot.IsDebugMode) {
                await bot.CheckTokenScopes();
                Console.WriteLine("Нажмите любую клавишу чтобы продолжить...");
                Console.ReadKey();
            }

            Console.Clear();
            DisplayStatus(authOk, authError, rewardsOk, rewardsError, chatOk, chatError);

            while (running) {
                var key = Console.ReadKey(true);

                switch (key.KeyChar) {
                    case 's':
                    case 'S':
                    case 'ы':
                    case 'Ы':
                        bot.ShowStatistics();
                        WaitForBack();
                        Console.Clear();
                        DisplayStatus(authOk, authError, rewardsOk, rewardsError, chatOk, chatError);
                        break;

                    case 'p':
                    case 'P':
                    case 'з':
                    case 'З':
                        HandlePreferences();
                        Console.Clear();
                        DisplayStatus(authOk, authError, rewardsOk, rewardsError, chatOk, chatError);
                        break;

                    case 'r':
                    case 'R':
                    case 'к':
                    case 'К':
                        Console.Clear();
                        WriteColor("Перезагрузка...\n", ConsoleColor.Yellow);
                        await bot.Disconnect(true);
                        bot = new TwitchSoundBot();
                        (authOk, authError, rewardsOk, rewardsError, chatOk, chatError) = await bot.Connect();
                        Console.Clear();
                        DisplayStatus(authOk, authError, rewardsOk, rewardsError, chatOk, chatError);
                        break;

                    case 'q':
                    case 'Q':
                    case 'й':
                    case 'Й':
                        if (!alreadyDisconnected) {
                            alreadyDisconnected = true;
                            running = false;
                            WriteColor("Выход без отключения наград...\n", ConsoleColor.Yellow);
                            await bot.Disconnect(false);
                        }
                        break;

                    case 'w':
                    case 'W':
                    case 'ц':
                    case 'Ц':
                        if (!alreadyDisconnected) {
                            alreadyDisconnected = true;
                            running = false;
                            WriteColor("Выход с отключением наград...\n", ConsoleColor.Yellow);
                            await bot.Disconnect(true);
                        }
                        break;
                }
            }

            WriteColor("Программа завершена.\n", ConsoleColor.Yellow);
        } catch (Exception ex) {
            WriteColor($"Ошибка: {ex.Message}\n", ConsoleColor.Red);
            WriteColor("Нажмите любую клавишу...\n", ConsoleColor.Gray);
            Console.ReadKey();
        }
    }

    static void DisplayStatus(bool authOk, string authError, bool rewardsOk, string rewardsError, bool chatOk, string chatError) {
        WriteColor("=== Twitch Sound Bot with Rewards ===\n", ConsoleColor.Cyan);
        Console.WriteLine();
        Console.WriteLine($"Канал: {bot.GetChannelName()}");

        Console.Write("Авторизация: ");
        if (authOk) {
            WriteColor("OK\n", ConsoleColor.Green);
        } else {
            WriteColor("Ошибка\n", ConsoleColor.Red);
            if (!string.IsNullOrEmpty(authError)) {
                WriteColor($"  {authError}\n", ConsoleColor.Yellow);
            }
        }

        Console.WriteLine($"Количество звуковых команд: {bot.GetTotalCommands()}");
        Console.WriteLine($"  - Для чата: {bot.GetChatEnabledCount()}");
        Console.WriteLine($"  - Для наград: {bot.GetRewardEnabledCount()}");

        var missingFiles = bot.GetMissingFiles();
        if (missingFiles.Count > 0) {
            WriteColor($"Отсутствуют файлы: {missingFiles.Count}\n", ConsoleColor.Yellow);
            foreach (var file in missingFiles) {
                WriteColor($"  {file}\n", ConsoleColor.Yellow);
            }
        }

        Console.Write("Награды: ");
        if (bot.RewardsEnabled) {
            WriteColor(rewardsOk ? "OK" : "Ошибка", rewardsOk ? ConsoleColor.Green : ConsoleColor.Red);
            Console.WriteLine($" ({(bot.RewardsEnabled ? "ВКЛ" : "ВЫКЛ")})");
            if (!rewardsOk) {
                string errorDetails = bot.GetLastRewardsError();
                if (!string.IsNullOrEmpty(errorDetails)) {
                    WriteColor($"  {errorDetails}\n", ConsoleColor.Yellow);
                } else {
                    WriteColor($"  Неизвестная ошибка при создании/обновлении наград\n", ConsoleColor.Yellow);
                }
            }
        } else {
            WriteColor("ВЫКЛ\n", ConsoleColor.Gray);
        }

        Console.Write("Команды в чате: ");
        if (bot.ChatEnabled) {
            WriteColor(chatOk ? "OK" : "Ошибка", chatOk ? ConsoleColor.Green : ConsoleColor.Red);
            Console.WriteLine($" ({(bot.ChatEnabled ? "ВКЛ" : "ВЫКЛ")})");
            if (!chatOk && !string.IsNullOrEmpty(chatError)) {
                WriteColor($"  {chatError}\n", ConsoleColor.Yellow);
            }
        } else {
            WriteColor("ВЫКЛ\n", ConsoleColor.Gray);
        }

        Console.WriteLine($"Всего использований: {bot.GetTotalUsage()}");
        Console.WriteLine();
        WriteColor("s - Статистика по командам\n", ConsoleColor.White);
        WriteColor("p - Настройки\n", ConsoleColor.White);
        WriteColor("r - Перезагрузить\n", ConsoleColor.White);
        WriteColor("w - Выход с отключением наград\n", ConsoleColor.White);
        WriteColor("q - Выход\n", ConsoleColor.White);
        Console.WriteLine();
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