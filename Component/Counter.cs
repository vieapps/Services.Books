#region Related components
using System;
using System.Diagnostics;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Converters;

using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

using net.vieapps.Components.Utility;
#endregion

namespace net.vieapps.Services.Books
{
	[Serializable, BsonIgnoreExtraElements, DebuggerDisplay("Type = {Type}, Total = {Total}")]
	public class CounterBase
	{

		public CounterBase()
		{
			this.Type = "View";
			this.Total = 0;
		}

		public string Type { get; set; }

		public int Total { get; set; }

		public JObject ToJson(bool removeType = false)
		{
			var json = this.ToJson<CounterBase>() as JObject;
			if (removeType)
				json.Remove("Type");
			return json;
		}
	}

	[Serializable, BsonIgnoreExtraElements]
	public class CounterInfo : CounterBase
	{

		public CounterInfo() : base()
		{
			this.LastUpdated = DateTime.Now;
			this.Month = 0;
			this.Week = 0;
		}

		public DateTime LastUpdated { get; set; }

		public int Month { get; set; }

		public int Week { get; set; }

		public new JObject ToJson(bool removeType = false)
		{
			var json = this.ToJson<CounterInfo>() as JObject;
			if (removeType)
				json.Remove("Type");
			return json;
		}
	}
}