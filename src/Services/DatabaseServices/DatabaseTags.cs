using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;

namespace Doccer_Bot.Services.DatabaseServiceComponents
{
    public class DatabaseTags
    {
        private readonly DatabaseService _databaseService;
        private readonly DatabaseSudo _databaseSudo;

        private MongoClient _mongodb;
        private string _mongodbName;

        public DatabaseTags(DatabaseService databaseService, DatabaseSudo databaseSudo)
        {
            _databaseService = databaseService;
            _databaseSudo = databaseSudo;

            _mongodb = _databaseService._mongodb;
            _mongodbName = _databaseService._mongodbName;

        }

        // called via .tag {name} command
        public async Task<string> GetTagContentsFromDatabase(SocketCommandContext context, string tagName)
        {
            var database = _mongodb.GetDatabase(_mongodbName);
            var tagCollection = database.GetCollection<Tag>("tags");

            // filter by name, search for passed tag name, get first tag matching filter
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
                filter = BuildTagFilterEq(context, "author_id", (long)user.Id);
            else
                filter = BuildTagFilterEq(context, "author_id", (long)context.User.Id);

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
                AuthorId = (long)context.User.Id,
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
            FilterDefinition<Tag> filter;
            var builder = Builders<Tag>.Filter;

            // if sudo mode enabled & calling user is in sudoers list, return all matching tags
            // else, return only global tags and tags made in this server
            if (_databaseSudo._sudoersList.Contains(context.User) && _databaseSudo.IsUserSudoer(context))
            {
                filter = builder.Eq(key, value);
            }
            else
            {
                // filter where following conditions are satisfied:
                // both = true: 
                //     either are true:
                //         global is true
                //         server_id is the calling server id
                //     key:value pair matches document in database

                filter = builder.And(
                    builder.Or(
                        builder.Eq("global", true),
                        builder.Eq("server_id", (long)context.Guild.Id)
                    ),
                    builder.Eq(key, value)
                );
            }

            return filter;
        }

        // returns a built filter that matches any tags that are accessible by the current server
        // this function is for regex (key, value) filters
        private FilterDefinition<Tag> BuildTagFilterRegex(SocketCommandContext context, string key, dynamic value)
        {
            FilterDefinition<Tag> filter;
            var builder = Builders<Tag>.Filter;

            // if sudo mode enabled & calling user is in sudoers list, return all matching tags
            // else, return only global tags and tags made in this server
            if (_databaseSudo._sudoersList.Contains(context.User) && _databaseSudo.IsUserSudoer(context))
            {
                filter = builder.Regex(key, value);
            }
            else
            {
                // filter where following conditions are satisfied:
                // both = true: 
                //     either are true:
                //         global is true
                //         server_id is the calling server id
                //     key:value pair matches document in database

                filter = builder.And(
                    builder.Or(
                        builder.Eq("global", true),
                        builder.Eq("server_id", (long)context.Guild.Id)
                    ),
                    builder.Regex(key, value)
                );
            }

            return filter;
        }

        // returns a built filter that matches any tags that are accessible by the current server
        // this function is for empty filters (return everything)
        private FilterDefinition<Tag> BuildTagFilterEmpty(SocketCommandContext context)
        {
            FilterDefinition<Tag> filter;
            var builder = Builders<Tag>.Filter;

            // if sudo mode enabled & calling user is in sudoers list, return all matching tags
            // else, return only global tags and tags made in this server
            if (_databaseSudo._sudoersList.Contains(context.User) && _databaseSudo.IsUserSudoer(context))
            {
                filter = builder.Empty;
            }
            else
            {
                // filter where following conditions are satisfied:
                // both = true: 
                //     either are true:
                //         global is true
                //         server_id is the calling server id
                //     empty (get all documents)

                filter = builder.And(
                    builder.Or(
                        builder.Eq("global", true),
                        builder.Eq("server_id", (long)context.Guild.Id)
                    ),
                    builder.Empty
                );
            }

            return filter;
        }

        // check if the calling user is either the author of the passed tag or if the calling user is an administrator
        private bool CheckTagUserPermission(SocketCommandContext context, Tag tag)
        {
            var author = (ulong)tag.AuthorId;
            // get calling user in context of calling guild
            var contextUser = context.User as IGuildUser;

            // if the calling user is the author or a guild admin
            if (context.User.Id == author || contextUser.GuildPermissions.Administrator)
            {
                return true;
            }

            // if the calling user is in sudo mode
            if (_databaseSudo._sudoersList.Contains(context.User) && _databaseSudo.IsUserSudoer(context))
            {
                return true;
            }
            return false;
        }
    }
}
