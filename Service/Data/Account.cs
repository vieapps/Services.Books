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
	public enum Level
	{
		Normal,
		Silver,
		Gold,
		Platinum,
		Diamond
	}

	public enum Reputation
	{
		Unknown,
		Low,
		Normal,
		Hight
	}

	[Serializable, BsonIgnoreExtraElements, DebuggerDisplay("ID = {ID}")]
	[Entity(CollectionName = "Accounts", TableName = "T_Books_Accounts", CacheStorageType = typeof(Utility), CacheStorageName = "Cache", Searchable = true)]
	public class Account : Repository<Account>
	{
		public Account() : base()
		{
			this.ID = "";
			this.Level = Level.Normal;
			this.Reputation = Reputation.Unknown;
			this.TotalPoints = 0;
			this.RestPoints = 0;
			this.TotalRewards = 0;
			this.TotalContributions = 0;
			this.LastSync = DateTime.Now;
			this.Favorites = new List<string>();
			this.Bookmarks = new List<Bookmark>();
			this.RatingPoints = new List<RatingPoint>();
		}

		#region Bookmark
		[Serializable]
		public class Bookmark
		{
			public string ID { get; set; }

			public int Chapter { get; set; }

			public int Position { get; set; }

			public DateTime Time { get; set; }

			public Bookmark()
			{
				this.ID = "";
				this.Chapter = 0;
				this.Position = 0;
				this.Time = DateTime.Now;
			}
		}

		public class BookmarkComparer : IEqualityComparer<Bookmark>
		{
			public bool Equals(Bookmark x, Bookmark y)
			{
				return object.ReferenceEquals(x, y)
					? true
					: object.ReferenceEquals(x, null) || Object.ReferenceEquals(y, null)
						? false
						: x.ID.IsEquals(y.ID);
			}

			public int GetHashCode(Bookmark bookmark)
			{
				return object.ReferenceEquals(bookmark, null)
					? 0
					: bookmark.ID.GetHashCode();
			}
		}
		#endregion

		#region Properties
		[JsonConverter(typeof(StringEnumConverter)), BsonRepresentation(BsonType.String), Sortable(IndexName = "Statistics")]
		public Level Level { get; set; }

		[JsonConverter(typeof(StringEnumConverter)), BsonRepresentation(BsonType.String), Sortable(IndexName = "Statistics")]
		public Reputation Reputation { get; set; }

		[Sortable(IndexName = "Statistics")]
		public int TotalPoints { get; set; }

		[Sortable(IndexName = "Statistics")]
		public int RestPoints { get; set; }

		[Sortable(IndexName = "Statistics")]
		public int TotalRewards { get; set; }

		[Sortable(IndexName = "Statistics")]
		public int TotalContributions { get; set; }

		[Sortable(IndexName = "Statistics")]
		public DateTime LastSync { get; set; }

		[JsonIgnore, AsJson]
		public List<string> Favorites { get; set; }

		[JsonIgnore, AsJson]
		public List<Bookmark> Bookmarks { get; set; }

		[AsJson]
		public List<RatingPoint> RatingPoints { get; set; }
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