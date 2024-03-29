﻿#region Related components
using System;
using System.IO;
using System.Text;
using System.Linq;
using System.Dynamic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Runtime.InteropServices;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using net.vieapps.Components.Security;
using net.vieapps.Components.Repository;
using net.vieapps.Components.Caching;
using net.vieapps.Components.Utility;
#endregion

namespace net.vieapps.Services.Books
{
	public class ServiceComponent : ServiceBase
	{
		public override string ServiceName => "Books";

		public override void Start(string[] args = null, bool initializeRepository = true, Action<IService> next = null)
		{
			// initialize caching storage
			Utility.Cache = new Cache($"VIEApps-Services-{this.ServiceName}", Components.Utility.Logger.GetLoggerFactory());

			// prepare URIs and paths
			Utility.FilesURI = this.GetHttpURI("Files", "https://fs.vieapps.net");
			while (Utility.FilesURI.EndsWith("/"))
				Utility.FilesURI = Utility.FilesURI.Left(Utility.FilesURI.Length - 1);
			Utility.FilesPath = this.GetPath("Books") ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data-files", "books");
			if (!Utility.FilesPath.IsEndsWith(Path.DirectorySeparatorChar))
				Utility.FilesPath += $"{Path.DirectorySeparatorChar}";

			// characters
			Utility.Chars = new List<string> { "0" };
			for (char @char = 'A'; @char <= 'Z'; @char++)
				Utility.Chars.Add($"{@char}");

			// prepare directories
			if (Directory.Exists(Utility.FilesPath))
			{
				void createDirectory(string directory, bool createMediaDirectory = true)
				{
					if (!Directory.Exists(directory))
						Directory.CreateDirectory(directory);
					if (createMediaDirectory)
					{
						var mediaDirectory = Path.Combine(directory, Definitions.MediaDirectory);
						if (!Directory.Exists(mediaDirectory))
							Directory.CreateDirectory(mediaDirectory);
					}
				}
				new[] { Utility.DirectoryOfDataFiles, Utility.DirectoryOfStatisticFiles, Utility.DirectoryOfContributedFiles }.ForEach(directory => createDirectory(directory, false));
				Utility.Chars.Select(@char => Path.Combine(Utility.DirectoryOfDataFiles, @char.ToLower()))
					.Concat(new[] { Path.Combine(Utility.DirectoryOfContributedFiles, "users"), Path.Combine(Utility.DirectoryOfContributedFiles, "crawlers"), Utility.DirectoryOfTempFiles, Utility.DirectoryOfTrashFiles })
					.ForEach(directory => createDirectory(directory));
			}

			// start the service
			base.Start(args, initializeRepository, _ =>
			{
				this.RegisterTimers(args);
				next?.Invoke(this);
			});
		}

		public override void Dispose(string[] args, bool available = true, bool disconnect = true, Action<IService> next = null)
			=> base.Dispose(args, available, disconnect, _ =>
			{
				if (this.Timers.Count > 0)
					this.FlushStatistics();
				next?.Invoke(this);
			});

		~ServiceComponent()
			=> this.Dispose();

		public override async Task<JToken> ProcessRequestAsync(RequestInfo requestInfo, CancellationToken cancellationToken = default)
		{
			var stopwatch = Stopwatch.StartNew();
			await this.WriteLogsAsync(requestInfo, $"Begin request ({requestInfo.Verb} {requestInfo.GetURI()})").ConfigureAwait(false);
			using (var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, this.CancellationTokenSource.Token))
				try
				{
					JToken json = null;
					switch (requestInfo.ObjectName.ToLower())
					{
						case "book":
							json = await this.ProcessBookAsync(requestInfo, cts.Token).ConfigureAwait(false);
							break;

						case "statistic":
							json = await this.ProcessStatisticAsync(requestInfo, cts.Token).ConfigureAwait(false);
							break;

						case "profile":
							json = await this.ProcessProfileAsync(requestInfo, cts.Token).ConfigureAwait(false);
							break;

						case "file":
							json = await this.ProcessFileAsync(requestInfo, cts.Token).ConfigureAwait(false);
							break;

						case "bookmarks":
							json = await this.ProcessBookmarksAsync(requestInfo, cts.Token).ConfigureAwait(false);
							break;

						case "crawl":
							this.CrawlBook(requestInfo);
							json = new JObject();
							break;

						case "categories":
							json = this.GetStatisticsOfCategories();
							break;

						case "authors":
							json = this.GetStatisticsOfAuthors(requestInfo.GetQueryParameter("char"));
							break;

						case "definitions":
							switch (requestInfo.GetObjectIdentity())
							{
								case "categories":
									json = Utility.Categories.List.Select(info => info.Name).ToJArray();
									break;

								case "book":
									json = this.GenerateFormControls<Book>();
									break;

								default:
									throw new InvalidRequestException($"The request is invalid [({requestInfo.Verb}): {requestInfo.GetURI()}]");
							}
							break;

						default:
							throw new InvalidRequestException($"The request is invalid [({requestInfo.Verb}): {requestInfo.GetURI()}]");
					}
					stopwatch.Stop();
					await this.WriteLogsAsync(requestInfo, $"Success response - Execution times: {stopwatch.GetElapsedTimes()}").ConfigureAwait(false);
					if (this.IsDebugResultsEnabled)
						await this.WriteLogsAsync(requestInfo, $"- Request: {requestInfo.ToString(this.JsonFormat)}" + "\r\n" + $"- Response: {json?.ToString(this.JsonFormat)}").ConfigureAwait(false);
					return json;
				}
				catch (Exception ex)
				{
					throw this.GetRuntimeException(requestInfo, ex, stopwatch);
				}
		}

		protected override Privileges Privileges
			=> new Privileges(true)
			{
				ContributiveRoles = new HashSet<string> { SystemRole.Authenticated.ToString() },
				DownloadableRoles = new HashSet<string> { SystemRole.Authenticated.ToString() }
			};

		Task<JObject> ProcessBookAsync(RequestInfo requestInfo, CancellationToken cancellationToken)
		{
			switch (requestInfo.Verb)
			{
				case "GET":
					return "search".IsEquals(requestInfo.GetObjectIdentity())
						? this.SearchBooksAsync(requestInfo, cancellationToken)
						: this.GetBookAsync(requestInfo, cancellationToken);

				case "POST":
					return this.CreateBookAsync(requestInfo, cancellationToken);

				case "PUT":
					return this.UpdateBookAsync(requestInfo, cancellationToken);

				case "DELETE":
					return this.DeleteBookAsync(requestInfo, cancellationToken);

				default:
					return Task.FromException<JObject>(new MethodNotAllowedException(requestInfo.Verb));
			}
		}

		#region Search books
		async Task<JObject> SearchBooksAsync(RequestInfo requestInfo, CancellationToken cancellationToken)
		{
			// check permissions
			if (!await this.IsViewerAsync(requestInfo.Session.User, "book", requestInfo.CorrelationID, cancellationToken).ConfigureAwait(false))
				throw new AccessDeniedException();

			// prepare
			var request = requestInfo.GetRequestExpando();

			var query = request.Get<string>("FilterBy.Query");

			var filter = request.Get<ExpandoObject>("FilterBy", null)?.ToFilterBy<Book>() ?? Filters<Book>.And(Filters<Book>.NotEquals("Status", "Inactive"));
			if (filter is FilterBys<Book> filterBy && filterBy.Children.FirstOrDefault(c => c is FilterBy<Book> cfilter && cfilter.Attribute.IsEquals("Status")) == null)
				filterBy.Children.Add(Filters<Book>.NotEquals("Status", "Inactive"));

			var sort = request.Get<ExpandoObject>("SortBy", null)?.ToSortBy<Book>();
			if (sort == null && string.IsNullOrWhiteSpace(query))
				sort = Sorts<Book>.Descending("LastUpdated");

			var pagination = request.Has("Pagination")
				? request.Get<ExpandoObject>("Pagination").GetPagination()
				: new Tuple<long, int, int, int>(-1, 0, 20, 1);

			var pageSize = pagination.Item3;
			var pageNumber = pagination.Item4;

			// check cache
			var cachedJson = string.IsNullOrWhiteSpace(query) ? await Utility.Cache.GetAsync<string>(this.GetCacheKeyOfObjectsJson(filter, sort, pageSize, pageNumber), cancellationToken).ConfigureAwait(false) : null;
			if (!string.IsNullOrWhiteSpace(cachedJson))
				return JObject.Parse(cachedJson);

			// prepare pagination
			var totalRecords = pagination.Item1 > -1
				? pagination.Item1
				: string.IsNullOrWhiteSpace(query)
					? await Book.CountAsync(filter, this.GetCacheKeyOfTotalObjects(filter, sort), cancellationToken).ConfigureAwait(false)
					: await Book.CountAsync(query, filter, cancellationToken).ConfigureAwait(false);


			var totalPages = new Tuple<long, int>(totalRecords, pageSize).GetTotalPages();
			if (totalPages > 0 && pageNumber > totalPages)
				pageNumber = totalPages;

			// search
			var objects = totalRecords > 0
				? string.IsNullOrWhiteSpace(query)
					? await Book.FindAsync(filter, sort, pageSize, pageNumber, this.GetCacheKey(filter, sort, pageSize, pageNumber), cancellationToken).ConfigureAwait(false)
					: await Book.SearchAsync(query, filter, null, pageSize, pageNumber, cancellationToken).ConfigureAwait(false)
				: new List<Book>();

			// build result
			pagination = new Tuple<long, int, int, int>(totalRecords, totalPages, pageSize, pageNumber);

			var result = new JObject
			{
				{ "FilterBy", filter.ToClientJson(query) },
				{ "SortBy", sort?.ToClientJson() },
				{ "Pagination", pagination.GetPagination() },
				{ "Objects", objects.Select(obj => obj.ToJson()).ToJArray() }
			};

			// update cache
			if (string.IsNullOrWhiteSpace(query))
				await Utility.Cache.SetAsync(this.GetCacheKeyOfObjectsJson(filter, sort, pageSize, pageNumber), result.ToString(Formatting.None), Utility.Cache.ExpirationTime / 2, cancellationToken).ConfigureAwait(false);

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
				if (!await this.IsSystemAdministratorAsync(requestInfo).ConfigureAwait(false))
					throw new AccessDeniedException();
			}

			// check permission on create new
			else if (!await this.IsContributorAsync(requestInfo.Session.User, "book", requestInfo.CorrelationID, cancellationToken).ConfigureAwait(false))
				throw new AccessDeniedException();

			// create new
			var book = requestInfo.GetBodyJson().Copy<Book>();
			await Book.CreateAsync(book, cancellationToken).ConfigureAwait(false);
			return book.ToJson();
		}
		#endregion

		#region Get a book & update related (counters, chapter, files, ...)
		async Task<JObject> GetBookAsync(RequestInfo requestInfo, CancellationToken cancellationToken)
		{
			// get the book
			var objectIdentity = requestInfo.GetObjectIdentity();

			var objectID = !string.IsNullOrWhiteSpace(objectIdentity) && objectIdentity.IsValidUUID()
				? objectIdentity
				: requestInfo.GetQueryParameter("x-object-id") ?? requestInfo.GetQueryParameter("object-id") ?? requestInfo.GetQueryParameter("book-id") ?? requestInfo.GetQueryParameter("id");

			var book = await Book.GetAsync<Book>(objectID, cancellationToken).ConfigureAwait(false);
			if (book == null)
				throw new InformationNotFoundException();

			// load from JSON file if has no chapter
			Book bookJson = null;
			if (objectID.Equals(objectIdentity) || "files".Equals(objectIdentity) || "brief-info".Equals(objectIdentity) || requestInfo.Query.ContainsKey("chapter"))
			{
				bookJson = await book.GetBookAsync(cancellationToken).ConfigureAwait(false);
				if (!book.SourceUrl.IsEquals(bookJson.SourceUrl))
				{
					book.SourceUrl = bookJson.SourceUrl;
					await Book.UpdateAsync(book, true, cancellationToken).ConfigureAwait(false);
				}

				if (!book.TotalChapters.Equals(bookJson.Chapters.Count))
				{
					book.TotalChapters = bookJson.Chapters.Count;
					await Book.UpdateAsync(book, true, cancellationToken).ConfigureAwait(false);
				}
			}

			// counters
			if ("counters".IsEquals(objectIdentity))
			{
				// update counters
				var result = await this.UpdateCounterAsync(book, requestInfo.GetParameter("x-action") ?? requestInfo.GetParameter("action") ?? "View", cancellationToken).ConfigureAwait(false);

				// send update message
				await this.SendUpdateMessageAsync(new UpdateMessage
				{
					DeviceID = "*",
					ExcludedDeviceID = requestInfo.Session.DeviceID,
					Type = $"{this.ServiceName}#Book#Counters",
					Data = result
				}, cancellationToken).ConfigureAwait(false);

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

				return new JObject
				{
					{ "ID", book.ID },
					{ "Chapter", chapter },
					{ "Content", bookJson.Chapters.Count > 0 ? book.NormalizeMediaURIs(bookJson.Chapters[chapter - 1]) : "" }
				};
			}

			// brief information
			else if ("brief-info".IsEquals(objectIdentity))
				return new JObject
				{
					{ "ID", bookJson.ID },
					{ "PermanentID", bookJson.GetPermanentID() },
					{ "Category", bookJson.Category },
					{ "Title", bookJson.Title },
					{ "Author", bookJson.Author },
					{ "Name", bookJson.Name }
				};

			// generate ebook files
			else if ("files".IsEquals(objectIdentity))
				return this.GenerateFiles(bookJson ?? book);

			// re-crawl
			else if ("recrawl".IsEquals(objectIdentity))
			{
				var sourceUrl = requestInfo.Query.ContainsKey("url") ? requestInfo.Query["url"] : null;
				var fullRecrawl = requestInfo.Query.ContainsKey("full") && "true".IsEquals(requestInfo.Query["full"]);
				this.ReCrawlBook(book, sourceUrl, fullRecrawl);
				return new JObject();
			}

			// book information
			else
				return book.ToJson(false, json =>
				{
					json["TOCs"] = bookJson.TOCs.ToJArray(toc => new JValue(UtilityService.RemoveTags(toc)));
					if (book.TotalChapters < 2)
						json["Body"] = bookJson.Chapters.Count > 0 ? book.NormalizeMediaURIs(bookJson.Chapters[0]) : "";
				});
		}

		async Task<JObject> UpdateCounterAsync(Book book, string action, CancellationToken cancellationToken = default)
		{
			// get and update
			var counter = book.Counters.FirstOrDefault(c => c.Type.IsEquals(action));
			if (counter != null)
			{
				// update counters
				counter.Total++;
				counter.Week = counter.LastUpdated.IsInCurrentWeek() ? counter.Week + 1 : 1;
				counter.Month = counter.LastUpdated.IsInCurrentMonth() ? counter.Month + 1 : 1;
				counter.LastUpdated = DateTime.Now;

				// reset counter of download
				if (!"download".IsEquals(action))
				{
					var downloadCounter = book.Counters.FirstOrDefault(c => c.Type.IsEquals("download"));
					if (downloadCounter != null)
					{
						if (!downloadCounter.LastUpdated.IsInCurrentWeek())
							downloadCounter.Week = 0;
						if (!downloadCounter.LastUpdated.IsInCurrentMonth())
							downloadCounter.Month = 0;
						downloadCounter.LastUpdated = DateTime.Now;
					}
				}

				// update database
				await Book.UpdateAsync(book, true, cancellationToken).ConfigureAwait(false);
			}

			// return data
			return new JObject
			{
				{ "ID", book.ID },
				{ "Counters", book.Counters.ToJArray(c => c.ToJson()) }
			};
		}
		#endregion

		#region Update a book
		async Task<JObject> UpdateBookAsync(RequestInfo requestInfo, CancellationToken cancellationToken)
		{
			// check permissions
			if (!await this.IsEditorAsync(requestInfo.Session.User, "book", requestInfo.CorrelationID, cancellationToken).ConfigureAwait(false))
				throw new AccessDeniedException();

			// prepare
			var book = await Book.GetAsync<Book>(requestInfo.GetObjectIdentity(), cancellationToken).ConfigureAwait(false);
			if (book == null)
				throw new InformationNotFoundException();

			// prepare old values
			var bookJson = await book.GetBookAsync(cancellationToken).ConfigureAwait(false);
			var name = book.Name;
			var category = book.Category;
			var author = book.Author;

			// old files to delete
			var oldFilePath = Utility.GetBookFilePath(name);
			var beDeletedFiles = new List<FileInfo>
			{
				new FileInfo(oldFilePath + ".epub"),
				new FileInfo(oldFilePath + ".mobi")
			};

			// update information
			var body = requestInfo.GetBodyExpando();
			var tocs = body.Get("TOCs", "").Replace("\t", "").Replace("\r", "").Trim().ToArray("\n");
			book.CopyFrom(body, "ID,TOCs,Cover".ToHashSet());

			// update JSON file
			if (bookJson != null)
			{
				bookJson.Title = book.Title;
				bookJson.Original = book.Original ?? "";
				bookJson.Author = book.Author ?? "";
				bookJson.Publisher = book.Publisher ?? "";
				bookJson.Producer = book.Producer ?? "";
				bookJson.Translator = book.Translator ?? "";
				bookJson.Category = book.Category ?? Utility.Categories[0].Name;
				bookJson.LastUpdated = DateTime.Now;

				// update TOCs
				if (tocs.Length.Equals(bookJson.TOCs.Count))
				{
					bookJson.TOCs = tocs.ToList();
					for (var index = 0; index < bookJson.Chapters.Count; index++)
						bookJson.Chapters[index] = "<h1>" + bookJson.TOCs[index] + "</h1>" + bookJson.Chapters[index].Substring(bookJson.Chapters[index].PositionOf("</h1>") + 5);
				}
				else
					tocs = bookJson.TOCs.ToArray();

				// update cover image
				var cover = body.Get("Cover", "");
				if (!string.IsNullOrWhiteSpace(cover) && cover.IsStartsWith(Definitions.MediaURI) && !cover.IsEquals(book.Cover))
				{
					if (!string.IsNullOrWhiteSpace(book.Cover) && book.Cover.IsStartsWith(Definitions.MediaURI))
						beDeletedFiles.Add(new FileInfo(Path.Combine(Utility.GetBookDirectory(name), Definitions.MediaDirectory, book.Cover.Replace(Definitions.MediaURI, bookJson.GetPermanentID() + "-"))));
					bookJson.Cover = book.Cover = cover;
				}

				// chapters
				book.TotalChapters = bookJson.TotalChapters = bookJson.Chapters.Count;

				// update JSON file
				await bookJson.ToJson(false, false, json =>
				{
					new[] { "Counters", "RatingPoints", "LastUpdated" }.ForEach(attr => json.Remove(attr));
					json["PermanentID"] = bookJson.GetPermanentID();
					json["Credits"] = bookJson.Credits ?? "";
					json["Stylesheet"] = bookJson.Stylesheet ?? "";
					json["TOCs"] = bookJson.TOCs.ToJArray();
					json["Chapters"] = bookJson.Chapters.ToJArray();
				}).ToString(Formatting.Indented).ToBytes().ToMemoryStream().SaveAsTextAsync(Utility.GetBookFilePath(bookJson.Name) + ".json").ConfigureAwait(false);

				// old files
				if (!name.IsEquals(bookJson.Name))
				{
					if (File.Exists(oldFilePath + ".json"))
					{
						var trashFilePath = Path.Combine(Utility.DirectoryOfTrashFiles, UtilityService.GetNormalizedFilename(name));
						if (File.Exists($"{trashFilePath}.json"))
							File.Delete($"{trashFilePath}.json");
						File.Move($"{oldFilePath}.json", $"{trashFilePath}.json");
					}
					UtilityService.MoveFiles(Path.Combine(Utility.GetBookDirectory(name), Definitions.MediaDirectory), Path.Combine(Utility.GetBookDirectory(bookJson.Name), Definitions.MediaDirectory), bookJson.GetPermanentID() + "-*.*");
				}
				else
				{
					beDeletedFiles.Add(new FileInfo(book.GetFilePath() + ".epub"));
					beDeletedFiles.Add(new FileInfo(book.GetFilePath() + ".mobi"));
				}
			}
			else
				tocs = book.TOCs.ToArray();

			// update database
			book.LastUpdated = DateTime.Now;
			await Book.UpdateAsync(book, requestInfo.Session.User.ID, cancellationToken).ConfigureAwait(false);

			// update statistics
			if (!category.IsEquals(book.Category))
			{
				var statistic = Utility.Categories[category];
				if (statistic != null)
					statistic.Counters--;
				statistic = Utility.Categories[book.Category];
				if (statistic != null)
					statistic.Counters++;
			}
			if (!author.IsEquals(book.Author))
			{
				var statistic = Utility.Authors[author];
				if (statistic != null)
					statistic.Counters--;
				statistic = Utility.Authors[book.Author];
				if (statistic != null)
					statistic.Counters++;
			}

			// clear related cached & send update message
			await Task.WhenAll(
				this.ClearRelatedCacheAsync(book, !category.IsEquals(book.Category) ? category : null, !author.IsEquals(book.Author) ? author : null, cancellationToken),
				this.SendUpdateMessageAsync(new UpdateMessage
				{
					Type = $"{this.ServiceName}#Book#Update",
					DeviceID = "*",
					Data = (bookJson ?? book).ToJson(false, json => json["TOCs"] = tocs.ToJArray())
				}, cancellationToken)
			).ConfigureAwait(false);

			// move old files to trash
			beDeletedFiles
				.Where(file => file.Exists)
				.ForEach(file =>
				{
					var path = ".json|.epub|.mobi".IsContains(file.Extension)
						? Path.Combine(Utility.DirectoryOfTrashFiles, file.Name)
						: Path.Combine(Utility.DirectoryOfTrashFiles, Definitions.MediaDirectory, file.Name);
					if (File.Exists(path))
						File.Delete(path);
					File.Move(file.FullName, path);
				});

#if DEBUG || UPDATELOGS
			body.Remove("Cover");
			await this.WriteLogsAsync(requestInfo.CorrelationID, new List<string>
			{
				$"Update a book [{book.Name}]",
				$"Request =>\r\n{body.ToJObject().ToString(Formatting.Indented)}",
				$"Be deleted files (be moved into trash): {(beDeletedFiles.Count < 1 ? "None" : beDeletedFiles.Count + " file(s)\r\n" + string.Join("\r\n=> ", beDeletedFiles.Select(file => file.FullName)))}"
			});
#endif

			// return
			return new JObject();
		}
		#endregion

		#region Delete a book
		async Task<JObject> DeleteBookAsync(RequestInfo requestInfo, CancellationToken cancellationToken)
		{
			// check permissions
			if (!await this.IsModeratorAsync(requestInfo.Session.User, "book", requestInfo.CorrelationID, cancellationToken).ConfigureAwait(false))
				throw new AccessDeniedException();

			// prepare
			var book = await Book.GetAsync<Book>(requestInfo.GetObjectIdentity(), cancellationToken).ConfigureAwait(false);
			if (book == null)
				throw new InformationNotFoundException();

			var path = book.GetBookDirectory();
			var filename = UtilityService.GetNormalizedFilename(book.Name);
			var bookJson = await book.GetBookAsync().ConfigureAwait(false);

			// delete from database
			await Book.DeleteAsync<Book>(book.ID, requestInfo.Session.User.ID, cancellationToken).ConfigureAwait(false);

			// move files
			UtilityService.MoveFiles(path, Utility.DirectoryOfTrashFiles, filename + ".*", true);
			if (!string.IsNullOrWhiteSpace(bookJson.GetPermanentID()))
				UtilityService.MoveFiles(Path.Combine(path, Definitions.MediaDirectory), Path.Combine(Utility.DirectoryOfTrashFiles, Definitions.MediaDirectory), bookJson.GetPermanentID() + "-*.*", true);

			// clear related cached & send update message
			await Task.WhenAll(
				this.ClearRelatedCacheAsync(book, null, null, cancellationToken),
				this.UpdateStatiscticsAsync(book, true, cancellationToken),
				this.SendUpdateMessageAsync(new UpdateMessage
				{
					Type = $"{this.ServiceName}#Book#Delete",
					DeviceID = "*",
					Data = new JObject
					{
						{ "ID", book.ID },
						{ "Category", book.Category },
						{ "Author", book.Author }
					}
				}, cancellationToken)
			).ConfigureAwait(false);

			// return
			return new JObject();
		}
		#endregion

		#region Crawl a book
		void CrawlBook(RequestInfo requestInfo)
			=> Task.Run(async () =>
			{
				try
				{
					await this.CrawlBookAsync(requestInfo).ConfigureAwait(false);
				}
				catch (Exception ex)
				{
					await this.WriteLogsAsync(requestInfo.CorrelationID, $"Error occurred while crawling => {ex.Message}", ex);
				}
			}).ConfigureAwait(false);

		async Task CrawlBookAsync(RequestInfo requestInfo)
		{
			// check permissions
			if (!await this.IsContributorAsync(requestInfo.Session.User, "book", requestInfo.CorrelationID).ConfigureAwait(false))
				throw new AccessDeniedException();

			// prepare
			var correlationID = requestInfo.CorrelationID ?? UtilityService.NewUUID;
			var sourceUrl = requestInfo.Query.ContainsKey("url") ? requestInfo.Query["url"] : null;
			var parser = string.IsNullOrWhiteSpace(sourceUrl)
				? null
				: sourceUrl.IsContains("vnthuquan.net")
					? new Parsers.Books.VnThuQuan() as IBookParser
					: sourceUrl.IsContains("isach.info")
						? new Parsers.Books.ISach() as IBookParser
						: null;

			if (parser == null)
			{
				await this.WriteLogsAsync(correlationID, $"No parser is matched for re-crawling a book [{sourceUrl}]").ConfigureAwait(false);
				return;
			}

			// crawl
			parser.SourceUrl = sourceUrl;
			parser.Contributor = requestInfo.Query.ContainsKey("contributor") ? requestInfo.Query["contributor"] : "";
			try
			{
				var crawler = new Crawler
				{
					CorrelationID = correlationID,
					UpdateLogs = (log, ex, updateCentralizedLogs) => this.WriteLogs(correlationID, log, ex)
				};

				await crawler.CrawlAsync(
					parser,
					Utility.DirectoryOfTempFiles,
					async (book, token) => await this.OnBookUpdatedAsync(book, token).ConfigureAwait(false),
					parser is Parsers.Books.ISach
						? UtilityService.GetAppSetting("Books:Crawler-ISachParalell", "false").CastAs<bool>()
						: UtilityService.GetAppSetting("Books:Crawler-VnThuQuanParalell", "true").CastAs<bool>(),
					this.CancellationTokenSource.Token
				).ConfigureAwait(false);

				await this.WriteLogsAsync(crawler.CorrelationID,
					$"The crawler is completed [{parser.Title}]" + "\r\n" +
					"--------------------------------------------------------------" + "\r\n" +
					string.Join("\r\n", crawler.Logs) + "\r\n"
					+ "--------------------------------------------------------------", null, this.ServiceName, "Crawlers"
				);
			}
			catch (Exception ex)
			{
				await this.WriteLogsAsync(correlationID, $"Error occurred while crawling a book [{parser.Title} - {parser.SourceUrl}]", ex).ConfigureAwait(false);
			}
		}

		void ReCrawlBook(Book book, string sourceUrl = null, bool fullRecrawl = false)
			=> Task.Run(() => this.ReCrawlBookAsync(book, sourceUrl, fullRecrawl)).ConfigureAwait(false);

		async Task ReCrawlBookAsync(Book book, string sourceUrl = null, bool fullRecrawl = false)
		{
			// check
			if (book == null)
				return;

			var filename = UtilityService.GetNormalizedFilename(book.Name) + ".json";
			var filepath = Path.Combine(Utility.DirectoryOfTempFiles, filename);
			if (File.Exists(filepath))
				return;

			// prepare
			if (File.Exists(Path.Combine(book.GetBookDirectory(), filename)))
				File.Copy(Path.Combine(book.GetBookDirectory(), filename), filepath, true);
			else
				await book.ToJson().ToString(Formatting.Indented).ToBytes().ToMemoryStream().SaveAsTextAsync(filepath).ConfigureAwait(false);

			var json = JObject.Parse(await new FileInfo(filepath).ReadAsTextAsync().ConfigureAwait(false));
			if (string.IsNullOrWhiteSpace(sourceUrl))
				sourceUrl = json.Get<string>("SourceUrl") ?? json.Get<string>("SourceUri");

			var parser = string.IsNullOrWhiteSpace(sourceUrl)
				? null
				: sourceUrl.IsContains("vnthuquan.net")
					? json.Copy<Parsers.Books.VnThuQuan>() as IBookParser
					: sourceUrl.IsContains("isach.info")
						? json.Copy<Parsers.Books.ISach>() as IBookParser
						: null;

			// crawl
			var correlationID = UtilityService.NewUUID;
			if (parser != null)
				try
				{
					// assign source url
					if (string.IsNullOrWhiteSpace(parser.SourceUrl))
						parser.SourceUrl = sourceUrl;

					// full re-crawl
					if (fullRecrawl)
					{
						parser.Original = null;
						parser.Credits = null;
						parser.Translator = null;
						parser.Cover = null;
						parser.TOCs = new List<string>();
						parser.Chapters = new List<string>();
					}

					// re-parse the book
					else if (parser.TOCs == null || parser.TOCs.Count < 1 || parser.TOCs[0].Equals(""))
					{
						await parser.ParseAsync().ConfigureAwait(false);
						if (string.IsNullOrWhiteSpace(parser.PermanentID) || !parser.PermanentID.IsValidUUID())
							parser.PermanentID = (parser.Title + " - " + parser.Author).Trim().ToLower().GetMD5();
					}

					// fetch
					var crawler = new Crawler
					{
						CorrelationID = correlationID,
						UpdateLogs = (log, ex, updateCentralizedLogs) => this.WriteLogs(correlationID, log, ex)
					};

					await crawler.CrawlAsync(
						parser,
						Utility.DirectoryOfTempFiles,
						async (b, token) => await this.OnBookUpdatedAsync(b, token).ConfigureAwait(false),
						parser is Parsers.Books.ISach
							? UtilityService.GetAppSetting("Books:Crawler-ISachParalell", "false").CastAs<bool>()
							: UtilityService.GetAppSetting("Books:Crawler-VnThuQuanParalell", "true").CastAs<bool>(),
						this.CancellationTokenSource.Token,
						fullRecrawl ? false : true
					).ConfigureAwait(false);

					await this.WriteLogsAsync(crawler.CorrelationID,
						$"The crawler is completed to re-crawl the book [{parser.Title}]" + "\r\n" +
						"--------------------------------------------------------------" + "\r\n" +
						string.Join("\r\n", crawler.Logs) + "\r\n"
						+ "--------------------------------------------------------------", null, this.ServiceName, "Crawlers"
					);
				}
				catch (Exception ex)
				{
					await this.WriteLogsAsync(correlationID, $"Error occurred while re-crawling a book [{parser.Title} - {parser.SourceUrl}]", ex).ConfigureAwait(false);
				}
			else
				await this.WriteLogsAsync(correlationID, $"No parser is matched for re-crawling a book [{sourceUrl}]").ConfigureAwait(false);

			// cleanup
			if (File.Exists(filepath))
				File.Delete(filepath);
		}
		#endregion

		async Task<JObject> ProcessStatisticAsync(RequestInfo requestInfo, CancellationToken cancellationToken)
		{
			switch ((requestInfo.GetObjectIdentity() ?? "").ToLower())
			{
				case "status":
					return this.GetStatisticsOfServiceStatus();

				case "categories":
					return this.GetStatisticsOfCategories();

				case "authors":
					return this.GetStatisticsOfAuthors(requestInfo.GetQueryParameter("char"));

				default:
					await this.SendStatisticsAsync(requestInfo.Session.DeviceID, cancellationToken).ConfigureAwait(false);
					return new JObject();
			}
		}

		#region Send statistics
		async Task SendStatisticsAsync(string deviceID, CancellationToken cancellationToken = default)
		{
			// prepare
			var messages = new List<BaseMessage>
			{
				new BaseMessage
				{
					Type = $"{this.ServiceName}#Statistic#Status",
					Data = this.GetStatisticsOfServiceStatus()
				},
				new BaseMessage
				{
					Type = $"{this.ServiceName}#Statistic#Categories",
					Data = this.GetStatisticsOfCategories()
				}
			};

			Utility.Chars.ForEach(@char =>
			{
				var data = this.GetStatisticsOfAuthors(@char);
				data["Char"] = @char;
				messages.Add(new BaseMessage
				{
					Type = $"{this.ServiceName}#Statistic#Authors",
					Data = data
				});
			});

			// send
			try
			{
				await this.SendUpdateMessagesAsync(messages, deviceID, null, cancellationToken).ConfigureAwait(false);
			}
			catch (Exception ex)
			{
				await this.WriteLogsAsync(UtilityService.NewUUID, "Error occurred while sending an update message of statistics", ex).ConfigureAwait(false);
			}
		}
		#endregion

		#region Get statistics
		JObject GetStatisticsOfServiceStatus()
			=> new JObject
			{
				{ "Total", Utility.Status.Count },
				{ "Objects", Utility.Status.ToJson() }
			};

		JObject GetStatisticsOfCategories()
			=> new JObject
			{
				{ "Total", Utility.Categories.Count },
				{ "Objects", Utility.Categories.ToJson() }
			};

		JObject GetStatisticsOfAuthors(string @char)
		{
			var authors = string.IsNullOrWhiteSpace(@char)
				? Utility.Authors.List.OrderBy(item => item.FirstChar).OrderBy(item => item.Name).ToList()
				: Utility.Authors.Find(@char).OrderBy(item => item.Name).ToList();

			return new JObject
			{
				{ "Total", authors.Count },
				{ "Objects", authors.ToJArray(a => a.ToJson()) }
			};
		}
		#endregion

		#region Flush & Re-compute statistics
		void FlushStatistics()
		{
			Utility.Status.Save(Utility.DirectoryOfStatisticFiles, "status.json");
			Utility.Categories.Save(Utility.DirectoryOfStatisticFiles, "categories.json");
			Utility.Authors.Save(Utility.DirectoryOfStatisticFiles, "authors-{0}.json", true);
		}

		async Task RecomputeStatistics(string correlationID)
		{
			var stopwatch = new Stopwatch();
			stopwatch.Start();

			// categories
			for (var index = 0; index < Utility.Categories.Count; index++)
			{
				var info = Utility.Categories[index];
				info.Counters = (int)await Book.CountAsync<Book>(Filters<Book>.Equals("Category", info.Name), null, null, this.CancellationTokenSource.Token).ConfigureAwait(false);
			}

			// authors
			Utility.Authors.Clear();
			var totalRecords = await Book.CountAsync(null, "", this.CancellationTokenSource.Token).ConfigureAwait(false);
			var totalPages = new Tuple<long, int>(totalRecords, 50).GetTotalPages();
			await this.WriteLogsAsync(correlationID, $"Total of {totalRecords} books need to process");

			var pageNumber = 0;
			while (pageNumber < totalPages)
			{
				pageNumber++;
				await this.WriteLogsAsync(correlationID, $"Process page {pageNumber} / {totalPages}");

				(await Book.FindAsync(null, Sorts<Book>.Ascending("LastUpdated"), 50, pageNumber, null, this.CancellationTokenSource.Token).ConfigureAwait(false))
					.Where(book => !string.IsNullOrWhiteSpace(book.Author))
					.ForEach(book =>
					{
						book.Author.GetAuthorNames().ForEach(a =>
						{
							var author = Utility.Authors[a];
							if (author != null)
								author.Counters++;
							else
								Utility.Authors.Add(new StatisticInfo
								{
									Name = a,
									Counters = 1,
									FirstChar = a.GetAuthorName().GetFirstChar().ToUpper()
								});
						});
					});
				await Task.Delay(UtilityService.GetRandomNumber(345, 678)).ConfigureAwait(false);
			}

			// status
			Utility.Status.Clear();
			Utility.Status.Add(new StatisticInfo
			{
				Name = "Books",
				Counters = totalRecords.CastAs<int>()
			});
			Utility.Status.Add(new StatisticInfo
			{
				Name = "Authors",
				Counters = Utility.Authors.Count
			});

			// flush into file
			this.FlushStatistics();

			// send update messages
			await this.SendStatisticsAsync("*", this.CancellationTokenSource.Token).ConfigureAwait(false);

			stopwatch.Stop();
			await this.WriteLogsAsync(UtilityService.NewUUID, $"Re-compute statistics is completed - Execution times: {stopwatch.GetElapsedTimes()}").ConfigureAwait(false);
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

				default:
					return Task.FromException<JObject>(new MethodNotAllowedException(requestInfo.Verb));
			}
		}

		#region Create an account profile
		async Task<JObject> CreateProfileAsync(RequestInfo requestInfo, CancellationToken cancellationToken)
		{
			// prepare identity
			var id = requestInfo.GetObjectIdentity() ?? requestInfo.Session.User.ID;

			// check permission
			var isSystemAdministrator = await this.IsSystemAdministratorAsync(requestInfo).ConfigureAwait(false);
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
			await Account.CreateAsync(account, cancellationToken).ConfigureAwait(false);
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
				gotRights = await this.IsSystemAdministratorAsync(requestInfo).ConfigureAwait(false);
			if (!gotRights)
				throw new AccessDeniedException();

			// get information
			var account = await Account.GetAsync<Account>(id, cancellationToken).ConfigureAwait(false);

			// special: not found
			if (account == null)
			{
				if (id.Equals(requestInfo.Session.User.ID))
				{
					account = new Account
					{
						ID = id
					};
					await Account.CreateAsync(account).ConfigureAwait(false);
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
				gotRights = await this.IsSystemAdministratorAsync(requestInfo).ConfigureAwait(false);
			if (!gotRights)
				throw new AccessDeniedException();

			// get existing information
			var account = await Account.GetAsync<Account>(id, cancellationToken).ConfigureAwait(false);
			if (account == null)
				throw new InformationNotFoundException();

			// update
			account.CopyFrom(requestInfo.GetBodyJson(), "ID,Title".ToHashSet(), _ => account.Title = null);
			await Account.UpdateAsync(account, requestInfo.Session.User.ID, cancellationToken).ConfigureAwait(false);
			return account.ToJson();
		}
		#endregion

		Task<JObject> ProcessFileAsync(RequestInfo requestInfo, CancellationToken cancellationToken)
			=> requestInfo.Verb.IsEquals("POST") && requestInfo.Extra != null && requestInfo.Extra.ContainsKey("x-convert")
				? this.CopyFilesAsync(requestInfo, cancellationToken)
				: Task.FromException<JObject>(new MethodNotAllowedException(requestInfo.Verb));

		#region Copy files of a book
		async Task<JObject> CopyFilesAsync(RequestInfo requestInfo, CancellationToken cancellationToken)
		{
			// prepare
			if (!await this.IsSystemAdministratorAsync(requestInfo).ConfigureAwait(false))
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
			var destination = Utility.DirectoryOfDataFiles + @"\" + name.GetFirstChar() + @"\";
			var filename = name + ".json";

			// copy file
			if (File.Exists(source + filename))
			{
				File.Copy(source + filename, destination + filename, true);
				var permanentID = Utility.GetBookAttribute(source + filename, "PermanentID");
				(await UtilityService.GetFilesAsync(source + Definitions.MediaDirectory, permanentID + "-*.*").ConfigureAwait(false))
					.ForEach(file => File.Copy(file.FullName, destination + Definitions.MediaDirectory + @"\" + file.Name, true));
			}

			return new JObject
			{
				{ "Status", "OK" }
			};
		}
		#endregion

		#region Generate e-book files of a book
		JObject GenerateFiles(Book book)
		{
			// run a task to generate files
			if (book != null)
				Task.Run(() => this.GenerateFilesAsync(book)).ConfigureAwait(false);

			// return status
			return new JObject
			{
				{ "Status", "OK" }
			};
		}

		async Task GenerateFilesAsync(Book book)
		{
			// prepare
			var correlationID = UtilityService.NewUUID;
			var filePath = Path.Combine(book.GetBookDirectory(), UtilityService.GetNormalizedFilename(book.Name));
			var flag = "Files-" + filePath.ToLower().GetMD5();
			if (await Utility.Cache.ExistsAsync(flag, this.CancellationTokenSource.Token).ConfigureAwait(false))
			{
				if (this.IsDebugLogEnabled)
					await this.WriteLogsAsync(correlationID, $"Other instance is currently generating e-book files, please wait for completed... [{flag}]", null, this.ServiceName, "Generators").ConfigureAwait(false);
				return;
			}

			// generate files
			if (!File.Exists(filePath + ".epub") || !File.Exists(filePath + ".mobi"))
			{
				// update flag
				await Task.WhenAll
				(
					Utility.Cache.SetAsync(flag, book.ID, 7, this.CancellationTokenSource.Token),
					this.IsDebugLogEnabled ? this.WriteLogsAsync(correlationID, $"Start to generate e-book files [{book.Name} => {flag}]", null, this.ServiceName, "Generators") : Task.CompletedTask
				).ConfigureAwait(false);

				// prepare
				var status = new Dictionary<string, bool>
				{
					{ "epub",  File.Exists(filePath + ".epub") },
					{ "mobi",  File.Exists(filePath + ".mobi") }
				};
				var generatingTasks = new List<Task>();

				if (!status["epub"])
					generatingTasks.Add(Task.Run(() => this.GenerateEpubFile(
						book,
						correlationID,
						p => status["epub"] = true,
						ex =>
						{
							status["epub"] = true;
							this.WriteLogs(correlationID, "Error occurred while generating EPUB file", ex, this.ServiceName, "Generators");
						}
					)));

				if (!status["mobi"])
					generatingTasks.Add(Task.Run(() => this.GenerateMobiFile(
						book,
						correlationID,
						p => status["mobi"] = true,
						ex =>
						{
							status["mobi"] = true;
							this.WriteLogs(correlationID, "Error occurred while generating MOBI file", ex, this.ServiceName, "Generators");
						}
					)));

				// wait for all tasks are completed
				while (!status["epub"] || !status["mobi"])
					await Task.Delay(UtilityService.GetRandomNumber(234, 567), this.CancellationTokenSource.Token).ConfigureAwait(false);

				// update innformation
				await Task.WhenAll(
					Utility.Cache.RemoveAsync(flag, this.CancellationTokenSource.Token),
					this.IsDebugLogEnabled ? this.WriteLogsAsync(correlationID, $"E-book files are generated [{book.Name} => {flag}]", null, this.ServiceName, "Generators") : Task.CompletedTask
				).ConfigureAwait(false);
			}

			// send the update message
			await this.SendUpdateMessageAsync(new UpdateMessage
			{
				Type = $"{this.ServiceName}#Book#Files",
				DeviceID = "*",
				Data = new JObject
				{
					{ "ID", book.ID },
					{ "Files", book.GetFiles() }
				}
			}, this.CancellationTokenSource.Token).ConfigureAwait(false);
		}

		public string CreditsApp => $"VIEApps NGX {this.ServiceName}";

		public string CreditsInAppContributor => $"<p>Chuyển đổi và đóng gói bằng <b>{this.CreditsApp}</b></p>";

		public string PageBreak => "<mbp:pagebreak/>";

		public string GetTOCItem(Book book, int index)
		{
			var toc = book.TOCs != null && index < book.TOCs.Count
				? UtilityService.RemoveTag(UtilityService.RemoveTag(book.TOCs[index], "a"), "p")
				: "";

			return string.IsNullOrWhiteSpace(toc)
				? (index + 1).ToString()
				: toc.GetNormalized().Replace("{0}", (index + 1).ToString());
		}

		void GenerateEpubFile(Book book, string correlationID = null, Action<string> onCompleted = null, Action<Exception> onError = null)
		{
			try
			{
				var stopwatch = Stopwatch.StartNew();
				if (this.IsDebugLogEnabled)
					this.WriteLogs(correlationID, $"Start to generate EPUB file [{book.Name}]", null, this.ServiceName, "Generators");

				// meta data
				var epub = new Components.Utility.Epub.Document();
				epub.AddBookIdentifier(new Guid(book.ID).ToString("D"));
				epub.AddLanguage(book.Language);
				epub.AddTitle(book.Title);
				epub.AddAuthor(book.Author);
				epub.AddContributor(this.CreditsInAppContributor.Replace("\n<p>", " - ").Replace("\n", "").Replace("<p>", "").Replace("</p>", "").Replace("<b>", "").Replace("</b>", "").Replace("<i>", "").Replace("</i>", ""));
				if (!string.IsNullOrWhiteSpace(book.Translator))
					epub.AddTranslator(book.Translator);
				if (!string.IsNullOrWhiteSpace(book.Publisher))
					epub.AddPublisher(book.Publisher);

				if (!string.IsNullOrWhiteSpace(book.Original))
					epub.AddMetaItem("book:Original", book.Original);
				if (!string.IsNullOrWhiteSpace(book.Producer))
					epub.AddMetaItem("book:Producer", book.Producer);
				if (!string.IsNullOrWhiteSpace(book.Source))
					epub.AddMetaItem("book:Source", book.Source);
				if (!string.IsNullOrWhiteSpace(book.SourceUrl))
					epub.AddMetaItem("book:SourceUrl", book.SourceUrl);

				// CSS stylesheet
				var stylesheet = !string.IsNullOrWhiteSpace(book.Stylesheet)
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
					var coverData = new FileInfo(book.Cover.NormalizeMediaFilePaths(book)).ReadAsBinary();
					if (coverData != null && coverData.Length > 0)
					{
						var coverId = epub.AddImageData("cover.jpg", coverData);
						epub.AddMetaItem("cover", coverId);
					}
				}

				// pages & nav points
				var pageTemplate = @"<!DOCTYPE html>
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
				var info = "<p class=\"author\">" + book.Author + "</p>" + "<h1 class=\"title\">" + book.Title + "</h1>";

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
					+ "\r" + "<hr/>" + this.CreditsInAppContributor + "</div>";

				epub.AddXhtmlData("page0.xhtml", pageTemplate.Replace("{0}", "Info").Replace("{1}", info.Replace("<p>", "\r" + "<p>")));

				// chapters
				var navs = book.TOCs?.Select((toc, index) => this.GetTOCItem(book, index)).ToList() ?? new List<string>();
				book.Chapters?.Select(chapter => chapter.NormalizeMediaFilePaths(book)).ForEach((chapter, index) =>
				{
					var start = chapter.PositionOf("<img");
					var end = -1;
					while (start > -1)
					{
						start = chapter.PositionOf("src=", start + 1) + 5;
						var @char = chapter[start - 1];
						end = chapter.PositionOf(@char.ToString(), start + 1);

						var image = chapter.Substring(start, end - start);
						var imageData = new FileInfo(image.Replace("file://", "")).ReadAsBinary();
						if (imageData != null && imageData.Length > 0)
							epub.AddImageData(image, imageData);

						start = chapter.PositionOf("<img", start + 1);
					}

					var name = string.Format("page{0}.xhtml", index + 1);
					epub.AddXhtmlData(name, pageTemplate.Replace("{0}", index < navs.Count ? navs[index] : book.Title).Replace("{1}", chapter.Replace("<p>", "\r" + "<p>")));
					if (book.Chapters.Count > 1)
						epub.AddNavPoint(index < navs.Count ? navs[index] : book.Title + " - " + (index + 1).ToString(), name, index + 1);
				});

				// save into file on disc
				var filePath = Path.Combine(book.GetBookDirectory(), UtilityService.GetNormalizedFilename(book.Name) + ".epub");
				epub.Generate(filePath);

				stopwatch.Stop();
				if (this.IsDebugLogEnabled)
					this.WriteLogs(correlationID, $"Generate EPUB file is completed [{book.Name}] - Execution times: {stopwatch.GetElapsedTimes()}", null, this.ServiceName, "Generators");

				// callback
				onCompleted?.Invoke(filePath);
			}
			catch (Exception ex)
			{
				onError?.Invoke(ex);
			}
		}

		void GenerateMobiFile(Book book, string correlationID = null, Action<string> onCompleted = null, Action<Exception> onError = null)
		{
			// file name & path
			var filename = book.ID;
			var filePath = book.GetBookDirectory() + Path.DirectorySeparatorChar.ToString();

			// generate
			try
			{
				var stopwatch = Stopwatch.StartNew();
				if (this.IsDebugLogEnabled)
					this.WriteLogs(correlationID, $"Start to generate MOBI file [{book.Name}]", null, this.ServiceName, "Generators");

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

				var content = "<!DOCTYPE html>" + "\n"
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
					+ "\n" + this.CreditsInAppContributor + "\n\n";

				var headingTags = "h1|h2|h3|h4|h5|h6".Split('|');
				var toc = "";
				var body = "";
				book.Chapters?.Select(chapter => chapter.NormalizeMediaFilePaths(book)).ForEach((chapter, index) =>
				{
					foreach (var tag in headingTags)
						chapter = chapter.Replace(StringComparison.OrdinalIgnoreCase, "<" + tag + ">", "\n<" + tag + ">").Replace(StringComparison.OrdinalIgnoreCase, "</" + tag + ">", "</" + tag + ">\n");
					chapter = chapter.Trim().Replace("</p><p>", "</p>\n<p>").Replace("\n\n", "\n");

					toc += book.Chapters.Count > 1 ? (!toc.Equals("") ? "\n" : "") + "<p><a href=\"#chapter" + (index + 1) + "\">" + this.GetTOCItem(book, index) + "</a></p>" : "";
					body += this.PageBreak + "\n"
						+ "<a name=\"chapter" + (index + 1) + "\"></a>" + "\n"
						+ chapter
						+ "\n\n";
				});

				if (!string.IsNullOrWhiteSpace(toc))
					content += this.PageBreak
						+ "\n"
						+ "<a name=\"toc\"></a>" + "\n"
						+ "<h1 class=\"toc\">" + (book.Language.Equals("en") ? "TABLE OF CONTENTS: " : "MỤC LỤC") + "</h1>" + "\n"
						+ toc
						+ "\n\n";

				content += body + "</body>\n" + "</html>";

				// geneate HTML file
				new[] { content }.SaveTo(Path.Combine(filePath, filename + ".html"), false);

				// prepare NCX
				if (book.TOCs != null && book.TOCs.Count > 0)
				{
					content = "<?xml version=\"1.0\" encoding=\"utf-8\"?>" + "\n"
						+ "<!DOCTYPE ncx PUBLIC \" -//NISO//DTD ncx 2005-1//EN\" \"http://www.daisy.org/z3986/2005/ncx-2005-1.dtd\">" + "\n"
						+ "<ncx xmlns=\"http://www.daisy.org/z3986/2005/ncx/\">" + "\n"
						+ "<head>" + "\n"
						+ "<meta name=\"dtb:uid\" content=\"urn:uuid:dbe" + new Guid(book.ID).ToString("D") + "\"/>" + "\n"
						+ "</head>" + "\n"
						+ "<docTitle><text>" + book.Title + @"</text></docTitle>" + "\n"
						+ "<docAuthor><text>" + book.Author + @"</text></docAuthor>" + "\n"
						+ "<navMap>";

					for (var index = 0; index < book.TOCs.Count; index++)
						content += "<navPoint id=\"navid" + (index + 1) + "\" playOrder=\"" + (index + 1) + "\">" + "\n"
							+ "<navLabel>" + "\n"
							+ "<text>" + this.GetTOCItem(book, index) + "</text>" + "\n"
							+ "</navLabel>" + "\n"
							+ "<content src=\"" + filename + ".html#chapter" + (index + 1) + "\"/>" + "\n"
							+ "</navPoint>" + "\n";

					content += "</navMap>" + "\n" + "</ncx>";

					// geneate NCX file
					new[] { content }.SaveTo(Path.Combine(filePath, filename + ".ncx"), false);
				}

				// prepare OPF
				content = "<?xml version=\"1.0\" encoding=\"utf-8\"?>" + "\n"
					+ "<package xmlns=\"http://www.idpf.org/2007/opf\" version=\"2.0\" unique-identifier=\"" + new Guid(book.ID).ToString("D") + "\">" + "\n"
					+ "<metadata xmlns:dc=\"http://purl.org/dc/elements/1.1/\" xmlns:opf=\"http://www.idpf.org/2007/opf\">" + "\n"
					+ "<dc:Title>" + book.Title + @"</dc:Title>" + "\n"
					+ "<dc:Creator opf:role=\"aut\">" + book.Author + @"</dc:Creator>" + "\n"
					+ (string.IsNullOrWhiteSpace(book.Publisher) ? "" : "<dc:Publisher>" + book.Publisher + "</dc:Publisher>" + "\n")
					+ "<dc:Language>" + book.Language + "</dc:Language>" + "\n"
					+ "<dc:Contributor>" + this.CreditsApp + "</dc:Contributor>" + "\n"
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
				new[] { content }.SaveTo(Path.Combine(filePath, filename + ".opf"), false);

				// generate MOBI
				var generator = UtilityService.GetAppSetting("Books:AmazonKindlegen", "AmazonKindlegen");
				var output = "";

				UtilityService.RunProcess(generator, "\"" + filePath + filename + ".opf\"",
					(sender, args) =>
					{
						// rename file
						File.Move(filePath + filename + ".mobi", filePath + UtilityService.GetNormalizedFilename(book.Name) + ".mobi");

						// delete temporary files
						UtilityService.GetFiles(filePath, filename + ".*").ForEach(file =>
						{
							try
							{
								file.Delete();
							}
							catch { }
						});

						stopwatch.Stop();
						if (this.IsDebugLogEnabled)
							this.WriteLogs(correlationID, $"Generate MOBI file is completed [{book.Name}] - Execution times: {stopwatch.GetElapsedTimes()}\r\n{output}", null, this.ServiceName, "Generators");

						// callback
						onCompleted?.Invoke(filePath + UtilityService.GetNormalizedFilename(book.Name) + ".mobi");
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
				UtilityService.GetFiles(filePath, $"{filename}.*").ForEach(file =>
				{
					try
					{
						file.Delete();
					}
					catch { }
				});
				onError?.Invoke(ex);
			}
		}
		#endregion

		async Task<JObject> ProcessBookmarksAsync(RequestInfo requestInfo, CancellationToken cancellationToken)
		{
			var account = await Account.GetAsync<Account>(requestInfo.Session.User.ID, cancellationToken).ConfigureAwait(false) ?? throw new InformationNotFoundException();
			switch (requestInfo.Verb)
			{
				case "GET":
					return new JObject
					{
						{ "ID", account.ID },
						{ "Objects", account.Bookmarks.ToJArray() },
						{ "Sync", true }
					};

				case "POST":
					account.Bookmarks = (requestInfo.GetBodyJson() as JArray)
						.Select(bookmark => bookmark.FromJson<Account.Bookmark>())
						.Concat(account.Bookmarks)
						.Distinct(new Account.BookmarkComparer())
						.Where(b => Book.Get<Book>(b.ID) != null)
						.OrderByDescending(b => b.Time)
						.Take(30)
						.ToList();
					account.LastSync = DateTime.Now;
					await Account.UpdateAsync(account, true, cancellationToken).ConfigureAwait(false);

					return new JObject
					{
						{ "ID", account.ID },
						{ "Objects", account.Bookmarks.ToJArray() }
					};

				case "DELETE":
					var id = requestInfo.GetObjectIdentity();
					account.Bookmarks = account.Bookmarks
						.Distinct(new Account.BookmarkComparer())
						.Where(b => !b.ID.IsEquals(id) && Book.Get<Book>(b.ID) != null)
						.OrderByDescending(b => b.Time)
						.Take(30)
						.ToList();
					account.LastSync = DateTime.Now;
					await Account.UpdateAsync(account, true, cancellationToken).ConfigureAwait(false);

					var data = new JObject
					{
						{ "ID", account.ID },
						{ "Objects", account.Bookmarks.ToJArray() }
					};

					var sessions = await this.GetSessionsAsync(requestInfo).ConfigureAwait(false);
					await sessions.Where(session => session.Item4).ForEachAsync((session, token) => this.SendUpdateMessageAsync(new UpdateMessage
					{
						Type = $"{this.ServiceName}#Bookmarks#Update",
						DeviceID = session.Item2,
						Data = data
					}, token), cancellationToken).ConfigureAwait(false);

					return data;

				default:
					throw new MethodNotAllowedException(requestInfo.Verb);
			}
		}

		#region Timers for working with background workers & schedulers
		public Crawler Crawler { get; private set; }

		public bool IsCrawlerRunning { get; private set; }

		void RegisterTimers(string[] args = null)
		{
			// last updated
			this.StartTimer(async () =>
			{
				try
				{
					await this.GetLastUpdatedBooksAsync().ConfigureAwait(false);
				}
				catch { }
			}, 25 * 60);

			this.StartTimer(async () => await this.SendLastUpdatedBooksAsync().ConfigureAwait(false), 2 * 60 * 60);

			// delete old .EPUB & .MOBI files
			this.StartTimer(() =>
			{
				var remainTime = DateTime.Now.AddDays(-30);
				UtilityService.GetFiles(Utility.FilesPath, "*.epub|*.mobi", true)
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
			}, 2 * 60 * 60);

			// delete trash/temp files
			this.StartTimer(() =>
			{
				var remainTime = DateTime.Now.AddDays(-90);
				UtilityService.GetFiles(Utility.DirectoryOfTrashFiles, "*.*", true)
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

				remainTime = DateTime.Now.AddDays(-1);
				UtilityService.GetFiles(Utility.DirectoryOfTempFiles, "*.*", true)
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
			}, 12 * 60 * 60);

			// scan new e-books
			this.IsCrawlerRunning = false;
			this.Crawler = new Crawler
			{
				UpdateLogs = (log, ex, updateCentralizedLogs) => this.WriteLogs(this.Crawler.CorrelationID, log, ex)
			};

			var runAtStartup = UtilityService.GetAppSetting("Books:Crawler-RunAtStartup");
			if (runAtStartup == null)
				runAtStartup = args?.FirstOrDefault(a => a.StartsWith("/books-crawler-run-at-startup:"))?.Replace(StringComparison.OrdinalIgnoreCase, "/books-crawler-run-at-startup:", "");

			this.StartTimer(() =>
			{
				if (this.IsCrawlerRunning)
					return;

				this.Crawler.CorrelationID = UtilityService.NewUUID;
				this.WriteLogs(this.Crawler.CorrelationID, "Start the crawler");
				this.IsCrawlerRunning = true;
				this.Crawler.Start(
					async (book, token) => await Task.WhenAll(this.OnBookUpdatedAsync(book, token), this.UpdateStatiscticsAsync(book, false, token)).ConfigureAwait(false),
					(times) =>
					{
						this.WriteLogs(this.Crawler.CorrelationID,
							$"The crawler is completed - Execution times: {times.GetElapsedTimes()}" + "\r\n" +
							"--------------------------------------------------------------" + "\r\n" +
							string.Join("\r\n", this.Crawler.Logs) + "\r\n"
							+ "--------------------------------------------------------------", null, this.ServiceName, "Crawlers"
						);
						this.IsCrawlerRunning = false;
						this.Crawler.Logs.Clear();
					},
					(ex) =>
					{
						this.WriteLogs(this.Crawler.CorrelationID,
							(ex is OperationCanceledException ? "......... Cancelled" : "Error occurred while crawling") + "\r\n" +
							"--------------------------------------------------------------" + "\r\n" +
							string.Join("\r\n", this.Crawler.Logs) + "\r\n"
							+ "--------------------------------------------------------------"
						, ex, this.ServiceName, "Crawlers");
						this.IsCrawlerRunning = false;
						this.Crawler.Logs.Clear();
					},
					this.CancellationTokenSource.Token
				);
			}, 8 * 60 * 60, "true".IsEquals(runAtStartup) ? 5678 : 0);

			// flush statistics (hourly)
			this.StartTimer(() =>
			{
				try
				{
					this.FlushStatistics();
				}
				catch { }
			}, 60 * 60);

			// recompute statistics
			var recomputeStatisticsAtStartup = UtilityService.GetAppSetting("Books:Statistics-RecomputeAtStartup");
			if (recomputeStatisticsAtStartup == null)
				recomputeStatisticsAtStartup = args?.FirstOrDefault(a => a.StartsWith("/books-statistics-recompute-at-startup:"))?.Replace(StringComparison.OrdinalIgnoreCase, "/books-statistics-recompute-at-startup:", "");

			if ("true".IsEquals(recomputeStatisticsAtStartup))
				Task.Run(async () =>
				{
					var correlationID = UtilityService.NewUUID;
					try
					{
						await this.WriteLogsAsync(correlationID, "Start to re-compute statistics").ConfigureAwait(false);
						await this.RecomputeStatistics(correlationID).ConfigureAwait(false);
					}
					catch (Exception ex)
					{
						await this.WriteLogsAsync(correlationID, "Error occurred while re-computing statistics", ex).ConfigureAwait(false);
					}
				}).ConfigureAwait(false);
		}
		#endregion

		#region Last updated books
		Task<List<Book>> GetLastUpdatedBooksAsync()
		{
			var filter = Filters<Book>.And(Filters<Book>.NotEquals("Status", "Inactive"));
			var sort = Sorts<Book>.Descending("LastUpdated");
			return Book.FindAsync(filter, sort, 20, 1, $"{this.GetCacheKey(filter, sort)}:1", this.CancellationTokenSource.Token);
		}

		async Task SendLastUpdatedBooksAsync()
		{
			try
			{
				var filter = Filters<Book>.And(Filters<Book>.NotEquals("Status", "Inactive"));
				var sort = Sorts<Book>.Descending("LastUpdated");
				var books = await this.GetLastUpdatedBooksAsync().ConfigureAwait(false);
				await this.SendUpdateMessagesAsync(
					books.Select(book => new BaseMessage
					{
						Type = $"{this.ServiceName}#Book#Update",
						Data = book.ToJson(false, json => json["TOCs"] = book.GetBook().TOCs.ToJArray())
					}).ToList(),
					"*",
					null,
					this.CancellationTokenSource.Token
				).ConfigureAwait(false);
			}
			catch { }
		}
		#endregion

		#region Update book & statistics when related information are changed
		async Task OnBookUpdatedAsync(Book book, CancellationToken cancellationToken = default)
		{
			// clear related cached & send update message
			await Task.WhenAll(
				this.ClearRelatedCacheAsync(book, null, null, cancellationToken),
				this.SendUpdateMessageAsync(new UpdateMessage
				{
					Type = $"{this.ServiceName}#Book#Update",
					DeviceID = "*",
					Data = book.ToJson(false, json => json["TOCs"] = book.TOCs.ToJArray())
				}, cancellationToken)
			).ConfigureAwait(false);

			// send update chapters
			if (book.TotalChapters > 1)
			{
				var bookJson = await book.GetBookAsync(cancellationToken).ConfigureAwait(false);
				await Task.WhenAll(bookJson.Chapters.Select((content, index) => this.SendUpdateMessageAsync(new UpdateMessage
				{
					Type = $"{this.ServiceName}#Book#Chapter",
					DeviceID = "*",
					Data = new JObject
					{
						{ "ID", book.ID },
						{ "Chapter", index + 1 },
						{ "Content", book.NormalizeMediaURIs(content) }
					}
				}, cancellationToken)));
			}
		}

		async Task ClearRelatedCacheAsync(Book book, string category = null, string author = null, CancellationToken cancellationToken = default)
		{
			try
			{
				var filter = Filters<Book>.NotEquals("Status", "Inactive");
				var sort = Sorts<Book>.Descending("LastUpdated");

				await Task.WhenAll(
					Utility.Cache.RemoveAsync($"{book.GetCacheKey()}:json"),
					Utility.Cache.RemoveAsync(this.GetRelatedCacheKeys(filter, sort), cancellationToken),
					Utility.Cache.RemoveAsync(this.GetRelatedCacheKeys(Filters<Book>.And(Filters<Book>.NotEquals("Status", "Inactive"), filter), sort), cancellationToken),
					Utility.Cache.RemoveAsync(this.GetRelatedCacheKeys(Filters<Book>.And(Filters<Book>.Equals("Category", book.Category), filter), sort), cancellationToken),
					Utility.Cache.RemoveAsync(this.GetRelatedCacheKeys(Filters<Book>.And(Filters<Book>.Equals("Category", book.Category), Filters<Book>.NotEquals("Status", "Inactive"), filter), sort), cancellationToken),
					Utility.Cache.RemoveAsync(this.GetRelatedCacheKeys(Filters<Book>.And(Filters<Book>.Equals("Author", book.Author), filter), sort), cancellationToken),
					Utility.Cache.RemoveAsync(this.GetRelatedCacheKeys(Filters<Book>.And(Filters<Book>.Equals("Author", book.Author), Filters<Book>.NotEquals("Status", "Inactive"), filter), sort), cancellationToken)
				).ConfigureAwait(false);

				if (!string.IsNullOrWhiteSpace(category))
					await Task.WhenAll(
						Utility.Cache.RemoveAsync(this.GetRelatedCacheKeys(Filters<Book>.And(Filters<Book>.Equals("Category", category), filter), sort), cancellationToken),
						Utility.Cache.RemoveAsync(this.GetRelatedCacheKeys(Filters<Book>.And(Filters<Book>.Equals("Category", category), Filters<Book>.NotEquals("Status", "Inactive"), filter), sort), cancellationToken)
					).ConfigureAwait(false);

				if (!string.IsNullOrWhiteSpace(author))
					await Task.WhenAll(
						Utility.Cache.RemoveAsync(this.GetRelatedCacheKeys(Filters<Book>.And(Filters<Book>.Equals("Author", author), filter), sort), cancellationToken),
						Utility.Cache.RemoveAsync(this.GetRelatedCacheKeys(Filters<Book>.And(Filters<Book>.Equals("Author", author), Filters<Book>.NotEquals("Status", "Inactive"), filter), sort), cancellationToken)
					).ConfigureAwait(false);
			}
			catch (Exception ex)
			{
				await this.WriteLogsAsync(UtilityService.NewUUID, $"Error occurred while clearing related cached of a book [{book.Title}]", ex).ConfigureAwait(false);
			}
		}

		async Task UpdateStatiscticsAsync(Book book, bool isDeleted, CancellationToken cancellationToken = default)
		{
			// prepare
			var authors = (book.Author ?? "").GetAuthorNames();

			// update statistic on deleted
			if (isDeleted)
			{
				var category = Utility.Categories[book.Category];
				if (category != null)
					category.Counters--;

				authors.ForEach(a =>
				{
					var author = Utility.Authors[a];
					if (author != null)
						author.Counters--;
				});

				var books = Utility.Status["Books"];
				if (books != null)
					books.Counters--;
			}

			// update statistic on created
			else
			{
				var category = Utility.Categories[book.Category];
				if (category != null)
					category.Counters++;

				authors.ForEach(a =>
				{
					var author = Utility.Authors[a];
					if (author != null)
						author.Counters++;
					else
					{
						Utility.Authors.Add(new StatisticInfo
						{
							Name = a,
							Counters = 1,
							FirstChar = a.GetAuthorName().GetFirstChar().ToUpper()
						});
						var theAuthors = Utility.Status["Authors"];
						if (theAuthors != null)
							theAuthors.Counters++;
					}
				});

				var books = Utility.Status["Books"];
				if (books != null)
					books.Counters++;
			}

			// send the updating message
			await this.SendStatisticsAsync("*", cancellationToken).ConfigureAwait(false);
		}
		#endregion

		#region Sync
		public override async Task<JToken> SyncAsync(RequestInfo requestInfo, CancellationToken cancellationToken = default)
		{
			var stopwatch = Stopwatch.StartNew();
			this.WriteLogs(requestInfo, $"Start sync ({requestInfo.Verb} {requestInfo.GetURI()})");
			using (var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, this.CancellationTokenSource.Token))
				try
				{
					// validate
					var json = await base.SyncAsync(requestInfo, cancellationToken).ConfigureAwait(false);

					// sync
					switch (requestInfo.ObjectName.ToLower())
					{
						case "account":
							json = await this.SyncAccountAsync(requestInfo, cts.Token).ConfigureAwait(false);
							break;

						case "book":
							json = await this.SyncBookAsync(requestInfo, cts.Token).ConfigureAwait(false);
							break;

						default:
							throw new InvalidRequestException($"The request for synchronizing is invalid ({requestInfo.Verb} {requestInfo.GetURI()})");
					}

					stopwatch.Stop();
					this.WriteLogs(requestInfo, $"Sync success - Execution times: {stopwatch.GetElapsedTimes()}");
					if (this.IsDebugResultsEnabled)
						this.WriteLogs(requestInfo,
							$"- Request: {requestInfo.ToString(this.JsonFormat)}" + "\r\n" +
							$"- Response: {json?.ToString(this.JsonFormat)}"
						);
					return json;
				}
				catch (Exception ex)
				{
					throw this.GetRuntimeException(requestInfo, ex, stopwatch);
				}
		}

		async Task<JToken> SyncAccountAsync(RequestInfo requestInfo, CancellationToken cancellationToken)
		{
			var data = requestInfo.GetBodyExpando();
			var account = await Account.GetAsync<Account>(data.Get<string>("ID"), cancellationToken).ConfigureAwait(false);
			if (account == null)
			{
				account = Account.CreateInstance(data);
				await Account.CreateAsync(account, cancellationToken).ConfigureAwait(false);
			}
			else
			{
				account.Fill(data);
				await Account.UpdateAsync(account, true, cancellationToken).ConfigureAwait(false);
			}
			return new JObject
			{
				{ "Sync", "Success" },
				{ "ID", account.ID },
				{ "Type", account.GetTypeName(true) }
			};
		}

		async Task<JToken> SyncBookAsync(RequestInfo requestInfo, CancellationToken cancellationToken)
		{
			var data = requestInfo.GetBodyExpando();
			var book = await Book.GetAsync<Book>(data.Get<string>("ID"), cancellationToken).ConfigureAwait(false);
			if (book == null)
			{
				book = Book.CreateInstance(data);
				await Book.CreateAsync(book, cancellationToken).ConfigureAwait(false);
			}
			else
			{
				book.Fill(data);
				await Book.UpdateAsync(book, true, cancellationToken).ConfigureAwait(false);
			}
			return new JObject
			{
				{ "Sync", "Success" },
				{ "ID", book.ID },
				{ "Type", book.GetTypeName(true) }
			};
		}

		protected override Task SendSyncRequestAsync(RequestInfo requestInfo, CancellationToken cancellationToken = default)
		{
			return base.SendSyncRequestAsync(requestInfo, cancellationToken);
		}
		#endregion

	}
}