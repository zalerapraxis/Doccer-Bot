using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Discord;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Doccer_Bot.Services.DatabaseServiceComponents
{
    public class DatabaseServers
    {
        private readonly DatabaseService _databaseService;

        private MongoClient _mongodb;
        private string _mongodbName;

        public DatabaseServers(DatabaseService databaseService)
        {
            _databaseService = databaseService;

            _mongodb = _databaseService._mongodb;
            _mongodbName = _databaseService._mongodbName;
        }

        public async Task<List<DiscordServer>> GetServersInfo()
        {
            var database = _mongodb.GetDatabase(_mongodbName);
            var serverCollection = database.GetCollection<DiscordServer>("servers");

            var servers = await serverCollection.Find(new BsonDocument()).ToListAsync();

            return servers;
        }

        public async Task<bool> AddServerInfo(DiscordServer newServer)
        {
            var database = _mongodb.GetDatabase(_mongodbName);
            var serverCollection = database.GetCollection<DiscordServer>("servers");

            await serverCollection.InsertOneAsync(newServer);

            return true;
        }

        public async Task<bool> RemoveServerInfo(DiscordServer server)
        {
            var database = _mongodb.GetDatabase(_mongodbName);
            var serverCollection = database.GetCollection<DiscordServer>("servers");

            var filter = Builders<DiscordServer>.Filter.Eq("server_id", server.ServerId);

            await serverCollection.DeleteOneAsync(filter);

            return true;
        }

        public async Task<bool> EditServerInfo(string serverId, string key, dynamic value)
        {
            var database = _mongodb.GetDatabase(_mongodbName);
            var serverCollection = database.GetCollection<DiscordServer>("servers");

            var filter = Builders<DiscordServer>.Filter.Eq("server_id", serverId);
            // do we need to check if server info exists?

            // stage change
            var update = Builders<DiscordServer>.Update.Set(key, value);

            // commit change
            await serverCollection.UpdateOneAsync(filter, update);

            return true;
        }
    }
}
