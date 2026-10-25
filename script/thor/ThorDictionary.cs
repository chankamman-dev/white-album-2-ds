// SPDX-License-Identifier: Apache-2.0
using Godot;
using System;
using System.Collections.Generic;
using System.Text;

/// <summary>One dictionary entry.</summary>
public sealed class DictSense
{
	public string Head = "";
	public string Reading = "";
	public string Pos = "";
	public string Zh = "";
	public string En = "";

	public string Meaning => !string.IsNullOrEmpty(Zh) ? Zh : En;
}

/// <summary>One word of a sentence breakdown, with its best dictionary entry and the runners-up.</summary>
public sealed class DictWord
{
	public string Surface = "";
	public string Base = "";
	public string Reading = "";
	public string Pos = "";
	public string Head = "";
	public string Zh = "";
	public string En = "";
	public bool IsName;
	public readonly List<DictSense> Alts = new();

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
	/// <summary>Desktop preview: canned words instead of the Android dictionary.</summary>
	public static bool Mock;
	private static bool _ready;
	private static readonly Dictionary<string, List<DictWord>> _cache = new();

	private static readonly Dictionary<string, string> PosNames = new()
	{
		{ "名詞", "名词" }, { "動詞", "动词" }, { "形容詞", "形容词" }, { "副詞", "副词" }, { "連体詞", "连体词" },
		{ "感動詞", "感叹词" }, { "接続詞", "接续词" }, { "接頭詞", "前缀" }, { "人名", "人名" },
	};

	// Kuromoji files pronouns, numbers, suffixes and な-adjective stems under 名詞
	private static readonly Dictionary<string, string> SubPos = new()
	{
		{ "代名詞", "代词" }, { "数", "数词" }, { "固有名詞", "专有名词" }, { "接尾", "后缀" }, { "形容動詞語幹", "形容词" },
	};

	public static bool Ready
	{
		get
		{
			if (Mock)
			{
				return true;
			}
			if (!_ready && Plugin != null)
			{
				_ready = (bool)Plugin.Call("dictReady") && ThorFurigana.Ready;
			}
			return _ready;
		}
	}

	/// <summary>
	/// Content words of a script line (particles, auxiliaries and symbols left out), each looked up once.
	/// Runs of nouns the dictionary knows as one word (三人, 学園祭) stay together. Results are cached per line.
	/// </summary>
	public static List<DictWord> Breakdown(string scriptText)
	{
		if (_cache.TryGetValue(scriptText, out List<DictWord> cached))
		{
			return cached;
		}
		var words = new List<DictWord>();
		if (Plugin == null || !Ready)
		{
			return MockBreakdown(scriptText) ?? words;
		}
		string plain = BaseText(scriptText);
		Variant parsed = Json.ParseString((string)Plugin.Call("analyze", plain));
		if (parsed.VariantType != Variant.Type.Array)
		{
			return words;
		}
		var tokens = new List<Godot.Collections.Dictionary>();
		foreach (Variant v in parsed.AsGodotArray())
		{
			tokens.Add(v.AsGodotDictionary());
		}
		var seen = new HashSet<string>();
		for (int i = 0; i < tokens.Count;)
		{
			Godot.Collections.Dictionary t = tokens[i];
			string pos = t["p"].AsString();
			string sub = t.ContainsKey("p2") ? t["p2"].AsString() : "*";
			int n = JoinLength(tokens, i);
			string surface = "", reading = "";
			for (int k = i; k < i + n; k++)
			{
				surface += tokens[k]["s"].AsString();
				reading += Hira(tokens[k]["r"].AsString());
			}
			i += n;
			if (!PosNames.TryGetValue(pos, out string posZh) || (n == 1 && pos == "動詞" && sub == "非自立"))
			{
				continue; // particles, auxiliaries, symbols, and the いる / しまう of て-forms
			}
			if (n == 1 && SubPos.TryGetValue(sub, out string subZh))
			{
				posZh = subZh;
			}
			string baseForm = n == 1 ? t["b"].AsString() : surface;
			if (baseForm == "*")
			{
				baseForm = surface;
			}
			if (!seen.Add(baseForm) || (surface.Length == 1 && IsKana(surface[0]) && pos == "名詞"))
			{
				continue;
			}
			// the token's reading is the surface's, which only tells the dictionary form's reading when they match
			var w = new DictWord { Surface = surface, Base = baseForm, Pos = posZh, Reading = surface == baseForm ? reading : "" };
			if (pos == "人名")
			{
				w.Head = surface;
				w.IsName = true;
				w.Zh = "人名（登场角色）";
				words.Add(w);
				continue;
			}
			Variant hits = Json.ParseString((string)Plugin.Call("lookup", surface, baseForm, w.Reading, posZh));
			if (hits.VariantType == Variant.Type.Array)
			{
				foreach (Variant hv in hits.AsGodotArray())
				{
					var e = hv.AsGodotDictionary();
					var sense = new DictSense
					{
						Head = e["h"].AsString(), Reading = e["r"].AsString(), Pos = e["p"].AsString(),
						Zh = e["zh"].AsString(), En = e["en"].AsString(),
					};
					if (string.IsNullOrEmpty(w.Head))
					{
						w.Head = sense.Head;
						w.Zh = sense.Zh;
						w.En = sense.En;
						if (!string.IsNullOrEmpty(sense.Reading))
						{
							w.Reading = sense.Reading;
						}
						if (!string.IsNullOrEmpty(sense.Pos))
						{
							w.Pos = sense.Pos;
						}
					}
					else
					{
						w.Alts.Add(sense);
					}
				}
			}
			if (string.IsNullOrEmpty(w.Head))
			{
				w.Head = baseForm;
			}
			words.Add(w);
		}
		if (_cache.Count > 64)
		{
			_cache.Clear();
		}
		_cache[scriptText] = words;
		return words;
	}

	/// <summary>How many tokens from <paramref name="i"/> form one dictionary word: up to three nouns or prefixes.</summary>
	private static int JoinLength(List<Godot.Collections.Dictionary> tokens, int i)
	{
		for (int n = Math.Min(3, tokens.Count - i); n >= 2; n--)
		{
			string joined = "";
			bool ok = true;
			for (int k = i; k < i + n && ok; k++)
			{
				string p = tokens[k]["p"].AsString();
				ok = p is "名詞" or "接頭詞";
				joined += tokens[k]["s"].AsString();
			}
			if (ok && (bool)Plugin.Call("hasWord", joined))
			{
				return n;
			}
		}
		return 1;
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

	private static List<DictWord> MockBreakdown(string text)
	{
		if (!Mock)
		{
			return null;
		}
		DictWord W(string surface, string head, string reading, string pos, string zh, string en)
		{
			return new DictWord { Surface = surface, Base = head, Head = head, Reading = reading, Pos = pos, Zh = zh, En = en };
		}
		var ru = W("いる", "居る", "いる", "动词", "存在；有；作为补助动词", "to be (of animate objects), to exist; to stay");
		ru.Alts.Add(new DictSense { Head = "要る", Reading = "いる", Pos = "动词", Zh = "需要，必要", En = "to be needed" });
		ru.Alts.Add(new DictSense { Head = "鋳る", Reading = "いる", Pos = "动词", Zh = "铸造", En = "to cast, to mint" });
		var words = new List<DictWord>
		{
			W("ほら", "ほら", "ほら", "感叹词", "", "look!, see!, here!, there!"),
			W("ここ", "此処", "ここ", "代词", "这里；说话人现在所处的地方、场所", "here, this place; this point"),
			ru,
			new DictWord { Surface = "雪菜", Base = "雪菜", Head = "雪菜", Reading = "せつな", Pos = "人名", Zh = "人名（登场角色）", IsName = true },
			W("三人", "三人", "さんにん", "名词", "三个人", "three people"),
			W("言っ", "言う", "いう", "动词", "说，讲；称，叫；听说，据说", "to say, to utter, to declare; to name, to call"),
		};
		return words;
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
