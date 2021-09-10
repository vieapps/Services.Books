#region Related components
using System;
using System.IO;
using System.Linq;
using System.Diagnostics;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MsgPack.Serialization;
using MongoDB.Bson.Serialization.Attributes;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using net.vieapps.Components.Utility;
using net.vieapps.Components.Repository;
#endregion

namespace net.vieapps.Services.Books
{
	[BsonIgnoreExtraElements, DebuggerDisplay("ID = {ID}, Title = {Title}")]
	[Entity(CollectionName = "Books", TableName = "T_Books_Books", CacheClass = typeof(Utility), CacheName = "Cache", Searchable = true)]
	public class Book : Repository<Book>
	{
		public Book() : base() { }

		[Property(MaxLength = 250, NotEmpty = true), Sortable, Searchable]
		[FormControl(Label = "{{books.info.controls.[name]}}")]
		public override string Title { get; set; } = "";

		[Property(MaxLength = 250), Searchable]
		[FormControl(Label = "{{books.info.controls.[name]}}")]
		public string Original { get; set; } = "";

		[Property(MaxLength = 250), Sortable(IndexName = "Info"), Searchable]
		[FormControl(Label = "{{books.info.controls.[name]}}")]
		public string Author { get; set; } = "";

		[Property(MaxLength = 250), Searchable]
		[FormControl(Label = "{{books.info.controls.[name]}}")]
		public string Translator { get; set; } = "";

		[Property(MaxLength = 250)]
		[FormControl(Label = "{{books.info.controls.[name]}}")]
		public string Publisher { get; set; } = "";

		[Property(MaxLength = 250)]
		[FormControl(Label = "{{books.info.controls.[name]}}")]
		public string Producer { get; set; } = "";

		[Property(MaxLength = 250, NotEmpty = true), Sortable(IndexName = "Info")]
		[FormControl(ControlType = "Select", DataType = "dropdown", Label = "{{books.info.controls.[name]}}", SelectValuesRemoteURI = "discovery/definitions?x-service-name=books&x-object-name=categories")]
		public string Category { get; set; } = "";

		[Property(MaxLength = 2)]
		[FormControl(Label = "{{books.info.controls.[name]}}")]
		public string Language { get; set; } = "vi";

		[Property(MaxLength = 50), Sortable]
		[FormControl(Excluded = true)]
		public string Status { get; set; } = "";

		[Sortable]
		[FormControl(Excluded = true)]
		public DateTime LastUpdated { get; set; } = DateTime.Now;

		[AsJson]
		[FormControl(Excluded = true)]
		public List<CounterInfo> Counters { get; set; } = new List<CounterInfo>
		{
			new CounterInfo { Type = "View" },
			new CounterInfo { Type = "Download" }
		};

		[AsJson]
		[FormControl(Excluded = true)]
		public List<RatingInfo> RatingPoints { get; set; } = new List<RatingInfo>();

		[Property(MaxLength = 250)]
		[FormControl(Label = "{{books.info.controls.[name]}}")]
		public string Source { get; set; } = "";

		[Property(MaxLength = 1000)]
		[FormControl(Excluded = true)]
		public string SourceUrl { get; set; } = "";

		[Property(MaxLength = 250)]
		[FormControl(Hidden = true)]
		public string Cover { get; set; } = "";

		[Property(MaxLength = 250)]
		[FormControl(Label = "{{books.info.controls.[name]}}")]
		public string Tags { get; set; } = "";

		[Property(MaxLength = 250)]
		[FormControl(Excluded = true)]
		public string Contributor { get; set; } = "";

		[Sortable]
		[FormControl(Excluded = true)]
		public int TotalChapters { get; set; } = 0;

		string _PermanentID = "";

		[Ignore, JsonIgnore, BsonIgnore, MessagePackIgnore]
		public string PermanentID
		{
			set => this._PermanentID = value;
			get
			{
				if (string.IsNullOrWhiteSpace(this._PermanentID))
				{
					if (string.IsNullOrWhiteSpace(this.ID) || string.IsNullOrWhiteSpace(this.Name))
						this._PermanentID = UtilityService.GetUUID();
					else
					{
						this._PermanentID = Utility.GetBookAttribute($"{this.GetFilePath()}.json", "PermanentID") ?? this.ID;
						Utility.Cache.Set(this);
					}
				}
				return this._PermanentID;
			}
		}

		[Ignore, JsonIgnore, BsonIgnore]
		public string Credits { get; set; } = "";

		[Ignore, JsonIgnore, BsonIgnore]
		public string Stylesheet { get; set; } = "";

		[Ignore, JsonIgnore, BsonIgnore]
		public List<string> TOCs { get; set; } = new List<string>();

		[Ignore, JsonIgnore, BsonIgnore]
		public List<string> Chapters { get; set; } = new List<string>();

		[Ignore, JsonIgnore, BsonIgnore]
		public HashSet<string> MediaFiles { get; set; } = new HashSet<string>();

		[Ignore, JsonIgnore, BsonIgnore]
		public string Name
			=> string.IsNullOrWhiteSpace(this.Title)
				? ""
				: this.Title.Trim() + (string.IsNullOrWhiteSpace(this.Author) ? "" : " - " + this.Author.Trim());

		[Ignore, JsonIgnore, BsonIgnore]
		public override string SystemID { get; set; }

		[Ignore, JsonIgnore, BsonIgnore]
		public override string RepositoryID { get; set; }

		[Ignore, JsonIgnore, BsonIgnore]
		public override string RepositoryEntityID { get; set; }

		public override JObject ToJson(bool addTypeOfExtendedProperties, Action<JObject> onCompleted)
			=> this.ToJson(true, addTypeOfExtendedProperties, onCompleted);

		public JObject ToJson(bool asNormalized, bool addTypeOfExtendedProperties, Action<JObject> onCompleted = null)
			=> base.ToJson(addTypeOfExtendedProperties, json =>
			{
				if (asNormalized)
				{
					json["Cover"] = this.GetCoverURI();
					json["TOCs"] = new JArray();
					json["Files"] = this.GetFiles();

					var download = this.Counters.FirstOrDefault(c => c.Type.IsEquals("Download"));
					if (download != null)
					{
						var gotUpdated = false;

						if (!download.LastUpdated.IsInCurrentMonth() && download.Total == download.Month)
						{
							download.Month = 0;
							gotUpdated = true;
						}

						if (!download.LastUpdated.IsInCurrentWeek() && download.Total == download.Week)
						{
							download.Week = 0;
							gotUpdated = true;
						}

						if (gotUpdated)
							json["Counters"] = this.Counters.ToJArray();
					}
				}
				onCompleted?.Invoke(json);
			});

		public JObject GetFiles()
		{
			var filePath = Path.Combine(this.GetBookDirectory(), UtilityService.GetNormalizedFilename(this.Name));
			var downloadUri = this.GetDownloadURI();

			return new JObject
			{
				{ "Epub", new JObject
					{
						{ "Size", Utility.GetFileSize($"{filePath}.epub") },
						{ "Url", $"{downloadUri}.epub" }
					}
				},
				{ "Mobi", new JObject()
					{
						{ "Size", Utility.GetFileSize($"{filePath}.mobi") },
						{ "Url", $"{downloadUri}.mobi" }
					}
				}
			};
		}

		internal static Task<Book> GetAsync(string title, string author, CancellationToken cancellationToken = default)
			=> string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(author)
				? Task.FromResult<Book>(null)
				: Book.GetAsync<Book>(Filters<Book>.And(Filters<Book>.Equals("Title", title), Filters<Book>.Equals("Author", author)), null, null, cancellationToken);
	}
}