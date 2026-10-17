// SPDX-License-Identifier: Apache-2.0
using Godot;
using System.Collections.Generic;
using System.Text;

/// <summary>One word of a sentence breakdown, with its best dictionary entry.</summary>
public sealed class DictWord
{
	public string Surface = "";
	public string Base = "";
	public string Reading = "";
	public string Pos = "";
	public string Head = "";
	public string Zh = "";
	public string En = "";

	/// <summary>Chinese gloss first, English when there is none.</summary>
	public string Meaning => !string.IsNullOrEmpty(Zh) ? Zh : En;
}

/// <summary>
/// Sentence breakdown and lookup through the Android bridge
/// (Kuromoji tokens + the bundled JMdict / zh.wiktionary database), and AnkiDroid export.
/// </summary>
public static class ThorDictionary
{
	public static GodotObject Plugin;
	private static bool _ready;

	private static readonly Dictionary<string, string> PosNames = new()
	{
		{ "名詞", "名词" }, { "動詞", "动词" }, { "形容詞", "形容词" }, { "副詞", "副词" }, { "連体詞", "连体词" },
		{ "感動詞", "感叹词" }, { "接続詞", "接续词" }, { "接頭詞", "前缀" },
	};

	public static bool Ready
	{
		get
		{
			if (!_ready && Plugin != null)
			{
				_ready = (bool)Plugin.Call("dictReady") && ThorFurigana.Ready;
			}
			return _ready;
		}
	}

	/// <summary>Content words of a script line (particles, auxiliaries and symbols left out), each looked up once.</summary>
	public static List<DictWord> Breakdown(string scriptText)
	{
		var words = new List<DictWord>();
		if (Plugin == null || !Ready)
		{
			return words;
		}
		string plain = BaseText(scriptText);
		Variant parsed = Json.ParseString((string)Plugin.Call("analyze", plain));
		if (parsed.VariantType != Variant.Type.Array)
		{
			return words;
		}
		var seen = new HashSet<string>();
		foreach (Variant v in parsed.AsGodotArray())
		{
			var t = v.AsGodotDictionary();
			string pos = t["p"].AsString();
			if (!PosNames.TryGetValue(pos, out string posZh))
			{
				continue;
			}
			string surface = t["s"].AsString();
			string baseForm = t["b"].AsString();
			if (baseForm == "*")
			{
				baseForm = surface;
			}
			if (!seen.Add(baseForm) || (surface.Length == 1 && IsKana(surface[0]) && pos == "名詞"))
			{
				continue;
			}
			var w = new DictWord { Surface = surface, Base = baseForm, Pos = posZh, Reading = Hira(t["r"].AsString()) };
			Variant hits = Json.ParseString((string)Plugin.Call("lookup", surface, baseForm, w.Reading));
			if (hits.VariantType == Variant.Type.Array && hits.AsGodotArray().Count > 0)
			{
				var e = hits.AsGodotArray()[0].AsGodotDictionary();
				w.Head = e["h"].AsString();
				w.Zh = e["zh"].AsString();
				w.En = e["en"].AsString();
				if (!string.IsNullOrEmpty(e["r"].AsString()))
				{
					w.Reading = e["r"].AsString();
				}
				if (!string.IsNullOrEmpty(e["p"].AsString()))
				{
					w.Pos = e["p"].AsString();
				}
			}
			if (string.IsNullOrEmpty(w.Head))
			{
				w.Head = baseForm;
			}
			words.Add(w);
		}
		return words;
	}

	/// <summary>Sends a word card to AnkiDroid. Returns ok / duplicate / no_anki / permission / error:...</summary>
	public static string SendToAnki(WordItem w)
	{
		if (Plugin == null)
		{
			return "error:no bridge";
		}
		string sentence = AnkiFurigana(ThorFurigana.Annotate(w.Sentence));
		string meaning = w.Meaning.Replace("\n", "<br>");
		return (string)Plugin.Call("ankiAdd", w.Word, w.Reading, meaning, sentence, "WHITE ALBUM2");
	}

	/// <summary>Script text with the script's own ruby reduced to the base text (for tokenizing).</summary>
	public static string BaseText(string text)
	{
		var sb = new StringBuilder();
		for (int i = 0; i < text.Length; i++)
		{
			char c = text[i];
			if (c == '\\')
			{
				i++;
			}
			else if (c == '<')
			{
				if (i + 1 < text.Length && (text[i + 1] == 'R' || text[i + 1] == 'r'))
				{
					int bar = text.IndexOf('|', i);
					int end = text.IndexOf('>', i);
					if (bar > i && end > bar)
					{
						sb.Append(text, i + 2, bar - i - 2);
						i = end;
						continue;
					}
				}
				i++;
			}
			else if (c is not ('>' or '^' or '`' or '~' or '|'))
			{
				sb.Append(c);
			}
		}
		return sb.ToString();
	}

	/// <summary>Ruby markup to Anki's furigana syntax: " 漢字[かんじ]".</summary>
	public static string AnkiFurigana(string text)
	{
		var sb = new StringBuilder();
		for (int i = 0; i < text.Length; i++)
		{
			char c = text[i];
			if (c == '\\')
			{
				if (i + 1 < text.Length && text[i + 1] == 'n')
				{
					sb.Append("<br>");
				}
				i++;
			}
			else if (c == '<' && i + 1 < text.Length && (text[i + 1] == 'R' || text[i + 1] == 'r'))
			{
				int bar = text.IndexOf('|', i);
				int end = text.IndexOf('>', i);
				if (bar > i && end > bar)
				{
					sb.Append(' ').Append(text, i + 2, bar - i - 2).Append('[').Append(text, bar + 1, end - bar - 1).Append(']');
					i = end;
				}
			}
			else if (c == '<')
			{
				i++;
			}
			else if (c is not ('>' or '^' or '`' or '~' or '|'))
			{
				sb.Append(c);
			}
		}
		return sb.ToString();
	}

	private static bool IsKana(char c)
	{
		return c >= '぀' && c <= 'ヿ';
	}

	public static string Hira(string s)
	{
		if (s == "*")
		{
			return "";
		}
		var chars = s.ToCharArray();
		for (int i = 0; i < chars.Length; i++)
		{
			if (chars[i] >= 'ァ' && chars[i] <= 'ヶ')
			{
				chars[i] = (char)(chars[i] - 0x60);
			}
		}
		return new string(chars);
	}
}
