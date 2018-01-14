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
		public void OnPostGet(RepositoryContext context, object @object)
		{
			if (@object is Book)
			{

			}
		}

		public Task OnPostGetAsync(RepositoryContext context, object @object, CancellationToken cancellationToken)
		{
			if (@object is Book)
			{

			}
			return Task.CompletedTask;
		}
	}
}