using System.ComponentModel;
using System.Linq;
using Discord;
using Discord.Commands;
using Discord.WebSocket;
using System.Threading.Tasks;
using Doccer_Bot.Services;

namespace Example.Modules
{
    [Name("Moderator")]
    [RequireContext(ContextType.Guild)]
    public class ChatClearingModule : ModuleBase<SocketCommandContext>
    {
        // Dependency Injection will fill this value in for us
        public ScheduleService ScheduleService { get; set; }

        // clear chatlogs
        [Command("clear")]
        [Summary("Clears the last x messages in current channel.")]
        [RequireUserPermission(GuildPermission.Administrator)]
        public async Task ClearChatlogsAsync(int count = 100)
        {
            var channel = Context.Channel as SocketTextChannel;
            var messages = await channel.GetMessagesAsync(count + 1).FlattenAsync();

            if (ScheduleService._eventEmbedMessage != null)
                // remove schedule embed message from the messages list, so it doesn't get deleted
                messages = messages.Where(msg => msg.Id != ScheduleService._eventEmbedMessage.Id);

            await channel.DeleteMessagesAsync(messages);
        }
    }
}
