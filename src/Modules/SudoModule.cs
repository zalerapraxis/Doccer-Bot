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
            if (DatabaseSudo.IsUserSudoer(Context))
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

        [Command("sudoer", RunMode = RunMode.Async)]
        [Summary("Adds or removes a user from the sudo list")]
        [Example("sudoer {add/remove} {@user}")]
        public async Task SudoerAddRemoveCommandAsync(string function, string username)
        {
            ulong userid;
            var userWasProvided = MentionUtils.TryParseUser(username, out userid);

            // check if the function passed is correct
            if (function != "add" && function != "remove")
            {
                await ReplyAsync("You didn't correctly specify what you wanted to do. Try \"add\" or \"remove\".");
                return;
            }

            // check if the user passed exists & was parsed correctly
            if (!userWasProvided)
            {
                await ReplyAsync("You didn't specify a user to run the command on. Try @mentioning a user.");
                return;
            }

            var user = Context.Guild.GetUser(userid) as IUser;

            string replyFunctionText;
            if (function == "add")
                replyFunctionText = "added to";
            else
                replyFunctionText = "removed from";

            switch (function)
            {
                case "add":
                    await DatabaseSudo.AddUserToSudoers(user);
                    break;
                case "remove":
                    await DatabaseSudo.RemoveUserFromSudoers(user);
                    break;
            }

            await ReplyAsync($"{user.Username} was successfully {replyFunctionText} the sudoers list.");
        }
    }
}
