#region Related components
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Collections.Generic;
using System.Dynamic;
using System.IO;

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
				this.CreateFolder(Utility.FolderOfContributedFiles);
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
					return Task.FromException<JObject>(new InvalidRequestException());

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

			var filter = request.Has("FilterBy")
				? request.Get<ExpandoObject>("FilterBy").ToFilterBy<Book>()
				: null;

			var sort = request.Has("SortBy")
				? request.Get<ExpandoObject>("SortBy").ToSortBy<Book>()
				: null;
			if (sort == null && string.IsNullOrWhiteSpace(query))
				sort = Sorts<Book>.Descending("LastUpdated");

			var pagination = request.Has("Pagination")
				? request.Get<ExpandoObject>("Pagination").GetPagination()
				: new Tuple<long, int, int, int>(-1, 0, 20, 1);

			var pageNumber = pagination.Item4;

			// check cache
			var cacheKey = string.IsNullOrWhiteSpace(query) && (filter != null || sort != null)
				? (filter != null ? filter.GetMD5() + ":" : "") + (sort != null ? sort.GetMD5() + ":" : "") + pageNumber.ToString()
				: "";

			var json = !cacheKey.Equals("")
				? await Utility.DataCache.GetAsync<string>(cacheKey + "-json")
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
					? await Book.FindAsync(filter, sort, pageSize, pageNumber, cacheKey, cancellationToken)
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
				Utility.DataCache.Set(cacheKey + "-json", json);
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
				if (!requestInfo.Session.User.IsSystemAdministrator)
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

			// check permission on convert
			if (requestInfo.Extra != null && requestInfo.Extra.ContainsKey("x-convert"))
			{
				if (!requestInfo.Session.User.IsSystemAdministrator)
					throw new AccessDeniedException();
			}

			// check permission on create
			else
			{
				var gotRights = requestInfo.Session.User.IsSystemAdministrator || (this.IsAuthenticated(requestInfo) && requestInfo.Session.User.ID.IsEquals(id));
				if (!gotRights)
					throw new AccessDeniedException();
			}

			// create account profile
			var account = requestInfo.GetBodyJson().Copy<Account>();
			account.ID = id;

			await Account.CreateAsync(account, cancellationToken);
			return account.ToJson();
		}
		#endregion

		#region Get an account profile
		async Task<JObject> GetProfileAsync(RequestInfo requestInfo, CancellationToken cancellationToken)
		{
			// check permissions
			var id = requestInfo.GetObjectIdentity() ?? requestInfo.Session.User.ID;
			var gotRights = requestInfo.Session.User.IsSystemAdministrator || (this.IsAuthenticated(requestInfo) && requestInfo.Session.User.ID.IsEquals(id));
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
			var gotRights = requestInfo.Session.User.IsSystemAdministrator || (this.IsAuthenticated(requestInfo) && requestInfo.Session.User.ID.IsEquals(id));
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
				try
				{
					return Task.FromResult(this.CopyFiles(requestInfo));
				}
				catch (Exception ex)
				{
					return Task.FromException<JObject>(ex);
				}

			return Task.FromException<JObject>(new MethodNotAllowedException(requestInfo.Verb));
		}

		#region Copy files
		JObject CopyFiles(RequestInfo requestInfo)
		{
			// prepare
			if (!requestInfo.Session.User.IsSystemAdministrator)
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