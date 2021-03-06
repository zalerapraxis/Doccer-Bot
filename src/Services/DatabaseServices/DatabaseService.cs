﻿using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using Doccer_Bot.Services.DatabaseServiceComponents;
using Microsoft.Extensions.Configuration;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Doccer_Bot.Services
{
    public class DatabaseService
    {
        public MongoClient _mongodb;
        public string _mongodbName;
        
        public DatabaseService(IConfigurationRoot config)
        {
            // assumes our db user auths to the same db as the one we're connecting to
            var username = config["dbUsername"];
            var password = config["dbPassword"];
            var host = config["dbHost"];
            var dbName = config["dbName"];

            _mongodb = new MongoClient($"mongodb://{username}:{password}@{host}/?authSource={dbName}");
            _mongodbName = dbName;
        }

        // called via .meme command and via textmemeservice
        public async Task<List<TextMeme>> GetTextMemes()
        {
            var database = _mongodb.GetDatabase(_mongodbName);
            var textmemesCollection = database.GetCollection<TextMeme>("textmemes");

            var textMemes = await textmemesCollection.Find(new BsonDocument()).ToListAsync();

            // convert \n linebreaks in mongo to .net linebreaks
            foreach (var textMeme in textMemes)
                textMeme.Text = textMeme.Text.Replace("\\n", Environment.NewLine);

            return textMemes;
        }
    }
}
