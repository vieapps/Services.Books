#region Related components
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Collections.Generic;
using System.Dynamic;

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
						ex => this.WriteInfo("Error occurred while registering the service", ex),
						this.OnInterCommunicateMessageReceived
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
						switch (requestInfo.Verb)
						{
							case "GET":
								if (!requestInfo.Query.ContainsKey("object-identity"))
									throw new InvalidRequestException();
								else if (requestInfo.Query["object-identity"].IsEquals("search"))
									return await this.SearchBooksAsync(requestInfo, cancellationToken);
								break;

							default:
								break;
						}
						await Task.Delay(0);
						break;

					case "statistic":
						await Task.Delay(0);
						break;
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
				this.WriteInfo("Error occurred while processing\r\n==> Request:\r\n" + requestInfo.ToJson().ToString(Newtonsoft.Json.Formatting.Indented), ex);
#else
				this.WriteInfo("Error occurred while processing - Correlation ID: " + requestInfo.CorrelationID);
#endif
				throw this.GetRuntimeException(requestInfo, ex);
			} 
		}

		void OnInterCommunicateMessageReceived(CommunicateMessage message)
		{
			
		}

		~ServiceComponent()
		{
			this.Dispose(false);
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

			var sort = request.Has("OrderBy")
				? request.Get<ExpandoObject>("OrderBy").ToSortBy<Book>()
				: null;
			if (sort == null && string.IsNullOrWhiteSpace(query))
				sort = Sorts<Book>.Descending("LastUpdated");

			var pagination = request.Has("Pagination")
				? request.Get<ExpandoObject>("Pagination").GetPagination()
				: new Tuple<long, int, int, int>(-1, 0, 20, 1);

			var pageNumber = pagination.Item4;

			// check cache
			var cacheKey = filter != null || sort != null
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
				{ "OrderBy", sort?.ToClientJson() },
				{ "Pagination", pagination?.GetPagination() },
				{ "Objects", objects?.ToJsonArray() }
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

	}
}