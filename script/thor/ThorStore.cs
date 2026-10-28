// SPDX-License-Identifier: Apache-2.0
using Godot;
using System.Collections.Generic;
using System.Linq;

/// <summary>A favourite line: the Chinese text stays in the game's glyph encoding (drawn with the game font).</summary>
public sealed class FavLine
{
	public string Name = "";
	public string Text = "";
	public int Segment = -1;
	public string JpName = "";
	public string JpText = "";
	public List<VoiceInfo> Voices = new();
	/// <summary>Source script and string-table indices, for the narration recordings.</summary>
	public string Script = "";
	public int[] TextIds = System.Array.Empty<int>();
	public long Time;

	public string Key => Name + "\u0001" + Text;
}

public sealed class WordItem
{
	public string Word = "";
	public string Reading = "";
	public string Pos = "";
	public string Meaning = "";
	/// <summary>Japanese script text of the sentence the word was looked up in.</summary>
	public string Sentence = "";
	public bool Anki;
	public long Time;
}

/// <summary>Favourite lines and the word list, kept as JSON in the app's user folder.</summary>
public static class ThorStore
{
	private const string LinesPath = "user://thor_favorites.json";
	private const string WordsPath = "user://thor_words.json";

	public static List<FavLine> Lines { get; private set; }
	public static List<WordItem> Words { get; private set; }
	public static int Version { get; private set; }

	public static void Load()
	{
		if (Lines != null)
		{
			return;
		}
		Lines = new List<FavLine>();
		Words = new List<WordItem>();
		foreach (Godot.Collections.Dictionary d in ReadArray(LinesPath))
		{
			var f = new FavLine
			{
				Name = Str(d, "name"), Text = Str(d, "text"), Segment = Int(d, "segment", -1),
				JpName = Str(d, "jpName"), JpText = Str(d, "jpText"), Time = Long(d, "time"), Script = Str(d, "script"),
			};
			if (d.TryGetValue("textIds", out Variant ids))
			{
				f.TextIds = ids.AsInt32Array();
			}
			if (d.TryGetValue("voices", out Variant vs))
			{
				foreach (Variant v in vs.AsGodotArray())
				{
					int[] a = v.AsInt32Array();
					if (a.Length == 4)
					{
						f.Voices.Add(new VoiceInfo { Label = a[0], Id = a[1], Chr = a[2], Volume = a[3] });
					}
				}
			}
			Lines.Add(f);
		}
		foreach (Godot.Collections.Dictionary d in ReadArray(WordsPath))
		{
			Words.Add(new WordItem
			{
				Word = Str(d, "word"), Reading = Str(d, "reading"), Pos = Str(d, "pos"), Meaning = Str(d, "meaning"),
				Sentence = Str(d, "sentence"), Anki = d.TryGetValue("anki", out Variant a) && a.AsBool(), Time = Long(d, "time"),
			});
		}
	}

	public static bool IsFavorite(BacklogEntry e)
	{
		Load();
		string key = e.Name + "\u0001" + e.Text;
		return Lines.Any(l => l.Key == key);
	}

	/// <summary>Adds or removes the entry; returns true when it is now a favourite.</summary>
	public static bool ToggleFavorite(BacklogEntry e)
	{
		Load();
		string key = e.Name + "\u0001" + e.Text;
		FavLine existing = Lines.FirstOrDefault(l => l.Key == key);
		if (existing != null)
		{
			Lines.Remove(existing);
			SaveLines();
			return false;
		}
		Lines.Insert(0, new FavLine
		{
			Name = e.Name ?? "", Text = e.Text ?? "", Segment = -1,
			JpName = ThorJapanese.Name(e) ?? "", JpText = ThorJapanese.Line(e) ?? "",
			Voices = e.VoiceInfos != null ? new List<VoiceInfo>(e.VoiceInfos) : new(),
			Script = e.ScriptName ?? "", TextIds = e.TextIds.ToArray(),
			Time = (long)Godot.Time.GetUnixTimeFromSystem(),
		});
		SaveLines();
		return true;
	}

	public static void RemoveLine(FavLine line)
	{
		Load();
		Lines.Remove(line);
		SaveLines();
	}

	public static WordItem FindWord(string word)
	{
		Load();
		return Words.FirstOrDefault(w => w.Word == word);
	}

	public static WordItem AddWord(WordItem item)
	{
		Load();
		WordItem existing = FindWord(item.Word);
		if (existing != null)
		{
			return existing;
		}
		item.Time = (long)Godot.Time.GetUnixTimeFromSystem();
		Words.Insert(0, item);
		SaveWords();
		return item;
	}

	public static void RemoveWord(WordItem item)
	{
		Load();
		Words.Remove(item);
		SaveWords();
	}

	public static void SaveWords()
	{
		var arr = new Godot.Collections.Array();
		foreach (WordItem w in Words)
		{
			arr.Add(new Godot.Collections.Dictionary
			{
				{ "word", w.Word }, { "reading", w.Reading }, { "pos", w.Pos }, { "meaning", w.Meaning },
				{ "sentence", w.Sentence }, { "anki", w.Anki }, { "time", w.Time },
			});
		}
		Write(WordsPath, arr);
	}

	private static void SaveLines()
	{
		var arr = new Godot.Collections.Array();
		foreach (FavLine l in Lines)
		{
			var voices = new Godot.Collections.Array();
			foreach (VoiceInfo v in l.Voices)
			{
				voices.Add(new int[] { v.Label, v.Id, v.Chr, v.Volume });
			}
			arr.Add(new Godot.Collections.Dictionary
			{
				{ "name", l.Name }, { "text", l.Text }, { "segment", l.Segment }, { "jpName", l.JpName },
				{ "jpText", l.JpText }, { "voices", voices }, { "script", l.Script }, { "textIds", l.TextIds },
				{ "time", l.Time },
			});
		}
		Write(LinesPath, arr);
	}

	private static void Write(string path, Godot.Collections.Array arr)
	{
		Version++;
		using var f = FileAccess.Open(path, FileAccess.ModeFlags.Write);
		f?.StoreString(Json.Stringify(arr, "\t"));
	}

	private static Godot.Collections.Array ReadArray(string path)
	{
		if (!FileAccess.FileExists(path))
		{
			return new Godot.Collections.Array();
		}
		Variant v = Json.ParseString(FileAccess.GetFileAsString(path));
		return v.VariantType == Variant.Type.Array ? v.AsGodotArray() : new Godot.Collections.Array();
	}

	private static string Str(Godot.Collections.Dictionary d, string k)
	{
		return d.TryGetValue(k, out Variant v) ? v.AsString() : "";
	}

	private static int Int(Godot.Collections.Dictionary d, string k, int def = 0)
	{
		return d.TryGetValue(k, out Variant v) ? (int)v.AsDouble() : def;
	}

	private static long Long(Godot.Collections.Dictionary d, string k)
	{
		return d.TryGetValue(k, out Variant v) ? (long)v.AsDouble() : 0;
	}
}
