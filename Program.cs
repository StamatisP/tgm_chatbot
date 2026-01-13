using System.Timers;
using Fleck;
using TwitchLib.Client;
using TwitchLib.Api;
using TwitchLib.Client.Events;
using TwitchLib.Client.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

// when publishing, use the command dotnet publish /p:Configuration=Release /p:PublishProfile=FolderProfile
namespace TwitchInteract
{
	public class Program
	{
		public static IWebSocketConnection pub_socket;
		static ManualResetEvent _quitEvent = new ManualResetEvent(false);
		public static System.Timers.Timer ChatCheckTimer;
		public static System.Timers.Timer UpdateViewerCount;
		public static WebSocketServer server;
		public static TwitchAPI API;

		public static bool VotingTime = false;
		public static bool AnarchyMode = false;
		public static bool ChatBossMode = false;
		public static ConnectionCredentials creds;
		public static Config config;

		public static void Main(string[] args)
		{
			Console.Title = "Twitch Interact";

			LoadConfig();

			Console.CancelKeyPress += (sender, eArgs) => {
				_quitEvent.Set();
				eArgs.Cancel = true;
			};
			TGM_Init();
			_quitEvent.WaitOne();
			Console.WriteLine("Program has ended, press any key to close this window.");
			Console.ReadKey();
		}

		private static void LoadConfig()
		{
			config = new Config();
			string configPath = Path.Combine(Directory.GetCurrentDirectory(), "config.json");

            if (File.Exists(configPath))
            {
				Console.WriteLine("config.json exists, loading...");
				config = JsonConvert.DeserializeObject<Config>(File.ReadAllText(configPath), new JsonSerializerSettings
				{
					Error = delegate(object sender, Newtonsoft.Json.Serialization.ErrorEventArgs e)
					{
						Console.WriteLine("Could not read config.json correctly, error: ");
						Console.WriteLine(e.ToString());
						Console.WriteLine("You will be prompted for the bot's info again, and this will delete the previous config.json file. If you do not wish to continue, press CTRL + C or close the window.");
						e.ErrorContext.Handled = true;
						PromptForNewConfig(configPath);
					}
				});
            }
            else
            {
				PromptForNewConfig(configPath);
            }

            creds = new ConnectionCredentials(config.BotName, config.BotOauth);
            API = new TwitchAPI() { Settings = { ClientId = config.BotName, AccessToken = config.BotOauth } };
        }

		static void PromptForNewConfig(string configPath)
		{
            Console.WriteLine("Enter your bot's name.");
            config.BotName = Console.ReadLine();
            Console.WriteLine("Enter your bot's access token. You can paste by right clicking in the console window. (https://twitchtokengenerator.com)");
            config.BotOauth = Console.ReadLine();
            Console.WriteLine("Enter your bot's client ID.");
            config.BotClientID = Console.ReadLine();
            Console.WriteLine("What Twitch channel do you want to monitor?");
            config.TwitchChannel = Console.ReadLine();
            Console.WriteLine("What's the maximum amount of seconds between any two chat messages in your stream? (If you have frequent chatters, set this to 10 or lower. 30 is a good default.)");
            config.ConfirmConnectionIntervalInSeconds = Int32.Parse(Console.ReadLine());
            File.WriteAllText(configPath, JsonConvert.SerializeObject(config));
        }

		static void TGM_Init()
		{
			UpdateViewerCount = new System.Timers.Timer();
			UpdateViewerCount.Elapsed += UpdateViewers;
			UpdateViewerCount.Interval = 15000;
			UpdateViewerCount.Enabled = false;

			ChatCheckTimer = new System.Timers.Timer();
			ChatCheckTimer.Elapsed += Program.TGM_Close;
			ChatCheckTimer.Interval = config.ConfirmConnectionIntervalInSeconds * 1000;
			ChatCheckTimer.Enabled = false;

			Program.server = new WebSocketServer("ws://0.0.0.0:8765")
			{
				RestartAfterListenError = true
			};

			Bot bot = new Bot(creds, config.TwitchChannel);

			server.Start(socket =>
			{
				Program.pub_socket = socket;
				socket.OnOpen = () =>
				{
					Console.WriteLine("Open server");
					ChatCheckTimer.Start();
					//UpdateViewerCount.Start();
				};
				socket.OnClose = () =>
				{
					Console.WriteLine("Close server");
					ChatCheckTimer.Stop();
					//UpdateViewerCount.Stop();
				};
				socket.OnMessage = message => MessageHandler(message, socket);
				socket.OnError = (Exception) =>
				{
					Console.WriteLine("Socket error: ", Exception.ToString());
				};
			});

			void MessageHandler(string message, IWebSocketConnection socket)
			{
				if (message != "ConnTest")
				{
					ConsoleColor prevColor = Console.ForegroundColor;
					Console.ForegroundColor = ConsoleColor.Green;
					Console.WriteLine(message + " " + DateTime.Now.ToString("hh:mm:ss"));
					Console.ForegroundColor = prevColor;
				}
				switch (message)
				{
					case "Connected Message":
						Console.WriteLine("WOO HOO! We connected!");
						break;
					case "VoteTime":
						VotingTime = true;
						break;
					case "VoteOver":
						VotingTime = false;
						break;
					case "anarchymode":
						AnarchyMode = true;
						Console.WriteLine("Anarchy Mode: On");
						break;
					case "democracymode":
						AnarchyMode = false;
						Console.WriteLine("Democracy Mode: On");
						break;
					default:
						if (message.StartsWith("VoteActions"))
						{
							var actions = message.Split(';');
							for (int i = 0; i < actions.Count(); i++)
							{
								if (i == 0) continue;
								if (!Bot.SilentMode) Bot.client.SendMessageAsync(config.TwitchChannel, actions[i]);
								//socket.Send(actions[i]);
							}
						}
						if (message.StartsWith("ChatBossStatus"))
						{
							var args = message.Split(';');
							ChatBossMode = Boolean.Parse(args[1]);
						}
						break;
				}
			}

			//TODO: BROKEN
			async void UpdateViewers(object source, ElapsedEventArgs e)
			{
				if (pub_socket != null && pub_socket.IsAvailable)
				{
					var chatters = await Task.Run(() => API.Helix.Chat.GetChattersAsync(config.TwitchChannel, config.TwitchChannel));
					Console.WriteLine("Viewers: " + chatters.Total);
					try
					{
						await pub_socket.Send("Viewers;" + chatters.Total.ToString());
					}
					catch (Fleck.ConnectionNotAvailableException)
					{
						Console.WriteLine("Cannot send viewer info if we are closing!");
					}
				}
			}

			Console.ForegroundColor = ConsoleColor.Green;
			Console.WriteLine("Initialized...");
		}

		static void TGM_Close(object source, ElapsedEventArgs e)
		{
			Console.ForegroundColor = ConsoleColor.Red;
			Console.WriteLine(String.Format("No chat messages have been posted in the last {0} seconds! This is a bug, restarting.", ChatCheckTimer.Interval / 1000));
			Console.ForegroundColor = ConsoleColor.Gray;
			UpdateViewerCount.Dispose();
			ChatCheckTimer.Dispose();
			pub_socket?.Close();
			server.Dispose();
			Bot.client.DisconnectAsync();
			TGM_Init();
		}
	}

	class Bot
	{
		public static TwitchClient client;
		readonly bool PrintTwitchChat = true;
		public static bool SilentMode = false;

		public Bot(ConnectionCredentials credentials, string TwitchChannel)
		{
			client = new TwitchClient();
			client.Initialize(credentials, TwitchChannel);
			Console.WriteLine($"Attempting to connect to ${TwitchChannel}");

			client.OnJoinedChannel += Client_OnJoinedChannel;
			client.OnMessageReceived += Client_OnMessageReceived;
			client.OnNewSubscriber += Client_OnNewSubscriber;
			client.OnConnected += Client_OnConnected;

			client.ConnectAsync();
		}

        private async Task Client_OnJoinedChannel(object sender, OnJoinedChannelArgs e)
        {
            Console.WriteLine("Connected to chat successfully!");
            if (!SilentMode) await client.SendMessageAsync(e.Channel, "Connected to chat successfully!");
        }

        private async Task Client_OnConnected(object sender, OnConnectedEventArgs e)
        {
            Console.WriteLine($"Connected to Twitch!");
        }

		private async Task Client_OnMessageReceived(object sender, OnMessageReceivedArgs e)
		{
			if (e.ChatMessage.Message[0] == '!')
			{
				string cmd = e.ChatMessage.Message.Substring(1);
				Console.ForegroundColor = ConsoleColor.Yellow;
				Console.WriteLine(DateTime.Now.ToString("hh:mm:ss") + " -	 Received command: " + cmd);
				if (cmd == "attack")
				{
					if (Program.ChatBossMode)
					{
						int damage = 1;
						if (e.ChatMessage.UserDetail.IsSubscriber) damage = 2;
						if (e.ChatMessage.Bits >= 100) damage += e.ChatMessage.Bits / 50;
						damage *= Math.Min(Math.Max(e.ChatMessage.SubscribedMonthCount, 3), 1); // max believable damage is around 66 (1000 bits), i might clamp it
						string message = "{0} has dealt {1} damage to the boss!";
						Console.WriteLine(String.Format(message, e.ChatMessage.Username, damage));
						if (Program.pub_socket != null) await Program.pub_socket.Send("AttackBoss;" + damage + ";" + e.ChatMessage.Username);
					}
				}
				else if (cmd == "silentmode")
				{
					if (e.ChatMessage.UserDetail.IsModerator)
					{
						SilentMode = !SilentMode;
						await client.SendMessageAsync(Program.config.TwitchChannel, "Silent mode: " + SilentMode);
					}
				}
				else if (cmd != "attack")
				{
					if (Program.VotingTime)
					{
						if (Program.pub_socket != null) await Program.pub_socket.Send("VoteInfo\n" + e.ChatMessage.Username + "\n" + cmd);
					}
					else if (Program.AnarchyMode)
					{
						if (Program.pub_socket != null) await Program.pub_socket.Send(cmd.ToLower());
					}
				}
			}
			else
			{
				if (PrintTwitchChat)
				{
					if (Program.pub_socket != null && Program.pub_socket.IsAvailable)
					{
						await Program.pub_socket.Send("PrintTwitchChat\n" + e.ChatMessage.Username + "\n" + e.ChatMessage.Message);
					}
				}
			}
			Program.ChatCheckTimer.Stop();
			Program.ChatCheckTimer.Start();
		}

		private Task Client_OnNewSubscriber(object sender, OnNewSubscriberArgs e)
		{
			if (Program.ChatBossMode)
			{
				int damage = 5;
				if (e.Subscriber.MsgParamSubPlan == TwitchLib.Client.Enums.SubscriptionPlan.Tier2) damage *= 2;
				else if (e.Subscriber.MsgParamSubPlan == TwitchLib.Client.Enums.SubscriptionPlan.Tier3) damage *= 4;
				string message = "{0} has subscribed and dealt {1} damage to the boss!";
				Console.WriteLine(String.Format(message, e.Subscriber.DisplayName, damage));
				Program.pub_socket.Send("AttackBoss;" + damage + ";" + e.Subscriber.DisplayName);
			}
			return Task.CompletedTask;
		}
	}

	[Serializable]
	public class Config
	{
		public string BotName { get; set; }
		public string BotOauth { get; set; }
		public string BotClientID { get; set; }
		public string TwitchChannel { get; set; }
		public int ConfirmConnectionIntervalInSeconds { get; set; } = 30;
	}
}
