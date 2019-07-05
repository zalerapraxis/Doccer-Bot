using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using Discord.WebSocket;
using Example;

namespace Doccer_Bot.Services
{
    public class TextMemeService
    {
        private readonly DatabaseService _databaseService;
        private readonly DiscordSocketClient _discord;


        public TextMemeService(DatabaseService databaseService, DiscordSocketClient discord)
        {
            _databaseService = databaseService;
            _discord = discord;

            // uncomment this to subscribe to the messagereceived event
            //_discord.MessageReceived += HandleNonCommandChatTriggers;
        }

        public string GetMemeTextForNoEvents()
        {
            var memes = _databaseService.GetTextMemes().Result;

            if (memes.Count == 0) // didn't find any text files in the directory, so return a filler string
                return "\"Just buy more raid days 4head\"";
            // randomly select a file by generating an index value
            Random rng = new Random();
            int index = rng.Next(0, memes.Count);
            var meme  = memes[index];

            return meme.Text;
        }

        public async Task HandleNonCommandChatTriggers(SocketMessage messageParam)
        {
            // Don't process the command if it was a system message
            var message = messageParam as SocketUserMessage;
            if (message == null) return;

            // Create a number to track where the prefix ends and the command begins
            int argPos = 0;

            // Determine if the message is a command based on the prefix and make sure no bots trigger commands
            // we want to end this call if any of these are true
            if ((message.HasCharPrefix('!', ref argPos) ||
                  message.HasMentionPrefix(_discord.CurrentUser, ref argPos)) ||
                message.Author.IsBot)
                return;

            // get the context of the message
            var context = new SocketCommandContext(_discord, message);

            // check messages for keywords to respond to here
        }
    }
}
