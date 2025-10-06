using System;
using System.Diagnostics;
using System.Threading.Tasks;

class TokenGetter {
    static void Main() {
        Console.WriteLine("=== Twitch Token Getter ===");

        string clientId = "9s7xj3vpmcrl3k0j09i5iwj84if09h"; // Ваш Client ID
        string redirectUri = "http://localhost:3000";
        string[] scopes = {
            "channel:manage:redemptions",
            "chat:edit",
            "chat:read"
        };

        string scopeString = string.Join("+", scopes);
        string authUrl = $"https://id.twitch.tv/oauth2/authorize?client_id={clientId}&redirect_uri={redirectUri}&response_type=token&scope={scopeString}";

        Console.WriteLine($"Откройте эту ссылку в браузере:");
        Console.WriteLine(authUrl);
        Console.WriteLine("\nПосле авторизации скопируйте access_token из адресной строки браузера");
        Console.WriteLine("(он будет после access_token= до &)");

        Process.Start(new ProcessStartInfo {
            FileName = authUrl,
            UseShellExecute = true
        });

        Console.WriteLine("\nВведите полученный access_token:");
        string accessToken = Console.ReadLine();

        Console.WriteLine($"\nВаш OAuth токен для bot_settings.txt:");
        Console.WriteLine($"oauth_token=oauth:{accessToken}");
        Console.WriteLine("\nНажмите любую клавишу...");
        Console.ReadKey();
    }
}