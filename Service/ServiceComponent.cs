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
	public class ServiceComponent : BaseService
	{

		#region Start
		public ServiceComponent() { }

		void WriteInfo(string info, Exception ex = null)
		{
			var msg = string.IsNullOrWhiteSpace(info)
				? ex != null ? ex.Message : ""
				: info;

			Console.WriteLine("~~~~~~~~~~~~~~~~~~~~>");
			Console.WriteLine(msg);
			if (ex != null)
				Console.WriteLine("-----------------------\r\n" + "==> [" + ex.GetType().GetTypeName(true) + "]: " + ex.Message + "\r\n" + ex.StackTrace + "\r\n-----------------------");
		}

		void CreateFolder(string path, bool mediaFolders = true)
		{
			if (!Directory.Exists(path))
				Directory.CreateDirectory(path);

			if (mediaFolders && !Directory.Exists(path + @"\" + Utility.MediaFolder))
				Directory.CreateDirectory(path + @"\" + Utility.MediaFolder);
		}

		internal void Start(string[] args = null, System.Action nextAction = null, Func<Task> nextActionAsync = null)
		{
			// initialize repository
			try
			{
				this.WriteInfo("Initializing the repository");
				RepositoryStarter.Initialize();
			}
			catch (Exception ex)
			{
				this.WriteInfo("Error occurred while initializing the repository", ex);
			}

			// prepare folders
			if (Directory.Exists(Utility.FilesPath))
			{
				this.CreateFolder(Utility.FolderOfDataFiles, false);
				foreach (var @char in Utility.Chars)
					this.CreateFolder(Utility.FolderOfDataFiles + @"\" + @char.ToLower());
				this.CreateFolder(Utility.FolderOfStatisticFiles, false);
				this.CreateFolder(Utility.FolderOfContributedFiles, false);
				this.CreateFolder(Utility.FolderOfContributedFiles + @"\users");
				this.CreateFolder(Utility.FolderOfContributedFiles + @"\crawlers");
				this.CreateFolder(Utility.FolderOfTempFiles);
				this.CreateFolder(Utility.FolderOfTrashFiles);
			}

			// start the service
			Task.Run(async () =>
			{
				try
				{
					await this.StartAsync(
						() => {
							var pid = Process.GetCurrentProcess().Id.ToString();
							this.WriteInfo("The service is registered - PID: " + pid);
							this.WriteLog(UtilityService.BlankUID, this.ServiceName, null, "The service [" + this.ServiceURI + "] is registered - PID: " + pid);
						},
						(ex) => {
							this.WriteInfo("Error occurred while registering the service", ex);
						}
					);
				}
				catch (Exception ex)
				{
					this.WriteInfo("Error occurred while starting the service", ex);
				}
			})
			.ContinueWith(async (task) =>
			{
				try
				{
					nextAction?.Invoke();
				}
				catch (Exception ex)
				{
					this.WriteInfo("Error occurred while running the next action (sync)", ex);
				}
				if (nextActionAsync != null)
					try
					{
						await nextActionAsync().ConfigureAwait(false);
					}
					catch (Exception ex)
					{
						this.WriteInfo("Error occurred while running the next action (async)", ex);
					}
			})
			.ConfigureAwait(false);
		}
		#endregion

		public override string ServiceName { get { return "books"; } }

		public override async Task<JObject> ProcessRequestAsync(RequestInfo requestInfo, CancellationToken cancellationToken = default(CancellationToken))
		{
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
				}

				// unknown
				var msg = "The request is invalid [" + this.ServiceURI + "]: " + requestInfo.Verb + " /";
				if (!string.IsNullOrWhiteSpace(requestInfo.ObjectName))
					msg += requestInfo.ObjectName + (!string.IsNullOrWhiteSpace(requestInfo.GetObjectIdentity()) ? "/" + requestInfo.GetObjectIdentity() : "");
				throw new InvalidRequestException(msg);
			}
			catch (Exception ex)
			{
#if DEBUG
				this.WriteInfo("Error occurred while processing\r\n==> Request:\r\n" + requestInfo.ToJson().ToString(Formatting.Indented), ex);
#else
				this.WriteInfo("Error occurred while processing - Correlation ID: " + requestInfo.CorrelationID);
#endif
				throw this.GetRuntimeException(requestInfo, ex);
			} 
		}

		#region Get privileges/actions (base on role)
		List<Privilege> GetPrivileges(RequestInfo requestInfo, User user, Privileges privileges)
		{
			var workingPrivileges = new List<Privilege>();

			var objects = "book,category,statistic".ToList();
			objects.ForEach(o => workingPrivileges.Add(new Privilege(this.ServiceName, o, PrivilegeRole.Viewer.ToString())));

			return workingPrivileges;
		}

		List<string> GetActions(RequestInfo requestInfo, PrivilegeRole role)
		{
			var actions = new List<Components.Security.Action>();
			switch (role)
			{
				case PrivilegeRole.Administrator:
					actions = new List<Components.Security.Action>()
					{
						Components.Security.Action.Full
					};
					break;

				case PrivilegeRole.Moderator:
					actions = new List<Components.Security.Action>()
					{
						Components.Security.Action.Approve,
						Components.Security.Action.Update,
						Components.Security.Action.Create,
						Components.Security.Action.View,
						Components.Security.Action.Download
					};
					break;

				case PrivilegeRole.Editor:
					actions = new List<Components.Security.Action>()
					{
						Components.Security.Action.Update,
						Components.Security.Action.Create,
						Components.Security.Action.View,
						Components.Security.Action.Download
					};
					break;

				case PrivilegeRole.Contributor:
					actions = new List<Components.Security.Action>()
					{
						Components.Security.Action.Create,
						Components.Security.Action.View,
						Components.Security.Action.Download
					};
					break;

				default:
					actions = new List<Components.Security.Action>()
					{
						Components.Security.Action.View,
						Components.Security.Action.Download
					};
					break;
			}
			return actions.Select(a => a.ToString()).ToList();
		}
		#endregion

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
			if (!this.IsAuthorized(requestInfo, Components.Security.Action.View, null, (user, privileges) => this.GetPrivileges(requestInfo, user, privileges), (role) => this.GetActions(requestInfo, role)))
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
				Utility.Cache.SetAbsolute(cacheKey + ":" + pageNumber.ToString() + "-json", json, Utility.CacheTime / 2);
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

		#region Get a book
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
			if (id.Equals(objectIdentity) || requestInfo.Query.ContainsKey("chapter"))
			{
				var keyJSON = id.GetCacheKey<Book>() + "-json";
				bookJSON = await Utility.Cache.GetAsync<Book>(keyJSON);
				if (bookJSON == null)
				{
					bookJSON = book.Clone();
					var jsonFilePath = bookJSON.GetFolderPath() + @"\" + bookJSON.Name + ".json";
					if (File.Exists(jsonFilePath))
					{
						bookJSON.CopyData(JObject.Parse(await UtilityService.ReadTextFileAsync(jsonFilePath, Encoding.UTF8)));
						await Utility.Cache.SetAsFragmentsAsync(keyJSON, bookJSON);
					}
				}
			}

			// counters
			if ("counters".IsEquals(objectIdentity))
			{
				// get and update
				var action = requestInfo.Query["action"].ToEnum<Components.Security.Action>();
				var counter = book.Counters.FirstOrDefault(c => c.Type.Equals(action));
				if (counter != null)
				{
					counter.LastUpdated = DateTime.Now;
					counter.Total++;
					counter.Week = counter.LastUpdated.IsInCurrentWeek() ? counter.Week + 1 : 1;
					counter.Month = counter.LastUpdated.IsInCurrentMonth() ? counter.Month + 1 : 1;
					await Book.UpdateAsync(book, cancellationToken);
				}

				// prepare data
				var data = new JObject()
				{
					{ "ID", book.ID },
					{ "Counters", book.Counters.ToJArray(c => c.ToJson()) }
				};

				// send update message
				await this.SendUpdateMessageAsync(new UpdateMessage()
				{
					DeviceID = "*",
					ExcludedDeviceID = requestInfo.Session.DeviceID,
					Type = "Books#Book#Counters",
					Data = data
				}, cancellationToken);

				// return update
				return data;
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
					{ "Content", bookJSON.Chapters[chapter - 1] }
				};
			}

			// book information
			else
			{
				// generate
				var json = book.ToJson();

				// update
				json["TOCs"] = bookJSON.TOCs.ToJArray(t => new JValue(UtilityService.RemoveTags(t)));
				if (book.TotalChapters < 2)
					json.Add(new JProperty("Body", bookJSON.Chapters[0].NormalizeMediaFileUris(book)));

				// return
				return json;
			}
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
			var gotRights = (this.IsAuthenticated(requestInfo) && requestInfo.Session.User.ID.IsEquals(id)) || await this.IsSystemAdministratorAsync(requestInfo);
			if (!gotRights)
				gotRights = this.IsAuthorized(requestInfo, Components.Security.Action.View);
			if (!gotRights)
				throw new AccessDeniedException();

			// get information
			var account = await Account.GetAsync<Account>(id, cancellationToken);
			if (account == null)
				throw new InformationNotFoundException();

			return account.ToJson();
		}
		#endregion

		#region Update an account profile
		async Task<JObject> UpdateProfileAsync(RequestInfo requestInfo, CancellationToken cancellationToken)
		{
			// check permissions
			var id = requestInfo.GetObjectIdentity() ?? requestInfo.Session.User.ID;
			var gotRights = (this.IsAuthenticated(requestInfo) && requestInfo.Session.User.ID.IsEquals(id)) || await this.IsSystemAdministratorAsync(requestInfo);
			if (!gotRights)
				gotRights = this.IsAuthorized(requestInfo, Components.Security.Action.Update);
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

		#region Copy files
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
				UtilityService.GetFiles(source + Utility.MediaFolder, permanentID + "-*.*").ForEach(file =>
				{
					File.Copy(file.FullName, destination + Utility.MediaFolder + @"\" + file.Name, true);
				});
			}

			return new JObject()
			{
				{ "Status", "OK" }
			};
		}
		#endregion

		#region Process inter-communicate messages
		protected override void ProcessInterCommunicateMessage(CommunicateMessage message)
		{

		}
		#endregion

	}
}