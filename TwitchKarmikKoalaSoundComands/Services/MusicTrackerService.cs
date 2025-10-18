using System;
using System.IO;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Collections.Generic;

public class MusicTrackerService {
    private HttpListener listener;
    private bool isRunning = false;
    private MusicData currentTrack = new MusicData();
    private readonly BotSettings settings;
    private Task serverTask;

    public MusicTrackerService(BotSettings settings) {
        this.settings = settings;
    }

    public void Start() {
        if (isRunning)
            return;

        try {
            listener = new HttpListener();
            listener.Prefixes.Add($"http://localhost:{settings.MusicTrackerPort}/");
            listener.Start();

            isRunning = true;
            serverTask = Task.Run(StartListener);

            WriteColor($"✅ Music Tracker запущен на порту {settings.MusicTrackerPort}\n", ConsoleColor.Green);
        } catch (Exception ex) {
            WriteColor($"❌ Ошибка запуска Music Tracker: {ex.Message}\n", ConsoleColor.Red);
        }
    }

    public void Stop() {
        if (!isRunning)
            return;

        isRunning = false;
        try {
            listener?.Stop();
            listener?.Close();
            serverTask?.Wait(1000);
            WriteColor("✅ Music Tracker остановлен\n", ConsoleColor.Yellow);
        } catch (Exception ex) {
            WriteColor($"❌ Ошибка остановки Music Tracker: {ex.Message}\n", ConsoleColor.Red);
        }
    }

    private async Task StartListener() {
        while (isRunning) {
            try {
                var context = await listener.GetContextAsync();
                _ = Task.Run(() => HandleRequest(context));
            } catch (HttpListenerException) {
                // Это нормально при остановке
                break;
            } catch (Exception ex) {
                if (isRunning) // Только если мы еще не останавливаемся
                {
                    WriteColor($"❌ Ошибка Music Tracker: {ex.Message}\n", ConsoleColor.Red);
                }
                break;
            }
        }
    }

    private async void HandleRequest(HttpListenerContext context) {
        var request = context.Request;
        var response = context.Response;

        try {
            // Добавляем CORS заголовки
            response.AddHeader("Access-Control-Allow-Origin", "*");
            response.AddHeader("Access-Control-Allow-Methods", "GET, POST, OPTIONS");
            response.AddHeader("Access-Control-Allow-Headers", "Content-Type");

            if (request.HttpMethod == "OPTIONS") {
                response.StatusCode = 200;
                response.Close();
                if (settings.DebugMode) {
                    WriteColor("🔧 Обработан OPTIONS запрос (CORS)\n", ConsoleColor.Cyan);
                }
                return;
            }

            if (request.HttpMethod == "POST" && request.Url.AbsolutePath == "/") {
                if (settings.DebugMode) {
                    WriteColor("📨 Получен POST запрос от Tampermonkey\n", ConsoleColor.Cyan);
                }

                using (var reader = new StreamReader(request.InputStream, request.ContentEncoding)) {
                    var json = await reader.ReadToEndAsync();

                    if (settings.DebugMode) {
                        WriteColor($"📝 Данные: {json}\n", ConsoleColor.Gray);
                    }

                    if (!string.IsNullOrWhiteSpace(json)) {
                        try {
                            var options = new JsonSerializerOptions {
                                PropertyNameCaseInsensitive = true
                            };

                            var musicData = JsonSerializer.Deserialize<MusicData>(json, options);

                            if (musicData != null) {
                                currentTrack = musicData;
                                PrintTrackInfo(musicData);

                                await SendResponse(response, 200, new { status = "success", message = "Track updated" });
                            } else {
                                await SendResponse(response, 400, new { status = "error", message = "Invalid data" });
                            }
                        } catch (JsonException jsonEx) {
                            WriteColor($"❌ Ошибка парсинга JSON: {jsonEx.Message}\n", ConsoleColor.Red);
                            await SendResponse(response, 400, new { status = "error", message = "Invalid JSON" });
                        }
                    } else {
                        await SendResponse(response, 400, new { status = "error", message = "Empty data" });
                    }
                }
            } else if (request.HttpMethod == "GET" && request.Url.AbsolutePath == "/") {
                if (settings.DebugMode) {
                    WriteColor("📥 Получен GET запрос\n", ConsoleColor.Cyan);
                }

                var options = new JsonSerializerOptions {
                    WriteIndented = true,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                };
                var json = JsonSerializer.Serialize(currentTrack, options);
                await SendResponse(response, 200, json, "application/json");
            } else {
                response.StatusCode = 404;
                response.Close();
            }
        } catch (Exception ex) {
            WriteColor($"❌ Ошибка обработки запроса: {ex.Message}\n", ConsoleColor.Red);
            response.StatusCode = 500;
            response.Close();
        }
    }

    private async Task SendResponse(HttpListenerResponse response, int statusCode, object data, string contentType = "application/json") {
        try {
            response.StatusCode = statusCode;
            response.ContentType = contentType;

            var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
            var buffer = Encoding.UTF8.GetBytes(json);

            await response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
            response.Close();
        } catch (Exception ex) {
            WriteColor($"❌ Ошибка отправки ответа: {ex.Message}\n", ConsoleColor.Red);
            response.Close();
        }
    }

    public MusicData GetCurrentTrack() {
        return currentTrack;
    }

    private void PrintTrackInfo(MusicData musicData) {
        if (settings.DebugMode) {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"🌐 От скрипта:");
            Console.ResetColor();

            if (string.IsNullOrWhiteSpace(musicData.Name)) {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("🎵 Трек: [НЕТ НАЗВАНИЯ]");
                Console.ResetColor();
            } else {
                Console.WriteLine($"🎵 Трек: {musicData.Name}");
            }

            if (string.IsNullOrWhiteSpace(musicData.Link)) {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("🔗 Ссылка: [НЕТ ССЫЛКИ]");
                Console.ResetColor();
            } else {
                Console.WriteLine($"🔗 Ссылка: {musicData.Link}");
            }

            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine($"⏰ Время: {DateTime.Now:HH:mm:ss}");
            Console.ResetColor();
            Console.WriteLine(new string('-', 50));
        }
    }

    private void WriteColor(string text, ConsoleColor color) {
        var originalColor = Console.ForegroundColor;
        Console.ForegroundColor = color;
        Console.Write(text);
        Console.ForegroundColor = originalColor;
    }
}

public class MusicData {
    public string Name { get; set; } = "";
    public string Link { get; set; } = "";
}