#region Related components
using System;
using System.IO;
using System.Linq;
using System.Diagnostics;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using MongoDB.Bson.Serialization.Attributes;

using net.vieapps.Components.Utility;
using net.vieapps.Components.Repository;
#endregion

namespace net.vieapps.Services.Books
{
	[Serializable, BsonIgnoreExtraElements, DebuggerDisplay("ID = {ID}, Name = {Name}")]
	[Entity(CollectionName = "Books", TableName = "T_Books_Books", CacheClass = typeof(Utility), CacheName = "Cache", Searchable = true)]
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
			this.Credits = "";
			this.TotalChapters = 0;
			this.LastUpdated = DateTime.Now;
			this.Counters = new List<CounterInfo>
			{
				new CounterInfo { Type = "View" },
				new CounterInfo { Type = "Download" }
			};
			this.RatingPoints = new List<RatingPoint>();
			this.TOCs = new List<string>();
			this.Chapters = new List<string>();
			this.MediaFiles = new HashSet<string>();
		}

		#region Properties
		[Property(MaxLength = 250, NotEmpty = true), Sortable, Searchable, FormControl(Label = "{{books.info.controls.[name]}}")]
		public override string Title { get; set; }

		[Property(MaxLength = 250), Searchable, FormControl(Label = "{{books.info.controls.[name]}}")]
		public string Original { get; set; }

		[Property(MaxLength = 250), Sortable(IndexName = "Info"), Searchable, FormControl(Label = "{{books.info.controls.[name]}}")]
		public string Author { get; set; }

		[Property(MaxLength = 250), Searchable, FormControl(Label = "{{books.info.controls.[name]}}")]
		public string Translator { get; set; }

		[Property(MaxLength = 250), FormControl(Label = "{{books.info.controls.[name]}}")]
		public string Publisher { get; set; }

		[Property(MaxLength = 250), FormControl(Label = "{{books.info.controls.[name]}}")]
		public string Producer { get; set; }

		[Property(MaxLength = 250, NotEmpty = true), Sortable(IndexName = "Info"), FormControl(ControlType = "Select", DataType = "dropdown", Label = "{{books.info.controls.[name]}}", SelectValuesRemoteURI = "books/definitions/categories")]
		public string Category { get; set; }

		[Property(MaxLength = 2), FormControl(Label = "{{books.info.controls.[name]}}")]
		public string Language { get; set; }

		[Property(MaxLength = 50), Sortable, FormControl(Excluded = true)]
		public string Status { get; set; }

		[Sortable, FormControl(Excluded = true)]
		public DateTime LastUpdated { get; set; }

		[AsJson, FormControl(Excluded = true)]
		public List<CounterInfo> Counters { get; set; }

		[AsJson, FormControl(Excluded = true)]
		public List<RatingPoint> RatingPoints { get; set; }

		[Property(MaxLength = 250), FormControl(Label = "{{books.info.controls.[name]}}")]
		public string Source { get; set; }

		[Property(MaxLength = 1000), FormControl(Excluded = true)]
		public string SourceUrl { get; set; }

		[Property(MaxLength = 250), FormControl(Hidden = true)]
		public string Cover { get; set; }

		[Property(MaxLength = 250), FormControl(Label = "{{books.info.controls.[name]}}")]
		public string Tags { get; set; }

		[Property(MaxLength = 250), FormControl(Excluded = true)]
		public string Contributor { get; set; }

		[Sortable, FormControl(Excluded = true)]
		public int TotalChapters { get; set; }

		string _PermanentID = "";

		[JsonIgnore, BsonIgnore, Ignore, FormControl(Excluded = true)]
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
						this._PermanentID = Utility.GetBookAttribute(this.GetFilePath() + ".json", "PermanentID") ?? this.ID;
						Utility.Cache.Set(this);
					}
				}
				return this._PermanentID;
			}
		}

		[JsonIgnore, BsonIgnore, Ignore]
		public string Credits { get; set; }

		[JsonIgnore, BsonIgnore, Ignore]
		public string Stylesheet { get; set; }

		[JsonIgnore, BsonIgnore, Ignore]
		public List<string> TOCs { get; set; }

		[JsonIgnore, BsonIgnore, Ignore]
		public List<string> Chapters { get; set; }

		[JsonIgnore, BsonIgnore, Ignore]
		public HashSet<string> MediaFiles { get; set; }

		[JsonIgnore, BsonIgnore, Ignore]
		public string Name
			=> string.IsNullOrWhiteSpace(this.Title)
				? ""
				: this.Title.Trim() + (string.IsNullOrWhiteSpace(this.Author) ? "" : " - " + this.Author.Trim());
		#endregion

		#region IBusinessEntity Properties
		[JsonIgnore, BsonIgnore, Ignore]
		public override string SystemID { get; set; }

		[JsonIgnore, BsonIgnore, Ignore]
		public override string RepositoryID { get; set; }

		[JsonIgnore, BsonIgnore, Ignore]
		public override string EntityID { get; set; }
		#endregion

		#region To JSON
		public override JObject ToJson(bool addTypeOfExtendedProperties, Action<JObject> onPreCompleted) => this.ToJson(addTypeOfExtendedProperties, onPreCompleted, true);

		public JObject ToJson(bool addTypeOfExtendedProperties = false, Action<JObject> onPreCompleted = null, bool asNormalized = true)
			=> base.ToJson(addTypeOfExtendedProperties, json =>
			{
				if (asNormalized)
				{
					json["Cover"] = this.GetCoverImageUri();
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
				onPreCompleted?.Invoke(json);
			});

		public JObject GetFiles()
		{
			var filePath = Path.Combine(this.GetFolderPath(), UtilityService.GetNormalizedFilename(this.Name));
			var downloadUri = this.GetDownloadUri();

			return new JObject
			{
				{ "Epub", new JObject()
					{
						{ "Size", Utility.GetFileSize(filePath + ".epub") },
						{ "Url", downloadUri + ".epub" }
					}
				},
				{ "Mobi", new JObject()
					{
						{ "Size", Utility.GetFileSize(filePath + ".mobi") },
						{ "Url", downloadUri + ".mobi" }
					}
				}
			};
		}
		#endregion

		internal static async Task<Book> GetAsync(string title, string author, CancellationToken cancellationToken = default(CancellationToken))
			=> string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(author)
				? null
				: await Book.GetAsync<Book>(Filters<Book>.And(Filters<Book>.Equals("Title", title), Filters<Book>.Equals("Author", author)), null, null, cancellationToken).ConfigureAwait(false);
	}
}