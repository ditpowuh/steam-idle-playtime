using System;
using System.Text.Json;
using Sharprompt;
using QRCoder;
using SteamKit2;
using SteamKit2.Internal;
using SteamKit2.Authentication;

namespace SteamIdlePlaytime {

  class Program {

    static SteamClient? steamClient;
    static CallbackManager? manager;
    static SteamUser? steamUser;

    static readonly string[] signinMethods = {
      "Credentials",
      "QR Code"
    };

    static string? selectedSigninMethod;

    static string? username;
    static string? password;

    static string? jsonFileName;

    static List<GameInfo> gameData = new List<GameInfo>();
    static DateTime lastProgressTick = DateTime.UtcNow;
    static int targetIndex = 0;
    static bool isRunning = true;

    static void Main(string[] args) {
      selectedSigninMethod = Prompt.Select("Select your sign-in method", signinMethods);

      if (selectedSigninMethod == "Credentials") {
        username = Prompt.Input<string>("Steam username");
        password = Prompt.Password("Steam password");
      }

      jsonFileName = Prompt.Input<string>("JSON file");
      if (!File.Exists(jsonFileName)) {
        Console.Error.WriteLine("JSON file not found.");
        return;
      }
      if (!jsonFileName.EndsWith(".json")) {
        Console.Error.WriteLine("File is not JSON.");
        return;
      }
      string jsonData = File.ReadAllText(jsonFileName);
      gameData = JsonSerializer.Deserialize<List<GameInfo>>(jsonData) ?? new List<GameInfo>();

      int completedCount = 0;
      foreach (GameInfo game in gameData) {
        if (game.appID == 0) {
          Console.Error.WriteLine("An AppID was not registered.");
          return;
        }
        if (game.progress >= game.target) {
          completedCount = completedCount + 1;
        }
      }
      if (completedCount >= gameData.Count) {
        Console.Error.WriteLine("All games in file were already completed.");
        return;
      }
      for (int i = 0; i < gameData.Count; i++) {
        if (gameData[i].progress < gameData[i].target) {
          targetIndex = i;
          break;
        }
      }

      steamClient = new SteamClient();
      manager = new CallbackManager(steamClient);
      steamUser = steamClient.GetHandler<SteamUser>();

      manager.Subscribe<SteamClient.ConnectedCallback>(OnConnected);
      manager.Subscribe<SteamClient.DisconnectedCallback>(OnDisconnected);
      manager.Subscribe<SteamUser.LoggedOnCallback>(OnLoggedOn);
      manager.Subscribe<SteamUser.LoggedOffCallback>(OnLoggedOff);

      Console.WriteLine("Connecting to Steam...");
      steamClient.Connect();

      AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
      Console.CancelKeyPress += OnCancelKeyPress;

      while (isRunning) {
        manager.RunWaitCallbacks(TimeSpan.FromSeconds(1));

        if ((DateTime.UtcNow - lastProgressTick) >= TimeSpan.FromMinutes(1)) {
          lastProgressTick = DateTime.UtcNow;

          GameInfo game = gameData[targetIndex];
          game.progress++;

          if (game.progress >= game.target) {
            Console.WriteLine($"Target reached for AppID {game.appID}");

            if (targetIndex == gameData.Count - 1) {
              Console.WriteLine("All games completed.");
              isRunning = false;
              break;
            }

            for (int i = targetIndex + 1; i < gameData.Count; i++) {
              if (gameData[i].progress < gameData[i].target) {
                targetIndex = i;
                break;
              }
            }

            StartIdle(gameData[targetIndex].appID);
          }
        }
      }

      if (steamUser != null) {
        steamUser.LogOff();
      }
    }

    static async Task LoginViaCredentials() {
      Console.WriteLine($"Logging in as '{username}' via credentials...");
      if (steamClient != null && steamUser != null) {
        CredentialsAuthSession authSession = await steamClient.Authentication.BeginAuthSessionViaCredentialsAsync(new AuthSessionDetails {
          Username = username,
          Password = password,
          Authenticator = new UserConsoleAuthenticator()
        });

        AuthPollResult pollResponse = await authSession.PollingWaitForResultAsync();

        steamUser.LogOn(new SteamUser.LogOnDetails {
          Username = pollResponse.AccountName,
          AccessToken = pollResponse.RefreshToken
        });
      }
    }

    static async Task LoginViaQRCode() {
      Console.WriteLine($"Preparing sign-in via QR code...");
      if (steamClient != null && steamUser != null) {
        QrAuthSession authSession = await steamClient.Authentication.BeginAuthSessionViaQRAsync(new AuthSessionDetails());

        authSession.ChallengeURLChanged = () => {
          Console.WriteLine("Steam has refreshed the challenge url.");
          DrawQRCode(authSession);
        };

        DrawQRCode(authSession);

        AuthPollResult pollResponse = await authSession.PollingWaitForResultAsync();

        Console.WriteLine($"Logging in as '{pollResponse.AccountName}'...");

        steamUser.LogOn(new SteamUser.LogOnDetails {
          Username = pollResponse.AccountName,
          AccessToken = pollResponse.RefreshToken
        });
      }
    }

    static async void OnConnected(SteamClient.ConnectedCallback callback) {
      Console.WriteLine($"Connected to Steam!");
      if (selectedSigninMethod == "Credentials") {
        await LoginViaCredentials();
      }
      else if (selectedSigninMethod == "QR Code") {
        await LoginViaQRCode();
      }
    }

    static void OnDisconnected(SteamClient.DisconnectedCallback callback) {
      Console.WriteLine("Disconnected from Steam.");
      isRunning = false;
    }

    static void OnLoggedOn(SteamUser.LoggedOnCallback callback) {
      if (callback.Result != EResult.OK) {
        Console.WriteLine($"Unable to logon to Steam: {callback.Result} / {callback.ExtendedResult}");
        isRunning = false;
        return;
      }
      Console.WriteLine("Successfully logged on! Starting idle...");

      StartIdle(gameData[targetIndex].appID);
    }

    static void OnLoggedOff(SteamUser.LoggedOffCallback callback) {
      Console.WriteLine($"Logged off of Steam: {callback.Result}");
    }

    static void StartIdle(ulong appID) {
      ClientMsgProtobuf<CMsgClientGamesPlayed> playGame = new ClientMsgProtobuf<CMsgClientGamesPlayed>(EMsg.ClientGamesPlayed);

      playGame.Body.games_played.Add(new CMsgClientGamesPlayed.GamePlayed {
        game_id = new GameID(appID)
      });

      if (steamClient != null) {
        steamClient.Send(playGame);
      }
      Console.WriteLine($"Now simulating play status for AppID: {appID}");
    }

    static void DrawQRCode(QrAuthSession authSession) {
      Console.WriteLine($"Challenge URL: {authSession.ChallengeURL}\n");

      using (QRCodeGenerator qrGenerator = new QRCodeGenerator()) {
        QRCodeData qrCodeData = qrGenerator.CreateQrCode(authSession.ChallengeURL, QRCodeGenerator.ECCLevel.L);
        using (AsciiQRCode qrCode = new AsciiQRCode(qrCodeData)) {
          string qrCodeAsAsciiArt = qrCode.GetGraphic(1, drawQuietZones: false);

          Console.WriteLine("Use the Steam Mobile App to sign in via QR code:\n");
          Console.WriteLine(qrCodeAsAsciiArt + "\n");
        }
      }
    }

    static void OnProcessExit(object? sender, EventArgs e) {
      ApplyBeforeTerminate();
    }

    static void OnCancelKeyPress(object? sender, ConsoleCancelEventArgs e) {
      ApplyBeforeTerminate();
      e.Cancel = true;
      Environment.Exit(0);
    }

    static async void ApplyBeforeTerminate() {
      if (steamUser != null) {
        steamUser.LogOff();
      }
      string jsonString = JsonSerializer.Serialize(gameData, new JsonSerializerOptions {
        WriteIndented = true
      });
      if (jsonFileName != null) {
        await File.WriteAllTextAsync(jsonFileName, jsonString);
      }
      Console.WriteLine("JSON file updated.");
    }

  }
}
