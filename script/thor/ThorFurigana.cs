// SPDX-License-Identifier: Apache-2.0
using Godot;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

/// <summary>
/// Adds furigana to Japanese script text using the Kuromoji tokenizer in the Android bridge.
/// Readings are written as the game's own ruby markup (&lt;R漢字|かな&gt;), so Wa2Label draws them.
/// Control codes and the script's existing ruby are kept as they are.
/// </summary>
public static class ThorFurigana
{
	public static GodotObject Plugin;
	/// <summary>Desktop preview: furigana layout on, but only the script's own ruby (no tokenizer).</summary>
	public static bool MockReady;

	public static bool Usable => Plugin != null || MockReady;
	private static bool _ready;
	private static readonly Dictionary<string, string> Cache = new();

	/// <summary>True when readings are available; the first query starts loading the dictionary.</summary>
	public static bool Ready
	{
		get
		{
			if (MockReady)
			{
				return true;
			}
			if (!_ready && Plugin != null)
			{
				_ready = (bool)Plugin.Call("furiganaReady");
			}
			return _ready;
		}
	}

	public static string Annotate(string jp)
	{
		if (string.IsNullOrEmpty(jp) || !Ready || Plugin == null)
		{
			return jp;
		}
		if (Cache.TryGetValue(jp, out string cached))
		{
			return cached;
		}
		var sb = new StringBuilder();
		var run = new StringBuilder();
		for (int i = 0; i < jp.Length; i++)
		{
			char c = jp[i];
			if (c == '\\' && i + 1 < jp.Length)
			{
				Flush(run, sb);
				sb.Append(c).Append(jp[i + 1]);
				i++;
			}
			else if (c == '<')
			{
				// tags (including the script's own ruby) are copied untouched
				Flush(run, sb);
				int depth = 0;
				int j = i;
				do
				{
					if (jp[j] == '<')
					{
						depth++;
					}
					else if (jp[j] == '>')
					{
						depth--;
					}
					j++;
				} while (j < jp.Length && depth > 0);
				sb.Append(jp, i, j - i);
				i = j - 1;
			}
			else if (c is '^' or '`' or '~' or '|' or '>')
			{
				Flush(run, sb);
				sb.Append(c);
			}
			else
			{
				run.Append(c);
			}
		}
		Flush(run, sb);
		string result = sb.ToString();
		Cache[jp] = result;
		return result;
	}

	private static void Flush(StringBuilder run, StringBuilder sb)
	{
		if (run.Length == 0)
		{
			return;
		}
		string text = run.ToString();
		run.Clear();
		if (!HasKanji(text))
		{
			sb.Append(text);
			return;
		}
		string lines = (string)Plugin.Call("readings", text);
		if (string.IsNullOrEmpty(lines))
		{
			sb.Append(text);
			return;
		}
		foreach (string line in lines.Split('\n'))
		{
			int tab = line.IndexOf('\t');
			if (tab < 0)
			{
				continue;
			}
			AppendToken(sb, line.Substring(0, tab), line.Substring(tab + 1));
		}
	}

	/// <summary>Ruby only over the kanji: okurigana and other kana of the word are matched out of the reading.</summary>
	private static void AppendToken(StringBuilder sb, string surface, string reading)
	{
		if (!HasKanji(surface) || reading == "*" || reading.Length == 0)
		{
			sb.Append(surface);
			return;
		}
		string hira = ToHiragana(reading);
		var groups = new List<(string Text, bool Kanji)>();
		foreach (char c in surface)
		{
			bool k = IsKanji(c);
			if (groups.Count > 0 && groups[^1].Kanji == k)
			{
				groups[^1] = (groups[^1].Text + c, k);
			}
			else
			{
				groups.Add((c.ToString(), k));
			}
		}
		var pattern = new StringBuilder("^");
		foreach (var g in groups)
		{
			pattern.Append(g.Kanji ? "(.+?)" : Regex.Escape(ToHiragana(g.Text)));
		}
		pattern.Append('$');
		Match m = Regex.Match(hira, pattern.ToString());
		if (!m.Success)
		{
			sb.Append("<R").Append(surface).Append('|').Append(hira).Append('>');
			return;
		}
		int capture = 1;
		foreach (var g in groups)
		{
			if (g.Kanji)
			{
				sb.Append("<R").Append(g.Text).Append('|').Append(m.Groups[capture++].Value).Append('>');
			}
			else
			{
				sb.Append(g.Text);
			}
		}
	}

	private static bool IsKanji(char c)
	{
		return (c >= '一' && c <= '鿿') || (c >= '㐀' && c <= '䶿') || c == '々' || c == '〆' || c == 'ヶ';
	}

	private static bool HasKanji(string s)
	{
		foreach (char c in s)
		{
			if (IsKanji(c))
			{
				return true;
			}
		}
		return false;
	}

	private static string ToHiragana(string s)
	{
		var chars = s.ToCharArray();
		for (int i = 0; i < chars.Length; i++)
		{
			if (chars[i] >= 'ァ' && chars[i] <= 'ヶ' && chars[i] != 'ヶ')
			{
				chars[i] = (char)(chars[i] - 0x60);
			}
		}
		return new string(chars);
	}
}
