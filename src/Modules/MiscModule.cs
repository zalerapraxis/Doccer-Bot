using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Discord;
using Discord.Addons.Interactive;
using Discord.Commands;
using Doccer_Bot.Modules.Common;
using Doccer_Bot.Services;

namespace Doccer_Bot.Modules
{
    [Name("Misc")]
    [Remarks("Random stuff")]
    public class MiscModule : InteractiveBase
    {
        [Command("vote", RunMode = RunMode.Async)]
        [Summary("Start a vote using yes/no reactions")]
        [Example("vote {message}")]
        [RequireUserPermission(GuildPermission.Administrator)]
        [RequireBotPermission(ChannelPermission.ManageMessages)]
        public async Task VoteCommandAsync([Remainder]string messageContents)
        {
            // save the message temporarily so we can delete it in a sec
            var commandMessage = Context.Message;

            // make a enumerable of our emojis to use
            IEmote[] emotes = new IEmote[]
            {
                new Emoji("✅"), new Emoji("❌"), 
            };

            // edit our messagecontents with who requested the vote
            messageContents = $"{messageContents} - (from {commandMessage.Author.Mention})";

            // delete the command message
            await commandMessage.DeleteAsync();
            // wait a second to avoid ratelimiting
            await Task.Delay(1500);
            // make a new message with the contents of the command message
            var responseMsg = await ReplyAsync(messageContents);
            // add our vote emojis to the message
            await responseMsg.AddReactionsAsync(emotes);
        }

        [Command("doccer")]
        [Summary("Say hi")]
        public async Task Doccer()
        {
            await ReplyAsync("Hi!");
        }
    }
}
