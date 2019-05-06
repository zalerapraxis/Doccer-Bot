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

        private string _directory = $"{Environment.CurrentDirectory}/Memes";
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
            // logic says that putting everything into one file to cut down on reads would
            // be best, but some of our files have multiple lines and I don't feel like trying
            // to add delimiters to stuff and parsing them and blah blah blah it's on an SSD anyway
            var fileList = System.IO.Directory.GetFiles(_directory);
            foreach (var file in fileList)
            {
                var fileContents = await System.IO.File.ReadAllTextAsync(file);
                _memes.Add(fileContents);
            }

            await _logger.Log(new LogMessage(LogSeverity.Info, GetType().Name, $"Loaded {_memes.Count} files."));
        }

        public string GetMemeTextForNoEvents()
        {
            if (_memes.Count == 0) // didn't find any text files in the directory, so return a filler string
                return "\"Just buy more raid days 4head\"";
            // randomly select a file by generating an index value
            Random rng = new Random();
            int index = rng.Next(0, _memes.Count);
            var meme  = _memes[index];

            return meme;
        }
    }
}
