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

		public RatingPoint()
		{
			this.Type = "General";
			this.Points = 0.0d;
			this.Total = 0;
		}

		public string Type { get; set; }

		public double Points { get; set; }

		public int Total { get; set; }

		[BsonIgnore]
		public double Average
		{
			get
			{
				return this.Total > 0 ? this.Points / this.Total : 0;
			}
		}

		public JObject ToJson(bool removeType = false)
		{
			var json = this.ToJson<RatingPoint>() as JObject;
			if (removeType)
				json.Remove("Type");
			return json;
		}
	}
}