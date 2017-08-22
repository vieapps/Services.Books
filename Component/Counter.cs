#region Related components
using System;
using System.Linq;
using System.Diagnostics;
using System.Collections.Generic;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Converters;

using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

using net.vieapps.Components.Utility;
#endregion

namespace net.vieapps.Services.Books
{
	[Serializable]
	[BsonIgnoreExtraElements]
	[DebuggerDisplay("Type = {Type}, Total = {Total}")]
	public class CounterBase
	{

		public CounterBase()
		{
			this.Type = Components.Security.Action.View;
			this.Total = 0;
		}

		#region Properties
		[JsonConverter(typeof(StringEnumConverter)), BsonRepresentation(BsonType.String)]
		public Components.Security.Action Type { get; set; }

		public int Total { get; set; }
		#endregion

		#region Methods
		public JObject ToJson(bool removeType = true)
		{
			var json = this.ToJson<CounterBase>() as JObject;
			if (removeType)
				json.Remove("Type");
			return json;
		}

		internal static JObject ToJObject(List<CounterBase> counters)
		{
			var json = new JObject();
			counters.ForEach(counter =>
			{
				json.Add(new JProperty(counter.Type.ToString(), counter.ToJson()));
			});
			return json;
		}

		internal static JArray ToArray(List<CounterBase> counters)
		{
			return counters.ToJArray();
		}

		internal static CounterBase Find(List<CounterBase> counters, string type)
		{
			return counters != null && counters.Count > 0 && !string.IsNullOrWhiteSpace(type)
				? counters.FirstOrDefault(counter => counter.Type.ToString().IsEquals(type))
				: null;
		}

		internal static CounterBase Update(CounterBase counter, int count)
		{
			if (counter != null)
				counter.Total += count;
			return counter;
		}

		internal static CounterBase Update(List<CounterBase> counters, string type, int counter)
		{
			return CounterBase.Update(CounterBase.Find(counters, type), counter);
		}

		internal static CounterBase Set(CounterBase counter, int count)
		{
			if (counter != null)
				counter.Total = count;
			return counter;
		}

		internal static CounterBase Set(List<CounterBase> counters, string type, int counter)
		{
			return CounterBase.Set(CounterBase.Find(counters, type), counter);
		}
		#endregion

	}

	[Serializable]
	[BsonIgnoreExtraElements]
	public class CounterInfo : CounterBase
	{

		public CounterInfo() : base()
		{
			this.LastUpdated = DateTime.Now;
			this.Month = 0;
			this.Week = 0;
		}

		#region Properties
		public DateTime LastUpdated { get; set; }

		public int Month { get; set; }

		public int Week { get; set; }
		#endregion

		#region Methods
		public new JObject ToJson(bool removeType = true)
		{
			var json = this.ToJson<CounterInfo>() as JObject;
			if (removeType)
				json.Remove("Type");
			return json;
		}

		internal static JObject ToJObject(List<CounterInfo> counters)
		{
			var json = new JObject();
			counters.ForEach(counter =>
			{
				json.Add(new JProperty(counter.Type.ToString(), counter.ToJson()));
			});
			return json;
		}

		internal static JArray ToArray(List<CounterInfo> counters)
		{
			return counters.ToJArray();
		}

		internal static CounterInfo Find(List<CounterInfo> counters, string type)
		{
			return counters != null && counters.Count > 0 && !string.IsNullOrWhiteSpace(type)
				? counters.FirstOrDefault(counter => counter.Type.ToString().IsEquals(type))
				: null;
		}

		internal static CounterInfo Update(CounterInfo counter, int count)
		{
			if (counter != null)
			{
				counter.Total += count;

				if (counter.LastUpdated.IsInCurrentWeek())
					counter.Week += count;
				else
					counter.Week = count;

				if (counter.LastUpdated.IsInCurrentMonth())
					counter.Month += count;
				else
					counter.Month = count;

				counter.LastUpdated = DateTime.Now;
			}
			return counter;
		}

		internal static CounterInfo Update(List<CounterInfo> counters, string type, int counter)
		{
			return CounterInfo.Update(CounterInfo.Find(counters, type), counter);
		}
		#endregion

	}
}