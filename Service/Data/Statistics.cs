#region Related components
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using MsgPack.Serialization;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using net.vieapps.Components.Utility;
#endregion

namespace net.vieapps.Services.Books
{
	public class Statistics
	{
		public Statistics() { }

		#region Properties
		public int Count => this.List.Count;

		public List<StatisticInfo> List { get; } = new List<StatisticInfo>();
		#endregion

		#region Methods
		public StatisticInfo this[string name]
		{
			get
			{
				var index = this.IndexOf(name);
				return index > -1 ? this.List[index] : null;
			}
			set
			{
				var index = this.IndexOf(name);
				if (index > -1)
					this.List[index] = value;
				else
					this.Add(value);
			}
		}

		public StatisticInfo this[int index]
		{
			get
			{
				return index > -1 && index < this.List.Count ? this.List[index] : null;
			}
			set
			{
				if (index > -1 && index < this.List.Count)
					this.List[index] = value;
				else
					this.Add(value);
			}
		}

		public void Add(StatisticInfo info)
		{
			this.List.Add(info);
		}

		public StatisticInfo FindFirst(string name)
		{
			return !string.IsNullOrWhiteSpace(name)
				? this.List.FirstOrDefault(item => item.Name.IsEquals(name))
				: null;
		}

		public int IndexOf(StatisticInfo info)
		{
			return this.List.IndexOf(info);
		}

		public int IndexOf(string name)
		{
			var item = this.FindFirst(name);
			return item == null
				? -1
				: this.IndexOf(item);
		}

		public IEnumerable<StatisticInfo> Find(string firstChar, string sortBy = "Name", string sortMode = "Ascending", int skip = 0, int take = 0)
		{
			var @char = string.IsNullOrWhiteSpace(firstChar)
				? ""
				: firstChar.GetFirstChar().ToUpper();

			var list = this.List.Where(item => string.IsNullOrWhiteSpace(@char) ? true : item.FirstChar.IsEquals(@char));

			if (!string.IsNullOrWhiteSpace(sortBy))
			{
				if (sortBy.IsEquals("Name"))
					list = "Ascending".IsEquals(sortMode)
						? list.OrderBy(item => item.FirstChar)
							.ThenBy(item => item.Name)
							.ThenByDescending(item => item.Counters)
						: list.OrderByDescending(item => item.FirstChar)
							.ThenByDescending(item => item.Name)
							.ThenByDescending(item => item.Counters);

				else if (sortBy.IsEquals("Counters"))
					list = "Ascending".IsEquals(sortMode)
						? list.OrderBy(item => item.Counters)
							.ThenBy(item => item.FirstChar)
							.ThenBy(item => item.Name)
						: list.OrderByDescending(item => item.Counters)
							.ThenBy(item => item.FirstChar)
							.ThenBy(item => item.Name);
			}

			if (skip > 0)
				list = list.Skip(skip);

			if (take > 0)
				list = list.Take(take);

			return list;
		}

		public void Clear()
		{
			this.List.Clear();
		}

		public override string ToString()
		{
			return this.ToJson().ToString(Formatting.None);
		}

		public JToken ToJson()
		{
			return this.List.ToJArray();
		}
		#endregion

		#region Working with files
		internal void Load(string path, string filename, bool seperatedByFirstChar = false)
		{
			var filePath = (string.IsNullOrWhiteSpace(path) ? "" : path.Trim() + Path.DirectorySeparatorChar.ToString()) + filename;

			if (seperatedByFirstChar)
				Utility.Chars
					.Where(@char => File.Exists(string.Format(filePath, @char)))
					.ForEach(@char => new FileInfo(string.Format(filePath, @char)).ReadAsText()
						.FromJson<List<StatisticInfo>>()
						.Where(item => item != null)
						.ForEach(item => this.List.Add(new StatisticInfo
						{
							Name = item.Name,
							Counters = item.Counters,
							FirstChar = @char.ToUpper()
						}))
					);

			else if (File.Exists(filePath))
				this.List.Append(new FileInfo(filePath).ReadAsText().FromJson<List<StatisticInfo>>());
		}

		internal void Save(string path, string filename, bool seperatedByFirstChar = false)
		{
			var filePath = (string.IsNullOrWhiteSpace(path) ? "" : path.Trim() + Path.DirectorySeparatorChar.ToString()) + filename;

			if (seperatedByFirstChar)
				Utility.Chars.ForEach(@char =>
				{
					var list = this.Find(@char);
					(list?.ToJArray() ?? new JArray()).ToString(Formatting.Indented).ToBytes().ToMemoryStream().SaveAsTextAsync(string.Format(filePath, @char));
				});

			else
				this.ToJson().ToString(Formatting.Indented).ToBytes().ToMemoryStream().SaveAsTextAsync(filePath);
		}
		#endregion

	}

	// -------------------------------------

	public class StatisticInfo : Services.StatisticInfo
	{
		public StatisticInfo() : base() { }

		[MessagePackIgnore]
		string _FirstChar = null;

		[JsonIgnore]
		public string FirstChar
		{
			set => this._FirstChar = value;
			get
			{
				if (this._FirstChar == null)
					this._FirstChar = this.Name.GetFirstChar().ToUpper();
				return this._FirstChar;
			}
		}
	}
}