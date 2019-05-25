using Discord;
using Discord.Commands;
using Microsoft.Extensions.Configuration;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Doccer_Bot.Modules.Common;

namespace Example.Modules
{
    [Name("Help")]
    public class HelpModule : ModuleBase<SocketCommandContext>
    {
        private readonly CommandService _service;
        private readonly IConfigurationRoot _config;

        public HelpModule(CommandService service, IConfigurationRoot config)
        {
            _service = service;
            _config = config;
        }

        [Command("helpmod")]
        [Summary("Displays help for a specific module - help {modulename}")]
        public async Task HelpModuleAsync(string requestedModule)
        {
            string prefix = _config["prefix"];
            var builder = new EmbedBuilder()
            {
                Color = Color.Blue,
                //Description = "These are the commands you can use."
            };

            var module = _service.Modules.FirstOrDefault(x => x.Name.ToLower() == requestedModule.ToLower());

            foreach (var cmd in module.Commands)
            {
                var result = await cmd.CheckPreconditionsAsync(Context);

                var example = cmd.Attributes.OfType<ExampleAttribute>().FirstOrDefault();

                StringBuilder descriptionBuilder = new StringBuilder();

                descriptionBuilder.Append(cmd.Summary);
                if (example != null && example.ExampleText != "")
                    descriptionBuilder.Append($" - Example: *{example.ExampleText}*");

                if (result.IsSuccess)
                {
                    builder.AddField(x =>
                    {
                        x.Name = $"{prefix}{cmd.Aliases.First()}";
                        x.Value = $"{descriptionBuilder}";
                        x.IsInline = false;
                    });
                }
                    
            }

            await ReplyAsync("", false, builder.Build());
        }

        [Command("help")]
        [Summary("Displays a full list of available commands.")]
        public async Task HelpAsync()
        {
            string prefix = _config["prefix"];
            var builder = new EmbedBuilder()
            {
                Color = Color.Blue,
                //Description = "These are the commands you can use."
            };
            
            foreach (var module in _service.Modules)
            {
                string description = null;
                foreach (var cmd in module.Commands)
                {
                    var result = await cmd.CheckPreconditionsAsync(Context);
                    if (result.IsSuccess)
                        description += $"{prefix}{cmd.Aliases.First()} - {cmd.Summary}\n";
                }
                
                if (!string.IsNullOrWhiteSpace(description))
                {
                    builder.AddField(x =>
                    {
                        x.Name = module.Name;
                        x.Value = description;
                        x.IsInline = false;
                    });
                }
            }

            await ReplyAsync("", false, builder.Build());
        }
    }
}
