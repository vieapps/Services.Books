#region Related components
using System;
using System.Diagnostics;
using System.Collections.Generic;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using MongoDB.Bson.Serialization.Attributes;

using net.vieapps.Components.Utility;
using net.vieapps.Components.Repository;
#endregion

namespace net.vieapps.Services.Books
{
	[Serializable, BsonIgnoreExtraElements, DebuggerDisplay("ID = {ID}, Name = {Name}")]
	[Entity(CollectionName = "Books", TableName = "T_Books_Books", CacheStorageType = typeof(Utility), CacheStorageName = "Cache", Searchable = true)]
	public class Book : Repository<Book>
	{
		public Book() : base()
		{
			this.ID = "";
			this.Title = "";
			this.Author = "";
			this.Translator = "";
			this.Category = "";
			this.Original = "";
			this.Publisher = "";
			this.Producer = "";
			this.Language = "vi";
			this.Status = "";
			this.Cover = "";
			this.Tags = "";
			this.Source = "";
			this.SourceUrl = "";
			this.Contributor = "";
			this.TotalChapters = 0;
			this.LastUpdated = DateTime.Now;
			this.Counters = new List<CounterInfo>()
			{
				new CounterInfo() { Type = Components.Security.Action.View },
				new CounterInfo() { Type = Components.Security.Action.Download }
			};
			this.RatingPoints = new List<RatingPoint>();
			this.TOCs = new List<string>();
			this.Chapters = new List<string>();
			this.ChapterUrls = new List<string>();
			this.MediaFiles = new HashSet<string>();
		}

		#region Properties
		[Property(MaxLength = 250), Sortable, Searchable]
		public override string Title { get; set; }

		[Property(MaxLength = 250), Sortable(IndexName = "Info"), Searchable]
		public string Author { get; set; }

		[Property(MaxLength = 250)]
		public string Translator { get; set; }

		[Property(MaxLength = 250), Sortable(IndexName = "Info")]
		public string Category { get; set; }

		[Property(MaxLength = 250), Searchable]
		public string Original { get; set; }

		[Property(MaxLength = 250)]
		public string Publisher { get; set; }

		[Property(MaxLength = 250)]
		public string Producer { get; set; }

		[Property(MaxLength = 2)]
		public string Language { get; set; }

		[Property(MaxLength = 50), Sortable]
		public string Status { get; set; }

		[Property(MaxLength = 250)]
		public string Cover { get; set; }

		[Property(MaxLength = 250)]
		public string Tags { get; set; }

		[Sortable]
		public DateTime LastUpdated { get; set; }

		[AsJson]
		public List<CounterInfo> Counters { get; set; }

		[AsJson]
		public List<RatingPoint> RatingPoints { get; set; }

		[Property(MaxLength = 250)]
		public string Source { get; set; }

		[Property(MaxLength = 1000)]
		public string SourceUrl { get; set; }

		[Property(MaxLength = 250)]
		public string Contributor { get; set; }

		[Sortable]
		public int TotalChapters { get; set; }

		string _PermanentID = "";

		[JsonIgnore, BsonIgnore, Ignore]
		public string PermanentID
		{
			set { this._PermanentID = value; }
			get
			{
				if (string.IsNullOrWhiteSpace(this._PermanentID))
				{
					if (string.IsNullOrWhiteSpace(this.ID) || string.IsNullOrWhiteSpace(this.Name))
						this._PermanentID = UtilityService.GetUUID();
					else
					{
						this._PermanentID = Utility.GetDataFromJsonFile(Utility.FolderOfDataFiles + @"\" + this.Name.GetFirstChar() + @"\" + this.Name + ".json", "PermanentID");
						Utility.Cache.Set(this);
					}
				}
				return this._PermanentID;
			}
		}

		[JsonIgnore, BsonIgnore, Ignore]
		public string Stylesheet { get; set; }

		[JsonIgnore, BsonIgnore, Ignore]
		public List<string> TOCs { get; set; }

		[JsonIgnore, BsonIgnore, Ignore]
		public List<string> Chapters { get; set; }

		[JsonIgnore, BsonIgnore, Ignore]
		public List<string> ChapterUrls { get; set; }

		[JsonIgnore, BsonIgnore, Ignore]
		public HashSet<string> MediaFiles { get; set; }

		[JsonIgnore, BsonIgnore, Ignore]
		public string Name
		{
			get
			{
				return string.IsNullOrWhiteSpace(this.Title)
					? ""
					: this.Title.Trim() + (string.IsNullOrWhiteSpace(this.Author) ? "" : " - " + this.Author.Trim());
			}
		}

		[JsonIgnore, BsonIgnore, Ignore]
		public override string SystemID { get; set; }

		[JsonIgnore, BsonIgnore, Ignore]
		public override string RepositoryID { get; set; }

		[JsonIgnore, BsonIgnore, Ignore]
		public override string EntityID { get; set; }
		#endregion

		#region To JSON
		public override JObject ToJson(bool addTypeOfExtendedProperties)
		{
			return this.ToJson(addTypeOfExtendedProperties, true);
		}

		public JObject ToJson(bool addTypeOfExtendedProperties, bool asNormalized)
		{
			var json = base.ToJson(addTypeOfExtendedProperties);
			if (asNormalized)
			{
				json["Cover"] = string.IsNullOrWhiteSpace(this.Cover)
					? Utility.MediaUri.NormalizeMediaFileUris(null)
					: this.Cover.NormalizeMediaFileUris(this);

				json["RatingPoints"] = RatingPoint.ToJObject(this.RatingPoints);
				json["Counters"] = CounterInfo.ToJObject(this.Counters);

				json.Add(new JProperty("Chapters", new JArray()));
				json.Add(new JProperty("TOCs", new JArray()));

				json.Add(new JProperty("Downloads", new JObject()
				{
					{ "Epub", this.GetDownloadUri() + ".epub" },
					{ "Mobi", this.GetDownloadUri() + ".mobi" }
				}));
			}

			return json;
		}
		#endregion

	}
}