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
				this.CreateFolder(Utility.FolderOfStatisticFiles);
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
				var objectIdentity = requestInfo.GetObjectIdentity();
				switch (requestInfo.ObjectName.ToLower())
				{

					#region Books
					case "book":
						switch (requestInfo.Verb)
						{
							case "GET":
								if ("search".IsEquals(objectIdentity))
									return await this.SearchBooksAsync(requestInfo, cancellationToken);
								else
									throw new InvalidRequestException();

							case "POST":
								if (requestInfo.Query.ContainsKey("x-convert"))
									return await this.CreateBookAsync(requestInfo, cancellationToken);
								throw new InvalidRequestException();

							default:
								throw new MethodNotAllowedException(requestInfo.Verb);
						}
					#endregion

					#region Statistics
					case "statistic":
						switch (requestInfo.Verb)
						{
							default:
								throw new MethodNotAllowedException(requestInfo.Verb);
						}
					#endregion

					#region Accounts
					case "account":
						switch (requestInfo.Verb)
						{
							case "GET":
								return await this.GetAccountAsync(requestInfo, cancellationToken);

							case "POST":
								if (requestInfo.Query.ContainsKey("x-convert"))
									return await this.CreateAccountAsync(requestInfo, cancellationToken);
								throw new InvalidRequestException();

							default:
								throw new MethodNotAllowedException(requestInfo.Verb);
						}
					#endregion

					#region Files
					case "file":
						switch (requestInfo.Verb)
						{
							case "POST":
								if (requestInfo.Query.ContainsKey("x-convert"))
									return this.CopyFiles(requestInfo, cancellationToken);
								throw new InvalidRequestException();

							default:
								throw new MethodNotAllowedException(requestInfo.Verb);
						}
						#endregion

				}

				// unknown
				var msg = "The request is invalid [" + this.ServiceURI + "]: " + requestInfo.Verb + " /";
				if (!string.IsNullOrWhiteSpace(requestInfo.ObjectName))
					msg += requestInfo.ObjectName + (requestInfo.Query.ContainsKey("object-identity") ? "/" + requestInfo.Query["object-identity"] : "");
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

		#region Search books
		async Task<JObject> SearchBooksAsync(RequestInfo requestInfo, CancellationToken cancellationToken = default(CancellationToken))
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
				{ "FilterBy", filter?.ToClientJson(query) },
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

		#region Create book
		async Task<JObject> CreateBookAsync(RequestInfo requestInfo, CancellationToken cancellationToken)
		{
			if (!this.IsAuthenticated(requestInfo) || !requestInfo.Session.User.IsSystemAdministrator)
				throw new AccessDeniedException();

			var book = new Book();
			book.CopyFrom(requestInfo.GetBodyJson());

			await Book.CreateAsync(book, cancellationToken);
			return book.ToJson();
		}
		#endregion

		#region Get account
		async Task<JObject> GetAccountAsync(RequestInfo requestInfo, CancellationToken cancellationToken)
		{
			var account = await Account.GetAsync<Account>(requestInfo.GetObjectIdentity() ?? requestInfo.Session.User.ID);
			if (account == null)
				throw new InformationNotFoundException();

			var json = await this.CallServiceAsync(new RequestInfo(requestInfo.Session)
			{
				ServiceName = "users",
				ObjectName = "profile",
				Query = new Dictionary<string, string>()
				{
					{ "object-identity", account.ID }
				}
			}, cancellationToken);

			var data = account.ToJson();
			foreach (var info in data)
				if (!info.Key.IsEquals("ID"))
					json.Add(info.Key, info.Value);

			return json;
		}
		#endregion

		#region Create account
		async Task<JObject> CreateAccountAsync(RequestInfo requestInfo, CancellationToken cancellationToken)
		{
			if (!this.IsAuthenticated(requestInfo) || !requestInfo.Session.User.IsSystemAdministrator)
				throw new AccessDeniedException();

			var account = new Account();
			account.CopyFrom(requestInfo.GetBodyJson());

			await Account.CreateAsync(account, cancellationToken);
			return account.ToJson();
		}
		#endregion

		#region Files
		JObject CopyFiles(RequestInfo requestInfo, CancellationToken cancellationToken)
		{
			// prepare
			if (!this.IsAuthenticated(requestInfo) || !requestInfo.Session.User.IsSystemAdministrator)
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