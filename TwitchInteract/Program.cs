using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Fleck;
using TwitchLib.Client;
using TwitchLib.Client.Enums;
using TwitchLib.Client.Events;
using TwitchLib.Client.Extensions;
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

        public static void Main(string[] args)
        {
			Console.CancelKeyPress += (sender, eArgs) => {
				_quitEvent.Set();
				eArgs.Cancel = true;
			};
			bool endapp = false;
            WebSocketServer server = new WebSocketServer("ws://0.0.0.0:8765");
			server.RestartAfterListenError = true;

            string BotName, BotOauth, TwitchChannel;
            if (File.Exists(Directory.GetCurrentDirectory() + "/config.ini"))
            {
                var parser = new FileIniDataParser();
                IniData data = parser.ReadFile("config.ini");
                BotName = data["TGM"]["BotName"];
                BotOauth = data["TGM"]["BotOauth"];
                TwitchChannel = data["TGM"]["TwitchChannel"];
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
                parser.WriteFile("config.ini", data);
            }
            Bot bot = new Bot(BotName, BotOauth, TwitchChannel);

            server.Start(socket =>
            {
                Program.pub_socket = socket;
                socket.OnOpen = () => Console.WriteLine("Open server");
                socket.OnClose = () => Console.WriteLine("Close server");
                socket.OnMessage = message => MessageHandler(message, socket);
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
                        if (!Bot.SilentMode) bot.client.SendMessage(TwitchChannel, actions[i]);
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
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("Initialized...");
			_quitEvent.WaitOne();
		}
    }

    class Bot
    {
        public TwitchClient client;
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
            Console.WriteLine("Connected successfully!");
            if (!SilentMode) client.SendMessage(e.Channel, "Connected successfully!");
        }

        private void Client_OnMessageReceived(object sender, OnMessageReceivedArgs e)
        {
            if (e.ChatMessage.Message[0] == '!')
            {
                string cmd = e.ChatMessage.Message.Substring(1);
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("Received command: " + cmd);
                if (Program.VotingTime)
                {
                    //Program.VoteMessages.Add(new Tuple<string, string>(e.ChatMessage.Username, cmd));
                    if (Program.pub_socket != null) Program.pub_socket.Send("VoteInfo\n" + e.ChatMessage.Username + "\n" + cmd);
                }
            }
            else 
            {
				if (PrintTwitchChat)
				{
					if (Program.pub_socket != null && Program.pub_socket.ConnectionInfo.ClientIpAddress != null)
					{
						Console.WriteLine(Program.pub_socket.ConnectionInfo.ClientIpAddress);
						Program.pub_socket.Send("PrintTwitchChat\n" + e.ChatMessage.Username + "\n" + e.ChatMessage.Message);
					}
				}
            }
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
