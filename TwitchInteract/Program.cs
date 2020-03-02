using System;
using System.IO;
using System.Linq;
using System.Timers;
using System.Threading;
using System.Threading.Tasks;
using Fleck;
using TwitchLib.Client;
using TwitchLib.Api;
using TwitchLib.Client.Events;
using TwitchLib.Client.Models;
using IniParser;
using IniParser.Model;

namespace TwitchInteract
{ 
    public class Program
    {
        public static bool VotingTime = false;
        public static bool AnarchyMode = false;
        public static IWebSocketConnection pub_socket;
		static ManualResetEvent _quitEvent = new ManualResetEvent(false);
		public static System.Timers.Timer ChatCheckTimer;
		public static System.Timers.Timer UpdateViewerCount;
		public static WebSocketServer server;
		public static string BotName, BotOauth, TwitchChannel;
		public static int? ChatCheckInterval = 15;
		public static TwitchAPI API;

		public static bool ChatBossMode = false;

		public static void Main(string[] args)
        {
			Console.Title = "Twitch Interact";
			if (File.Exists(Directory.GetCurrentDirectory() + "/config.ini"))
			{
				var parser = new FileIniDataParser();
				IniData data = parser.ReadFile("config.ini");
				BotName = data["TGM"]["BotName"] ?? "gameruiner9000";
				BotOauth = data["TGM"]["BotOauth"];
				TwitchChannel = data["TGM"]["TwitchChannel"];
				ChatCheckInterval = Convert.ToInt16(data["TGM"]["ConfirmConnectionIntervalInSeconds"]);
				ChatCheckInterval *= 1000;
			}
			else
			{
				Console.WriteLine("Enter your bot's name.");
				BotName = Console.ReadLine();
				Console.WriteLine("Now enter your bot's OAuth key. You can paste by right clicking in the console window. (https://www.twitchapps.com/tmi/)");
				BotOauth = Console.ReadLine();
				Console.WriteLine("What Twitch channel do you want to monitor?");
				TwitchChannel = Console.ReadLine();
				Console.WriteLine("What's the maximum amount of seconds between any two chat messages in your stream? (If you have frequent chatters, set this to 5 or lower.)");
				ChatCheckInterval = Int32.Parse(Console.ReadLine());
				var parser = new FileIniDataParser();
				IniData data = new IniData();
				data["TGM"]["BotName"] = BotName;
				data["TGM"]["BotOauth"] = BotOauth;
				data["TGM"]["TwitchChannel"] = TwitchChannel;
				data["TGM"]["ConfirmConnectionIntervalInSeconds"] = ChatCheckInterval.ToString();
				parser.WriteFile("config.ini", data);
			}

			API = new TwitchAPI();
			API.Settings.AccessToken = BotOauth;

			Console.CancelKeyPress += (sender, eArgs) => {
				_quitEvent.Set();
				eArgs.Cancel = true;
			};
			TGM_Init();
			_quitEvent.WaitOne();
		}

		static void TGM_Init()
		{
			UpdateViewerCount = new System.Timers.Timer();
			UpdateViewerCount.Elapsed += UpdateViewers;
			UpdateViewerCount.Interval = 15000;
			UpdateViewerCount.Enabled = false;

			ChatCheckTimer = new System.Timers.Timer();
			ChatCheckTimer.Elapsed += Program.TGM_Close;
			ChatCheckTimer.Interval = (double) ChatCheckInterval;
			ChatCheckTimer.Enabled = false;

			Program.server = new WebSocketServer("ws://0.0.0.0:8765")
			{
				RestartAfterListenError = true
			};

			Bot bot = new Bot(Program.BotName, BotOauth, TwitchChannel);

			server.Start(socket =>
			{
				Program.pub_socket = socket;
				socket.OnOpen = () => 
				{
					Console.WriteLine("Open server");
					ChatCheckTimer.Enabled = true;
					ChatCheckTimer.Stop();
					ChatCheckTimer.Start();
					UpdateViewerCount.Enabled = true;
					UpdateViewerCount.Stop();
					UpdateViewerCount.Start();
				};
				socket.OnClose = () =>
				{
					Console.WriteLine("Close server");
					ChatCheckTimer.Enabled = false;
					UpdateViewerCount.Enabled = false;
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
					Console.ForegroundColor = ConsoleColor.Green;
					Console.WriteLine(message + " " + DateTime.Now.ToString("hh:mm:ss"));
				}
				switch(message)
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
								if (!Bot.SilentMode) Bot.client.SendMessage(TwitchChannel, actions[i]);
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

			async void UpdateViewers(object source, ElapsedEventArgs e)
			{
				if (pub_socket != null && pub_socket.IsAvailable)
				{
					var chatters = await Task.Run(() => API.Undocumented.GetChattersAsync(TwitchChannel));
					Console.WriteLine("Viewers: " + chatters.Count);
					try
					{
						await pub_socket.Send("Viewers;" + chatters.Count.ToString());
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
			pub_socket.Close();
			server.Dispose();
			Bot.client.Disconnect();
			//GC.Collect();
			TGM_Init();
			//System.Diagnostics.Process.Start(Environment.GetCommandLineArgs()[0], Environment.GetCommandLineArgs()[1]);
			//Environment.Exit(0);
		}
	}

    class Bot
    {
        public static TwitchClient client;
		readonly bool PrintTwitchChat = true;
        public static bool SilentMode = false;

        public Bot(string BotName, string BotOauth, string TwitchChannel)
        {
            ConnectionCredentials credentials = new ConnectionCredentials(BotName, BotOauth);

            client = new TwitchClient();
            client.Initialize(credentials, TwitchChannel);

            client.OnLog += Client_OnLog;
            client.OnJoinedChannel += Client_OnJoinedChannel;
            client.OnMessageReceived += Client_OnMessageReceived;
            client.OnWhisperReceived += Client_OnWhisperReceived;
            client.OnNewSubscriber += Client_OnNewSubscriber;
            client.OnConnected += Client_OnConnected;

            client.Connect();
        }

        private void Client_OnLog(object sender, OnLogArgs e)
        {
            //Console.WriteLine($"{e.DateTime.ToString()}: {e.BotUsername} - {e.Data}");
        }

        private void Client_OnConnected(object sender, OnConnectedArgs e)
        {
            Console.WriteLine($"Connected to {e.AutoJoinChannel}");
        }

        private void Client_OnJoinedChannel(object sender, OnJoinedChannelArgs e)
        {
            Console.WriteLine("Connected to chat successfully!");
            if (!SilentMode) client.SendMessage(e.Channel, "Connected to chat successfully!");
        }

        private void Client_OnMessageReceived(object sender, OnMessageReceivedArgs e)
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
						Console.WriteLine("sneed");
						int damage = 1;
						if (e.ChatMessage.IsSubscriber) damage = 2;
						if (e.ChatMessage.Bits >= 100) damage += e.ChatMessage.Bits / 50;
						damage *= Math.Min(Math.Max(e.ChatMessage.SubscribedMonthCount, 3), 1); // max believable damage is around 66 (1000 bits), i might clamp it
						string message = "{0} has dealt {1} damage to the boss!";
						Console.WriteLine(String.Format(message, e.ChatMessage.Username, damage));
						if (Program.pub_socket != null) Program.pub_socket.Send("AttackBoss;" + damage + ";" + e.ChatMessage.Username);
					}
				}
				else if (cmd == "silentmode")
				{
					if (e.ChatMessage.IsModerator)
					{
						SilentMode = !SilentMode;
						client.SendMessage(Program.TwitchChannel, "Silent mode: " + SilentMode);
					}
				}
				else if (cmd != "attack")
				{
					//Program.VoteMessages.Add(new Tuple<string, string>(e.ChatMessage.Username, cmd));
					if (Program.VotingTime)
					{
						if (Program.pub_socket != null) Program.pub_socket.Send("VoteInfo\n" + e.ChatMessage.Username + "\n" + cmd);
					}
					else if (Program.AnarchyMode)
					{
						if (Program.pub_socket != null) Program.pub_socket.Send(cmd.ToLower());
					}
                }
            }
            else 
            {
				if (PrintTwitchChat)
				{
					if (Program.pub_socket != null && Program.pub_socket.IsAvailable)
					{
						Program.pub_socket.Send("PrintTwitchChat\n" + e.ChatMessage.Username + "\n" + e.ChatMessage.Message);
					}
				}
            }
			Program.ChatCheckTimer.Stop();
			Program.ChatCheckTimer.Start();
			//Console.WriteLine("chat received");
		}

        private void Client_OnWhisperReceived(object sender, OnWhisperReceivedArgs e)
        {
            //if (e.WhisperMessage.Username == "my_friend")
                //client.SendWhisper(e.WhisperMessage.Username, "Hey! Whispers are so cool!!");
        }

        private void Client_OnNewSubscriber(object sender, OnNewSubscriberArgs e)
        {
			/*if (e.Subscriber.SubscriptionPlan == SubscriptionPlan.Prime)
                client.SendMessage(e.Channel, $"Welcome {e.Subscriber.DisplayName} to the substers! You just earned 500 points! So kind of you to use your Twitch Prime on this channel!");
            else
                client.SendMessage(e.Channel, $"Welcome {e.Subscriber.DisplayName} to the substers! You just earned 500 points!");*/
			if (Program.ChatBossMode)
			{
				int damage = 5;
				if (e.Subscriber.SubscriptionPlan == TwitchLib.Client.Enums.SubscriptionPlan.Tier2) damage *= 2;
				if (e.Subscriber.SubscriptionPlan == TwitchLib.Client.Enums.SubscriptionPlan.Tier3) damage *= 4;
				string message = "{0} has subscribed and dealt {1} damage to the boss!";
				Console.WriteLine(String.Format(message, e.Subscriber.DisplayName, damage));
				Program.pub_socket.Send("AttackBoss;" + damage + ";" + e.Subscriber.DisplayName);
			}
		}
    }
}
