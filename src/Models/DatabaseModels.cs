using System;
using System.Collections.Generic;
using System.Text;
using Discord;
using Doccer_Bot.Models;
using Google.Apis.Auth.OAuth2;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Doccer_Bot.Services
{
    public class TextMeme
    {
        [BsonId]
        public ObjectId Id { get; set; }

        [BsonElement("name")]
        public string Name { get; set; }

        [BsonElement("text")]
        public string Text { get; set; }
    }

    public class Tag
    {
        [BsonId]
        public ObjectId Id { get; set; }

        [BsonElement("name")]
        public string Name { get; set; }

        [BsonElement("text")]
        public string Text { get; set; }

        [BsonElement("description")]
        public string Description { get; set; }

        [BsonElement("author_id")]
        public long AuthorId { get; set; }

        [BsonElement("server_id")]
        public long ServerId { get; set; }

        [BsonElement("global")]
        public bool Global { get; set; }

        [BsonElement("date_added")]
        public DateTime DateAdded { get; set; }

        [BsonElement("uses")]
        public int Uses { get; set; }
    }

    public class Server
    {
        [BsonId]
        public ObjectId Id { get; set; }

        [BsonElement("server_name")]
        public string ServerName { get; set; }

        [BsonElement("server_id")]
        public string ServerId { get; set;}

        [BsonElement("configchannel_id")]
        public string ConfigChannelId { get; set; }

        [BsonElement("reminderchannel_id")]
        public string ReminderChannelId { get; set; }

        [BsonElement("calendar_id")]
        public string CalendarId { get; set; }

        [BsonIgnore]
        public List<CalendarEvent> Events = new List<CalendarEvent>();

        [BsonIgnore]
        public IGuild DiscordServer { get; set; }

        [BsonIgnore]
        public ITextChannel ConfigChannel { get; set; }

        [BsonIgnore]
        public ITextChannel ReminderChannel { get; set; }

        [BsonIgnore]
        public IUserMessage EventEmbedMessage { get; set; }

        [BsonIgnore]
        public UserCredential GoogleUserCredential { get; set; }
    }

    // internal list of servers, used for global access - not actually stored in the database
    public class Servers
    {
        public static List<Server> ServerList = new List<Server>();
    }
}
