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

            // get messages older than two weeks, which cannot be bulk-deleted
            var oldMessages = messages.Where(msg => msg.Timestamp.AddDays(14) < DateTimeOffset.Now);

            var server = Servers.ServerList.Find(x => x.DiscordServer == Context.Guild);

            if (server != null && server.EventEmbedMessage != null)
                // remove schedule embed message from the messages list, so it doesn't get deleted
                messages = messages.Where(msg => msg.Id != server.EventEmbedMessage.Id);

            try
            {
                // bulk delete messages - only works on messages less than two weeks old
                await channel.DeleteMessagesAsync(messages);
            }
            catch
            {
                if (oldMessages.Any())
                {
                    // individually delete messages two weeks or older
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
