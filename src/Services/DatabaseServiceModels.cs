using System;
using System.Collections.Generic;
using System.Text;
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
}
