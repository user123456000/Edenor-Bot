﻿global using System;
global using System.Threading;
global using System.Threading.Tasks;
global using Discord;
global using Discord.WebSocket;
global using System.Linq;
global using System.Text.Json;
global using System.IO;
global using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Net;
using Discord.Net;
using Discord.Rest;
using MinecraftConnection;
using MinecraftConnection.RCON;
using OpenAI_API;
using OpenAI_API.Models;
using Discord_Bot.handlers;
using System.Net.Http;
using Discord.Webhook;
using Newtonsoft.Json;
using System.ComponentModel;

namespace Discord_Bot
{
    class Program
    {
        public static Program instance = null;
        private DiscordWebhookClient loggerWebhook;
        public DiscordSocketClient client;
        public SocketGuild edenor;
        public OpenAIAPI openAIAPI;
        public UserDatabase userDatabase;
        static Timer googleTimer;
        static Timer databaseUpdater;
        static void Main(string[] args)
            => new Program().MainAsync().GetAwaiter().GetResult();

        static string configDir = (Environment.CurrentDirectory + "/config.json");

        private bool ready = false;

        private BotConfig config = null;

        public MinecraftCommands rcon;

        static bool enableCommands = true;

        //CallbackAPIHandler callbackAPI;

        public Program()
        {
            logInfo("Trying to start bot!");

            instance = this;

            logInfo("Trying to find config!");
            string stream = File.ReadAllText(configDir);
            config = System.Text.Json.JsonSerializer.Deserialize<BotConfig>(stream);

            config.enableRconFunctions = config.enableRconFunctions == null ? false : config.enableRconFunctions;

            if (config.loggerWebhookURL != null)
            {
                loggerWebhook = new DiscordWebhookClient(config.loggerWebhookURL);
            }

            logInfo("Setuping timers!");
            googleTimer = new Timer(GoogleSheetsHelper.timer, new AutoResetEvent(true), 1000, 1800000);
            databaseUpdater = new Timer(UserDatabase.timer, new AutoResetEvent(true), 300000, 300000);

            if (config.enableRconFunctions)
            {
                if (config.rconIP != null && config.rconPort != null && config.rconPassword != null)
                {
                    try
                    {
                        rcon = new MinecraftCommands(config.rconIP, Convert.ToUInt16(config.rconPort), config.rconPassword);
                    }
                    catch (Exception e)
                    {
                        logError(e.Message + e.StackTrace);
                    }
                }
            }

            /*if (config.openAIAPIKey != null)
            {
                openAIAPI = new OpenAIAPI(new APIAuthentication(config.openAIAPIKey));
                openAIAPI.Chat.DefaultChatRequestArgs = new OpenAI_API.Chat.ChatRequest() { Model = Model.ChatGPTTurbo, MaxTokens = 5};
                ChatGPTModule.chat = openAIAPI.Chat.CreateConversation();
                ChatGPTModule.ready = true;
                ChatGPTModule.chat.AppendSystemMessage("Ты дискорд бот дискорд сервера по майнкрафт серверу под названием Эденор. Соответственно тебя тоже зовут Эденор. Ты должен помогать игрокам по вопросам игры или давать им совет обращаться к администрации, если ответа на этот вопрос нигде нет.");
            }*/

            logInfo("Starting bot!");
            var socketConfig = new DiscordSocketConfig
            {
                GatewayIntents = GatewayIntents.AllUnprivileged | GatewayIntents.GuildMembers | GatewayIntents.MessageContent,
                MessageCacheSize = 50,
                AlwaysDownloadUsers = true
            };

            client = new DiscordSocketClient(socketConfig);
            client.MessageReceived += MessagesHandler;
            client.Log += Log;
            client.Ready += onReady;
            client.SlashCommandExecuted += CommandsHandler.onCommand;
            client.ButtonExecuted += ButtonsHandler.onButton;
            client.MessageDeleted += onMessageDeleted;
            client.ModalSubmitted += ModalsHandler.onModal;
            client.SelectMenuExecuted += SelectMenuHandler.onSelect;
            client.UserBanned += BanHandler.onBan;
            client.UserJoined += OnGuildJoin.onJoin;
            client.ThreadCreated += TicketHandler.onNewThread;
            client.Disconnected += onDisconnected;
            client.GuildMemberUpdated += OnUserUpdated.onUpdate;

            logInfo("Setuping GoogleSheetsHelper");
            GoogleSheetsHelper.setupHelper();

            logInfo("Trying to load user database!");
            userDatabase = new UserDatabase(677860751695806515);

            /*while (enableCommands)
            {
                if (Console.ReadLine() != null)
                {
                    string cmd = Console.ReadLine();
                    if (cmd == "exit")
                    {
                        try
                        {
                            logInfo("Trying to kill bot process");
                            Process.GetCurrentProcess().Kill();
                        }
                        catch (Exception ex)
                        {
                            logError("Failed to kill bot process!\n" + ex.Message + ex.StackTrace);
                        }
                    }
                    else if (cmd == "saveBase")
                    {
                        UserDatabase.saveData();
                    }
                }
            }*/
        }
        private async Task MainAsync()
        {
            /*callbackAPI = new CallbackAPIHandler();
            await callbackAPI.startHost();*/

            if (config.token == null)
            {
                logError("Invalid token!");
                Process.GetCurrentProcess().Kill();
            }

            logInfo("Finishing bot starting!");
            var token = config.token;

            await client.LoginAsync(TokenType.Bot, token);
            await client.StartAsync();
            
            await Task.Delay(Timeout.Infinite);

            await GoogleSheetsHelper.reloadInfos();
            await NumberCountingModule.loadAll();
        }
        private async Task onReady()
        {
            ready = true;
            logTrace("Ready to work, bitches!");

            var edenGame = new Game("Эденор", ActivityType.Playing, ActivityProperties.Join, "https://edenor.ru/");
            await client.SetActivityAsync(edenGame);

            edenor = client.CurrentUser.MutualGuilds.First(); //Easy access to edenor guild

            logInfo("Setup bot commands!");
            await handlers.CommandsHandler.setupCommands();

            logInfo("Trying to init database!");
            await userDatabase.initDatabase();
        }
        private async Task onDisconnected(Exception arg)
        {
            logError(arg.Message + arg.StackTrace);
            await userDatabase.saveData();
        }

        public void start(object stateInfo)
        {
            client.StartAsync();
        }
        private Task Log(LogMessage arg)
        {
            switch (arg.Severity)
            {
                case LogSeverity.Critical:
                    logCritical(arg.ToString(), arg.Source);
                    break;
                case LogSeverity.Error:
                    logError(arg.ToString(), arg.Source);
                    break;
                case LogSeverity.Warning:
                    logWarn(arg.ToString(), arg.Source);
                    break;
                case LogSeverity.Info:
                    logInfo(arg.ToString(), arg.Source);
                    break;
                default:
                    logTrace(arg.ToString(), arg.Source);
                    break;
            }
            return Task.CompletedTask;
        }
        private async Task MessagesHandler(SocketMessage msg)
        {
            if (msg.Channel.Id == 1062273336354275348)
            {
                await NumberCountingModule.doWork(msg);
                return;
            }

            /*if (msg.Content.Contains($"<@{client.CurrentUser.Id}>"))
            {
                await ChatGPTModule.HandleMessage(msg);
                return;
            }*/

            return;
        }

        private Task onMessageDeleted(Cacheable<IMessage, ulong> arg1, Cacheable<IMessageChannel, ulong> arg2)
        {
            try
            {
                if (arg2.Value.Id == 1062273336354275348)
                {
                    NumberCountingModule.onMessageDeleted(arg1.Value, arg2.Value);
                }
                return Task.CompletedTask;
            }
            catch (Exception e)
            {
                logError(e.Message);
                return Task.CompletedTask;
            }
        }

        private static string Timestamp => $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}";
        public static async Task logTrace(object msg, [CallerMemberName] string caller = "", [CallerLineNumber] int lineNumber = 0)
        {
            msg = msg.ToString();
            if (instance.loggerWebhook == null)
            {
                Console.WriteLine($"[{Timestamp}] {caller} line: {lineNumber}: [TRACE]:  {msg}");
            }
            else
            {
                await instance.loggerWebhook.SendMessageAsync($"[{Timestamp}] {caller} line: {lineNumber}: [TRACE]:  {msg}");
            }        
        }

        public static async Task logError(object msg, [CallerMemberName] string caller = "", [CallerLineNumber] int lineNumber = 0)
        {
            msg = msg.ToString();
            if (instance.loggerWebhook == null)
            {
                Console.WriteLine($"[{Timestamp}] {caller} line: {lineNumber}: [ERROR]:  {msg}");
            }
            else
            {
                await instance.loggerWebhook.SendMessageAsync($"[{Timestamp}] {caller} line: {lineNumber}: [ERROR]:  {msg}");
            }
        }

        public static async Task logInfo(object msg, [CallerMemberName] string caller = "", [CallerLineNumber] int lineNumber = 0)
        {
            msg = msg.ToString();
            if (instance.loggerWebhook == null)
            {
                Console.WriteLine($"[{Timestamp}] {caller} line: {lineNumber}: [INFO]:  {msg}");
            }
            else
            {
                await instance.loggerWebhook.SendMessageAsync($"[{Timestamp}] {caller} line: {lineNumber}: [INFO]:  {msg}");
            }
        }

        public static async Task logWarn(object msg, [CallerMemberName] string caller = "", [CallerLineNumber] int lineNumber = 0)
        {
            if (instance.loggerWebhook == null)
            {
                Console.WriteLine($"[{Timestamp}] {caller} line: {lineNumber}: [WARN]:  {msg}");
            }
            else
            {
                await instance.loggerWebhook.SendMessageAsync($"[{Timestamp}] {caller} line: {lineNumber}: [WARN]:  {msg}");
            }
        }

        public static async Task logCritical (object msg, [CallerMemberName] string caller = "", [CallerLineNumber] int lineNumber = 0)
        {
            msg = msg.ToString();
            if (instance.loggerWebhook == null)
            {
                Console.WriteLine($"[{Timestamp}] {caller} line: {lineNumber}: [CRITICAL ERROR]:  {msg}");
            }
            else
            {
                await instance.loggerWebhook.SendMessageAsync($"[{Timestamp}] {caller} line: {lineNumber}: [CRITICAL ERROR]:  {msg}");
            }
        }

        public void timer(object stateInfo)
        {
            client.StartAsync();
        }
    }

    class BotConfig
    {
        public string token { set; get; }
        public bool enableRconFunctions { set; get; }
        public string rconIP { set; get; }
        public string rconPort { set; get;}
        public string rconPassword { set; get; }
        public string openAIAPIKey { set; get; }

        public string loggerWebhookURL { set; get; }
    }
}
