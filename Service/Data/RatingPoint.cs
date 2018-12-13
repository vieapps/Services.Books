#region Related components
using System;
using System.Diagnostics;

using Newtonsoft.Json.Linq;
using MongoDB.Bson.Serialization.Attributes;

using net.vieapps.Components.Utility;
#endregion

namespace net.vieapps.Services.Books
{
	[Serializable, BsonIgnoreExtraElements, DebuggerDisplay("Type = {Type}, Average = {Average}, Total = {Total}")]
	public class RatingPoint
	{
		public RatingPoint() { }

		public string Type { get; set; } = "General";

		public double Points { get; set; } = 0.0d;

		public int Total { get; set; } = 0;

		[BsonIgnore]
		public double Average => this.Total > 0 ? this.Points / this.Total : 0;

		public JObject ToJson(bool removeType = false)
		{
			var json = this.ToJson<RatingPoint>() as JObject;
			if (removeType)
				json.Remove("Type");
			return json;
		}
	}
}