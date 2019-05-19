using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Discord;
using Discord.Addons.Interactive;
using Discord.Commands;

namespace Doccer_Bot.Modules
{
    public class MiscModule : InteractiveBase
    {
        // set up google auth
        [RequireUserPermission(GuildPermission.Administrator)]
        [Summary("Automatically add vote reactions to the command message.")]
        [Command("vote", RunMode = RunMode.Async)]
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
    }
}
