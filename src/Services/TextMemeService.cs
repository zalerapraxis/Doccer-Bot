using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Discord;
using Example;

namespace Doccer_Bot.Services
{
    public class TextMemeService
    {
        private readonly LoggingService _logger;

        private string _directory = $"{Environment.CurrentDirectory}/memes";
        private List<string> _memes = new List<string>();

        public TextMemeService(LoggingService logger)
        {
            _logger = logger;
        }

        public async Task Initialize()
        {
            // create directory if not present
            if (!System.IO.Directory.Exists(_directory))
            {
                System.IO.Directory.CreateDirectory(_directory);
            }

            // get list of files
            var fileList = System.IO.Directory.GetFiles(_directory);
            foreach (var file in fileList)
            {
                var fileContents = await System.IO.File.ReadAllTextAsync(file);
                _memes.Add(fileContents);
            }

            await _logger.Log(new LogMessage(LogSeverity.Info, GetType().Name, $"Loaded {_memes.Count} files."));
        }

        public async Task<string> GetMemeTextForNoEvents()
        {
            // randomly select a file by generating an index value
            Random rng = new Random();
            int index = rng.Next(0, _memes.Count);
            var meme  = _memes[index];

            return meme;
        }
    }
}
