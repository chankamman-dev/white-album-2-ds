// SPDX-License-Identifier: Apache-2.0
using Godot;
using System.Text;

/// <summary>
/// System text-to-speech (Android TextToSpeech, Japanese) for lines that have no recorded voice.
/// Only the Japanese original can be spoken: the Chinese patch stores its text in remapped kanji slots.
/// </summary>
public static class ThorSpeech
{
	public static GodotObject Plugin;

	/// <summary>2 ready, 1 starting, 0 not started, -1 engine failed, -2 no Japanese voice.</summary>
	public static int State => Plugin == null ? -1 : (int)Plugin.Call("speechState");

	public static bool Speak(string scriptText)
	{
		string plain = Plain(scriptText);
		if (Plugin == null || string.IsNullOrWhiteSpace(plain))
		{
			return false;
		}
		return (bool)Plugin.Call("speak", plain);
	}

	public static void Stop()
	{
		Plugin?.Call("stopSpeaking");
	}

	/// <summary>Script text to speakable text: control codes removed, the script's own ruby read as its reading.</summary>
	public static string Plain(string text)
	{
		if (string.IsNullOrEmpty(text))
		{
			return "";
		}
		var sb = new StringBuilder();
		for (int i = 0; i < text.Length; i++)
		{
			char c = text[i];
			switch (c)
			{
				case '\\':
					i++; // two-character escape such as \n or \k
					break;
				case '<':
					if (i + 1 < text.Length && (text[i + 1] == 'R' || text[i + 1] == 'r'))
					{
						int bar = text.IndexOf('|', i);
						int end = text.IndexOf('>', i);
						if (bar > i && end > bar)
						{
							sb.Append(text, bar + 1, end - bar - 1);
							i = end;
							break;
						}
					}
					i++; // tag letter; its content is kept and the closing '>' dropped
					while (i + 1 < text.Length && (char.IsDigit(text[i + 1]) || text[i + 1] == ':'))
					{
						i++;
					}
					break;
				case '>':
				case '^':
				case '`':
				case '~':
				case '|':
					break;
				default:
					sb.Append(c);
					break;
			}
		}
		return sb.ToString();
	}
}
