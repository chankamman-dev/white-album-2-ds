// SPDX-License-Identifier: Apache-2.0
using Godot;
using System.Collections.Generic;
using System.Text;

/// <summary>
/// Original Japanese lines for the Chinese build. The Chinese patch (ck-gal.pak) keeps the
/// scripts' bytecode and string-table order, so a line's string index in the Chinese table
/// is the same index in the Japanese script.pak table.
/// </summary>
public static class ThorJapanese
{
	private const string PakName = "script.pak";
	private static Dictionary<string, FileEntry> _index;
	private static bool _indexTried;
	private static readonly Dictionary<string, string[]> Tables = new();

	public static bool Available
	{
		get
		{
			EnsureIndex();
			return _index != null && _index.Count > 0;
		}
	}

	/// <summary>Japanese text of a backlog entry (segments joined like the game joins them), or null.</summary>
	public static string Line(BacklogEntry e)
	{
		string[] table = Table(e.ScriptName);
		if (table == null || e.TextIds.Count == 0)
		{
			return null;
		}
		var sb = new StringBuilder();
		foreach (int id in e.TextIds)
		{
			if (id <= 0 || id >= table.Length)
			{
				return null;
			}
			if (sb.Length > 0)
			{
				sb.Append("\\k");
			}
			sb.Append(table[id]);
		}
		return sb.ToString();
	}

	/// <summary>Japanese text of one segment (one SetMessage) of an entry.</summary>
	public static string Segment(BacklogEntry e, int index)
	{
		string[] table = Table(e.ScriptName);
		if (table == null || index < 0 || index >= e.TextIds.Count)
		{
			return null;
		}
		int id = e.TextIds[index];
		return id > 0 && id < table.Length ? table[id] : null;
	}

	public static string Name(BacklogEntry e)
	{
		if (e.NameTextId == 0)
		{
			return "春希";
		}
		string[] table = Table(e.ScriptName);
		return table != null && e.NameTextId > 0 && e.NameTextId < table.Length ? table[e.NameTextId] : null;
	}

	private static string[] Table(string script)
	{
		if (string.IsNullOrEmpty(script))
		{
			return null;
		}
		if (Tables.TryGetValue(script, out string[] cached))
		{
			return cached;
		}
		string[] table = null;
		EnsureIndex();
		if (_index != null && _index.TryGetValue(script.ToLower() + ".txt", out FileEntry entry))
		{
			byte[] data = Wa2Resource.ReadEntry(entry);
			if (data != null)
			{
				table = Wa2EngineMain.Engine.Wa2Encoding.GetString(data).Split(',');
			}
		}
		Tables[script] = table;
		return table;
	}

	/// <summary>Reads the PACK directory of script.pak without touching the game's own file table.</summary>
	private static void EnsureIndex()
	{
		if (_indexTried)
		{
			return;
		}
		_indexTried = true;
		string full = System.IO.Path.Combine(ProjectSettings.GlobalizePath(Wa2Resource.ResPath), PakName);
		if (!System.IO.File.Exists(full))
		{
			return;
		}
		try
		{
			using var fs = new System.IO.FileStream(full, System.IO.FileMode.Open, System.IO.FileAccess.Read);
			using var reader = new System.IO.BinaryReader(fs);
			if (reader.ReadUInt32() != 0x5041434B)
			{
				return;
			}
			reader.ReadUInt64();
			uint count = reader.ReadUInt32();
			var index = new Dictionary<string, FileEntry>();
			Encoding sjis = Encoding.GetEncoding("shift_jis");
			for (int i = 0; i < count; i++)
			{
				fs.Seek(16 + i * 44, System.IO.SeekOrigin.Begin);
				uint crypted = reader.ReadUInt32();
				string name = sjis.GetString(reader.ReadBytes(24)).ToLower().Replace("\0", "");
				reader.ReadUInt64();
				uint offset = reader.ReadUInt32();
				uint size = reader.ReadUInt32();
				index[name] = new FileEntry { PkgPath = PakName, Offset = offset, Size = size, Crypted = crypted, FileName = name };
			}
			_index = index;
		}
		catch (System.Exception ex)
		{
			GD.PrintErr("ThorJapanese: cannot read script.pak: " + ex.Message);
		}
	}
}
