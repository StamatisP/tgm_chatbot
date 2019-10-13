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
		public static int? ChatCheckInterval;
		public static TwitchAPI API;

		public static void Main(string[] args)
        {
			if (File.Exists(Directory.GetCurrentDirectory() + "/config.ini"))
			{
				var parser = new FileIniDataParser();
				IniData data = parser.ReadFile("config.ini");
				BotName = data["TGM"]["BotName"] ?? "gameruiner9000";
				BotOauth = data["TGM"]["BotOauth"];
				TwitchChannel = data["TGM"]["TwitchChannel"];
				int? default_interval = 5;
				ChatCheckInterval = (int?) Convert.ToInt16(data["TGM"]["ConfirmConnectionIntervalInSeconds"]) ?? default_interval;
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
			ChatCheckTimer = new System.Timers.Timer();
			UpdateViewerCount = new System.Timers.Timer();
			UpdateViewerCount.Elapsed += UpdateViewers;

			ChatCheckTimer.Elapsed += Program.TGM_Close;
			ChatCheckTimer.Interval = (double) ChatCheckInterval;
			ChatCheckTimer.Enabled = false;

			UpdateViewerCount.Interval = 15000;
			UpdateViewerCount.Enabled = false;
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
					UpdateViewerCount.Enabled = true;
				};
				socket.OnClose = () =>
				{
					Console.WriteLine("Close server");
					ChatCheckTimer.Enabled = false;
					UpdateViewerCount.Enabled = false;
				};
				socket.OnMessage = message => MessageHandler(message, socket);
				socket.OnError = (Exception) => Console.WriteLine("Socket error: ", Exception.ToString());
			});

			void MessageHandler(string message, IWebSocketConnection socket)
			{
				if (message != "ConnTest")
				{
					Console.ForegroundColor = ConsoleColor.Green;
					Console.WriteLine(message + " " + DateTime.Now.ToString("hh:mm:ss"));
				}
				if (message == "Connected Message!")
				{
					Console.WriteLine("WOO HOO! We connected!");
				}
				else if (message == "VoteTime")
				{
					VotingTime = true;
				}
				else if (message.StartsWith("VoteActions"))
				{
					var actions = message.Split(';');
					for (int i = 0; i < actions.Count(); i++)
					{
						if (i == 0) continue;
						if (!Bot.SilentMode) Bot.client.SendMessage(TwitchChannel, actions[i]);
						//socket.Send(actions[i]);
					}
				}
				else if (message == "VoteOver")
				{
					VotingTime = false;
				}
				else if (message == "anarchymode")
				{
					AnarchyMode = true;
				}
				else if (message == "democracymode")
				{
					AnarchyMode = false;
				}
			}

			async void UpdateViewers(object source, ElapsedEventArgs e)
			{
				var chatters = await Task.Run(() => API.Undocumented.GetChattersAsync(TwitchChannel));
				Console.WriteLine("Viewers: " + chatters.Count);
				if (pub_socket != null && pub_socket.IsAvailable)
				{
					await pub_socket.Send("Viewers;" + chatters.Count.ToString());
				}
			}

			Console.ForegroundColor = ConsoleColor.Green;
			Console.WriteLine("Initialized...");
		}

		static void TGM_Close(object source, ElapsedEventArgs e)
		{
			Console.ForegroundColor = ConsoleColor.Red;
			Console.WriteLine(String.Format("No chat messages have been posted in the last {0} seconds! Restarting, probably a bug.", ChatCheckTimer.Interval / 1000));
			UpdateViewerCount.Dispose();
			ChatCheckTimer.Dispose();
			pub_socket.Close();
			server.Dispose();
			Bot.client.Disconnect();
			TGM_Init();
			//System.Diagnostics.Process.Start(Environment.GetCommandLineArgs()[0], Environment.GetCommandLineArgs()[1]);
			//Environment.Exit(0);
		}
	}

    class Bot
    {
        public static TwitchClient client;
		bool PrintTwitchChat = true;
        public static bool SilentMode = true;

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
            if (!SilentMode) client.SendMessage(e.Channel, "Connected successfully!");
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
					if (Program.pub_socket != null) Program.pub_socket.Send("AttackBoss");
				}
                else if (Program.VotingTime && cmd != "attack")
                {
                    //Program.VoteMessages.Add(new Tuple<string, string>(e.ChatMessage.Username, cmd));
                    if (Program.pub_socket != null) Program.pub_socket.Send("VoteInfo\n" + e.ChatMessage.Username + "\n" + cmd);
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
            if (e.WhisperMessage.Username == "my_friend")
                client.SendWhisper(e.WhisperMessage.Username, "Hey! Whispers are so cool!!");
        }

        private void Client_OnNewSubscriber(object sender, OnNewSubscriberArgs e)
        {
            /*if (e.Subscriber.SubscriptionPlan == SubscriptionPlan.Prime)
                client.SendMessage(e.Channel, $"Welcome {e.Subscriber.DisplayName} to the substers! You just earned 500 points! So kind of you to use your Twitch Prime on this channel!");
            else
                client.SendMessage(e.Channel, $"Welcome {e.Subscriber.DisplayName} to the substers! You just earned 500 points!");*/
        }
    }
}
