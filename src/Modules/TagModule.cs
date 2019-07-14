using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Discord;
using Discord.Addons.Interactive;
using Discord.Commands;
using Discord.WebSocket;
using Doccer_Bot.Modules.Common;
using Doccer_Bot.Services;
using Doccer_Bot.Services.DatabaseServiceComponents;

namespace Doccer_Bot.Modules
{
    [Name("Tags")]
    [Remarks("Store & retrieve info, links, funny stuff, etc.")]
    public class TagModule : InteractiveBase
    {
        public DatabaseTags DatabaseTags { get; set; }
        public DiscordSocketClient DiscordSocketClient { get; set; }
        public EventReactionAddedService EventReactionAddedService { get; set; }

        private Dictionary<IUser, IUserMessage> _dictFindTagUserEmbedPairs = new Dictionary<IUser, IUserMessage>();
        

        [Command("tag", RunMode = RunMode.Async)]
        [Summary("Get a tag by name")]
        [Example("tag {name}")]
        public async Task TagGetCommandAsync(string tagName)
        {
            // check if user intended to run a different command but forgot some parameters
            // since this is the .tag command and other commands use .tag as their base, the bot may
            // redirect commands to this function, which will cause it to search for the command function
            // as if it were a tag - catch these and notify the user to check their syntax.
            // doing it this way so we can keep the base .tag command and have .tag function commands as well
            List<string> commandCatchFilter = new List<string>()
            {
                "add", "remove", "edit", "rename", "describe", "global", "info", "search"
            };
            if (commandCatchFilter.Contains(tagName.ToLower()))
            {
                await ReplyAsync("You're missing some parts from that command. Check your syntax.");
                return;
            }
            
            var response = await DatabaseTags.GetTagContentsFromDatabase(Context, tagName);

            // if we found a response, use it
            if (response != null)
                await ReplyAsync(response);
            else
            {
                // attempt to find a tag & retry
                await FindTagAndRetry("get", tagName);
            }
        }


        [Command("tag add", RunMode = RunMode.Async)]
        [Summary("Add a new tag")]
        [Alias("tag create")]
        [Example("tag add {name} {content}")]
        public async Task TagAddCommandAsync(string tagName, [Remainder] string content)
        {
            if (!content.Any())
            {
                await ReplyAsync(
                    "You did not enter anything to put inside the tag. The syntax is `.tag add name content`.");
                return;
            }

            var success = await DatabaseTags.AddTagToDatabase(Context, tagName, content);

            if (success)
                await ReplyAsync($"Tag '{tagName}' added.");
            else
                await ReplyAsync("Couldn't add tag - a tag with this name already exists.");
        }


        [Command("tag make", RunMode = RunMode.Async)]
        [Summary("React a ⭐ to a message you want to make into a tag")]
        public async Task TagMakeCommandAsync(string tagName = null)
        {
            // find a msg that the calling user has reacted with the right emote to
            IEmote starEmote = new Emoji("⭐");
            var message = await EventReactionAddedService.GetMessageByReactionAdded(starEmote, Context);

            if (message == null)
            {
                await ReplyAsync($"You need to select a message (using an {starEmote} reaction) to use this command.");
            }

            await ReplyAsync("What do you want to name your tag?");
            var userResponseTagName = await NextMessageAsync(true, true, TimeSpan.FromSeconds(30));
            if (userResponseTagName == null)
            {
                await ReplyAsync("You took too long choosing a name. Try again.");
                return;
            }

            var success = await DatabaseTags.AddTagToDatabase(Context, userResponseTagName.Content, message.Message.Content);

            if (success)
                await ReplyAsync($"Tag '{userResponseTagName}' added.");
            else
                await ReplyAsync("Couldn't add tag - a tag with this name already exists.");
        }


        [Command("tag remove", RunMode = RunMode.Async)]
        [Summary("Remove a tag")]
        [Alias("tag delete")]
        [Example("tag remove {name}")]
        public async Task TagRemoveCommandAsync(string tagName)
        {
            var result = await DatabaseTags.RemoveTagFromDatabase(Context, tagName);

            if (result == 2)
                await ReplyAsync($"Tag '{tagName}' deleted.");
            else if (result == 1)
                await ReplyAsync("Couldn't remove tag - you are not the author of that tag.");
            else if (result == 0)
            {
                // attempt to find a tag & retry
                await FindTagAndRetry("remove", tagName);
            }
        }


        [Command("tag edit", RunMode = RunMode.Async)]
        [Summary("Edit a tag's contents")]
        [Example("tag edit {name} {content}")]
        public async Task TagEditCommandAsync(string tagName, [Remainder] string newContent)
        {
            var result = await DatabaseTags.EditTagInDatabase(Context, tagName, "text", newContent);

            if (result == 2)
                await ReplyAsync($"Tag '{tagName}' edited.");
            else if (result == 1)
                await ReplyAsync("Couldn't edit tag - you are not the author of that tag.");
            else if (result == 0)
            {
                // attempt to find a tag & retry
                await FindTagAndRetry("edit", tagName, newContent);
            }
                
        }


        [Command("tag rename", RunMode = RunMode.Async)]
        [Summary("Rename a tag")]
        [Example("tag rename {name} {newName}")]
        public async Task TagRenameCommandAsync(string tagName, string newName)
        {
            var result = await DatabaseTags.EditTagInDatabase(Context, tagName, "name", newName);

            if (result == 2)
                await ReplyAsync($"Tag '{tagName}' renamed to '{newName}'.");
            else if (result == 1)
                await ReplyAsync("Couldn't rename tag - you are not the author of that tag.");
            else if (result == 0)
            {
                // attempt to find a tag & retry
                await FindTagAndRetry("rename", tagName, newName);
            }
        }


        [Command("tag describe", RunMode = RunMode.Async)]
        [Summary("Describe a tag - descriptions are optional and used for tag lists & info")]
        [Example("tag describe {description}")]
        public async Task TagDescribeCommandAsync(string tagName, [Remainder] string description)
        {
            var result = await DatabaseTags.EditTagInDatabase(Context, tagName, "description", description);

            if (result == 2)
                await ReplyAsync($"Tag '{tagName}' description set.");
            else if (result == 1)
                await ReplyAsync("Couldn't set tag's description - you are not the author of that tag.");
            else if (result == 0)
            {
                // attempt to find a tag & retry
                await FindTagAndRetry("describe", tagName, description);
            }
        }


        [Command("tag global", RunMode = RunMode.Async)]
        [Summary("Toggle the tag's global status. Global tags can be used on other servers")]
        [Example("tag global {name} {true/false}")]
        public async Task TagGlobalCommandAsync(string tagName, string flag)
        {
            bool global;

            if (flag == "true" || flag == "yes")
                global = true;
            else
            if (flag == "false" || flag == "no")
                global = false;
            else
            {
                await ReplyAsync($"Invalid flag '{flag}' entered. Command accepts true/yes and false/no.");
                return;
            }

            var result = await DatabaseTags.EditTagInDatabase(Context, tagName, "global", global);

            if (result == 2)
                await ReplyAsync($"Tag '{tagName}' global status set to '{flag}'.");
            else if (result == 1)
                await ReplyAsync("Couldn't set tag's global status - you are not the author of that tag.");
            else if (result == 0)
            {
                // attempt to find a tag & retry
                await FindTagAndRetry("global", tagName, flag);
            }
        }


        [Command("tag list", RunMode = RunMode.Async)]
        [Summary("Get a list of tags by you or someone else")]
        [Example("tag list (@username) - username is optional")]
        public async Task TagGetByUserCommandAsync(IUser user = null)
        {
            var results = await DatabaseTags.GetTagsByUserFromDatabase(Context, user);

            if (results.Any())
            {
                EmbedBuilder embedBuilder = new EmbedBuilder();
                StringBuilder stringBuilder = new StringBuilder();

                foreach (var result in results)
                {
                    stringBuilder.AppendLine(result);
                }

                embedBuilder.AddField("Tags", stringBuilder.ToString(), true);
                
                // build author field with mentioned user or calling user
                if (user != null)
                    embedBuilder.WithAuthor(user.Username, user.GetAvatarUrl());
                else
                    embedBuilder.WithAuthor(Context.User.Username, Context.User.GetAvatarUrl());
                
                await ReplyAsync(null, false, embedBuilder.Build());
            }
            else
            {
                await ReplyAsync("User hasn't made any tags.");
            }
            
        }


        [Command("tag info", RunMode = RunMode.Async)]
        [Summary("Get tag info")]
        [Example("tag info {name}")]
        public async Task TagGetInfoCommandAsync(string tagName)
        {
            var tag = await DatabaseTags.GetTagInfoFromDatabase(Context, tagName);

            if (tag != null)
            {
                EmbedBuilder embedBuilder = new EmbedBuilder();

                // get author info
                var author = DiscordSocketClient.GetUser((ulong) tag.AuthorId);

                embedBuilder.Title = $"Tag: {tagName}";

                // if the author of the tag still exists in the server, use their name
                // otherwise, just display unknown, no need to handle this
                if (author != null) 
                    embedBuilder.AddField("Owner", author, true);
                else
                    embedBuilder.AddField("Owner", "Unknown", true);

                // if description is set, display it
                if (tag.Description.Length > 0)
                    embedBuilder.AddField("Description", tag.Description);

                embedBuilder.AddField("Uses", tag.Uses, true);
                embedBuilder.AddField("Global", tag.Global, true);
                embedBuilder.WithFooter("Tag created at");
                embedBuilder.WithTimestamp(tag.DateAdded);
                embedBuilder.WithColor(Color.Blue);

                await ReplyAsync(null, false, embedBuilder.Build());
            }

            else
            {
                // attempt to find a tag & retry
                await FindTagAndRetry("info", tagName);
            }
        }


        [Command("tag all", RunMode = RunMode.Async)]
        [Summary("Get list of all tags")]
        [Alias("tags")]
        public async Task TagGetAllCommandAsync(string extra = null)
        {
            var results = await DatabaseTags.GetAllTagsFromDatabase(Context);

            if (results.Any())
            {
                EmbedBuilder embedBuilder = new EmbedBuilder();
                StringBuilder tagsColumn1builder = new StringBuilder();
                StringBuilder tagsColumn2builder = new StringBuilder();

                var i = 0;
                foreach (var result in results)
                {
                    // 0 for left column, 1 for right, 2 resets to 0
                    // creates two strings containing alternating lists of meme names from memes list
                    if (i == 2)
                        i = 0;
                    if (i == 0)
                        tagsColumn1builder.AppendLine(result);
                    if (i == 1)
                        tagsColumn2builder.AppendLine(result);

                    i++;
                }

                embedBuilder.AddField("Tags", tagsColumn1builder.ToString(), true);
                embedBuilder.AddField("\u200b", tagsColumn2builder.ToString(), true);

                await ReplyAsync(null, false, embedBuilder.Build());
            }
            else
                await ReplyAsync("No tags found for this server.");
        }


        [Command("tag search", RunMode = RunMode.Async)]
        [Summary("Search for a tag - tag search {searchterm}")]
        [Alias("tag find")]
        [Example("tag search {searchterm}")]
        public async Task TagSearchCommandAsync(string search)
        {
            var results = await DatabaseTags.SearchTagsInDatabase(Context, search);

            if (results != null)
            {
                EmbedBuilder embedBuilder = new EmbedBuilder();
                StringBuilder stringBuilder = new StringBuilder();

                foreach (var result in results)
                {
                    stringBuilder.AppendLine(result);
                }

                embedBuilder.AddField("Tags", stringBuilder.ToString(), true);

                await ReplyAsync(null, false, embedBuilder.Build());
            }
            else
                await ReplyAsync("No results found.");
        }

        // if the user supplies a tagname that doesn't exist, search the database to see if there are
        // any tags containing the user-supplied text

        // if there are any results from the database, post a list of the results & add emoji reactions
        // corresponding to each item in the list

        // when the user selects a emoji, it will pass the corresponding tagName and any extra passed
        // parameters to the RetryCommandUsingFoundTag function

        // afterwards, save a pairing of the calling user object and the searchResults message into a 
        // dictionary, for use by the RetryCommandUsingFoundTag function
        private async Task FindTagAndRetry(string functionToRetry, params string[] args)
        {
            var tagName = args[0];

            var searchResponse = await DatabaseTags.SearchTagsInDatabase(Context, tagName);

            // if single result, just use that
            if (searchResponse.Count == 1)
            {
                await RetryCommandUsingFoundTag(searchResponse[0], functionToRetry, args);
                return;
            }
                
            // if the single-result check didn't catch but there are results
            if (searchResponse.Any())
            {
                string[] numbers = new[] { "0⃣", "1⃣", "2⃣", "3⃣", "4⃣", "5⃣", "6⃣", "7⃣", "8⃣", "9⃣" };
                var numberEmojis = new List<Emoji>();

                EmbedBuilder embedBuilder = new EmbedBuilder();
                StringBuilder stringBuilder = new StringBuilder();

                // add the number of emojis we need to the emojis list, and build our string-list of search results
                for (int i = 0; i < searchResponse.Count && i < numbers.Length; i++)
                {
                    numberEmojis.Add(new Emoji(numbers[i]));
                    stringBuilder.AppendLine($"{numbers[i]} - {searchResponse[i]}");
                }

                embedBuilder.WithDescription(stringBuilder.ToString());
                embedBuilder.WithColor(Color.Blue);

                // build a message and add reactions to it
                // reactions will be watched, and the one selected will fire the HandleFindTagReactionResult method, passing
                // that reaction's corresponding tagname and the function passed into this parameter
                var messageContents = new ReactionCallbackData("Did you mean... ", embedBuilder.Build());
                for (int i = 0; i < searchResponse.Count; i++)
                {
                    var counter = i;
                    messageContents.AddCallBack(numberEmojis[counter], (c, r) => RetryCommandUsingFoundTag(searchResponse[counter], functionToRetry, args));
                }

                var message = await InlineReactionReplyAsync(messageContents);

                // add calling user and searchResults embed to a dict as a pair
                // this way we can hold multiple users' reaction messages and operate on them separately
                _dictFindTagUserEmbedPairs.Add(Context.User, message);
            }
            else
            {
                await ReplyAsync("I can't find any tags like what you're looking for.");
            }
        }

        // delete the searchResults message from the calling function for the calling user, and then
        // re-call the parent command function with the new tagName and the old parameters
        private async Task RetryCommandUsingFoundTag(string foundTagName, string functionToRetry, params string[] args)
        {
            // grab the calling user's pair of calling user & searchResults embed
            var dictEntry = _dictFindTagUserEmbedPairs.FirstOrDefault(x => x.Key == Context.User);

            // delete the calling user's searchResults embed, if it exists
            if (dictEntry.Key != null)
                await dictEntry.Value.DeleteAsync();

            // pick out the function to retry and pass the original
            // function's arguments back into it with the newly selected tag
            // args[0] will be the incomplete tagname, args[1] and onward will be any other arguments
            switch (functionToRetry)
            {
                case "get":
                    await TagGetCommandAsync(foundTagName);
                    break;
                case "remove":
                    await TagRemoveCommandAsync(foundTagName);
                    break;
                case "edit":
                    await TagEditCommandAsync(foundTagName, args[1]);
                    break;
                case "rename":
                    await TagRenameCommandAsync(foundTagName, args[1]);
                    break;
                case "describe":
                    await TagDescribeCommandAsync(foundTagName, args[1]);
                    break;
                case "global":
                    await TagGlobalCommandAsync(foundTagName, args[1]);
                    break;
                case "info":
                    await TagGetInfoCommandAsync(foundTagName);
                    break;
            }
        }
    }
}
