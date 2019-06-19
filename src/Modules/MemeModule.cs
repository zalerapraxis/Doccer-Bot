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
    [Name("Memes")]
    public class MemeModule : InteractiveBase
    {
        public DatabaseService DatabaseService { get; set; }

        [Command("meme", RunMode = RunMode.Async)]
        [Summary("Posts a copypasta - returns a list of all stored copypasta if left blank")]
        [Example("meme {memename}")]
        public async Task PostMemeCommandAsync([Remainder]string query = null)
        {
            var memes = await DatabaseService.GetTextMemes();

            if (query != null)
            {
                var response = memes.Find(x => x.Name == query);
                await ReplyAsync(response.Text);
            }
            else
            {
                EmbedBuilder embedBuilder = new EmbedBuilder();
                // these each represent a a field in the embed - they map out to columns due to the inline param of addfield
                StringBuilder sBuilder1 = new StringBuilder();
                StringBuilder sBuilder2 = new StringBuilder();

                // sort the results into two columns for displaying in embed
                var i = 0;
                foreach (var meme in memes)
                {
                    // 0 for left column, 1 for right, 2 resets to 0
                    // creates two strings containing alternating lists of meme names from memes list
                    if (i == 2)
                        i = 0;
                    if (i == 0)
                        sBuilder1.AppendLine(meme.Name);
                    if (i == 1)
                        sBuilder2.AppendLine(meme.Name);

                    i++;
                }

                embedBuilder.AddField("Options", sBuilder1.ToString(), true);
                embedBuilder.AddField("\u200b", sBuilder2.ToString(), true); // name string here is a zero-width space

                await ReplyAsync(null, false, embedBuilder.Build());
            }
        }
    }
}
