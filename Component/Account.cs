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
			this.TotalSpent = 0.0d;
			this.TotalSold = 0.0d;
			this.Deposit = 0.0d;
			this.Debt = 0.0d;
			this.TotalPoints = 0;
			this.RestPoints = 0;
			this.TotalRewards = 0;
			this.TotalContributions = 0;
			this.LastSync = DateTime.Now;
			this.Favorites = new List<string>();
			this.Bookmarks = new List<Bookmark>();
			this.Counters = new List<CounterInfo>();
		}

		#region Bookmark
		[Serializable]
		public class Bookmark
		{
			public string ID { get; set; }

			public string Name { get; set; }

			public int Chapter { get; set; }

			public int Position { get; set; }

			public DateTime Time { get; set; }

			public string Device { get; set; }

			public Bookmark()
			{
				this.ID = "";
				this.Name = "";
				this.Chapter = 0;
				this.Position = 0;
				this.Time = DateTime.Now;
				this.Device = "";
			}
		}
		#endregion

		#region Properties
		[JsonConverter(typeof(StringEnumConverter)), BsonRepresentation(BsonType.String), Sortable]
		public Level Level { get; set; }

		[JsonConverter(typeof(StringEnumConverter)), BsonRepresentation(BsonType.String), Sortable]
		public Reputation Reputation { get; set; }

		[Sortable(IndexName = "Statistics")]
		public double TotalSpent { get; set; }

		[Sortable(IndexName = "Statistics")]
		public double TotalSold { get; set; }

		[Sortable(IndexName = "Statistics")]
		public double Deposit { get; set; }

		[Sortable(IndexName = "Statistics")]
		public double Debt { get; set; }

		[Sortable(IndexName = "Statistics")]
		public int TotalPoints { get; set; }

		[Sortable(IndexName = "Statistics")]
		public int RestPoints { get; set; }

		[Sortable(IndexName = "Statistics")]
		public int TotalRewards { get; set; }

		[Sortable(IndexName = "Statistics")]
		public int TotalContributions { get; set; }

		public DateTime LastSync { get; set; }

		[AsJson]
		public List<string> Favorites { get; set; }

		[AsJson]
		public List<Bookmark> Bookmarks { get; set; }

		[AsJson]
		public List<CounterInfo> Counters { get; set; }
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