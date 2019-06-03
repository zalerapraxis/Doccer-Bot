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
    [Name("Sudo")]
    public class SudoModule : InteractiveBase
    {
        public DatabaseService DatabaseService { get; set; }

        [Command("sudo", RunMode = RunMode.Async)]
        [Summary("Ignores private scope of tags & allows bot administration")]
        [Example("sudo")]
        public async Task SudoToggleCommandAsync()
        {
            var currentUser = Context.User as IUser;
            if (DatabaseService.DatabaseTags.UserIsSudoer(Context))
            {
                if (DatabaseService.DatabaseTags._sudoersList.Contains(currentUser))
                {
                    DatabaseService.DatabaseTags._sudoersList.Remove(currentUser);
                    await ReplyAsync("Disabled your Sudo mode.");
                }
                else
                {
                    DatabaseService.DatabaseTags._sudoersList.Add(currentUser);
                    await ReplyAsync("Enabled your Sudo mode.");
                }
            }
            else
                await ReplyAsync("You are not in the sudoers list.");
        }
    }
}
