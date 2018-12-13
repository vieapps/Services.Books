#region Related components
using System;
using System.Diagnostics;
using System.Collections.Generic;

using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

using net.vieapps.Components.Security;
using net.vieapps.Components.Repository;
using net.vieapps.Components.Utility;
#endregion

namespace net.vieapps.Services.Books
{
	[Serializable]
	public enum Level
	{
		Normal,
		Silver,
		Gold,
		Platinum,
		Diamond
	}

	[Serializable]
	public enum Reputation
	{
		Unknown,
		Low,
		Normal,
		Hight
	}

	[Serializable, BsonIgnoreExtraElements, DebuggerDisplay("ID = {ID}")]
	[Entity(CollectionName = "Accounts", TableName = "T_Books_Accounts", CacheClass = typeof(Utility), CacheName = "Cache", Searchable = true)]
	public class Account : Repository<Account>
	{
		public Account() : base() { }

		#region Bookmark
		[Serializable]
		public class Bookmark
		{
			public Bookmark() { }

			public string ID { get; set; } = "";

			public int Chapter { get; set; } = 0;

			public int Position { get; set; } = 0;

			public DateTime Time { get; set; } = DateTime.Now;
		}

		public class BookmarkComparer : IEqualityComparer<Bookmark>
		{
			public bool Equals(Bookmark x, Bookmark y) => object.ReferenceEquals(x, y) ? true : x == null || y == null ? false : x.ID.IsEquals(y.ID);

			public int GetHashCode(Bookmark bookmark) => bookmark == null ? 0 : bookmark.ID.GetHashCode();
		}
		#endregion

		#region Properties
		[JsonConverter(typeof(StringEnumConverter)), BsonRepresentation(BsonType.String), Sortable(IndexName = "Statistics")]
		public Level Level { get; set; } = Level.Normal;

		[JsonConverter(typeof(StringEnumConverter)), BsonRepresentation(BsonType.String), Sortable(IndexName = "Statistics")]
		public Reputation Reputation { get; set; } = Reputation.Unknown;

		[Sortable(IndexName = "Statistics")]
		public int TotalPoints { get; set; } = 0;

		[Sortable(IndexName = "Statistics")]
		public int RestPoints { get; set; } = 0;

		[Sortable(IndexName = "Statistics")]
		public int TotalRewards { get; set; } = 0;

		[Sortable(IndexName = "Statistics")]
		public int TotalContributions { get; set; } = 0;

		[Sortable(IndexName = "Statistics")]
		public DateTime LastSync { get; set; } = DateTime.Now;

		[JsonIgnore, AsJson]
		public List<string> Favorites { get; set; } = new List<string>();

		[JsonIgnore, AsJson]
		public List<Bookmark> Bookmarks { get; set; } = new List<Bookmark>();

		[AsJson]
		public List<RatingPoint> RatingPoints { get; set; } = new List<RatingPoint>();
		#endregion

		#region IBusinessEntity Properties
		[JsonIgnore, BsonIgnore, Ignore]
		public override string Title { get; set; }

		[JsonIgnore, BsonIgnore, Ignore]
		public override string SystemID { get; set; }

		[JsonIgnore, BsonIgnore, Ignore]
		public override string RepositoryID { get; set; }

		[JsonIgnore, BsonIgnore, Ignore]
		public override string EntityID { get; set; }

		[JsonIgnore, BsonIgnore, Ignore]
		public override Privileges OriginalPrivileges { get; set; }
		#endregion

	}
}