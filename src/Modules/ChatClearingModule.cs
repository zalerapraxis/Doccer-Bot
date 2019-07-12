using System;
using System.ComponentModel;
using System.Linq;
using Discord;
using Discord.Commands;
using Discord.WebSocket;
using System.Threading.Tasks;
using Doccer_Bot.Modules.Common;
using Doccer_Bot.Services;

namespace Example.Modules
{
    [Name("Clear")]
    [RequireContext(ContextType.Guild)]
    public class ChatClearingModule : ModuleBase<SocketCommandContext>
    {
        public LoggingService _logger { get; set; }

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
            var messages = await channel.GetMessagesAsync(count + 1).FlattenAsync();

            var server = Servers.ServerList.Find(x => x.DiscordServer == Context.Guild);

            // remove schedule embed message from the messages list, so it doesn't get deleted
            if (server != null && server.EventEmbedMessage != null)
                messages = messages.Where(msg => msg.Id != server.EventEmbedMessage.Id);

            // remove reminder messages from the messages list, so they don't get deleted
            if (server != null && server.Events.Exists(x => x.AlertMessage != null))
                messages = messages.Where(msg => server.Events.Any(x => msg.Id != x.AlertMessage.Id));

            try
            {
                // bulk delete messages - only works on messages less than two weeks old
                await channel.DeleteMessagesAsync(messages);
            }
            catch
            {
                await _logger.Log(new LogMessage(LogSeverity.Info, GetType().Name,
                    "Could not bulk delete messages, switching to individual deletion"));

                // notify the user that they started up a manual delete
                var responseMsg =
                    await ReplyAsync(
                        "Some of the messages you selected are older than two weeks, so we have to individually delete them. This will take a minute.");

                // don't delete the notification
                messages = messages.Where(msg => msg.Id != responseMsg.Id);

                // get messages older than two weeks, which cannot be bulk-deleted, and new messages that can be bulk-deleted
                var oldMessages = messages.Where(msg => msg.Timestamp < DateTimeOffset.Now.AddDays(-14));
                var newMessages = messages.Where(msg => msg.Timestamp > DateTimeOffset.Now.AddDays(-14));

                if (oldMessages.Any())
                {
                    // bulk delete whatever new messages we can
                    await channel.DeleteMessagesAsync(newMessages);

                    // individually delete old messages
                    foreach (var oldMessage in oldMessages)
                    {
                        await oldMessage.DeleteAsync();
                        await Task.Delay(250);
                    }
                }
            }
        }
    }
}
