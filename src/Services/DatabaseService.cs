using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using Microsoft.Extensions.Configuration;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Doccer_Bot.Services
{
    public class DatabaseService
    {
        private readonly IConfigurationRoot _config;
        private MongoClient _mongodb;
        private string _mongodbName;

        public DatabaseService(IConfigurationRoot config)
        {
            _config = config;
        }

        public async Task Initialize()
        {
            // assumes our db user auths to the same db as the one we're connecting to
            var username = _config["dbUsername"];
            var password = _config["dbPassword"];
            var host = _config["dbHost"];
            var dbName = _config["dbName"];

            _mongodb = new MongoClient($"mongodb://{username}:{password}@{host}/?authSource={dbName}");
            _mongodbName = dbName;
        }

        public async Task<List<Server>> GetServersInfo()
        {
            var database = _mongodb.GetDatabase(_mongodbName);
            var serverCollection = database.GetCollection<Server>("servers");

            var servers = await serverCollection.Find(new BsonDocument()).ToListAsync();

            return servers;
        }

        public async Task<bool> AddServerInfo(Server newServer)
        {
            var database = _mongodb.GetDatabase(_mongodbName);
            var serverCollection = database.GetCollection<Server>("servers");

            await serverCollection.InsertOneAsync(newServer);

            return true;
        }

        public async Task<bool> RemoveServerInfo(Server server)
        {
            var database = _mongodb.GetDatabase(_mongodbName);
            var serverCollection = database.GetCollection<Server>("servers");

            var filter = Builders<Server>.Filter.Eq("server_id", server.ServerId);

            await serverCollection.DeleteOneAsync(filter);

            return true;
        }

        public async Task<bool> EditServerInfo(string serverId, string key, string value)
        {
            var database = _mongodb.GetDatabase(_mongodbName);
            var serverCollection = database.GetCollection<Server>("servers");

            var filter = Builders<Server>.Filter.Eq("server_id", serverId);
            // do we need to check if server info exists?

            // stage change
            var update = Builders<Server>.Update.Set(key, value);

            // commit change
            await serverCollection.UpdateOneAsync(filter, update);

            return true;
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

        // called via .tag {name} command
        public async Task<string> GetTagContentsFromDatabase(SocketCommandContext context, string tagName)
        {
            var database = _mongodb.GetDatabase(_mongodbName);
            var tagCollection = database.GetCollection<Tag>("tags");

            // filter by name, search for passed tag name, get first tag matching filter
            //var filter = Builders<Tag>.Filter.Eq("name", tagName);
            var filter = BuildTagFilterEq(context, "name", tagName);

            var tagExists = tagCollection.FindAsync(filter).Result.Any();

            if (tagExists)
            {
                // get tag from db
                var tag = await tagCollection.FindAsync(filter).Result.FirstOrDefaultAsync();

                // increment its uses count
                tag.Uses += 1;

                // stage uses change to tag
                var update = Builders<Tag>.Update.Set("uses", tag.Uses);

                // commit uses change to tag
                await tagCollection.UpdateOneAsync(filter, update);

                // return tag to calling function
                return tag.Text;
            }

            return null;
        }

        // called via. tag all command - returns all tags in db
        public async Task<List<string>> GetAllTagsFromDatabase(SocketCommandContext context)
        {
            var database = _mongodb.GetDatabase(_mongodbName);
            var tagCollection = database.GetCollection<Tag>("tags");

            FilterDefinition<Tag> filter;

            filter = BuildTagFilterEmpty(context);
            
            // use whichever filter to collect results from database as list of tags
            var dbResponse = await tagCollection.FindAsync(filter).Result.ToListAsync();

            // grab all of the tags and add them to a results list containing only tag names
            var results = new List<string>();
            foreach (var tag in dbResponse)
            {
                StringBuilder stringBuilder = new StringBuilder();

                stringBuilder.Append($"**{tag.Name}**");
                if (tag.Description.Length > 0)
                    stringBuilder.Append($" - {tag.Description}");
                results.Add(stringBuilder.ToString());
            }

            return results;
        }

        // called via. tag all (search) command - search is an optional parameter used to search db for tags
        // return results of search, either all tags in db or tags matching searchText parameter
        public async Task<List<string>> SearchTagsInDatabase(SocketCommandContext context, string searchTerm)
        {
            var database = _mongodb.GetDatabase(_mongodbName);
            var tagCollection = database.GetCollection<Tag>("tags");

            FilterDefinition<Tag> filter;

            filter = BuildTagFilterRegex(context, "name", $"({searchTerm})");

            // use whichever filter to collect results from database as list of tags
            var dbResponse = await tagCollection.FindAsync(filter).Result.ToListAsync();

            // grab all of the tags and add them to a results list containing only tag names
            var results = new List<string>();
            foreach (var tag in dbResponse)
            {
                results.Add(tag.Name);
            }

            return results;
        }

        // called via .tag list (@mentioned user) command - user param is optional
        // calling without user returns list of calling user's tags
        // calling with user returns list of mentioned user's tags
        public async Task<List<string>> GetTagsByUserFromDatabase(SocketCommandContext context, IUser user = null)
        {
            var database = _mongodb.GetDatabase(_mongodbName);
            var tagCollection = database.GetCollection<Tag>("tags");

            FilterDefinition<Tag> filter;

            if (user != null)
                filter = BuildTagFilterEq(context, "author_id", (long) user.Id);
            else
                filter = BuildTagFilterEq(context, "author_id", (long) context.User.Id);

            // use whichever filter to collect results from database as list of tags
            var dbResponse = await tagCollection.FindAsync(filter).Result.ToListAsync();

            // grab all of the tags and add them to a results list containing only tag names
            var results = new List<string>();
            foreach (var tag in dbResponse)
            {
                results.Add(tag.Name);
            }

            return results;
        }

        // called via .tag info {name} command - returns tag object if tag found, or null otherwise
        public async Task<Tag> GetTagInfoFromDatabase(SocketCommandContext context, string tagName)
        {
            var database = _mongodb.GetDatabase(_mongodbName);
            var tagCollection = database.GetCollection<Tag>("tags");

            // filter by name, search for passed tag name, get first tag matching filter
            var filter = BuildTagFilterEq(context, "name", tagName);
            var tagExists = tagCollection.FindAsync(filter).Result.Any();

            if (tagExists)
            {
                var response = await tagCollection.FindAsync(filter).Result.FirstOrDefaultAsync();
                return response;
            }

            return null;
        }

        // called via .tag add {name} {contents} command - returns true if add successful, false otherwise
        public async Task<bool> AddTagToDatabase(SocketCommandContext context, string tagName, string content)
        {
            var newTag = new Tag()
            {
                Name = tagName,
                Text = content,
                Description = "",
                AuthorId = (long) context.User.Id,
                ServerId = (long)context.Guild.Id,
                Global = false,
                DateAdded = DateTime.Now,
                Uses = 0,
            };

            var database = _mongodb.GetDatabase(_mongodbName);
            var tagCollection = database.GetCollection<Tag>("tags");

            var filter = BuildTagFilterEq(context, "name", newTag.Name);
            var tagExists = tagCollection.FindAsync(filter).Result.Any();

            if (!tagExists)
            {
                await tagCollection.InsertOneAsync(newTag);
                return true;
            }

            return false;
        }

        // called via .tag remove {name} - returns values 0, 1, 2 corresponding to different success states
        // 0 = failed, tag does not exist
        // 1 = failed, calling user doesn't have permission to delete this tag
        // 2 = success, tag was deleted
        public async Task<int> RemoveTagFromDatabase(SocketCommandContext context, string tagName)
        {
            var database = _mongodb.GetDatabase(_mongodbName);
            var tagCollection = database.GetCollection<Tag>("tags");

            var filter = BuildTagFilterEq(context, "name", tagName);
            var tagExists = tagCollection.FindAsync(filter).Result.Any();

            if (tagExists)
            {
                // get tag's author
                var tag = await tagCollection.FindAsync(filter).Result.FirstOrDefaultAsync();
                
                // check if calling user has permission to modify the tag
                if (CheckTagUserPermission(context, tag))
                {
                    await tagCollection.DeleteOneAsync(filter);
                    return 2; // success, tag was deleted 
                }

                return 1; // 1 = failed, calling user doesn't have permission to delete this tag
            }

            return 0; // 0 = failed, tag does not exist
        }

        // called via .tag edit {name} {content}
        public async Task<int> EditTagInDatabase(SocketCommandContext context, string tagName, string key, dynamic value)
        {
            var database = _mongodb.GetDatabase(_mongodbName);
            var tagCollection = database.GetCollection<Tag>("tags");

            var filter = BuildTagFilterEq(context, "name", tagName);
            var tagExists = tagCollection.FindAsync(filter).Result.Any();

            if (tagExists)
            {
                // get tag
                var tag = await tagCollection.FindAsync(filter).Result.FirstOrDefaultAsync();

                // check if calling user has permission to modify the tag
                if (CheckTagUserPermission(context, tag))
                {
                    // stage change to tag
                    var update = Builders<Tag>.Update.Set(key, value);

                    // commit change to tag
                    tagCollection.UpdateOne(filter, update);

                    return 2; // success, tag was modified 
                }

                return 1; // 1 = failed, calling user doesn't have permission to modify this tag
            }

            return 0; // 0 = failed, tag does not exist
        }

        // returns a built filter that matches any tags that are accessible by the current server
        // this function is for eq(key, value) filters
        private FilterDefinition<Tag> BuildTagFilterEq(SocketCommandContext context, string key, dynamic value)
        {
            // filter where following conditions are satisfied:
            // both = true: 
            //     either are true:
            //         global is true
            //         server_id is the calling server id
            //     key:value pair matches document in database

            var builder = Builders<Tag>.Filter;

            var filter = builder.And(
                builder.Or(
                    builder.Eq("global", true),
                    builder.Eq("server_id", (long) context.Guild.Id)
                ),
                builder.Eq(key, value)
            );

            return filter;
        }

        // returns a built filter that matches any tags that are accessible by the current server
        // this function is for regex (key, value) filters
        private FilterDefinition<Tag> BuildTagFilterRegex(SocketCommandContext context, string key, dynamic value)
        {
            // filter where following conditions are satisfied:
            // both = true: 
            //     either are true:
            //         global is true
            //         server_id is the calling server id
            //     key:value pair matches document in database

            var builder = Builders<Tag>.Filter;

            var filter = builder.And(
                builder.Or(
                    builder.Eq("global", true),
                    builder.Eq("server_id", (long)context.Guild.Id)
                ),
                builder.Regex(key, value)
            );

            return filter;
        }

        // returns a built filter that matches any tags that are accessible by the current server
        // this function is for empty filters (return everything)
        private FilterDefinition<Tag> BuildTagFilterEmpty(SocketCommandContext context)
        {
            // filter where following conditions are satisfied:
            // both = true: 
            //     either are true:
            //         global is true
            //         server_id is the calling server id
            //     empty (get all documents)

            var builder = Builders<Tag>.Filter;

            var filter = builder.And(
                builder.Or(
                    builder.Eq("global", true),
                    builder.Eq("server_id", (long)context.Guild.Id)
                ),
                builder.Empty
            );

            return filter;
        }

        // check if the calling user is either the author of the passed tag or if the calling user is an administrator
        private bool CheckTagUserPermission(SocketCommandContext context, Tag tag)
        {
            var author = (ulong)tag.AuthorId;
            // get calling user in context of calling guild
            var contextUser = context.User as IGuildUser;

            if (context.User.Id == author || contextUser.GuildPermissions.Administrator)
            {
                return true;
            }
            return false;
        }
    }
}
