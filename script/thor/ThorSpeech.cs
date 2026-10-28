// SPDX-License-Identifier: Apache-2.0
using Godot;
using System.Collections.Generic;
using System.IO;
using System.Text;

/// <summary>
/// Reading lines that have no recorded voice: pre-generated narration recordings
/// (Wa2Res/narration/&lt;script&gt;/&lt;text id&gt;.ogg, made on the PC) when they exist, otherwise
/// system text-to-speech (Android TextToSpeech, Japanese).
/// Only the Japanese original can be spoken: the Chinese patch stores its text in remapped kanji slots.
/// </summary>
public static class ThorSpeech
{
	public static GodotObject Plugin;

	private static AudioStreamPlayer _player;
	private static readonly Queue<string> Queue = new();
	private static int _hasNarration = -1;

	private static string NarrationDir => Path.Combine(ProjectSettings.GlobalizePath(Wa2Resource.ResPath), "narration");

	/// <summary>True when the narration recordings are installed.</summary>
	public static bool HasNarration
	{
		get
		{
			if (_hasNarration < 0)
			{
				_hasNarration = Wa2Resource.ResPath != null && Directory.Exists(NarrationDir) ? 1 : 0;
			}
			return _hasNarration == 1;
		}
	}

	/// <summary>Creates the narration player under <paramref name="host"/>, on the game's voice bus.</summary>
	public static void Attach(Node host)
	{
		_player = new AudioStreamPlayer { Bus = "VOICE" };
		host.AddChild(_player);
		_player.Finished += PlayNext;
	}

	/// <summary>
	/// Reads the given segments of a script: their narration recordings when there are any,
	/// otherwise <paramref name="scriptText"/> with the system voice.
	/// </summary>
	public static bool SpeakLine(string script, IEnumerable<int> textIds, string scriptText)
	{
		if (_player != null && HasNarration && !string.IsNullOrEmpty(script) && textIds != null)
		{
			var files = new List<string>();
			foreach (int id in textIds)
			{
				string path = Path.Combine(NarrationDir, script, id + ".ogg");
				if (File.Exists(path))
				{
					files.Add(path);
				}
			}
			if (files.Count > 0)
			{
				Stop();
				files.ForEach(Queue.Enqueue);
				PlayNext();
				return true;
			}
		}
		return Speak(scriptText);
	}

	private static void PlayNext()
	{
		while (Queue.Count > 0)
		{
			AudioStreamOggVorbis stream = AudioStreamOggVorbis.LoadFromFile(Queue.Dequeue());
			if (stream != null)
			{
				_player.Stream = stream;
				_player.Play();
				return;
			}
		}
	}

	/// <summary>2 ready, 1 starting, 0 not started, -1 engine failed, -2 no Japanese voice, -3 the last line failed to synthesize.</summary>
	public static int State => Plugin == null ? -1 : (int)Plugin.Call("speechState");

	/// <summary>When a line was last handed to the engine (ticks, ms), to report a failed synthesis once.</summary>
	public static ulong LastSpeak;

	public static bool Speak(string scriptText)
	{
		string plain = Plain(scriptText);
		if (Plugin == null || string.IsNullOrWhiteSpace(plain))
		{
			return false;
		}
		StopNarration();
		LastSpeak = Time.GetTicksMsec();
		return (bool)Plugin.Call("speak", plain);
	}

	/// <summary>Opens the engine's voice download screen on the top screen.</summary>
	public static void OpenInstall()
	{
		Plugin?.Call("openSpeechInstall");
	}

	public static void Stop()
	{
		StopNarration();
		Plugin?.Call("stopSpeaking");
	}

	private static void StopNarration()
	{
		Queue.Clear();
		_player?.Stop();
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
