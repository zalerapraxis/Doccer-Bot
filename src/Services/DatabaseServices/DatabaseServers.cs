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
    }
}
