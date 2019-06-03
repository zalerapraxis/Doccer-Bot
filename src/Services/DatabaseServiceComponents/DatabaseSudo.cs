using System;
using System.Collections.Generic;
using System.Text;
using Discord;
using Discord.Commands;
using MongoDB.Driver;

namespace Doccer_Bot.Services.DatabaseServiceComponents
{
    public class DatabaseSudo
    {
        private readonly DatabaseService _databaseService;

        private MongoClient _mongodb;
        private string _mongodbName;

        // should we move this to its own service?
        public List<IUser> _sudoersList = new List<IUser>();

        public DatabaseSudo(DatabaseService databaseService)
        {
            _databaseService = databaseService;

            _mongodb = _databaseService._mongodb;
            _mongodbName = _databaseService._mongodbName;
        }

        public bool UserIsSudoer(SocketCommandContext context)
        {
            var database = _mongodb.GetDatabase(_mongodbName);
            var sudoersCollection = database.GetCollection<SudoUser>("sudoers");

            var builder = Builders<SudoUser>.Filter;
            FilterDefinition<SudoUser> filter = builder.Eq("user_id", context.User.Id.ToString());

            var userInSudoers = sudoersCollection.FindAsync(filter).Result.Any();

            return userInSudoers;
        }

    }
}
