#region Related components
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Collections.Generic;
using System.Dynamic;
using System.IO;
using System.Text;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using net.vieapps.Components.Utility;
using net.vieapps.Components.Security;
using net.vieapps.Components.Repository;
#endregion

namespace net.vieapps.Services.Books
{
	public class ServiceComponent : ServiceBase
	{
		public ServiceComponent() { }

		#region Start
		public override void Start(string[] args = null, bool initializeRepository = true, System.Action nextAction = null, Func<Task> nextActionAsync = null)
		{
			// prepare folders
			if (Directory.Exists(Utility.FilesPath))
				try
				{
					this.CreateFolder(Utility.FolderOfDataFiles, false);
					Utility.Chars.ForEach(@char => this.CreateFolder(Utility.FolderOfDataFiles + @"\" + @char.ToLower()));
					this.CreateFolder(Utility.FolderOfStatisticFiles, false);
					this.CreateFolder(Utility.FolderOfContributedFiles, false);
					this.CreateFolder(Utility.FolderOfContributedFiles + @"\users");
					this.CreateFolder(Utility.FolderOfContributedFiles + @"\crawlers");
					this.CreateFolder(Utility.FolderOfTempFiles);
					this.CreateFolder(Utility.FolderOfTrashFiles);
				}
				catch (Exception ex)
				{
					this.WriteLog(UtilityService.NewUID, "Error occurred while preparing the folders of the service", ex);
				}

			// register timers
			this.RegisterTimers();

			// start the service
			base.Start(args, initializeRepository, nextAction, nextActionAsync);
		}

		void CreateFolder(string path, bool mediaFolders = true)
		{
			if (!Directory.Exists(path))
				Directory.CreateDirectory(path);

			if (mediaFolders && !Directory.Exists(path + @"\" + Utility.MediaFolder))
				Directory.CreateDirectory(path + @"\" + Utility.MediaFolder);
		}
		#endregion

		public override string ServiceName { get { return "books"; } }

		public override async Task<JObject> ProcessRequestAsync(RequestInfo requestInfo, CancellationToken cancellationToken = default(CancellationToken))
		{
#if DEBUG
			this.WriteLog(requestInfo.CorrelationID, "Process the request" + "\r\n" + "Request ==>" + "\r\n" + requestInfo.ToJson().ToString(Formatting.Indented));
#endif
			try
			{
				switch (requestInfo.ObjectName.ToLower())
				{
					case "book":
						return await this.ProcessBookAsync(requestInfo, cancellationToken);

					case "statistic":
						return await this.ProcessStatisticAsync(requestInfo, cancellationToken);

					case "profile":
						return await this.ProcessProfileAsync(requestInfo, cancellationToken);

					case "file":
						return await this.ProcessFileAsync(requestInfo, cancellationToken);

					case "bookmarks":
						return await this.ProcessBookmarksAsync(requestInfo, cancellationToken);
				}

				// unknown
				var msg = "The request is invalid [" + this.ServiceURI + "]: " + requestInfo.Verb + " /";
				if (!string.IsNullOrWhiteSpace(requestInfo.ObjectName))
					msg += requestInfo.ObjectName + (!string.IsNullOrWhiteSpace(requestInfo.GetObjectIdentity()) ? "/" + requestInfo.GetObjectIdentity() : "");
				throw new InvalidRequestException(msg);
			}
			catch (Exception ex)
			{
				this.WriteLog(requestInfo.CorrelationID, "Error occurred while processing: " + ex.Message + " [" + ex.GetType().ToString() + "]", ex);
				throw this.GetRuntimeException(requestInfo, ex);
			} 
		}

		protected override List<Privilege> GetPrivileges(User user, Privileges privileges)
		{
			var role = this.GetPrivilegeRole(user);
			return "book,category,statistic,profile".ToList()
				.Select(o => new Privilege(this.ServiceName, o, null, role))
				.ToList();
		}

		Task<JObject> ProcessBookAsync(RequestInfo requestInfo, CancellationToken cancellationToken)
		{
			switch (requestInfo.Verb)
			{
				case "GET":
					if ("search".IsEquals(requestInfo.GetObjectIdentity()))
						return this.SearchBooksAsync(requestInfo, cancellationToken);
					else
						return this.GetBookAsync(requestInfo, cancellationToken);

				case "POST":
					return this.CreateBookAsync(requestInfo, cancellationToken);
			}

			return Task.FromException<JObject>(new MethodNotAllowedException(requestInfo.Verb));
		}

		#region Search books
		async Task<JObject> SearchBooksAsync(RequestInfo requestInfo, CancellationToken cancellationToken)
		{
			// check permissions
			if (!await this.IsAuthorizedAsync(
					requestInfo, Components.Security.Action.View, null,
					(user, privileges) => this.GetPrivileges(user, privileges),
					(role) => this.GetPrivilegeActions(role))
				)
				throw new AccessDeniedException();

			// prepare
			var request = requestInfo.GetRequestExpando();

			var query = request.Get<string>("FilterBy.Query");

			var filter = request.Get<ExpandoObject>("FilterBy", null)?.ToFilterBy<Book>();
			if (filter == null)
				filter = Filters<Book>.NotEquals("Status", "Inactive");
			else if (filter is FilterBys<Book> && (filter as FilterBys<Book>).Children.FirstOrDefault(e => (e as FilterBy<Book>).Attribute.IsEquals("Status")) == null)
				(filter as FilterBys<Book>).Children.Add(Filters<Book>.NotEquals("Status", "Inactive"));

			var sort = request.Get<ExpandoObject>("SortBy", null)?.ToSortBy<Book>();
			if (sort == null && string.IsNullOrWhiteSpace(query))
				sort = Sorts<Book>.Descending("LastUpdated");

			var pagination = request.Has("Pagination")
				? request.Get<ExpandoObject>("Pagination").GetPagination()
				: new Tuple<long, int, int, int>(-1, 0, 20, 1);

			var pageNumber = pagination.Item4;

			// check cache
			var cacheKey = string.IsNullOrWhiteSpace(query)
				? this.GetCacheKey<Book>(filter, sort)
				: "";

			var json = !cacheKey.Equals("")
				? await Utility.Cache.GetAsync<string>(cacheKey + ":" + pageNumber.ToString() + "-json")
				: "";

			if (!string.IsNullOrWhiteSpace(json))
				return JObject.Parse(json);

			// prepare pagination
			var totalRecords = pagination.Item1 > -1
				? pagination.Item1
				:  -1;

			if (totalRecords < 0)
				totalRecords = string.IsNullOrWhiteSpace(query)
					? await Book.CountAsync(filter, cacheKey + "-total", cancellationToken)
					: await Book.CountByQueryAsync(query, filter, cancellationToken);

			var pageSize = pagination.Item3;

			var totalPages = (new Tuple<long, int>(totalRecords, pageSize)).GetTotalPages();
			if (totalPages > 0 && pageNumber > totalPages)
				pageNumber = totalPages;

			// search
			var objects = totalRecords > 0
				? string.IsNullOrWhiteSpace(query)
					? await Book.FindAsync(filter, sort, pageSize, pageNumber, cacheKey + ":" + pageNumber.ToString(), cancellationToken)
					: await Book.SearchAsync(query, filter, pageSize, pageNumber, cancellationToken)
				: new List<Book>();

			// build result
			pagination = new Tuple<long, int, int, int>(totalRecords, totalPages, pageSize, pageNumber);

			var result = new JObject()
			{
				{ "FilterBy", (filter ?? new FilterBys<Book>()).ToClientJson(query) },
				{ "SortBy", sort?.ToClientJson() },
				{ "Pagination", pagination.GetPagination() },
				{ "Objects", objects.ToJsonArray() }
			};

			// update cache
			if (!cacheKey.Equals(""))
			{
#if DEBUG
				json = result.ToString(Formatting.Indented);
#else
				json = result.ToString(Formatting.None);
#endif
				await Utility.Cache.SetAsync(cacheKey + ":" + pageNumber.ToString() + "-json", json, Utility.CacheTime / 2);
			}

			// return the result
			return result;
		}
		#endregion

		#region Create a book
		async Task<JObject> CreateBookAsync(RequestInfo requestInfo, CancellationToken cancellationToken)
		{
			// check permission on convert
			if (requestInfo.Extra != null && requestInfo.Extra.ContainsKey("x-convert"))
			{
				if (!await this.IsSystemAdministratorAsync(requestInfo))
					throw new AccessDeniedException();
			}

			// check permission on create new
			else
			{

			}

			// create new
			var book = requestInfo.GetBodyJson().Copy<Book>();
			await Book.CreateAsync(book, cancellationToken);
			return book.ToJson();
		}
		#endregion

		#region Get a book & update related (counters, chapter, files, ...)
		async Task<JObject> GetBookAsync(RequestInfo requestInfo, CancellationToken cancellationToken)
		{
			// get the book
			var objectIdentity = requestInfo.GetObjectIdentity();

			var id = !string.IsNullOrWhiteSpace(objectIdentity) && objectIdentity.IsValidUUID()
				? objectIdentity
				: requestInfo.Query.ContainsKey("id")
					? requestInfo.Query["id"]
					: null;

			var book = await Book.GetAsync<Book>(id, cancellationToken);
			if (book == null)
				throw new InformationNotFoundException();

			// load from JSON file if has no chapter
			Book bookJSON = null;
			if (id.Equals(objectIdentity) || "files".Equals(objectIdentity) || requestInfo.Query.ContainsKey("chapter"))
			{
				var keyJSON = id.GetCacheKey<Book>() + "-json";
				bookJSON = await Utility.Cache.GetAsync<Book>(keyJSON);
				if (bookJSON == null)
				{
					bookJSON = book.Clone();
					var jsonFilePath = book.GetFolderPath() + @"\" + UtilityService.GetNormalizedFilename(book.Name) + ".json";
					if (File.Exists(jsonFilePath))
					{
						bookJSON.CopyData(JObject.Parse(await UtilityService.ReadTextFileAsync(jsonFilePath, Encoding.UTF8)));
						await Utility.Cache.SetFragmentsAsync(keyJSON, bookJSON);
						if (book.SourceUrl != bookJSON.SourceUrl)
						{
							book.SourceUrl = bookJSON.SourceUrl;
							await Book.UpdateAsync(book);
						}
					}
				}
			}

			// counters
			if ("counters".IsEquals(objectIdentity))
			{
				// update counters
				var result = await this.UpdateCounterAsync(book, requestInfo.Query["action"] ?? "View", cancellationToken);

				// send update message
				await this.SendUpdateMessageAsync(new UpdateMessage()
				{
					DeviceID = "*",
					ExcludedDeviceID = requestInfo.Session.DeviceID,
					Type = "Books#Book#Counters",
					Data = result
				}, cancellationToken);

				// return update
				return result;
			}

			// chapter
			else if (requestInfo.Query.ContainsKey("chapter"))
			{
				var chapter = requestInfo.Query["chapter"].CastAs<int>();
				chapter = chapter < 1
					? 1
					: chapter > book.TotalChapters
						? book.TotalChapters
						: chapter;

				return new JObject()
				{
					{ "ID", book.ID },
					{ "Chapter", chapter },
					{ "Content", bookJSON.Chapters.Count > 0 ? bookJSON.Chapters[chapter - 1].NormalizeMediaFileUris(book) : "" }
				};
			}

			// generate files
			else if ("files".IsEquals(objectIdentity))
				return this.GenerateFiles(book);

			// book information
			else
			{
				// generate
				var json = book.ToJson();

				// update
				json["TOCs"] = bookJSON.TOCs.ToJArray(toc => new JValue(UtilityService.RemoveTags(toc)));
				if (book.TotalChapters < 2)
					json.Add(new JProperty("Body", bookJSON.Chapters.Count > 0 ? bookJSON.Chapters[0].NormalizeMediaFileUris(book) : ""));

				// return
				return json;
			}
		}

		async Task<JObject> UpdateCounterAsync(Book book, string action, CancellationToken cancellationToken = default(CancellationToken))
		{
			// get and update
			var counter = book.Counters.FirstOrDefault(c => c.Type.Equals(action));
			if (counter != null)
			{
				counter.Total++;
				counter.Week = counter.LastUpdated.IsInCurrentWeek() ? counter.Week + 1 : 1;
				counter.Month = counter.LastUpdated.IsInCurrentMonth() ? counter.Month + 1 : 1;
				counter.LastUpdated = DateTime.Now;
				await Book.UpdateAsync(book, cancellationToken);
			}

			// return data
			return new JObject()
			{
				{ "ID", book.ID },
				{ "Counters", book.Counters.ToJArray(c => c.ToJson()) }
			};
		}
		#endregion

		async Task<JObject> ProcessStatisticAsync(RequestInfo requestInfo, CancellationToken cancellationToken)
		{
			// prepre
			var objectIdentity = requestInfo.GetObjectIdentity();

			// individual statistic
			if ("status".IsEquals(objectIdentity))
				return this.GetStatisticsOfServiceStatus();

			else if ("categories".IsEquals(objectIdentity))
				return this.GetStatisticsOfCategories();

			else if ("authors".IsEquals(objectIdentity))
				return this.GetStatisticsOfAuthors(requestInfo.GetQueryParameter("char"));

			// all statistics (via RTU)
			else
			{
				var messages = new List<BaseMessage>()
				{
					new BaseMessage()
					{
						Type = "Books#Statistic#Status",
						Data = this.GetStatisticsOfServiceStatus()
					},
					new BaseMessage()
					{
						Type = "Books#Statistic#Categories",
						Data = this.GetStatisticsOfCategories()
					}
				};
				Utility.Chars.ForEach(@char =>
				{
					var data = this.GetStatisticsOfAuthors(@char);
					data.Add(new JProperty("Char", @char));

					messages.Add(new BaseMessage()
					{
						Type = "Books#Statistic#Authors",
						Data = data
					});
				});

				await this.SendUpdateMessagesAsync(messages, requestInfo.Session.DeviceID, null, cancellationToken);

				return new JObject();
			}
		}

		#region Get statistics
		JObject GetStatisticsOfServiceStatus()
		{
			return new JObject()
			{
				{ "Total", Utility.Status.Count },
				{ "Objects", Utility.Status.ToJson() }
			};
		}

		JObject GetStatisticsOfCategories()
		{
			return new JObject()
			{
				{ "Total", Utility.Categories.Count },
				{ "Objects", Utility.Categories.ToJson() }
			};
		}

		JObject GetStatisticsOfAuthors(string @char)
		{
			var authors = Utility.Authors.Find(@char).ToList();
			return new JObject()
			{
				{ "Total", authors.Count },
				{ "Objects", authors.ToJArray(a => a.ToJson()) }
			};
		}
		#endregion

		Task<JObject> ProcessProfileAsync(RequestInfo requestInfo, CancellationToken cancellationToken)
		{
			switch (requestInfo.Verb)
			{
				case "GET":
					return this.GetProfileAsync(requestInfo, cancellationToken);

				case "POST":
					return this.CreateProfileAsync(requestInfo, cancellationToken);

				case "PUT":
					return this.UpdateProfileAsync(requestInfo, cancellationToken);
			}

			return Task.FromException<JObject>(new MethodNotAllowedException(requestInfo.Verb));
		}

		#region Create an account profile
		async Task<JObject> CreateProfileAsync(RequestInfo requestInfo, CancellationToken cancellationToken)
		{
			// prepare identity
			var id = requestInfo.GetObjectIdentity() ?? requestInfo.Session.User.ID;

			// check permission
			var isSystemAdministrator = await this.IsSystemAdministratorAsync(requestInfo);
			if (requestInfo.Extra != null && requestInfo.Extra.ContainsKey("x-convert"))
			{
				if (!isSystemAdministrator)
					throw new AccessDeniedException();
			}

			// check permission on create
			else
			{
				var gotRights = isSystemAdministrator || (this.IsAuthenticated(requestInfo) && requestInfo.Session.User.ID.IsEquals(id));
				if (!gotRights)
					throw new AccessDeniedException();
			}

			// create account profile
			var account = requestInfo.GetBodyJson().Copy<Account>();

			// reassign identity
			if (requestInfo.Extra == null || !requestInfo.Extra.ContainsKey("x-convert"))
				account.ID = id;

			// update database
			await Account.CreateAsync(account, cancellationToken);
			return account.ToJson();
		}
		#endregion

		#region Get an account profile
		async Task<JObject> GetProfileAsync(RequestInfo requestInfo, CancellationToken cancellationToken)
		{
			// check permissions
			var id = requestInfo.GetObjectIdentity() ?? requestInfo.Session.User.ID;
			var gotRights = this.IsAuthenticated(requestInfo) && requestInfo.Session.User.ID.IsEquals(id);
			if (!gotRights)
				gotRights = await this.IsSystemAdministratorAsync(requestInfo);
			if (!gotRights)
				gotRights = await this.IsAuthorizedAsync(requestInfo, Components.Security.Action.View, null, this.GetPrivileges, this.GetPrivilegeActions);
			if (!gotRights)
				throw new AccessDeniedException();

			// get information
			var account = await Account.GetAsync<Account>(id, cancellationToken);

			// special: not found
			if (account == null)
			{
				if (id.Equals(requestInfo.Session.User.ID))
				{
					account = new Account()
					{
						ID = id
					};
					await Account.CreateAsync(account);
				}
				else
					throw new InformationNotFoundException();
			}

			// return JSON
			return account.ToJson();
		}
		#endregion

		#region Update an account profile
		async Task<JObject> UpdateProfileAsync(RequestInfo requestInfo, CancellationToken cancellationToken)
		{
			// check permissions
			var id = requestInfo.GetObjectIdentity() ?? requestInfo.Session.User.ID;
			var gotRights = this.IsAuthenticated(requestInfo) && requestInfo.Session.User.ID.IsEquals(id);
			if (!gotRights)
				gotRights = await this.IsSystemAdministratorAsync(requestInfo);
			if (!gotRights)
				gotRights = await this.IsAuthorizedAsync(requestInfo, Components.Security.Action.Update, null, this.GetPrivileges, this.GetPrivilegeActions);
			if (!gotRights)
				throw new AccessDeniedException();

			// get existing information
			var account = await Account.GetAsync<Account>(id, cancellationToken);
			if (account == null)
				throw new InformationNotFoundException();

			// update
			account.CopyFrom(requestInfo.GetBodyJson());
			account.ID = id;

			await Account.UpdateAsync(account, cancellationToken);
			return account.ToJson();
		}
		#endregion

		Task<JObject> ProcessFileAsync(RequestInfo requestInfo, CancellationToken cancellationToken)
		{
			// convert file
			if (requestInfo.Verb.IsEquals("POST") && requestInfo.Extra != null && requestInfo.Extra.ContainsKey("x-convert"))
				return this.CopyFilesAsync(requestInfo);

			return Task.FromException<JObject>(new MethodNotAllowedException(requestInfo.Verb));
		}

		#region Copy files of a book
		async Task<JObject> CopyFilesAsync(RequestInfo requestInfo)
		{
			// prepare
			if (!await this.IsSystemAdministratorAsync(requestInfo))
				throw new AccessDeniedException();

			var name = requestInfo.Extra != null && requestInfo.Extra.ContainsKey("Name")
				? requestInfo.Extra["Name"]
				: null;

			var path = requestInfo.Extra != null && requestInfo.Extra.ContainsKey("Path")
				? requestInfo.Extra["Path"]
				: null;

			if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
				throw new InvalidRequestException();

			var source = path + @"\" + name.GetFirstChar() + @"\";
			var destination = Utility.FolderOfDataFiles + @"\" + name.GetFirstChar() + @"\";
			var filename = name + ".json";

			// copy file
			if (File.Exists(source + filename))
			{
				File.Copy(source + filename, destination + filename, true);

				var permanentID = Utility.GetDataFromJsonFile(source + filename, "PermanentID");
				UtilityService.GetFiles(source + Utility.MediaFolder, permanentID + "-*.*")
					.ForEach(file => File.Copy(file.FullName, destination + Utility.MediaFolder + @"\" + file.Name, true));
			}

			return new JObject()
			{
				{ "Status", "OK" }
			};
		}
		#endregion

		#region Generate files of a book
		JObject GenerateFiles(Book book)
		{
			// run a task to generate files
			if (book != null)
				Task.Run(async () =>
				{
					await this.GenerateFilesAsync(book);
				}).ConfigureAwait(false);

			// return status
			return new JObject()
			{
				{ "Status", "OK" }
			};
		}

		async Task GenerateFilesAsync(Book book)
		{
			// prepare
			var filePath = book.GetFolderPath() + @"\" + UtilityService.GetNormalizedFilename(book.Name);
			var flag = "Files-" + filePath.ToLower().GetMD5();
			if (await Utility.Cache.ExistsAsync(flag))
				return;

			// generate files
			if (!File.Exists(filePath + ".epub") || !File.Exists(filePath + ".mobi"))
			{
				// update flag
				await Utility.Cache.SetAsync(flag, book.ID);

				// prepare
				var correlationID = UtilityService.NewUID;
				var status = new Dictionary<string, bool>()
				{
					{ "epub",  File.Exists(filePath + ".epub") },
					{ "mobi",  File.Exists(filePath + ".mobi") }
				};

				if (!status["epub"])
					this.GenerateEpubFile(book, correlationID,
						() =>
						{
							status["epub"] = true;
						},
						(ex) =>
						{
							status["epub"] = true;
							this.WriteLog(correlationID, "Error occurred while generating EPUB file", ex);
						}
					);

				if (!status["mobi"])
					this.GenerateMobiFile(book, correlationID,
						() =>
						{
							status["mobi"] = true;
						},
						(ex) =>
						{
							status["mobi"] = true;
							this.WriteLog(correlationID, "Error occurred while generating MOBI file", ex);
						}
					);

				// wait for all tasks are completed
				while (!status["epub"] || !status["mobi"])
					await Task.Delay(789);

				// update flag
				await Utility.Cache.RemoveAsync(flag);
			}

			// send the update message
			await this.SendUpdateMessageAsync(new UpdateMessage()
			{
				Type = "Books#Book#Files",
				DeviceID = "*",
				Data = new JObject()
				{
					{ "ID", book.ID },
					{ "Files", book.GetFiles() }
				}				
			});
		}

		public string CreditsInApp
		{
			get { return "<p>Chuyển đổi và đóng gói bằng <b>VIEApps Online Books</b></p>"; }
		}

		public string PageBreak
		{
			get { return "<mbp:pagebreak/>"; }
		}

		public string GetTOCItem(Book book, int index)
		{
			string toc = book.TOCs != null && index < book.TOCs.Count
				? UtilityService.RemoveTag(UtilityService.RemoveTag(book.TOCs[index], "a"), "p")
				: "";

			return string.IsNullOrWhiteSpace(toc)
				? (index + 1).ToString()
				: toc.GetNormalized().Replace("{0}", (index + 1).ToString());
		}

		void GenerateEpubFile(Book book, string correlationID = null, System.Action onCompleted = null, Action<Exception> onError = null)
		{
			try
			{
#if DEBUG || GENERATORLOGS
				this.WriteLog(correlationID, "Start to generate EPUB file [" + book.Name + "]");
				var stopwatch = new Stopwatch();
				stopwatch.Start();
#endif

				// prepare
				var navs = book.TOCs.Select((toc, index) => this.GetTOCItem(book, index)).ToList();
				var pages = book.Chapters.Select(c => c.NormalizeMediaFilePaths(book)).ToList();

				// meta data
				Epub.Document epub = new Epub.Document();
				epub.AddBookIdentifier(UtilityService.GetUUID(book.ID));
				epub.AddLanguage(book.Language);
				epub.AddTitle(book.Title);
				epub.AddAuthor(book.Author);
				epub.AddMetaItem("dc:contributor", this.CreditsInApp.Replace("\n<p>", " - ").Replace("\n", "").Replace("<p>", "").Replace("</p>", "").Replace("<b>", "").Replace("</b>", "").Replace("<i>", "").Replace("</i>", ""));

				if (!string.IsNullOrWhiteSpace(book.Translator))
					epub.AddTranslator(book.Translator);

				if (!string.IsNullOrWhiteSpace(book.Original))
					epub.AddMetaItem("book:Original", book.Original);

				if (!string.IsNullOrWhiteSpace(book.Publisher))
					epub.AddMetaItem("book:Publisher", book.Publisher);

				if (!string.IsNullOrWhiteSpace(book.Producer))
					epub.AddMetaItem("book:Producer", book.Producer);

				if (!string.IsNullOrWhiteSpace(book.Source))
					epub.AddMetaItem("book:Source", book.Source);

				if (!string.IsNullOrWhiteSpace(book.SourceUrl))
					epub.AddMetaItem("book:SourceUrl", book.SourceUrl);

				// CSS stylesheet
				string stylesheet = !string.IsNullOrWhiteSpace(book.Stylesheet)
					? book.Stylesheet
					: @"
					h1, h2, h3, h4, h5, h6, p, div, blockquote { 
						display: block; 
						clear: both; 
						overflow: hidden; 
						text-indent: 0; 
						text-align: left; 
						padding: 0;
						margin: 0.5em 0;
					}
					h1, h2, h3, h4, h5, h6 { 
						font-family: sans-serif;
					}
					h1 { 
						font-size: 1.4em;
						font-weight: bold;
					}
					h2 { 
						font-size: 1.3em;
						font-weight: bold;
					}
					h3 { 
						font-size: 1.2em;
						font-weight: bold;
					}
					h1.title { 
						font-size: 1.8em;
						font-weight: bold;
						margin: 1em 0;
					}
					p, div, blockquote { 
						font-family: serif;
						line-height: 1.42857143;
					}
					p.author { 
						font-family: serif;
						font-weight: bold;
						font-size: 0.9em;
						text-transform: uppercase;
					}
					div.app-credits>p, p.info, blockquote { 
						font-family: sans-serif;
						font-size: 0.8em;
					}";

				epub.AddStylesheetData("style.css", stylesheet.Replace("\t", ""));

				// cover image
				if (!string.IsNullOrWhiteSpace(book.Cover))
				{
					byte[] coverData = UtilityService.ReadFile(book.Cover.NormalizeMediaFilePaths(book));
					if (coverData != null && coverData.Length > 0)
					{
						string coverId = epub.AddImageData("cover.jpg", coverData);
						epub.AddMetaItem("cover", coverId);
					}
				}

				// pages & nav points
				string pageTemplate = @"<!DOCTYPE html>
				<html xmlns=""http://www.w3.org/1999/xhtml"">
					<head>
						<title>{0}</title>
						<meta http-equiv=""Content-Type"" content=""text/html; charset=utf-8""/>
						<link type=""text/css"" rel=""stylesheet"" href=""style.css""/>
						<style type=""text/css"">
							@page {
								padding: 0;
								margin: 0;
							}
						</style>
					</head>
					<body>
						{1}
					</body>
				</html>".Trim().Replace("\t", "");

				// info
				string info = "<p class=\"author\">" + book.Author + "</p>"
										+ "<h1 class=\"title\">" + book.Title + "</h1>";

				if (!string.IsNullOrWhiteSpace(book.Original))
					info += "<p class=\"info\">" + (book.Language.Equals("en") ? "Original: " : "Nguyên tác: ") + "<b><i>" + book.Original + "</i></b></p>";

				info += "<hr/>";

				if (!string.IsNullOrWhiteSpace(book.Translator))
					info += "<p class=\"info\">" + (book.Language.Equals("en") ? "Translator: " : "Dịch giả: ") + "<b><i>" + book.Translator + "</i></b></p>";

				if (!string.IsNullOrWhiteSpace(book.Publisher))
					info += "<p class=\"info\">" + (book.Language.Equals("en") ? "Pubisher: " : "NXB: ") + "<b><i>" + book.Publisher + "</i></b></p>";

				if (!string.IsNullOrWhiteSpace(book.Producer))
					info += "<p class=\"info\">" + (book.Language.Equals("en") ? "Producer: " : "Sản xuất: ") + "<b><i>" + book.Producer + "</i></b></p>";

				info += "<div class=\"credits\">"
						+ (!string.IsNullOrWhiteSpace(book.Source) ? "<p>" + (book.Language.Equals("en") ? "Source: " : "Nguồn: ") + "<b><i>" + book.Source + "</i></b></p>" : "")
						+ "\r" + "<hr/>" + this.CreditsInApp + "</div>";

				epub.AddXhtmlData("page0.xhtml", pageTemplate.Replace("{0}", "Info").Replace("{1}", info.Replace("<p>", "\r" + "<p>")));

				// chapters
				for (int index = 0; index < pages.Count; index++)
				{
					string name = string.Format("page{0}.xhtml", index + 1);
					string content = pages[index].NormalizeMediaFilePaths(book);

					int start = content.IndexOf("<img", StringComparison.OrdinalIgnoreCase), end = -1;
					while (start > -1)
					{
						start = content.IndexOf("src=", start + 1, StringComparison.OrdinalIgnoreCase) + 5;
						char @char = content[start - 1];
						end = content.IndexOf(@char.ToString(), start + 1, StringComparison.OrdinalIgnoreCase);

						string image = content.Substring(start, end - start);
						byte[] imageData = UtilityService.ReadFile(image.NormalizeMediaFilePaths(book));
						if (imageData != null && imageData.Length > 0)
							epub.AddImageData(image, imageData);

						start = content.IndexOf("<img", start + 1, StringComparison.OrdinalIgnoreCase);
					}

					epub.AddXhtmlData(name, pageTemplate.Replace("{0}", index < navs.Count ? navs[index] : book.Title).Replace("{1}", content.Replace("<p>", "\r" + "<p>")));
					if (book.Chapters.Count > 1)
						epub.AddNavPoint(index < navs.Count ? navs[index] : book.Title + " - " + (index + 1).ToString(), name, index + 1);
				}

				// save into file on disc
				epub.Generate(book.GetFolderPath() + @"\" + UtilityService.GetNormalizedFilename(book.Name) + ".epub");
				try
				{
					Directory.Delete(epub.GetTempDirectory(), true);
				}
				catch { }

#if DEBUG || GENERATORLOGS
				stopwatch.Stop();
				this.WriteLog(correlationID, "Generate EPUB file is completed [" + book.Name + "] - Excution times: " + stopwatch.GetElapsedTimes());
#endif

				// callback when done
				onCompleted?.Invoke();
			}
			catch (Exception ex)
			{
				onError?.Invoke(ex);
			}
		}

		void GenerateMobiFile(Book book, string correlationID = null, System.Action onCompleted = null, Action<Exception> onError = null)
		{
			try
			{
#if DEBUG || GENERATORLOGS
				this.WriteLog(correlationID, "Start to generate MOBI file [" + book.Name + "]");
				var stopwatch = new Stopwatch();
				stopwatch.Start();
#endif

				// file name & path
				string filename = book.ID;
				string filePath = book.GetFolderPath() + @"\";

				// prepare HTML
				var stylesheet = !string.IsNullOrWhiteSpace(book.Stylesheet)
					? book.Stylesheet
					: @"
						h1, h2, h3, h4, h5, h6, p, div, blockquote { 
							display: block; 
							clear: both; 
							overflow: hidden; 
							text-indent: 0; 
							text-align: left; 
							margin: 0.75em 0;
						}
						h1, h2, h3, h4, h5, h6 { 
							font-family: sans-serif;
						}
						h1 { 
							font-size: 1.5em;
							font-weight: bold;
						}
						h2 { 
							font-size: 1.4em;
							font-weight: bold;
						}
						h3 { 
							font-size: 1.3em;
							font-weight: bold;
						}
						h1.title { 
							font-size: 2em;
							font-weight: bold;
							margin: 1em 0;
						}
						p.author, p.translator { 
							margin: 0.5em 0;
						}
						p, div, blockquote { 
							font-family: serif;
							line-height: 1.42857143;
						}
						p.app-credits, blockquote { 
							font-family: sans-serif;
							font-size: 0.8em;
						}";

				string content = "<!DOCTYPE html>" + "\n"
						+ "<html xmlns=\"http://www.w3.org/1999/xhtml\">" + "\n"
						+ "<head>" + "\n"
						+ "<title>" + book.Title + "</title>" + "\n"
						+ "<meta http-equiv=\"content-type\" content=\"text/html; charset=utf-8\"/>" + "\n"
						+ "<meta name=\"content-language\" content=\"" + book.Language + "\"/>" + "\n"
						+ (string.IsNullOrWhiteSpace(book.Author) ? "" : "<meta name=\"author\" content=\"" + book.Author + "\"/>" + "\n")
						+ (string.IsNullOrWhiteSpace(book.Original) ? "" : "<meta name=\"book:Original\" content=\"" + book.Original + "\"/>" + "\n")
						+ (string.IsNullOrWhiteSpace(book.Translator) ? "" : "<meta name=\"book:Translator\" content=\"" + book.Translator + "\"/>" + "\n")
						+ (string.IsNullOrWhiteSpace(book.Category) ? "" : "<meta name=\"book:Category\" content=\"" + book.Category + "\"/>" + "\n")
						+ (string.IsNullOrWhiteSpace(book.Cover) ? "" : "<meta name=\"book:Cover\" content=\"" + book.Cover.NormalizeMediaFilePaths(book) + "\"/>" + "\n")
						+ (string.IsNullOrWhiteSpace(book.Publisher) ? "" : "<meta name=\"book:Publisher\" content=\"" + book.Publisher + "\"/>" + "\n")
						+ (string.IsNullOrWhiteSpace(book.Producer) ? "" : "<meta name=\"book:Publisher\" content=\"" + book.Producer + "\"/>" + "\n")
						+ (string.IsNullOrWhiteSpace(book.Source) ? "" : "<meta name=\"book:Source\" content=\"" + book.Source + "\"/>" + "\n")
						+ (string.IsNullOrWhiteSpace(book.SourceUrl) ? "" : "<meta name=\"book:SourceUrl\" content=\"" + book.SourceUrl + "\"/>" + "\n")
						+ (string.IsNullOrWhiteSpace(book.Tags) ? "" : "<meta name=\"book:Tags\" content=\"" + book.Tags + "\"/>" + "\n")
						+ "<meta name=\"book:PermanentID\" content=\"" + book.PermanentID + "\"/>" + "\n"
						+ "<style type=\"text/css\">" + stylesheet.Replace("\t", "") + "</style>" + "\n"
						+ "</head>" + "\n"
						+ "<body>" + "\n"
						+ "<a name=\"start\"></a>" + "\n"
						+ (string.IsNullOrWhiteSpace(book.Author) ? "" : "<p class=\"author\">" + book.Author + "</p>" + "\n")
						+ "<h1 class=\"title\">" + book.Title + "</h1>" + "\n"
						+ (string.IsNullOrWhiteSpace(book.Translator) ? "" : "<p class=\"translator\">Dịch giả: " + book.Translator + "</p>" + "\n")
						+ "\n";

				content += this.PageBreak
						+ "\n"
						+ "<h1 class=\"credits\">" + (book.Language.Equals("en") ? "INFO" : "THÔNG TIN") + "</h1>"
						+ (!string.IsNullOrWhiteSpace(book.Publisher) ? "\n<p>" + (book.Language.Equals("en") ? "Publisher: " : "NXB: ") + book.Publisher + "</p>" : "")
						+ (!string.IsNullOrWhiteSpace(book.Producer) ? "\n<p>" + (book.Language.Equals("en") ? "Producer: " : "Sản xuất: ") + book.Producer + "</p>" : "")
						+ (!string.IsNullOrWhiteSpace(book.Source) ? "\n<p>" + (book.Language.Equals("en") ? "Source: " : "Nguồn: ") + book.Source + "</p>" : "")
						+ "\n" + this.CreditsInApp + "\n\n";

				string[] headingTags = "h1|h2|h3|h4|h5|h6".Split('|');
				string toc = "", body = "";
				if (book.Chapters != null && book.Chapters.Count > 0)
					for (int index = 0; index < book.Chapters.Count; index++)
					{
						string chapter = book.Chapters[index].NormalizeMediaFileUris(book);
						foreach (string tag in headingTags)
							chapter = chapter.Replace(StringComparison.OrdinalIgnoreCase, "<" + tag + ">", "\n<" + tag + ">").Replace(StringComparison.OrdinalIgnoreCase, "</" + tag + ">", "</" + tag + ">\n");
						chapter = chapter.Trim().Replace("</p><p>", "</p>\n<p>").Replace("\n\n", "\n");

						toc += book.Chapters.Count > 1 ? (!toc.Equals("") ? "\n" : "") + "<p><a href=\"#chapter" + (index + 1) + "\">" + this.GetTOCItem(book, index) + "</a></p>" : "";
						body += this.PageBreak + "\n"
									+ "<a name=\"chapter" + (index + 1) + "\"></a>" + "\n"
									+ chapter
									+ "\n\n";
					}

				if (!string.IsNullOrWhiteSpace(toc))
					content += this.PageBreak
						+ "\n"
						+ "<a name=\"toc\"></a>" + "\n"
						+ "<h1 class=\"toc\">" + (book.Language.Equals("en") ? "TABLE OF CONTENTS: " : "MỤC LỤC") + "</h1>" + "\n"
						+ toc
						+ "\n\n";

				content += body + "</body>\n" + "</html>";

				// geneate HTML file
				UtilityService.WriteTextFile(filePath + filename + ".html", content, false);

				// prepare NCX
				if (book.TOCs != null && book.TOCs.Count > 0)
				{
					content = "<?xml version=\"1.0\" encoding=\"utf-8\"?>" + "\n"
							+ "<!DOCTYPE ncx PUBLIC \" -//NISO//DTD ncx 2005-1//EN\" \"http://www.daisy.org/z3986/2005/ncx-2005-1.dtd\">" + "\n"
							+ "<ncx xmlns=\"http://www.daisy.org/z3986/2005/ncx/\">" + "\n"
							+ "<head>" + "\n"
							+ "<meta name=\"dtb:uid\" content=\"urn:uuid:dbe" + UtilityService.GetUUID(book.ID) + "\"/>" + "\n"
							+ "</head>" + "\n"
							+ "<docTitle><text>" + book.Title + @"</text></docTitle>" + "\n"
							+ "<docAuthor><text>" + book.Author + @"</text></docAuthor>" + "\n"
							+ "<navMap>";

					for (int index = 0; index < book.TOCs.Count; index++)
						content += "<navPoint id=\"navid" + (index + 1) + "\" playOrder=\"" + (index + 1) + "\">" + "\n"
							+ "<navLabel>" + "\n"
							+ "<text>" + this.GetTOCItem(book, index) + "</text>" + "\n"
							+ "</navLabel>" + "\n"
							+ "<content src=\"" + filename + ".html#chapter" + (index + 1) + "\"/>" + "\n"
							+ "</navPoint>" + "\n";

					content += "</navMap>" + "\n"
							+ "</ncx>";

					// geneate NCX file
					UtilityService.WriteTextFile(filePath + filename + ".ncx", content, false);
				}

				// prepare OPF
				content = "<?xml version=\"1.0\" encoding=\"utf-8\"?>" + "\n"
					+ "<package xmlns=\"http://www.idpf.org/2007/opf\" version=\"2.0\" unique-identifier=\"" + UtilityService.GetUUID(book.ID) + "\">" + "\n"
					+ "<metadata xmlns:dc=\"http://purl.org/dc/elements/1.1/\" xmlns:opf=\"http://www.idpf.org/2007/opf\">" + "\n"
					+ "<dc:Title>" + book.Title + @"</dc:Title>" + "\n"
					+ "<dc:Creator opf:role=\"aut\">" + book.Author + @"</dc:Creator>" + "\n"
					+ (string.IsNullOrWhiteSpace(book.Publisher) ? "" : "<dc:Publisher>" + book.Publisher + "</dc:Publisher>" + "\n")
					+ "<dc:Language>" + book.Language + "</dc:Language>" + "\n"
					+ "<dc:Contributor>VIEApps Online Books</dc:Contributor>" + "\n"
					+ "</metadata> " + "\n"
					+ "<manifest>" + "\n"
					+ "<item id=\"ncx\" media-type=\"application/x-dtbncx+xml\" href=\"" + filename + ".ncx\"/> " + "\n"
					+ (string.IsNullOrWhiteSpace(book.Cover) ? "" : "<item id=\"cover\" media-type=\"image/" + (book.Cover.EndsWith(".png") ? "png" : book.Cover.EndsWith(".gif") ? "gif" : "jpeg") + "\" href=\"" + book.Cover.NormalizeMediaFilePaths(book) + "\"/>" + "\n")
					+ "<item id=\"contents\" media-type=\"application/xhtml+xml\" href=\"" + filename + ".html\"/> " + "\n"
					+ "</manifest>" + "\n"
					+ "<spine toc=\"" + (book.TOCs != null && book.TOCs.Count > 0 ? "ncx" : "toc") + "\">" + "\n"
					+ "<itemref idref=\"contents\"/>" + "\n"
					+ "</spine>" + "\n"
					+ "<guide>" + "\n"
					+ (string.IsNullOrWhiteSpace(book.Cover) ? "" : "<reference type=\"cover\" title=\"Cover\" href=\"" + book.Cover.NormalizeMediaFilePaths(book) + "\"/>" + "\n")
					+ "<reference type=\"toc\" title=\"Table of Contents\" href=\"" + filename + ".html#toc\"/>" + "\n"
					+ "<reference type=\"text\" title=\"Starting Point\" href=\"" + filename + ".html#start\"/>" + "\n"
					+ "</guide>" + "\n"
					+ "</package>";

				// generate OPF
				UtilityService.WriteTextFile(filePath + filename + ".opf", content, false);

				// generate MOBI
				string generator = UtilityService.GetAppSetting("Mobi.Generator", "VIEApps.Services.Books.Mobi.Generator.dll");
				var output = "";

				UtilityService.RunProcess(generator, "\"" + filePath + filename + ".opf\"",
					(sender, args) =>
					{
						// rename file
						File.Move(filePath + filename + ".mobi", filePath + UtilityService.GetNormalizedFilename(book.Name) + ".mobi");

						// delete temporary files
						UtilityService.GetFiles(filePath, filename + ".*")
							.ForEach(file =>
							{
								try
								{
									file.Delete();
								}
								catch { }
							});

#if DEBUG || GENERATORLOGS
						stopwatch.Stop();
						this.WriteLog(correlationID,  "Generate MOBI file is completed [" + book.Name + "] - Excution times: " + stopwatch.GetElapsedTimes() + "\r\n" + output);
#endif

						// call back when done
						onCompleted?.Invoke();
					},
					(sender, args) =>
					{
						if (!string.IsNullOrWhiteSpace(args.Data))
							output += "\r\n" + args.Data;
					}
				);
			}
			catch (Exception ex)
			{
				onError?.Invoke(ex);
			}
		}
		#endregion

		async Task<JObject> ProcessBookmarksAsync(RequestInfo requestInfo, CancellationToken cancellationToken)
		{
			var account = await Account.GetAsync<Account>(requestInfo.Session.User.ID);
			if (account == null)
				throw new InformationNotFoundException();

			switch (requestInfo.Verb)
			{
				// get bookmarks
				case "GET":
					return new JObject()
					{
						{"ID", account.ID },
						{"Sync", true },
						{ "Objects", account.Bookmarks.ToJArray() }
					};

				// update bookmarks
				case "POST":
					var bookmarks = requestInfo.GetBodyJson() as JArray;
					foreach (JObject bookmark in bookmarks)
						account.Bookmarks.Add(bookmark.FromJson<Account.Bookmark>());

					account.Bookmarks = account.Bookmarks
						.OrderByDescending(b => b.Time)
						.Distinct(new Account.BookmarkComparer())
						.Take(30)
						.ToList();
					account.LastSync = DateTime.Now;
					await Account.UpdateAsync(account, cancellationToken);

					return new JObject()
					{
						{"ID", account.ID },
						{ "Objects", account.Bookmarks.ToJArray() }
					};

				// delete a bookmark
				case "DELETE":
					var id = requestInfo.GetObjectIdentity();
					account.Bookmarks = account.Bookmarks
						.Where(b => !b.ID.IsEquals(id))
						.OrderByDescending(b => b.Time)
						.Distinct(new Account.BookmarkComparer())
						.Take(30)
						.ToList();
					account.LastSync = DateTime.Now;
					await Account.UpdateAsync(account, cancellationToken);

					var data = new JObject()
					{
						{"ID", account.ID },
						{"Sync", true },
						{ "Objects", account.Bookmarks.ToJArray() }
					};

					var sessions = await this.GetSessionsAsync(requestInfo);
					await sessions.Where(s => s.Item4).ForEachAsync(async (session, ctoken) =>
					{
						await this.SendUpdateMessageAsync(new UpdateMessage()
						{
							Type = "Books#Bookmarks",
							DeviceID = session.Item2,
							Data = data
						}, ctoken);
					}, cancellationToken);

					return data;
			}

			throw new MethodNotAllowedException(requestInfo.Verb);
		}

		#region Process inter-communicate messages
		protected override void ProcessInterCommunicateMessage(CommunicateMessage message)
		{
			// prepare
			var data = message.Data?.ToExpandoObject();
			if (data == null)
				return;

			// update counters
			if (message.Type.IsEquals("Download") && !string.IsNullOrWhiteSpace(data.Get<string>("UserID")) && !string.IsNullOrWhiteSpace(data.Get<string>("BookID")))
				try
				{
					var book = Book.Get<Book>(data.Get<string>("BookID"));
					if (book != null)
					{
						Task.Run(async () =>
						{
							var result = await this.UpdateCounterAsync(book, Components.Security.Action.Download.ToString());
							await this.SendUpdateMessageAsync(new UpdateMessage()
							{
								DeviceID = "*",
								Type = "Books#Book#Counters",
								Data = result
							});
						}).ConfigureAwait(false);
					}
#if DEBUG
					this.WriteLog(UtilityService.NewUID, "Update counters successful" + "\r\n" + "=====>" + "\r\n" + message.ToJson().ToString(Formatting.Indented));
#endif
				}
#if DEBUG
				catch (Exception ex)
				{
					this.WriteLog(UtilityService.NewUID, "Error occurred while updating counters", ex);
				}
#else
				catch { }
#endif
		}
		#endregion

		#region Timers for working with background workers & schedulers
		internal List<System.Timers.Timer> _timers = new List<System.Timers.Timer>();

		void StartTimer(int interval, Action<object, System.Timers.ElapsedEventArgs> action, bool autoReset = true)
		{
			var timer = new System.Timers.Timer()
			{
				Interval = interval * 1000,
				AutoReset = autoReset
			};
			timer.Elapsed += new System.Timers.ElapsedEventHandler(action);
			timer.Start();
			this._timers.Add(timer);
		}

		void RegisterTimers()
		{
			// delete old .EPUB & .MOBI files
			this.StartTimer(60 * 60, (sender, args) =>
			{
				var remainTime = DateTime.Now.AddDays(-30);
				UtilityService.GetFiles(Utility.FolderOfDataFiles, "*.epub|*.mobi", true)
					.Where(file => file.LastWriteTime < remainTime)
					.ToList()
					.ForEach(file =>
					{
						try
						{
							file.Delete();
						}
						catch { }
					});
			});

			// scan new e-books from iSach.info & VnThuQuan.net

		}
		#endregion

	}
}