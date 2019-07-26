using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using Discord.WebSocket;
using Doccer_Bot.Models;

namespace Doccer_Bot.Services
{
    public class EventReactionAddedService
    {
        private readonly DiscordSocketClient _discord;

        private List<ReactionAddedEventMessage> ReactedMessages = new List<ReactionAddedEventMessage>();

        public EventReactionAddedService(DiscordSocketClient discord)
        {
            _discord = discord;

            // uncomment this to subscribe to the messagereceived event
            //_discord.MessageReceived += HandleNonCommandChatTriggers;
            _discord.ReactionAdded += HandleReactionAddedEvent;
        }

        
        private async Task HandleReactionAddedEvent(Cacheable<IUserMessage, ulong> cacheable, ISocketMessageChannel channel, SocketReaction reaction)
        {
            // get rid of any messages stored away that are older than 5 minutes
            ReactedMessages = ReactedMessages.Where(x => x.Timestamp > DateTime.Now.AddMinutes(-5)).ToList();

            // get rid of any messages that use the same reaction from the same user
            ReactedMessages = ReactedMessages
                .Where(x => x.Reaction.UserId != reaction.UserId && !(x.Reaction.Emote.Equals(reaction.Emote))).ToList();

            // get message and add it to list
            var reactedMessage = await cacheable.GetOrDownloadAsync();

            // add it to the list
            var tempMessage = new ReactionAddedEventMessage()
            {
                Message = reactedMessage,
                Reaction = reaction,
                Server = ((IGuildChannel) channel).Guild as SocketGuild,
                Timestamp = DateTime.Now                
            };
            ReactedMessages.Add(tempMessage);
        }

        public async Task<ReactionAddedEventMessage> GetMessageByReactionAdded(IEmote reactionEmote, SocketCommandContext context)
        {
            // select a message where the message reaction equals the requested emote, and the context user matches the user who reacted with the emote
            var msg = ReactedMessages.FirstOrDefault(x => x.Reaction.Emote.Equals(reactionEmote) && x.Reaction.UserId == context.User.Id);

            if (msg != null)
            {
                // remove this message from the reactedmessages list
                ReactedMessages = ReactedMessages.Where(x => x.Message.Id != msg.Message.Id).ToList();

                return msg;
            }    
            else
                return null;
        }
    }
}
