#region Related components
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Newtonsoft.Json.Linq;

using net.vieapps.Components.Utility;
using net.vieapps.Components.Caching;
using net.vieapps.Components.Repository;
#endregion

namespace net.vieapps.Services.Books
{
	[EventHandlers]
	public class EventHandlers: IPostGetHandler
	{
		public void OnPostGet<T>(RepositoryContext context, T @object) where T : class
		{
			if (@object is Book)
			{

			}
		}

		public Task OnPostGetAsync<T>(RepositoryContext context, T @object, CancellationToken cancellationToken) where T : class
		{
			if (@object is Book)
			{
				
			}
			return Task.CompletedTask;
		}
	}
}