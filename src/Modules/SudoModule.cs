using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Discord;
using Discord.Addons.Interactive;
using Discord.Commands;
using Doccer_Bot.Modules.Common;
using Doccer_Bot.Services;
using Doccer_Bot.Services.DatabaseServiceComponents;

namespace Doccer_Bot.Modules
{
    [Name("Sudo")]
    public class SudoModule : InteractiveBase
    {
        public DatabaseSudo DatabaseSudo { get; set; }

        [Command("sudo", RunMode = RunMode.Async)]
        [Summary("Ignores private scope of tags & allows bot administration")]
        [Example("sudo")]
        public async Task SudoToggleCommandAsync()
        {
            var currentUser = Context.User as IUser;
            if (DatabaseSudo.UserIsSudoer(Context))
            {
                if (DatabaseSudo._sudoersList.Contains(currentUser))
                {
                    DatabaseSudo._sudoersList.Remove(currentUser);
                    await ReplyAsync("Disabled your Sudo mode.");
                }
                else
                {
                    DatabaseSudo._sudoersList.Add(currentUser);
                    await ReplyAsync("Enabled your Sudo mode.");
                }
            }
            else
                await ReplyAsync("You are not in the sudoers list.");
        }
    }
}
