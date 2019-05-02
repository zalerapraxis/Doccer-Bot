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
        private string _directory = $"{Environment.CurrentDirectory}/memes";

        public TextMemeService()
        {
        }

        public async Task<string> GetMemeTextForNoEvents()
        {
            if (!System.IO.Directory.Exists(_directory))
            {
                System.IO.Directory.CreateDirectory(_directory);
            }

            // get list of files
            var fileList = System.IO.Directory.GetFiles(_directory);

            // randomly select a file by generating an index value
            Random rng = new Random();
            int index = rng.Next(0, fileList.Length);
            var file = fileList[index];

            // read the contents of the file
            var response = await System.IO.File.ReadAllTextAsync(file);

            return response;
        }
    }
}
