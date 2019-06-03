using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
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

        // called via .tag add {name} {contents} command - returns true if add successful, false otherwise
        public async Task AddUserToSudoers(IUser user)
        {
            var newUser = new SudoUser()
            {
                Username = user.Username,
                userId = user.Id.ToString()
            };

            var database = _mongodb.GetDatabase(_mongodbName);
            var sudoCollection = database.GetCollection<SudoUser>("sudoers");

            await sudoCollection.InsertOneAsync(newUser);
        }

        public async Task RemoveUserFromSudoers(IUser user)
        {
            var database = _mongodb.GetDatabase(_mongodbName);
            var sudoCollection = database.GetCollection<SudoUser>("sudoers");

            var filter = Builders<SudoUser>.Filter.Eq("user_id", user.Id);

            await sudoCollection.DeleteOneAsync(filter);
        }

        public bool IsUserSudoer(SocketCommandContext context)
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
