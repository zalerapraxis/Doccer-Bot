using System;
using System.ComponentModel;
using System.Linq;
using Discord;
using Discord.Commands;
using Discord.WebSocket;
using System.Threading.Tasks;
using Doccer_Bot.Modules.Common;
using Doccer_Bot.Services;
using NLog;

namespace Example.Modules
{
    [Name("Clear")]
    [Remarks("Clearing chat messages")]
    [RequireContext(ContextType.Guild)]
    public class ChatClearingModule : ModuleBase<SocketCommandContext>
    {
        public EventReactionAddedService EventReactionAddedService { get; set; }

        private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

        // clear chatlogs
        [Command("clear")]
        [Summary("Clears the last x messages in current channel - default 100")]
        [Alias("clean", "prune")]
        [Example("clear 100")]
        [RequireBotPermission(ChannelPermission.ManageMessages)]
        [RequireUserPermission(GuildPermission.Administrator)]
        public async Task ClearChatlogsAsync(int count = 100)
        {
            var channel = Context.Channel as SocketTextChannel;
            var messages = await channel.GetMessagesAsync(count).FlattenAsync();
            var server = Servers.ServerList.Find(x => x.DiscordServerObject == Context.Guild);

            // remove schedule embed message from the messages list, so it doesn't get deleted
            if (server != null && server.EventEmbedMessage != null)
                messages = messages.Where(msg => msg.Id != server.EventEmbedMessage.Id);

            // remove reminder messages from the messages list, so they don't get deleted
            if (server != null && server.Events.Exists(x => x.AlertMessage != null))
                messages = messages.Where(msg => server.Events.Any(x => msg.Id != x.AlertMessage.Id));

            // delete the command message
            await Context.Message.DeleteAsync();

            try
            {
                // bulk delete messages - only works on messages less than two weeks old
                await channel.DeleteMessagesAsync(messages);
            }
            catch
            {
                Logger.Log(LogLevel.Info, "Could not bulk delete messages, switching to individual deletion");

                // get messages older than two weeks, which cannot be bulk-deleted, and new messages that can be bulk-deleted
                var oldMessages = messages.Where(msg => msg.Timestamp < DateTimeOffset.Now.AddDays(-14));
                var newMessages = messages.Where(msg => msg.Timestamp > DateTimeOffset.Now.AddDays(-14));

                if (oldMessages.Any())
                {
                    // notify the user that they started up a manual delete
                    var responseMsg =
                        await ReplyAsync(
                            "Some of the messages you selected are older than two weeks, so we have to individually delete them. This will take a minute.");

                    // don't delete the notification yet
                    newMessages = newMessages.Where(msg => msg.Id != responseMsg.Id);

                    // bulk delete whatever new messages we can
                    await channel.DeleteMessagesAsync(newMessages);

                    // individually delete old messages
                    foreach (var oldMessage in oldMessages)
                    {
                        await oldMessage.DeleteAsync();
                        await Task.Delay(250);
                    }

                    // done with deleting stuff, so delete the notification
                    await responseMsg.DeleteAsync();
                }
            }
        }


        // clear chatlogs
        [Command("rclear")]
        [Summary("Clears messages up to a message specified via a reaction")]
        [RequireBotPermission(ChannelPermission.ManageMessages)]
        [RequireUserPermission(GuildPermission.Administrator)]
        public async Task ReactionClearChatlogsAsync()
        {
            var channel = Context.Channel as SocketTextChannel;

            IEmote deleteEmote = new Emoji("✖");
            var message = await EventReactionAddedService.GetMessageByReactionAdded(deleteEmote, Context);

            if (message != null)
            {
                // get all messages from after the reacted message
                var messagesAfter = await channel.GetMessagesAsync(message.Message, Direction.After).FlattenAsync();

                // and bulk delete them
                if (messagesAfter.Any())
                {
                    // delete the reacted message and everything after it
                    await channel.DeleteMessageAsync(message.Message);
                    await channel.DeleteMessagesAsync(messagesAfter);
                }
                    
            }
            else
            {
                await ReplyAsync($"You need to select a message (using an {deleteEmote} reaction) to use this command.");
            }
        }
    }
}
