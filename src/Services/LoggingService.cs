using Discord;
using Discord.Commands;
using Discord.WebSocket;
using System;
using System.IO;
using System.Threading.Tasks;
using NLog;
using NLog.LayoutRenderers;
using NLog.Layouts;
using NLog.Targets;

namespace Doccer_Bot.Services
{
    public class LoggingService
    {
        private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

        private readonly DiscordSocketClient _discord;
        private readonly CommandService _commands;

        private string _logDirectory { get; }
        private string _logFile => Path.Combine(_logDirectory, "log.txt");

        // DiscordSocketClient and CommandService are injected automatically from the IServiceProvider
        public LoggingService(DiscordSocketClient discord, CommandService commands)
        {
            _logDirectory = Path.Combine(AppContext.BaseDirectory, "logs");
            
            _discord = discord;
            _commands = commands;
            
            _discord.Log += OnLogAsync;
            _commands.Log += OnLogAsync;

            var logConfig = new NLog.Config.LoggingConfiguration();
            
            var logFile = new NLog.Targets.FileTarget("logFile") { FileName = _logFile, ArchiveEvery = FileArchivePeriod.Day };
            var logConsole = new NLog.Targets.ConsoleTarget("logConsole");

            logConfig.AddRule(LogLevel.Info, LogLevel.Fatal, logFile);
            logConfig.AddRule(LogLevel.Info, LogLevel.Fatal, logConsole);

            NLog.LogManager.Configuration = logConfig;
        }

        private void HandleDiscordLogs(LogSeverity severity, string source, string message, Exception exception = null)
        {
            var logLevel = LogLevel.FromString(Enum.Parse(typeof(LogSeverity), severity.ToString()).ToString());

            Logger.Log(logLevel, message);
        }


        // passes LogMessages from discordclient and commandservice over to the logging function
        private async Task OnLogAsync(LogMessage msg)
        {
            HandleDiscordLogs(msg.Severity, msg.Source, msg.Message, msg.Exception);
        }
    }
}
