// SPDX-License-Identifier: Apache-2.0
using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

/// <summary>
/// Lower-screen companion, assembled from the game's own UI art so it reads like
/// one more in-game page: page header strip, navy backlog column, arrow scrollbar,
/// teal glass buttons, config-style arrows/cells and the Leaf footer with the BGM title.
/// Pages: 文本记录 (newest first, current line excluded) / 快捷操作 (commands, choices) / 操作说明.
/// Input arrives as plain touch points from <see cref="ThorDualScreen"/>.
/// </summary>
public partial class ThorBottomUi : Control
{
	private enum Tab { Log, Fav, Actions, Help }
	private const int TabCount = 4;

	private sealed class LogRow
	{
		public BacklogEntry Entry;
		public CanvasItem Name;
		public Wa2Label Text;
		public float Y;
		public float H;
		public bool HasVoice;
		public bool VoiceEnabled;
		public Wa2Label Jp;
		public bool IsCurrent;
		public float VoiceY = float.NaN;
		/// <summary>Japanese text read by system TTS (lines without a recorded voice).</summary>
		public string Speech;
	}

	private readonly record struct Hit(Rect2 Rect, string Id, Action Act, bool Enabled, Action Long = null);

	private static readonly int[] FontSizes = { 32, 36, 40, 44, 48 };
	private const string ConfigPath = "user://thor_dualscreen.cfg";

	// game art is laid out at 1280x720; the lower screen uses it at 1.5x like the top screen
	private const float Art = 1.5f;
	private const float HeaderH = 40f * Art;
	private const float FooterH = 56f * Art;
	private const float TabsH = 92f;
	private const float ContentTop = HeaderH + TabsH;
	private const float RowGap = 30f;
	private const float TapSlop = 14f;

	private static readonly Color TextWhite = new(1f, 1f, 1f);
	private static readonly Color TextSoft = new(0.78f, 0.88f, 0.95f);
	private static readonly Color Teal = new(0.42f, 0.86f, 0.9f);
	private static readonly Color ActiveYellow = new(1f, 0.92f, 0.42f);
	private static readonly Color Shadow = new(0.0f, 0.12f, 0.16f, 0.85f);

	// sprite rects in the game textures
	private static readonly Rect2 ArrowLeftSrc = new(482, 159, 51, 35);   // sys_03100
	private static readonly Rect2 ArrowRightSrc = new(890, 159, 51, 35);  // sys_03100
	private static readonly Rect2 GlassBtnSrc = new(1, 3, 158, 34);        // sys_00101 "是", rows 0/40/80
	private static readonly Rect2 GlassPanelSrc = new(22, 13, 500, 184);   // sys_00100

	public bool Dirty = true;

	// Japanese original (script.pak) under each line, plus the current line pinned on top
	private bool _showJp;
	private bool _showRuby = true;
	private bool _speakNarration;
	private BacklogEntry _spokenEntry;
	private int _spokenCount = -1;
	private static readonly Color SpeechTint = new(0.55f, 0.92f, 1f);
	private Texture2D _jpFont;
	private Texture2D _jpShadow;
	private static readonly Color JpColor = new(0.66f, 0.88f, 1f);

	private Wa2EngineMain E => Wa2EngineMain.Engine;
	private Tab _tab = Tab.Log;
	private int _fontSize = 40;

	private Texture2D _texBacklogTop;   // sys_00032 Backlog header
	private Texture2D _texPlainTop;     // sys_05000 header strip
	private Texture2D _texFooter;       // sys_09000 Leaf footer
	private Texture2D _texColumn;       // sys_00033 backlog column
	private Texture2D _texScroll;       // sys_00030 scrollbar
	private Texture2D _texVoice;        // sys_00031 voice button
	private Texture2D _texAdvIcons;     // sys_00010 ADV command icons
	private Texture2D _texBars;         // sys_00200 choice bars
	private Texture2D _texConfig;       // sys_03100 config page
	private Texture2D _texGlassBtn;     // sys_00101 glass buttons
	private Texture2D _texBgmNames;     // sys_00300 BGM titles
	private Texture2D _texBgmMeter;     // sys_00301
	private Texture2D _texSnow;         // sys_00000
	private StyleBoxTexture _glassPanel;
	private StyleBoxFlat _sectionPanel;
	private SystemFont _sans;
	private SystemFont _serifItalic;
	private SystemFont _sansJp;

	private Control _logPage;
	private Control _actionsPage;
	private Control _helpPage;
	private ThorScrollList _log;
	private ThorScrollList _dragList;
	private readonly List<LogRow> _rows = new();
	private long _logSignature = long.MinValue;
	private string _stateSignature = "";
	private CanvasItem _emptyHint;

	private readonly Dictionary<string, CanvasItem> _texts = new();
	private readonly List<CanvasItem> _choiceTexts = new();
	private readonly List<(CanvasItem Title, CanvasItem En, CanvasItem Body)> _helpSections = new();
	private int _choiceSignature = -1;
	private bool _choicesShown;
	private bool _autoSwitched;

	// touch
	private readonly List<Hit> _hits = new();
	private bool _touching;
	private bool _dragging;
	private Vector2 _downPos;
	private Vector2 _lastPos;
	private float _downScroll;
	private ulong _lastMoveMs;
	private ulong _downMs;
	private bool _longFired;
	private string _pressed = "";

	// voice replay
	private List<VoiceInfo> _voiceQueue;
	private int _voiceIdx;

	public override void _Ready()
	{
		MouseFilter = MouseFilterEnum.Ignore;
		LoadConfig();
		_showJp |= MockJapanese;
		_texBacklogTop = Grp("sys_00032");
		_texPlainTop = Grp("sys_05000");
		_texFooter = Grp("sys_09000");
		_texColumn = Grp("sys_00033");
		_texScroll = Grp("sys_00030");
		_texVoice = Grp("sys_00031");
		_texAdvIcons = Grp("sys_00010");
		_texBars = Grp("sys_00200");
		_texConfig = Grp("sys_03100");
		_texGlassBtn = Grp("sys_00101");
		_texBgmNames = Grp("sys_00300");
		_texBgmMeter = Grp("sys_00301");
		_texSnow = Grp("sys_00000");
		_jpFont = ResourceLoader.Load<Texture2D>("res://assets/fonts/jp/本体80.png");
		_jpShadow = ResourceLoader.Load<Texture2D>("res://assets/fonts/jp/袋影80.png");
		_sans = new SystemFont { FontNames = new[] { "Noto Sans CJK SC", "Noto Sans SC", "Source Han Sans SC", "Microsoft YaHei", "sans-serif" } };
		// dictionary headwords: Japanese glyph shapes, and kanji the game's JP atlas gets wrong (此処)
		_sansJp = new SystemFont { FontNames = new[] { "Noto Sans CJK JP", "Noto Sans JP", "Source Han Sans JP", "Yu Gothic UI", "Meiryo", "sans-serif" } };
		_serifItalic = new SystemFont { FontNames = new[] { "Noto Serif", "Times New Roman", "serif" }, FontItalic = true, FontWeight = 600 };

		_glassPanel = new StyleBoxTexture { Texture = Grp("sys_00100"), RegionRect = GlassPanelSrc };
		_glassPanel.SetTextureMarginAll(24);
		_sectionPanel = new StyleBoxFlat { BgColor = new Color(0, 0, 0, 0.42f), BorderColor = new Color(Teal, 0.45f), AntiAliasing = true };
		_sectionPanel.SetCornerRadiusAll(16);
		_sectionPanel.BorderWidthTop = 2;

		_logPage = Page("LogPage");
		_actionsPage = Page("ActionsPage");
		_helpPage = Page("HelpPage");
		_log = new ThorScrollList { Name = "LogList" };
		_logPage.AddChild(_log);
		_log.Content.Draw += DrawRows;
		_logPage.Draw += DrawLogChrome;
		_actionsPage.Draw += DrawActions;
		_helpPage.Draw += DrawHelp;

		// page titles drawn in the header strip (the Backlog strip already carries its own title art)
		AddText("title.actions.en", "Command", 40, TextWhite, _actionsPage, _serifItalic);
		AddText("title.actions", "快捷操作", 22, TextWhite, _actionsPage);
		AddText("title.help.en", "Guide", 40, TextWhite, _helpPage, _serifItalic);
		AddText("title.help", "操作说明", 22, TextWhite, _helpPage);

		string[,] tabs = { { "文本记录", "Backlog" }, { "收藏", "Favorites" }, { "快捷操作", "Command" }, { "操作说明", "Guide" } };
		for (int i = 0; i < TabCount; i++)
		{
			AddText("tab" + i, tabs[i, 0], 32, TextWhite, this);
			AddText("tab" + i + ".en", tabs[i, 1], 18, TextSoft, this, _serifItalic);
		}
		AddText("fontsize", "文字大小", 30, TextWhite, _helpPage);
		AddText("jp", "日文原文", 30, TextWhite, _helpPage);
		AddText("ruby", "汉字注音", 30, TextWhite, _helpPage);
		AddText("speech.label", "系统语音", 30, TextWhite, _helpPage);
		AddText("speech", "自动朗读", 30, TextWhite, _helpPage);
		AddText("speech.hint", "", 24, TextSoft, _helpPage);
		AddText("status", "", 26, TextWhite, this);
		AddText("choose", "请选择", 30, ActiveYellow, _actionsPage);
		foreach (ActionDef a in ActionDefs)
		{
			AddText("act." + a.Id, a.Label, 32, TextWhite, _actionsPage);
		}
		AddText("act.hide.show", "显示文本", 32, TextWhite, _actionsPage);
		_emptyHint = MakeText("开始游戏后，这里会显示文本记录。", 30, TextSoft);
		_logPage.AddChild(_emptyHint);
		BuildHelp();
		InitPages();

		E.SoundMgr.GetVoicePlayer(0).Finished += OnVoiceFinished;
		Relayout();
	}

	private static Texture2D Grp(string name)
	{
		return ResourceLoader.Load<Texture2D>("res://assets/grp/" + name + ".png");
	}

	public void MarkDirty()
	{
		Dirty = true;
	}

	// ---- per-frame sync ---------------------------------------------------

	public void Tick(double delta)
	{
		AutoSpeak();
		TickPages(delta);
		SyncChoices();
		SyncLog();
		string state = string.Join("|", _tab, E.State, E.AutoMode, E.SkipMode || E.Skipping, E.AdvMain.State,
			ThorActions.TopUi(E)?.Name ?? "", E.VideoPlayer.IsPlaying, ThorActions.CanSaveLoad(E), ThorActions.AdvIdle(E),
			_choiceSignature, Size, E.SoundMgr.BgmId);
		if (state != _stateSignature)
		{
			_stateSignature = state;
			Dirty = true;
		}
		if (!_touching)
		{
			foreach (ThorScrollList list in ScrollLists())
			{
				if (list.Fling((float)delta))
				{
					Dirty = true;
				}
			}
		}
		else if (!_dragging && !_longFired && _pressed != "" && Time.GetTicksMsec() - _downMs > 550)
		{
			Hit? hit = HitAt(_downPos);
			if (hit is { Long: not null } h && h.Id == _pressed)
			{
				_longFired = true;
				_pressed = "";
				h.Long();
				Dirty = true;
			}
		}
	}

	private void SyncLog()
	{
		List<BacklogEntry> logs = E.Backlogs;
		int n = logs.Count;
		bool rubyOn = _showJp && _showRuby && ThorFurigana.Usable && ThorFurigana.Ready;
		long sig = HashCode.Combine(n, _fontSize, (int)Size.X, _showJp, rubyOn);
		if (_showJp && n >= 1 && ThorActions.InGame(E))
		{
			BacklogEntry cur = logs[n - 1];
			sig = HashCode.Combine(sig, RuntimeHelpers.GetHashCode(cur), cur.TextIds.Count, cur.NameTextId, E.AdvMain.TextLabel.Segment);
		}
		if (n >= 2)
		{
			BacklogEntry last = logs[n - 2];
			sig = HashCode.Combine(sig, RuntimeHelpers.GetHashCode(last), last.Text?.Length ?? 0, last.Segment, logs[0]);
		}
		if (sig == _logSignature)
		{
			return;
		}
		_logSignature = sig;
		_voiceQueue = null;
		RebuildRows();
		Dirty = true;
	}

	private void SyncChoices()
	{
		bool visible = ThorActions.ChoicesVisible(E);
		int sig = visible ? HashCode.Combine(E.SelectItems.Count, E.SelectIdx, E.SelectItems.Count > 0 ? E.SelectItems[0].Text : "") : -1;
		if (visible && !_choicesShown)
		{
			_choicesShown = true;
			if (_tab == Tab.Log)
			{
				_tab = Tab.Actions;
				_autoSwitched = true;
			}
		}
		else if (!visible && _choicesShown)
		{
			_choicesShown = false;
			if (_autoSwitched && _tab == Tab.Actions)
			{
				_tab = Tab.Log;
			}
			_autoSwitched = false;
		}
		if (sig == _choiceSignature)
		{
			return;
		}
		_choiceSignature = sig;
		foreach (CanvasItem t in _choiceTexts)
		{
			t.QueueFree();
		}
		_choiceTexts.Clear();
		if (visible)
		{
			for (int i = 0; i < Math.Min(3, E.SelectItems.Count); i++)
			{
				CanvasItem text = MakeGameText(E.SelectItems[i].Text ?? "", 38, TextWhite, 999);
				_actionsPage.AddChild(text);
				_choiceTexts.Add(text);
			}
		}
		Dirty = true;
	}

	// ---- backlog ------------------------------------------------------------

	private float ListTop => ContentTop + 6f;
	private float ListBottom => Size.Y - FooterH - 6f;
	private float RowRight => Size.X - 84f;
	private const float VoiceLeft = 30f;
	private const float TextLeft = 100f;

	private void RebuildRows()
	{
		BacklogEntry anchor = null;
		float anchorOffset = 0;
		if (_log.Scroll > 1f)
		{
			LogRow first = _rows.FirstOrDefault(r => r.Y + r.H > _log.Scroll);
			if (first != null)
			{
				anchor = first.Entry;
				anchorOffset = first.Y - _log.Scroll;
			}
		}
		foreach (LogRow row in _rows)
		{
			row.Name?.QueueFree();
			row.Text?.QueueFree();
			row.Jp?.QueueFree();
		}
		_rows.Clear();

		List<BacklogEntry> logs = E.Backlogs;
		int maxChars = Math.Max(8, (int)((RowRight - TextLeft) / _fontSize));
		int nameSize = Math.Max(28, (int)Mathf.Round(_fontSize * 0.85f));
		int paragraph = (int)Mathf.Round(_fontSize * 0.42f);
		bool ruby = _showJp && _showRuby && ThorFurigana.Usable && ThorFurigana.Ready;
		int jpSize = Math.Max(30, (int)Mathf.Round(_fontSize * (ruby ? 0.9f : 0.8f)));
		// furigana needs room above every line
		int jpParagraph = ruby ? (int)(jpSize * RubyScale) + 10 : Math.Max(16, (int)Mathf.Round(jpSize * 0.5f));
		int curParagraph = ruby ? (int)(_fontSize * RubyScale) + 12 : paragraph + 6;
		int jpChars = Math.Max(8, (int)((RowRight - TextLeft) / jpSize));
		bool jp = _showJp && ThorJapanese.Available;
		float y = 18f;

		// current line: the top screen shows the Chinese, so only the original is pinned here
		if (jp && logs.Count > 0 && ThorActions.InGame(E))
		{
			BacklogEntry cur = logs[^1];
			string line = ThorJapanese.Line(cur);
			if (!string.IsNullOrEmpty(line))
			{
				var row = new LogRow { Entry = cur, Y = y, IsCurrent = true };
				float top = y + 24f;
				string name = ThorJapanese.Name(cur);
				float nameH = 0;
				if (!string.IsNullOrWhiteSpace(name))
				{
					row.Name = JpLabel(name, nameSize, 999, paragraph, TextSoft, -1, false);
					SetPos(row.Name, new Vector2(TextLeft, top));
					nameH = nameSize + 10f;
				}
				int curChars = Math.Max(8, (int)((RowRight - TextLeft) / _fontSize));
				float rubyPad = ruby ? _fontSize * RubyScale + 4f : 0f;
				row.Jp = JpLabel(ruby ? ThorFurigana.Annotate(line) : line, _fontSize, curChars, curParagraph, TextWhite, E.AdvMain.TextLabel.Segment, ruby);
				row.Jp.Position = new Vector2(TextLeft, top + nameH + rubyPad);
				float h = JpHeight(row.Jp, _fontSize, curParagraph);
				row.HasVoice = cur.VoiceInfos != null && cur.VoiceInfos.Count > 0;
				row.VoiceEnabled = row.HasVoice && E.Prefs.CanPlayCharVoice(cur.VoiceInfos[0].Chr);
				row.VoiceY = top - 4f;
				if (!row.HasVoice && SpeechAvailable)
				{
					row.Speech = line;
				}
				row.H = 24f + nameH + rubyPad + h + 26f;
				_rows.Add(row);
				y += row.H + RowGap;
			}
		}

		for (int i = logs.Count - 2; i >= 0; i--)
		{
			BacklogEntry e = logs[i];
			if (string.IsNullOrEmpty(e.Text))
			{
				continue;
			}
			var row = new LogRow { Entry = e, Y = y };
			float nameH = 0;
			if (!string.IsNullOrWhiteSpace(e.Name))
			{
				row.Name = MakeGameText(e.Name, nameSize, TextSoft, 999);
				_log.Content.AddChild(row.Name);
				SetPos(row.Name, new Vector2(TextLeft, y));
				nameH = nameSize + 14f;
			}
			var label = new Wa2Label
			{
				FontSize = _fontSize,
				MaxChars = maxChars,
				ParagraphSpacing = paragraph,
				LineSpacing = 0,
				Color = TextWhite,
			};
			_log.Content.AddChild(label);
			label.Segment = e.Segment;
			TextParseResult r = label.SetText(e.Text);
			int lines = (int)Mathf.Round(r.EndPosition.Y / (_fontSize + paragraph)) + 1;
			label.Position = new Vector2(TextLeft, y + nameH);
			row.Text = label;
			row.HasVoice = e.VoiceInfos != null && e.VoiceInfos.Count > 0;
			row.VoiceEnabled = row.HasVoice && E.Prefs.CanPlayCharVoice(e.VoiceInfos[0].Chr);
			row.H = Mathf.Max(nameH + lines * (_fontSize + paragraph) - paragraph, 52f);
			if (!row.HasVoice && SpeechAvailable)
			{
				row.Speech = ThorJapanese.Line(e);
			}
			string original = jp ? ThorJapanese.Line(e) : null;
			if (!string.IsNullOrEmpty(original))
			{
				// ruby is drawn 13px above its line, so leave room before the first line
				float rubyPad = ruby ? jpSize * RubyScale + 4f : 0f;
				row.Jp = JpLabel(ruby ? ThorFurigana.Annotate(original) : original, jpSize, jpChars, jpParagraph, JpColor, -1, ruby);
				row.Jp.Position = new Vector2(TextLeft, y + row.H + 22f + rubyPad);
				row.H += 22f + rubyPad + JpHeight(row.Jp, jpSize, jpParagraph);
			}
			_rows.Add(row);
			y += row.H + RowGap + _fontSize * 0.6f;
		}
		_log.ContentHeight = y;
		if (anchor != null)
		{
			LogRow same = _rows.FirstOrDefault(r => r.Entry == anchor);
			SetScroll(same != null ? same.Y - anchorOffset : 0);
		}
		else
		{
			SetScroll(0);
		}
		_emptyHint.Visible = _rows.Count == 0;
	}

	private void SetScroll(float value)
	{
		if (_log.SetScroll(value))
		{
			Dirty = true;
		}
	}

	/// <summary>Lists that can scroll right now, topmost first.</summary>
	private IEnumerable<ThorScrollList> ScrollLists()
	{
		foreach (ThorScrollList list in ExtraScrollLists())
		{
			yield return list;
		}
		if (_tab == Tab.Log)
		{
			yield return _log;
		}
	}

	public void ScrollBy(float delta)
	{
		ThorScrollList list = ScrollLists().FirstOrDefault();
		if (list != null)
		{
			list.Velocity = 0;
			if (list.SetScroll(list.Scroll + delta))
			{
				Dirty = true;
			}
		}
	}

	public void NextTab()
	{
		_tab = (Tab)(((int)_tab + 1) % TabCount);
		ClosePopup();
		_autoSwitched = false;
		Dirty = true;
	}

	private void DrawRows()
	{
		float viewTop = _log.Scroll - 60f;
		float viewBottom = _log.Scroll + _log.Size.Y + 60f;
		foreach (LogRow row in _rows)
		{
			bool visible = row.Y + row.H >= viewTop && row.Y <= viewBottom;
			if (row.Text != null)
			{
				row.Text.Visible = visible;
			}
			if (row.Name != null)
			{
				row.Name.Visible = visible;
			}
			if (row.Jp != null)
			{
				row.Jp.Visible = visible;
			}
			if (visible && _pressed == RowId(row))
			{
				_log.Content.DrawRect(new Rect2(VoiceLeft + 56f, row.Y - 8f, RowRight - VoiceLeft - 56f, row.H + 16f), new Color(Teal, 0.10f));
			}
			if (visible && ThorStore.IsFavorite(row.Entry))
			{
				DrawStar(_log.Content, new Vector2(VoiceLeft + 24f, row.Y + (row.IsCurrent ? 96f : 72f)), 12f);
			}
			if (visible && row.IsCurrent)
			{
				// config-page style section panel around the pinned original line
				_sectionPanel.Draw(_log.Content.GetCanvasItem(), new Rect2(16f, row.Y, RowRight - 16f + 4f, row.H));
			}
			if (visible && row.HasVoice)
			{
				int state = !row.VoiceEnabled ? 0 : _pressed == VoiceId(row) ? 2 : 1;
				_log.Content.DrawTextureRectRegion(_texVoice, VoiceRect(row), new Rect2(0, 28 * state, 28, 28));
			}
			else if (visible && row.Speech != null)
			{
				int state = _pressed == SpeechId(row) ? 2 : 1;
				_log.Content.DrawTextureRectRegion(_texVoice, VoiceRect(row), new Rect2(0, 28 * state, 28, 28), SpeechTint);
			}
		}
	}

	/// <summary>Original-language line in the game's Japanese font atlas (same glyph map).</summary>
	private const float RubyScale = 0.6f;

	private Wa2Label JpLabel(string text, int size, int maxChars, int paragraph, Color color, int segment, bool ruby)
	{
		var label = new Wa2Label
		{
			FontSize = size, MaxChars = maxChars, ParagraphSpacing = paragraph, LineSpacing = 0, Color = color,
			CenterRuby = ruby, RubyScale = RubyScale,
		};
		_log.Content.AddChild(label);
		label.FontTexture = _jpFont;
		label.ShadowTexture = _jpShadow;
		label.Segment = segment;
		label.SetText(text);
		label.SetMeta("thor_size", size);
		return label;
	}

	private static float JpHeight(Wa2Label label, int size, int paragraph)
	{
		TextParseResult r = label.Update(-1);
		int lines = (int)Mathf.Round(r.EndPosition.Y / (size + paragraph)) + 1;
		return lines * (size + paragraph) - paragraph;
	}

	private Rect2 VoiceRect(LogRow row)
	{
		return new Rect2(VoiceLeft, float.IsNaN(row.VoiceY) ? row.Y - 4f : row.VoiceY, 48f, 48f);
	}

	private bool SpeechAvailable => ThorJapanese.Available && (ThorSpeech.Plugin != null || ThorSpeech.HasNarration || MockJapanese);

	private static string SpeechId(LogRow row)
	{
		return "tts." + RuntimeHelpers.GetHashCode(row.Entry);
	}

	/// <summary>Reads the newest segment of the current line when it has no recorded voice.</summary>
	private void AutoSpeak()
	{
		if (!_speakNarration || ThorSpeech.Plugin == null || !ThorActions.InGame(E) || E.Backlogs.Count == 0)
		{
			return;
		}
		BacklogEntry cur = E.Backlogs[^1];
		int count = cur.TextIds.Count;
		if (cur == _spokenEntry && count == _spokenCount)
		{
			return;
		}
		_spokenEntry = cur;
		_spokenCount = count;
		bool voiced = cur.VoiceInfos != null && cur.VoiceInfos.Count > 0;
		string segment = count > 0 ? ThorJapanese.Segment(cur, count - 1) : null;
		if (voiced || E.SkipMode || E.Skipping || string.IsNullOrEmpty(segment))
		{
			ThorSpeech.Stop();
			return;
		}
		ThorSpeech.SpeakLine(cur.ScriptName, new[] { cur.TextIds[count - 1] }, segment);
	}

	private static string RowId(LogRow row)
	{
		return "row." + RuntimeHelpers.GetHashCode(row.Entry);
	}

	private static void DrawStar(CanvasItem canvas, Vector2 c, float r)
	{
		var pts = new Vector2[10];
		for (int i = 0; i < 10; i++)
		{
			float a = -Mathf.Pi / 2f + i * Mathf.Pi / 5f;
			float rr = i % 2 == 0 ? r : r * 0.45f;
			pts[i] = c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * rr;
		}
		canvas.DrawColoredPolygon(pts, ActiveYellow);
	}

	private static string VoiceId(LogRow row)
	{
		return "voice." + RuntimeHelpers.GetHashCode(row.Entry);
	}

	private void PlayVoices(List<VoiceInfo> infos)
	{
		_voiceQueue = infos;
		_voiceIdx = 0;
		VoiceInfo v = infos[0];
		E.SoundMgr.PlayVoice(v.Label, v.Id, v.Chr, v.Volume);
	}

	private void OnVoiceFinished()
	{
		if (_voiceQueue != null && _voiceIdx < _voiceQueue.Count - 1)
		{
			_voiceIdx++;
			VoiceInfo v = _voiceQueue[_voiceIdx];
			E.SoundMgr.PlayVoice(v.Label, v.Id, v.Chr, v.Volume);
		}
		else
		{
			_voiceQueue = null;
		}
	}

	// log toolbar: config-style ← cells → and a glass "回到最新" button
	// font size control lives in the guide page's settings panel
	private float FontRowY => _fontRowY;
	private float _fontRowY = 900f;
	private Rect2 FontLeftRect => new(220f, FontRowY, ArrowLeftSrc.Size.X * Art, ArrowLeftSrc.Size.Y * Art);
	private Rect2 FontCellsRect => new(FontLeftRect.End.X + 14f, FontRowY + 6f, FontSizes.Length * 34f, 40f);
	private Rect2 FontRightRect => new(FontCellsRect.End.X + 10f, FontRowY, ArrowRightSrc.Size.X * Art, ArrowRightSrc.Size.Y * Art);
	private Rect2 RubyRect => new(Size.X - 62f - 180f, FontRowY, 180f, GlassBtnSrc.Size.Y * Art);
	private float _speechRowY = 980f;
	private Rect2 SpeechRect => new(220f, _speechRowY, 180f, GlassBtnSrc.Size.Y * Art);
	private Rect2 JpRect => new(RubyRect.Position.X - 16f - 180f, FontRowY, 180f, GlassBtnSrc.Size.Y * Art);
	private Rect2 ScrollUpRect => new(Size.X - 64f, ListTop, 48f, 72f);
	private Rect2 ScrollDownRect => new(Size.X - 64f, ListBottom - 72f, 48f, 72f);

	private void DrawLogChrome()
	{

		if (_rows.Count > 0)
		{
			DrawScrollbar(_logPage, _log, ScrollUpRect, ScrollDownRect, "scroll");
		}
	}

	/// <summary>The game's backlog scrollbar (sys_00030): arrow buttons, track and grabber.</summary>
	private void DrawScrollbar(CanvasItem canvas, ThorScrollList list, Rect2 up, Rect2 down, string id)
	{
		var track = new Rect2(up.Position.X, up.End.Y, 48f, down.Position.Y - up.End.Y);
		canvas.DrawTextureRectRegion(_texScroll, up, new Rect2(_pressed == id + ".up" ? 64 : 0, 0, 32, 48));
		canvas.DrawTextureRectRegion(_texScroll, down, new Rect2(_pressed == id + ".down" ? 64 : 0, 592, 32, 48));
		canvas.DrawTextureRectRegion(_texScroll, track, new Rect2(0, 48, 32, 544));
		if (list.MaxScroll > 0)
		{
			float viewH = list.Size.Y;
			float knobH = Mathf.Max(90f, (track.Size.Y - 16f) * viewH / list.ContentHeight);
			float knobY = track.Position.Y + 8f + (track.Size.Y - 16f - knobH) * (list.Scroll / list.MaxScroll);
			var knob = new Rect2(track.Position.X + 12f, knobY, 24f, knobH);
			canvas.DrawTextureRectRegion(_texScroll, new Rect2(knob.Position, new Vector2(24f, 18f)), new Rect2(64, 48, 16, 12));
			canvas.DrawTextureRectRegion(_texScroll, new Rect2(knob.Position + new Vector2(0, 18f), new Vector2(24f, knobH - 36f)), new Rect2(64, 60, 16, 64));
			canvas.DrawTextureRectRegion(_texScroll, new Rect2(knob.Position + new Vector2(0, knobH - 18f), new Vector2(24f, 18f)), new Rect2(64, 124, 16, 12));
		}
	}

	/// <summary>Arrow hit areas for a list's scrollbar (page up / page down).</summary>
	private void AddScrollbarHits(ThorScrollList list, Rect2 up, Rect2 down, string id)
	{
		float page = list.Size.Y * 0.8f;
		_hits.Add(new Hit(up.Grow(8f), id + ".up", () => { list.Velocity = 0; list.SetScroll(list.Scroll - page); }, true));
		_hits.Add(new Hit(down.Grow(8f), id + ".down", () => { list.Velocity = 0; list.SetScroll(list.Scroll + page); }, true));
	}

	private void DrawArrow(CanvasItem canvas, Rect2 dst, Rect2 src, string id, bool enabled)
	{
		Color mod = !enabled ? new Color(0.6f, 0.6f, 0.6f, 0.45f) : _pressed == id ? new Color(1.35f, 1.35f, 1.2f) : Colors.White;
		canvas.DrawTextureRectRegion(_texConfig, dst, src, mod);
	}

	/// <summary>Three-slice of the game's 是/否 glass button, with the baked caption skipped.</summary>
	private void DrawGlassButton(CanvasItem canvas, Rect2 dst, string id, bool enabled)
	{
		int row = !enabled ? 0 : _pressed == id ? 2 : 0;
		var src = new Rect2(GlassBtnSrc.Position + new Vector2(0, 40 * row), GlassBtnSrc.Size);
		Color mod = enabled ? Colors.White : new Color(0.6f, 0.6f, 0.6f, 0.5f);
		float edge = 56f;
		float scale = dst.Size.Y / src.Size.Y;
		float dEdge = Mathf.Min(edge * scale, dst.Size.X / 2f);
		canvas.DrawTextureRectRegion(_texGlassBtn, new Rect2(dst.Position, new Vector2(dEdge, dst.Size.Y)), new Rect2(src.Position, new Vector2(edge, src.Size.Y)), mod);
		canvas.DrawTextureRectRegion(_texGlassBtn, new Rect2(dst.Position + new Vector2(dEdge, 0), new Vector2(dst.Size.X - 2 * dEdge, dst.Size.Y)),
			new Rect2(src.Position + new Vector2(edge, 0), new Vector2(10, src.Size.Y)), mod);
		canvas.DrawTextureRectRegion(_texGlassBtn, new Rect2(dst.End.X - dEdge, dst.Position.Y, dEdge, dst.Size.Y),
			new Rect2(src.End.X - edge, src.Position.Y, edge, src.Size.Y), mod);
	}

	// ---- command page -------------------------------------------------------

	private sealed record ActionDef(string Id, string Label, int Icon);

	private static readonly ActionDef[] ActionDefs =
	{
		new("save", "存档", 3),
		new("load", "读档", 4),
		new("qsave", "快速存档", 0),
		new("qload", "快速读档", 1),
		new("option", "设置", 2),
		new("hide", "隐藏文本", 8),
		new("auto", "自动", 6),
		new("skip", "快进", 7),
		new("back", "返回", 5),
		new("title", "回到标题", -1),
	};

	private bool ActionEnabled(string id)
	{
		switch (id)
		{
			case "save":
			case "load":
			case "qsave":
				return ThorActions.CanSaveLoad(E);
			case "qload":
				return ThorActions.CanQuickLoad(E);
			case "option":
			case "title":
				return ThorActions.AdvIdle(E);
			case "hide":
				return ThorActions.AtAdv(E) && (E.AdvMain.State == Wa2AdvMain.AdvState.WAIT_CLICK || E.AdvMain.State == Wa2AdvMain.AdvState.HIDE);
			case "auto":
			case "skip":
				return ThorActions.AtAdv(E);
			case "back":
				return ThorActions.MenuOpen(E);
			default:
				return false;
		}
	}

	private bool ActionActive(string id)
	{
		return id switch
		{
			"auto" => E.AutoMode,
			"skip" => E.SkipMode,
			"hide" => E.AdvMain.State == Wa2AdvMain.AdvState.HIDE,
			_ => false,
		};
	}

	private void RunAction(string id)
	{
		switch (id)
		{
			case "save":
				E.AdvMain.OnSaveButtonDown();
				break;
			case "load":
				E.AdvMain.OnLoadButtonDown();
				break;
			case "qsave":
				ThorActions.QuickSave(E);
				break;
			case "qload":
				ThorActions.QuickLoad(E);
				break;
			case "option":
				E.AdvMain.OnOptionButtonDown();
				break;
			case "hide":
				ThorDualScreen.ToggleWindow(E);
				break;
			case "auto":
				E.AdvMain.OnAutoButtonDown();
				break;
			case "skip":
				E.AdvMain.OnSkipButtonDown();
				break;
			case "back":
			case "title":
				E.Back();
				break;
		}
	}

	private Rect2 ActionRect(int i)
	{
		const int columns = 5;
		float cellW = (Size.X - 60f - (columns - 1) * 16f) / columns;
		float cellH = 172f;
		float top = ContentTop + 22f;
		return new Rect2(30f + (i % columns) * (cellW + 16f), top + (i / columns) * (cellH + 16f), cellW, cellH);
	}

	private float ChoicesTop => ActionRect(ActionDefs.Length - 1).End.Y + 26f;

	private Rect2 ChoiceRect(int i)
	{
		return new Rect2(24f, ChoicesTop + 50f + i * 100f, Size.X - 48f, 96f);
	}

	private void DrawActions()
	{
		for (int i = 0; i < _choiceTexts.Count; i++)
		{
			Rect2 rect = ChoiceRect(i);
			bool enabled = ThorActions.ChoiceEnabled(E, i);
			int row = _pressed == "choice." + i && enabled ? 1 : 0;
			_actionsPage.DrawTextureRectRegion(_texBars, rect, new Rect2(0, 72 * row, 784, 72), enabled ? Colors.White : new Color(0.7f, 0.7f, 0.7f, 0.6f));
			_choiceTexts[i].Modulate = enabled ? Colors.White : new Color(0.62f, 0.62f, 0.62f, 1f);
		}
		for (int i = 0; i < ActionDefs.Length; i++)
		{
			ActionDef a = ActionDefs[i];
			Rect2 rect = ActionRect(i);
			bool enabled = ActionEnabled(a.Id);
			bool pressed = enabled && _pressed == "act." + a.Id;
			bool active = ActionActive(a.Id);
			_glassPanel.ModulateColor = !enabled ? new Color(0.55f, 0.6f, 0.65f, 0.55f) : pressed ? new Color(1.3f, 1.3f, 1.3f) : Colors.White;
			_glassPanel.Draw(_actionsPage.GetCanvasItem(), rect);
			if (a.Icon >= 0)
			{
				var icon = new Rect2(rect.Position.X + (rect.Size.X - 80f) / 2f, rect.Position.Y + 20f, 80f, 80f);
				Color mod = enabled ? Colors.White : new Color(1, 1, 1, 0.4f);
				_actionsPage.DrawTextureRectRegion(_texAdvIcons, icon, new Rect2(a.Icon * 40, active ? 40 : 0, 40, 40), mod);
			}
			Color label = !enabled ? new Color(0.75f, 0.8f, 0.85f, 0.55f) : active ? ActiveYellow : TextWhite;
			_texts["act." + a.Id].Modulate = label;
			if (a.Id == "hide")
			{
				_texts["act.hide.show"].Modulate = label;
			}
		}
	}

	// ---- guide page ---------------------------------------------------------

	private static readonly string[,] HelpText =
	{
		{ "操作", "CONTROLS", "上屏：点击推进文本，在游戏设置里打开后长按可快进\n菜单和标题：十字键／左摇杆选择　A 确定　B 返回\n剧情中：A 推进　X 自动　Y 隐藏文本　按住 R1 快进\nL1 切换下屏页面　十字键上下 滚动文本记录\n查词时：十字键左右 上一句／下一句　B 关闭" },
		{ "文本记录", "BACKLOG", "最新的台词在最上面，点语音图标重播语音。\n没有配音的句子显示青色图标，点击朗读日文原文。" },
		{ "快捷操作", "COMMAND", "存档、读档、设置、自动、快进等按钮。\n出现选项时会自动切到这一页，可以直接点选。" },
	};

	private void BuildHelp()
	{
		int width = 1240 - 140;
		for (int i = 0; i < HelpText.GetLength(0); i++)
		{
			CanvasItem title = MakeText(HelpText[i, 0], 30, TextWhite);
			CanvasItem en = MakeText(HelpText[i, 1], 18, Teal, 0, _serifItalic);
			CanvasItem body = MakeText(HelpText[i, 2], 28, TextSoft, width);
			_helpPage.AddChild(title);
			_helpPage.AddChild(en);
			_helpPage.AddChild(body);
			_helpSections.Add((title, en, body));
		}
	}

	private readonly List<Rect2> _helpRects = new();

	private void DrawHelp()
	{
		foreach (Rect2 rect in _helpRects)
		{
			_sectionPanel.Draw(_helpPage.GetCanvasItem(), rect);
			_helpPage.DrawRect(new Rect2(rect.Position + new Vector2(18, 18), new Vector2(6, 34)), Teal);
		}
		if (ThorJapanese.Available)
		{
			DrawGlassButton(_helpPage, JpRect, "jp", true);
			DrawGlassButton(_helpPage, RubyRect, "ruby", _showJp && ThorFurigana.Usable);
		}
		if (SpeechAvailable)
		{
			DrawGlassButton(_helpPage, SpeechRect, "speech", true);
		}
		int idx = Array.IndexOf(FontSizes, _fontSize);
		DrawArrow(_helpPage, FontLeftRect, ArrowLeftSrc, "font-", idx > 0);
		DrawArrow(_helpPage, FontRightRect, ArrowRightSrc, "font+", idx < FontSizes.Length - 1);
		Rect2 cells = FontCellsRect;
		for (int i = 0; i < FontSizes.Length; i++)
		{
			var cell = new Rect2(cells.Position.X + i * 34f, cells.Position.Y, 28f, cells.Size.Y);
			if (i <= idx)
			{
				var top = new Color(1f, 0.9f, 0.45f);
				var bottom = new Color(0.94f, 0.5f, 0.32f);
				_helpPage.DrawPolygon(new[] { cell.Position, new Vector2(cell.End.X, cell.Position.Y), cell.End, new Vector2(cell.Position.X, cell.End.Y) },
					new[] { top, top, bottom, bottom });
			}
			else
			{
				_helpPage.DrawRect(cell, new Color(0.02f, 0.14f, 0.18f, 0.85f));
			}
			_helpPage.DrawRect(cell, new Color(1, 1, 1, 0.9f), false, 2f);
		}

	}

	// ---- layout -----------------------------------------------------------

	public void Relayout()
	{
		_hits.Clear();
		float w = Size.X;
		// the word popup covers the page; the game-font labels would otherwise show through it
		_logPage.Visible = _tab == Tab.Log && !_popupOpen;
		_favPage.Visible = _tab == Tab.Fav && !_popupOpen;
		_actionsPage.Visible = _tab == Tab.Actions;
		_helpPage.Visible = _tab == Tab.Help;
		foreach (Control page in new[] { _logPage, _favPage, _actionsPage, _helpPage })
		{
			page.Size = Size;
			page.QueueRedraw();
		}

		// header titles (Backlog uses the game's own title art)
		HeaderTitle("title.actions.en", "title.actions");
		HeaderTitle("title.help.en", "title.help");

		// tabs
		for (int i = 0; i < TabCount; i++)
		{
			Rect2 rect = TabRect(i);
			Tab t = (Tab)i;
			_hits.Add(new Hit(rect, "tab" + i, () => { _tab = t; _autoSwitched = false; ClosePopup(); }, true));
			Vector2 zh = TextSize(_texts["tab" + i]);
			Vector2 en = TextSize(_texts["tab" + i + ".en"]);
			float total = zh.X + 12f + en.X;
			float x = rect.Position.X + (rect.Size.X - total) / 2f;
			SetPos(_texts["tab" + i], new Vector2(x, rect.Position.Y + (rect.Size.Y - zh.Y) / 2f - 3f));
			SetPos(_texts["tab" + i + ".en"], new Vector2(x + zh.X + 12f, rect.Position.Y + (rect.Size.Y - en.Y) / 2f + 2f));
			Color c = _tab == t ? TextWhite : new Color(0.66f, 0.76f, 0.86f);
			_texts["tab" + i].Modulate = c;
			_texts["tab" + i + ".en"].Modulate = _tab == t ? Teal : new Color(0.5f, 0.6f, 0.7f);
		}

		// footer status (where in-game pages show EXIT / DEFAULT)
		SetText("status", _toast != "" ? _toast : ThorActions.StatusText(E));
		Vector2 st = TextSize(_texts["status"]);
		SetPos(_texts["status"], new Vector2(w - 36f - st.X, Size.Y - FooterH + (FooterH - st.Y) / 2f + 8f));

		RelayoutPopup();
		if (_popupOpen)
		{
			// the popup covers the page: only tabs and popup controls react
		}
		else if (_tab == Tab.Fav)
		{
			RelayoutFav();
		}
		else if (_tab == Tab.Log)
		{
			if (_rows.Count > 0)
			{
				AddScrollbarHits(_log, ScrollUpRect, ScrollDownRect, "scroll");
			}

			_log.Layout(new Rect2(0, ListTop, w - 76f, ListBottom - ListTop));
			SetPos(_emptyHint, new Vector2(TextLeft, ListTop + 40f));
			foreach (LogRow row in _rows)
			{
				if (_log.ToOwner(new Rect2(VoiceLeft + 60f, row.Y - 8f, RowRight - VoiceLeft - 60f, row.H + 16f)) is { } rowRect)
				{
					BacklogEntry entry = row.Entry;
					bool lookup = !string.IsNullOrEmpty(ThorJapanese.Line(entry)) && (ThorDictionary.Plugin != null || ThorDictionary.Mock);
					_hits.Add(new Hit(rowRect, RowId(row), () => OpenPopup(entry), lookup,
						() => Toast(ThorStore.ToggleFavorite(entry) ? "已收藏这句台词" : "已取消收藏")));
				}
				bool speech = !row.HasVoice && row.Speech != null;
				if (!speech && (!row.HasVoice || !row.VoiceEnabled))
				{
					continue;
				}
				Rect2 r = VoiceRect(row);
				r.Position += new Vector2(0, ListTop - _log.Scroll);
				r = r.Grow(16f);
				if (r.End.Y < ListTop || r.Position.Y > ListBottom)
				{
					continue;
				}
				if (speech)
				{
					string text = row.Speech;
					BacklogEntry said = row.Entry;
					_hits.Add(new Hit(r, SpeechId(row), () => ThorSpeech.SpeakLine(said.ScriptName, said.TextIds, text), true));
					continue;
				}
				List<VoiceInfo> infos = row.Entry.VoiceInfos;
				_hits.Add(new Hit(r, VoiceId(row), () => PlayVoices(infos), true));
			}
		}
		else if (_tab == Tab.Actions)
		{
			SetPos(_texts["choose"], new Vector2(40f, ChoicesTop + 6f));
			_texts["choose"].Visible = _choiceTexts.Count > 0;
			for (int i = 0; i < _choiceTexts.Count; i++)
			{
				Rect2 rect = ChoiceRect(i);
				SetPos(_choiceTexts[i], new Vector2(rect.Position.X + 110f, rect.Position.Y + (rect.Size.Y - 38f) / 2f - 2f));
				int idx = i;
				_hits.Add(new Hit(rect, "choice." + i, () => E.AdvMain.OnSelectMessageButtonDown(idx), ThorActions.ChoiceEnabled(E, i)));
			}
			bool hidden = ActionActive("hide");
			for (int i = 0; i < ActionDefs.Length; i++)
			{
				ActionDef a = ActionDefs[i];
				Rect2 rect = ActionRect(i);
				var labelRect = a.Icon >= 0
					? new Rect2(rect.Position.X, rect.Position.Y + 108f, rect.Size.X, 46f)
					: rect;
				CenterText("act." + a.Id, labelRect);
				if (a.Id == "hide")
				{
					CenterText("act.hide.show", labelRect);
				}
				string id = a.Id;
				_hits.Add(new Hit(rect, "act." + id, () => RunAction(id), ActionEnabled(id)));
			}
			_texts["act.hide"].Visible = !hidden;
			_texts["act.hide.show"].Visible = hidden;
		}
		else
		{
			_helpRects.Clear();
			float y = ContentTop + 14f;
			foreach (var (title, en, body) in _helpSections)
			{
				Vector2 ts = TextSize(title);
				SetPos(title, new Vector2(70f, y + 16f));
				SetPos(en, new Vector2(70f + ts.X + 16f, y + 16f + ts.Y - TextSize(en).Y - 4f));
				SetPos(body, new Vector2(70f, y + 64f));
				float h = 64f + TextSize(body).Y + 22f;
				_helpRects.Add(new Rect2(30f, y, w - 60f, h));
				y += h + 14f;
			}
			// settings: 文字大小 (backlog text size)
			var settings = new Rect2(30f, y, w - 60f, 92f + 76f);
			_helpRects.Add(settings);
			_fontRowY = y + (92f - ArrowLeftSrc.Size.Y * Art) / 2f;
			Vector2 fs = TextSize(_texts["fontsize"]);
			SetPos(_texts["fontsize"], new Vector2(70f, y + (92f - fs.Y) / 2f));
			int idx = Array.IndexOf(FontSizes, _fontSize);
			_hits.Add(new Hit(FontLeftRect.Grow(10f), "font-", () => SetFontSize(-1), idx > 0));
			_hits.Add(new Hit(FontRightRect.Grow(10f), "font+", () => SetFontSize(+1), idx < FontSizes.Length - 1));
			bool rubyUsable = _showJp && ThorFurigana.Usable;
			CenterText("jp", JpRect);
			CenterText("ruby", RubyRect);
			_texts["jp"].Visible = ThorJapanese.Available;
			_texts["ruby"].Visible = ThorJapanese.Available;
			_texts["jp"].Modulate = _showJp ? ActiveYellow : TextWhite;
			_texts["ruby"].Modulate = !rubyUsable ? new Color(0.75f, 0.8f, 0.85f, 0.5f) : _showRuby ? ActiveYellow : TextWhite;
			if (ThorJapanese.Available)
			{
				_hits.Add(new Hit(JpRect.Grow(8f), "jp", () => { _showJp = !_showJp; SaveConfig(); }, true));
				_hits.Add(new Hit(RubyRect.Grow(8f), "ruby", () => { _showRuby = !_showRuby; SaveConfig(); }, rubyUsable));
			}
			_speechRowY = y + 92f + (76f - GlassBtnSrc.Size.Y * Art) / 2f - 14f;
			Vector2 sl = TextSize(_texts["speech.label"]);
			SetPos(_texts["speech.label"], new Vector2(70f, SpeechRect.Position.Y + (SpeechRect.Size.Y - sl.Y) / 2f));
			CenterText("speech", SpeechRect);
			int ttsState = ThorSpeech.State;
			bool narration = ThorSpeech.HasNarration;
			bool speechUsable = SpeechAvailable && (narration || ttsState != -1 && ttsState != -2);
			_texts["speech"].Modulate = !speechUsable ? new Color(0.75f, 0.8f, 0.85f, 0.5f) : _speakNarration ? ActiveYellow : TextWhite;
			bool needVoice = !narration && ttsState is -2 or -3;
			SetText("speech.hint", narration ? "无配音的旁白用预生成的春希语音朗读"
				: needVoice ? "日语语音包未下载，点这里到上屏安装"
				: ttsState == -1 && ThorSpeech.Plugin != null ? "系统语音引擎不可用"
				: "无配音的句子出现时朗读日文原文");
			_texts["speech.hint"].Modulate = needVoice ? ActiveYellow : TextWhite;
			Vector2 sh = TextSize(_texts["speech.hint"]);
			var hintPos = new Vector2(SpeechRect.End.X + 20f, SpeechRect.Position.Y + (SpeechRect.Size.Y - sh.Y) / 2f);
			SetPos(_texts["speech.hint"], hintPos);
			if (needVoice)
			{
				// Google's default Japanese voice is a network voice; the offline one has to be downloaded
				var hintRect = new Rect2(hintPos - new Vector2(10f, 14f), sh + new Vector2(20f, 28f));
				_hits.Add(new Hit(hintRect, "speech.install", ThorSpeech.OpenInstall, true));
			}
			_hits.Add(new Hit(SpeechRect.Grow(8f), "speech", () =>
			{
				_speakNarration = !_speakNarration;
				_spokenEntry = null;
				if (!_speakNarration)
				{
					ThorSpeech.Stop();
				}
				SaveConfig();
			}, speechUsable));
		}
		QueueRedraw();
	}

	private Rect2 TabRect(int i)
	{
		float tabW = (Size.X - 24f) / TabCount;
		return new Rect2(12f + i * tabW, HeaderH + 12f, tabW, 72f);
	}

	private void HeaderTitle(string enId, string zhId)
	{
		Vector2 en = TextSize(_texts[enId]);
		SetPos(_texts[enId], new Vector2(54f, (HeaderH - en.Y) / 2f - 4f));
		Vector2 zh = TextSize(_texts[zhId]);
		SetPos(_texts[zhId], new Vector2(54f + en.X + 12f, HeaderH - zh.Y - 10f));
	}

	private void SetFontSize(int step)
	{
		int idx = Math.Clamp(Array.IndexOf(FontSizes, _fontSize) + step, 0, FontSizes.Length - 1);
		_fontSize = FontSizes[idx];
		SaveConfig();
	}

	public override void _Draw()
	{
		float w = Size.X;
		float h = Size.Y;
		// navy backlog column behind every page, as in the game's text history
		DrawRect(new Rect2(0, 0, w, h), new Color(0.09f, 0.15f, 0.22f));
		DrawTextureRect(_texColumn, new Rect2(0, HeaderH, w, h - HeaderH - FooterH), false);
		if (_tab is Tab.Actions or Tab.Help || (_tab == Tab.Fav && _favRows.Count == 0))
		{
			DrawLogoWatermark(h - FooterH);
		}

		// page header strip: left half keeps the title art, right half the WHITE ALBUM 2 logo
		// (the 1.5x strip is wider than this screen, so the two halves are cross-faded)
		Texture2D top = _tab == Tab.Log ? _texBacklogTop : _texPlainTop;
		float leftSrcX = _tab == Tab.Log ? 0f : 430f;
		const float fade = 120f;
		float seam = w / 2f;
		DrawStrip(top, seam - fade / 2f, w, 1280f - (w - (seam - fade / 2f)) / Art, 1f, 1f);
		DrawStrip(top, 0, seam - fade / 2f, leftSrcX, 1f, 1f);
		DrawStrip(top, seam - fade / 2f, seam + fade / 2f, leftSrcX + (seam - fade / 2f) / Art, 1f, 0f);

		// tabs on the game's choice bars
		for (int i = 0; i < TabCount; i++)
		{
			Rect2 rect = TabRect(i);
			bool active = (int)_tab == i;
			bool pressed = _pressed == "tab" + i;
			Color mod = active ? Colors.White : pressed ? new Color(1.2f, 1.2f, 1.2f) : new Color(0.8f, 0.85f, 0.9f, 0.9f);
			DrawTextureRectRegion(_texBars, rect.Grow(6f), new Rect2(0, active ? 0 : 72, 784, 72), mod);
		}

		// Leaf footer with the BGM title, like every in-game page
		float footY = h - FooterH;
		DrawTextureRectRegion(_texFooter, new Rect2(0, footY, w, FooterH), new Rect2(640 - w / 2f / Art, 0, w / Art, 56));
		int pos = Array.IndexOf(Wa2Def.BgmSlot, E.SoundMgr.BgmId);
		if (pos >= 0)
		{
			if (!NativeMeter)
			{
				// desktop preview: static meter (on the Thor the bridge animates it)
				DrawTextureRectRegion(_texBgmMeter, new Rect2(MeterRect.Position, new Vector2(MeterRect.Size.X * 0.6f, MeterRect.Size.Y)),
					new Rect2(0, 0, 192 * 0.6f, 8));
			}
			DrawTextureRectRegion(_texBgmNames, new Rect2(24f, footY + 48f, 264f * Art, 16f * Art), new Rect2(0, 16 * pos, 264, 16));
		}
	}

	/// <summary>When true the BGM level meter is drawn live by the Android bridge, not in this frame.</summary>
	public bool NativeMeter;

	/// <summary>The game's BGM meter (sys_00301, a left-to-right progress bar) in the footer.</summary>
	public Rect2 MeterRect => new(24f, Size.Y - FooterH + 30f, 192f * Art, 8f * Art);

	public bool BgmTitleShown => Array.IndexOf(Wa2Def.BgmSlot, E.SoundMgr.BgmId) >= 0;

	/// <summary>Meter sprite at its on-screen size, RGBA8.</summary>
	public Image MeterImage()
	{
		Image img = _texBgmMeter.GetImage();
		img.Convert(Image.Format.Rgba8);
		img.Resize((int)MeterRect.Size.X, (int)MeterRect.Size.Y, Image.Interpolation.Bilinear);
		img.PremultiplyAlpha(); // Android ARGB_8888 bitmaps are premultiplied
		return img;
	}

	/// <summary>Header strip slice at 1.5x from source x, with a horizontal alpha ramp.</summary>
	private void DrawStrip(Texture2D tex, float x0, float x1, float srcX, float alpha0, float alpha1)
	{
		float tw = tex.GetWidth();
		float th = tex.GetHeight();
		float u0 = srcX / tw;
		float u1 = (srcX + (x1 - x0) / Art) / tw;
		float v1 = 40f / th;
		var a = new Color(1, 1, 1, alpha0);
		var b = new Color(1, 1, 1, alpha1);
		DrawPolygon(
			new[] { new Vector2(x0, 0), new Vector2(x1, 0), new Vector2(x1, HeaderH), new Vector2(x0, HeaderH) },
			new[] { a, b, b, a },
			new[] { new Vector2(u0, 0), new Vector2(u1, 0), new Vector2(u1, v1), new Vector2(u0, v1) },
			tex);
	}

	// ---- touch ------------------------------------------------------------

	public void HandleTouch(int action, Vector2 pos)
	{
		switch (action)
		{
			case ThorDualScreen.TouchDown:
				_touching = true;
				_dragging = false;
				_downPos = pos;
				_lastPos = pos;
				_dragList = ScrollLists().FirstOrDefault(l => l.Contains(pos));
				_downScroll = _dragList?.Scroll ?? 0;
				if (_dragList != null)
				{
					_dragList.Velocity = 0;
				}
				_lastMoveMs = Time.GetTicksMsec();
				_downMs = _lastMoveMs;
				_longFired = false;
				Relayout();
				_pressed = HitAt(pos)?.Id ?? "";
				Dirty = true;
				break;
			case ThorDualScreen.TouchMove:
				if (!_touching)
				{
					return;
				}
				if (!_dragging && _dragList != null && Mathf.Abs(pos.Y - _downPos.Y) > TapSlop)
				{
					_dragging = true;
					_pressed = "";
					Dirty = true;
				}
				if (_dragging)
				{
					ulong now = Time.GetTicksMsec();
					float dt = Mathf.Max(1, now - _lastMoveMs) / 1000f;
					float v = -(pos.Y - _lastPos.Y) / dt;
					_dragList.Velocity = Mathf.Lerp(_dragList.Velocity, v, 0.5f);
					_lastMoveMs = now;
					if (_dragList.SetScroll(_downScroll - (pos.Y - _downPos.Y)))
					{
						Dirty = true;
					}
				}
				else if (_pressed != "" && HitAt(pos)?.Id != _pressed)
				{
					_pressed = "";
					Dirty = true;
				}
				_lastPos = pos;
				break;
			case ThorDualScreen.TouchUp:
			case ThorDualScreen.TouchCancel:
				if (!_touching)
				{
					return;
				}
				_touching = false;
				if (_dragging)
				{
					if (Time.GetTicksMsec() - _lastMoveMs > 80)
					{
						_dragList.Velocity = 0;
					}
				}
				else if (action == ThorDualScreen.TouchUp && _pressed != "" && !_longFired)
				{
					Hit? hit = HitAt(pos);
					if (hit is { } h && h.Id == _pressed && h.Enabled)
					{
						h.Act();
					}
				}
				_dragging = false;
				_pressed = "";
				Dirty = true;
				break;
		}
	}

	private Hit? HitAt(Vector2 pos)
	{
		for (int i = _hits.Count - 1; i >= 0; i--)
		{
			if (_hits[i].Rect.HasPoint(pos))
			{
				return _hits[i];
			}
		}
		return null;
	}

	// ---- text helpers -----------------------------------------------------

	/// <summary>
	/// Game text (names, lines, choices) is stored in the Chinese patch's glyph
	/// encoding, so it must go through the game bitmap font.
	/// </summary>
	private static Wa2Label MakeGameText(string text, int size, Color color, int maxChars)
	{
		var label = new Wa2Label
		{
			FontSize = size,
			MaxChars = maxChars,
			ParagraphSpacing = (int)Mathf.Round(size * 0.34f),
			LineSpacing = 0,
			Color = color,
		};
		label.SetText(text);
		label.SetMeta("thor_size", size);
		return label;
	}

	/// <summary>UI strings: the patch remaps kanji slots, so they use a system CJK font.</summary>
	private Label MakeText(string text, int size, Color color, int wrapWidth = 0, Font font = null)
	{
		var plain = new Label
		{
			Text = text,
			MouseFilter = Control.MouseFilterEnum.Ignore,
			AutowrapMode = wrapWidth > 0 ? TextServer.AutowrapMode.WordSmart : TextServer.AutowrapMode.Off,
		};
		plain.AddThemeFontOverride("font", font ?? _sans);
		plain.AddThemeFontSizeOverride("font_size", size);
		plain.AddThemeColorOverride("font_color", color);
		plain.AddThemeColorOverride("font_shadow_color", Shadow);
		plain.AddThemeConstantOverride("shadow_offset_x", 2);
		plain.AddThemeConstantOverride("shadow_offset_y", 2);
		plain.AddThemeConstantOverride("shadow_outline_size", 4);
		if (wrapWidth > 0)
		{
			plain.Size = new Vector2(wrapWidth, 0);
			plain.CustomMinimumSize = new Vector2(wrapWidth, 0);
		}
		return plain;
	}

	/// <summary>Japanese in a system font (dictionary words, names), shaped as Japanese.</summary>
	private Label MakeJpText(string text, int size, Color color, int wrapWidth = 0)
	{
		Label label = MakeText(text, size, color, wrapWidth, _sansJp);
		label.Language = "ja";
		return label;
	}

	private static Vector2 TextSize(CanvasItem item)
	{
		if (item is Wa2Label w)
		{
			int size = (int)w.GetMeta("thor_size");
			return new Vector2(w.Text.Length * size, size);
		}
		if (item is Label l)
		{
			return l.GetCombinedMinimumSize();
		}
		return Vector2.Zero;
	}

	private void AddText(string id, string text, int size, Color color, Node parent, Font font = null)
	{
		Label item = MakeText(text, size, color, 0, font);
		item.Name = id.Replace('.', '_');
		parent.AddChild(item);
		_texts[id] = item;
	}

	private void SetText(string id, string text)
	{
		if (_texts[id] is Label label && label.Text != text)
		{
			label.Text = text;
			label.ResetSize();
		}
	}

	private static void SetPos(CanvasItem item, Vector2 pos)
	{
		if (item is Node2D node)
		{
			node.Position = pos;
		}
		else if (item is Control control)
		{
			control.Position = pos;
		}
	}

	private void CenterText(string id, Rect2 rect)
	{
		CanvasItem item = _texts[id];
		SetPos(item, rect.Position + (rect.Size - TextSize(item)) / 2f);
	}

	private Control Page(string name)
	{
		var page = new Control { Name = name, MouseFilter = MouseFilterEnum.Ignore };
		AddChild(page);
		return page;
	}

	private void LoadConfig()
	{
		var cfg = new ConfigFile();
		if (cfg.Load(ConfigPath) == Error.Ok)
		{
			_showJp = (bool)cfg.GetValue("log", "japanese", false);
			_showRuby = (bool)cfg.GetValue("log", "furigana", true);
			_speakNarration = (bool)cfg.GetValue("log", "speak_narration", false);
			int size = (int)cfg.GetValue("log", "font_size", 40);
			if (Array.IndexOf(FontSizes, size) >= 0)
			{
				_fontSize = size;
			}
		}
	}

	private void SaveConfig()
	{
		var cfg = new ConfigFile();
		cfg.SetValue("log", "font_size", _fontSize);
		cfg.SetValue("log", "japanese", _showJp);
		cfg.SetValue("log", "furigana", _showRuby);
		cfg.SetValue("log", "speak_narration", _speakNarration);
		cfg.Save(ConfigPath);
	}

	// ---- desktop mock -----------------------------------------------------

	public static bool MockJapanese;
	public static bool MockWords;

	public static void FillMockBacklog(Wa2EngineMain e)
	{
		if (e.Backlogs.Count > 0)
		{
			return;
		}
		string[][] lines =
		{
			new[] { "", "ゆきが　ふりはじめた。まどのそとの　まちは　しずかで、がいとうだけが　ひかっている。" },
			new[] { "ハルキ", "……トウマ、まだ　いるのか？" },
			new[] { "カズサ", "いるよ。さきに　かえると　おもった？" },
			new[] { "", "そう　いって、かのじょは　てを　けんばんに　もどした。" },
			new[] { "セツナ", "ハルキくん、カズサ、いっしょに　うたおう。あのときみたいに。" },
			new[] { "ハルキ", "ああ。" },
			new[] { "", "いま　ひょうじちゅうの　いちぎょうは　きろくに　でない。" },
		};
		int textId = 0;
		foreach (string[] l in lines)
		{
			// mock entries point at real lines of script 1001 so the original-text view can be checked
			var entry = new BacklogEntry { Name = l[0], Text = l[1], Segment = -1, ScriptName = "1001" };
			entry.TextIds.Add(textId++);
			if (l[0] != "")
			{
				entry.VoiceInfos.Add(new VoiceInfo { Chr = 1, Id = 1, Label = 1, Volume = 256 });
			}
			e.Backlogs.Add(entry);
		}
		// sample favourites and words for the desktop preview
		ThorStore.Load();
		if (ThorStore.Lines.Count == 0)
		{
			ThorStore.ToggleFavorite(e.Backlogs[1]);
			ThorStore.ToggleFavorite(e.Backlogs[3]);
		}
		if (ThorStore.Words.Count == 0)
		{
			ThorStore.AddWord(new WordItem { Word = "見上げる", Reading = "みあげる", Pos = "动词", Meaning = "仰望，抬头看；景仰，钦佩\nto look up at", Sentence = "俺は、そんな白さの源を探すため、\\n真上にある空を見上げる。" });
			ThorStore.AddWord(new WordItem { Word = "粉雪", Reading = "こなゆき", Pos = "名词", Meaning = "小雪；细雪", Sentence = "空から降りてくる粉雪は、" });
		}
	}
}
