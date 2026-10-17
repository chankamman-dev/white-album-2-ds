// SPDX-License-Identifier: Apache-2.0
using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

/// <summary>
/// 收藏 page (favourite lines / word list) and the 查词 popup that breaks a Japanese line into words.
/// </summary>
public partial class ThorBottomUi
{
	private enum FavMode { Lines, Words }

	private sealed class ListButton
	{
		public Rect2 Rect;
		public string Id;
		public CanvasItem Text;
		public Action Act;
		public bool Enabled = true;
	}

	private sealed class SimpleRow
	{
		public float Y;
		public float H;
		public readonly List<CanvasItem> Nodes = new();
		public readonly List<ListButton> Buttons = new();
		public Rect2? Icon;
		public bool IconSpeech;
		public string IconId;
		public Action IconAct;
		public Rect2? Tap;
		public string TapId;
		public Action TapAct;
	}

	private Control _favPage;
	private ThorScrollList _favList;
	private readonly List<SimpleRow> _favRows = new();
	private FavMode _favMode = FavMode.Lines;
	private string _favBuilt = "";
	private string _armedDelete = "";
	private ulong _armedUntil;

	private Control _popup;
	private ThorScrollList _popupList;
	private readonly List<SimpleRow> _popupRows = new();
	private bool _popupOpen;
	private string _popupSentence = "";
	private bool _popupLoaded;
	private double _popupRetry;

	private string _toast = "";
	private ulong _toastUntil;
	private Texture2D _texLogo;

	private const float FavListTop = ContentTop + 84f;

	private void InitPages()
	{
		_texLogo = Grp("sys_04010");
		_favPage = Page("FavPage");
		_favPage.Draw += DrawFavChrome;
		_favList = new ThorScrollList { Name = "FavList" };
		_favPage.AddChild(_favList);
		_favList.Content.Draw += () => DrawSimpleRows(_favList, _favRows);
		AddText("title.fav.en", "Favorites", 40, TextWhite, _favPage, _serifItalic);
		AddText("title.fav", "收藏", 22, TextWhite, _favPage);
		AddText("fav.lines", "名台词", 30, TextWhite, _favPage);
		AddText("fav.words", "生词本", 30, TextWhite, _favPage);
		AddText("fav.hint", "", 24, TextSoft, _favPage);

		_popup = Page("Popup");
		_popup.Draw += DrawPopupChrome;
		_popupList = new ThorScrollList { Name = "PopupList" };
		_popup.AddChild(_popupList);
		_popupList.Content.Draw += () => DrawSimpleRows(_popupList, _popupRows);
		AddText("popup.title", "查词", 32, TextWhite, _popup);
		AddText("popup.credit", "词典：JMdict（EDRDG）、中文维基词典 · CC BY-SA", 20, TextSoft, _popup);
		AddText("popup.close", "关闭", 30, TextWhite, _popup);
		AddText("popup.wait", "正在加载词典……", 30, TextSoft, _popup);
		_popup.Visible = false;
		ThorStore.Load();
		if (MockWords)
		{
			_favMode = FavMode.Words;
		}
	}

	private IEnumerable<ThorScrollList> ExtraScrollLists()
	{
		if (_popupOpen)
		{
			yield return _popupList;
			yield break;
		}
		if (_tab == Tab.Fav)
		{
			yield return _favList;
		}
	}

	private void Toast(string text)
	{
		_toast = text;
		_toastUntil = Time.GetTicksMsec() + 2500;
		Dirty = true;
	}

	private void TickPages(double delta)
	{
		if (_toast != "" && Time.GetTicksMsec() > _toastUntil)
		{
			_toast = "";
			Dirty = true;
		}
		if (_armedDelete != "" && Time.GetTicksMsec() > _armedUntil)
		{
			_armedDelete = "";
			_favBuilt = "";
		}
		if (_tab == Tab.Fav)
		{
			string sig = string.Join("|", _favMode, ThorStore.Version, _fontSize, _showRuby, ThorFurigana.Ready, _armedDelete, Size);
			if (sig != _favBuilt)
			{
				_favBuilt = sig;
				BuildFavRows();
				Dirty = true;
			}
		}
		if (_popupOpen && !_popupLoaded)
		{
			_popupRetry -= delta;
			if (_popupRetry <= 0)
			{
				_popupRetry = 0.4;
				LoadPopup();
			}
		}
	}

	// ---- rows -----------------------------------------------------------------

	private static void ClearRows(List<SimpleRow> rows)
	{
		foreach (SimpleRow r in rows)
		{
			foreach (CanvasItem n in r.Nodes)
			{
				n.QueueFree();
			}
			foreach (ListButton b in r.Buttons)
			{
				b.Text?.QueueFree();
			}
		}
		rows.Clear();
	}

	private ListButton RowButton(ThorScrollList list, SimpleRow row, string id, string label, float right, float y, Action act, bool active = false)
	{
		Label text = MakeText(label, 26, active ? ActiveYellow : TextWhite);
		list.Content.AddChild(text);
		Vector2 ts = TextSize(text);
		float w = Mathf.Max(120f, ts.X + 44f);
		var rect = new Rect2(right - w, y, w, 44f);
		SetPos(text, rect.Position + (rect.Size - ts) / 2f);
		var b = new ListButton { Rect = rect, Id = id, Text = text, Act = act };
		row.Buttons.Add(b);
		return b;
	}

	private CanvasItem AddNode(ThorScrollList list, SimpleRow row, CanvasItem node, Vector2 pos)
	{
		if (node.GetParent() == null)
		{
			list.Content.AddChild(node);
		}
		SetPos(node, pos);
		row.Nodes.Add(node);
		return node;
	}

	/// <summary>A Japanese label in the game font, or the system font when a glyph is missing from it.</summary>
	private CanvasItem JpWord(ThorScrollList list, string text, int size, Color color)
	{
		bool covered = text.All(c => Wa2Def.FontMap.ContainsKey(c));
		if (!covered)
		{
			Label l = MakeText(text, size, color);
			list.Content.AddChild(l);
			return l;
		}
		var label = new Wa2Label { FontSize = size, MaxChars = 999, ParagraphSpacing = 8, LineSpacing = 0, Color = color };
		list.Content.AddChild(label);
		label.FontTexture = _jpFont;
		label.ShadowTexture = _jpShadow;
		label.Segment = -1;
		label.SetText(text);
		label.SetMeta("thor_size", size);
		return label;
	}

	private float JpBlock(ThorScrollList list, SimpleRow row, string jp, float y, int size, Color color, float left, float right)
	{
		bool ruby = _showRuby && ThorFurigana.Usable && ThorFurigana.Ready;
		int paragraph = ruby ? (int)(size * RubyScale) + 10 : Math.Max(14, size / 2);
		float pad = ruby ? size * RubyScale + 4f : 0f;
		int chars = Math.Max(8, (int)((right - left) / size));
		Wa2Label label = JpLabelIn(list, ruby ? ThorFurigana.Annotate(jp) : jp, size, chars, paragraph, color, ruby);
		label.Position = new Vector2(left, y + pad);
		row.Nodes.Add(label);
		return pad + JpHeight(label, size, paragraph);
	}

	private Wa2Label JpLabelIn(ThorScrollList list, string text, int size, int maxChars, int paragraph, Color color, bool ruby)
	{
		var label = new Wa2Label
		{
			FontSize = size, MaxChars = maxChars, ParagraphSpacing = paragraph, LineSpacing = 0, Color = color,
			CenterRuby = ruby, RubyScale = RubyScale,
		};
		list.Content.AddChild(label);
		label.FontTexture = _jpFont;
		label.ShadowTexture = _jpShadow;
		label.Segment = -1;
		label.SetText(text);
		label.SetMeta("thor_size", size);
		return label;
	}

	private void DrawSimpleRows(ThorScrollList list, List<SimpleRow> rows)
	{
		Control c = list.Content;
		float top = list.Scroll - 80f;
		float bottom = list.Scroll + list.Size.Y + 80f;
		foreach (SimpleRow row in rows)
		{
			bool visible = row.Y + row.H >= top && row.Y <= bottom;
			foreach (CanvasItem n in row.Nodes)
			{
				n.Visible = visible;
			}
			foreach (ListButton b in row.Buttons)
			{
				if (b.Text != null)
				{
					b.Text.Visible = visible;
				}
			}
			if (!visible)
			{
				continue;
			}
			_sectionPanel.Draw(c.GetCanvasItem(), new Rect2(10f, row.Y, c.Size.X - 20f, row.H));
			if (row.Tap is { } tap && _pressed == row.TapId)
			{
				c.DrawRect(tap, new Color(Teal, 0.12f));
			}
			if (row.Icon is { } icon)
			{
				int state = _pressed == row.IconId ? 2 : 1;
				c.DrawTextureRectRegion(_texVoice, icon, new Rect2(0, 28 * state, 28, 28), row.IconSpeech ? SpeechTint : Colors.White);
			}
			foreach (ListButton b in row.Buttons)
			{
				DrawGlassButton(c, b.Rect, b.Id, b.Enabled);
			}
		}
	}

	private void AddRowHits(ThorScrollList list, List<SimpleRow> rows)
	{
		foreach (SimpleRow row in rows)
		{
			if (row.Tap is { } tap && list.ToOwner(tap) is { } tr)
			{
				_hits.Add(new Hit(tr, row.TapId, row.TapAct, true));
			}
			if (row.Icon is { } icon && list.ToOwner(icon.Grow(14f)) is { } ir)
			{
				_hits.Add(new Hit(ir, row.IconId, row.IconAct, true));
			}
			foreach (ListButton b in row.Buttons)
			{
				if (list.ToOwner(b.Rect.Grow(6f)) is { } br)
				{
					_hits.Add(new Hit(br, b.Id, b.Act, b.Enabled));
				}
			}
		}
	}

	// ---- 收藏 page ---------------------------------------------------------------

	private void BuildFavRows()
	{
		ClearRows(_favRows);
		float w = Size.X - 76f;
		float right = w - 30f;
		float y = 10f;
		if (_favMode == FavMode.Lines)
		{
			int nameSize = Math.Max(28, (int)Mathf.Round(_fontSize * 0.85f));
			int paragraph = (int)Mathf.Round(_fontSize * 0.42f);
			foreach (FavLine line in ThorStore.Lines)
			{
				var row = new SimpleRow { Y = y };
				float cy = y + 22f;
				FavLine item = line;
				string key = "fav." + RuntimeHelpers.GetHashCode(item);
				if (item.Voices.Count > 0)
				{
					row.Icon = new Rect2(VoiceLeft, cy - 4f, 48f, 48f);
					row.IconId = key + ".voice";
					row.IconAct = () => PlayVoices(item.Voices);
				}
				else if (!string.IsNullOrEmpty(item.JpText) && SpeechAvailable)
				{
					row.Icon = new Rect2(VoiceLeft, cy - 4f, 48f, 48f);
					row.IconSpeech = true;
					row.IconId = key + ".tts";
					row.IconAct = () => ThorSpeech.Speak(item.JpText);
				}
				if (!string.IsNullOrWhiteSpace(item.Name))
				{
					AddNode(_favList, row, MakeGameText(item.Name, nameSize, TextSoft, 999), new Vector2(TextLeft, cy));
					cy += nameSize + 14f;
				}
				var cn = new Wa2Label
				{
					FontSize = _fontSize, MaxChars = Math.Max(8, (int)((right - TextLeft) / _fontSize)),
					ParagraphSpacing = paragraph, LineSpacing = 0, Color = TextWhite,
				};
				_favList.Content.AddChild(cn);
				cn.Segment = item.Segment;
				TextParseResult r = cn.SetText(item.Text);
				AddNode(_favList, row, cn, new Vector2(TextLeft, cy));
				cy += ((int)Mathf.Round(r.EndPosition.Y / (_fontSize + paragraph)) + 1) * (_fontSize + paragraph) - paragraph;
				if (!string.IsNullOrEmpty(item.JpText))
				{
					cy += 18f;
					int jpSize = Math.Max(30, (int)Mathf.Round(_fontSize * 0.85f));
					cy += JpBlock(_favList, row, item.JpText, cy, jpSize, JpColor, TextLeft, right);
				}
				cy += 16f;
				string del = key + ".del";
				RowButton(_favList, row, del, _armedDelete == del ? "确认删除" : "删除", right, cy, () => ArmOrDelete(del, () => ThorStore.RemoveLine(item)), _armedDelete == del);
				cy += 44f + 18f;
				row.H = cy - y;
				_favRows.Add(row);
				y = cy + 14f;
			}
		}
		else
		{
			foreach (WordItem word in ThorStore.Words)
			{
				var row = new SimpleRow { Y = y };
				WordItem item = word;
				string key = "word." + RuntimeHelpers.GetHashCode(item);
				y = WordRow(_favList, row, y, item.Word, item.Reading, item.Pos, item.Meaning, item.Sentence, right);
				float by = y - 62f;
				string del = key + ".del";
				ListButton d = RowButton(_favList, row, del, _armedDelete == del ? "确认删除" : "删除", right, by,
					() => ArmOrDelete(del, () => ThorStore.RemoveWord(item)), _armedDelete == del);
				RowButton(_favList, row, key + ".anki", item.Anki ? "已发送 Anki" : "发送到 Anki", d.Rect.Position.X - 14f, by,
					() => SendAnki(item), item.Anki);
				_favRows.Add(row);
				y += 14f;
			}
		}
		_favList.ContentHeight = y;
		_favList.SetScroll(_favList.Scroll);
	}

	/// <summary>Word head, reading, part of speech, meaning and (optionally) the example sentence.</summary>
	private float WordRow(ThorScrollList list, SimpleRow row, float y, string head, string reading, string pos, string meaning, string sentence, float right)
	{
		float cy = y + 20f;
		CanvasItem h = AddNode(list, row, JpWord(list, head, 44, TextWhite), new Vector2(40f, cy));
		float hx = 40f + TextSize(h).X + 18f;
		Rect2 headRect = new(30f, cy - 6f, hx - 30f, 56f);
		row.Tap = headRect;
		row.TapId = "say." + RuntimeHelpers.GetHashCode(row);
		string say = string.IsNullOrEmpty(reading) ? head : reading;
		row.TapAct = () => ThorSpeech.Speak(say);
		if (!string.IsNullOrEmpty(reading) && reading != head)
		{
			AddNode(list, row, MakeText(reading, 28, Teal), new Vector2(hx, cy + 10f));
			hx += TextSize(row.Nodes[^1]).X + 16f;
		}
		if (!string.IsNullOrEmpty(pos))
		{
			AddNode(list, row, MakeText(pos, 22, TextSoft), new Vector2(hx, cy + 16f));
		}
		cy += 62f;
		Label m = MakeText(string.IsNullOrEmpty(meaning) ? "（词典里没有这个词）" : meaning, 28, TextWhite, (int)(right - 40f));
		m.MaxLinesVisible = 3;
		list.Content.AddChild(m);
		AddNode(list, row, m, new Vector2(40f, cy));
		cy += Mathf.Min(TextSize(m).Y, 3 * 40f) + 8f;
		if (!string.IsNullOrEmpty(sentence))
		{
			cy += 10f;
			cy += JpBlock(list, row, sentence, cy, 28, JpColor, 40f, right);
		}
		cy += 18f + 44f + 18f;
		row.H = cy - y;
		return cy;
	}

	private void ArmOrDelete(string id, Action delete)
	{
		if (_armedDelete == id)
		{
			_armedDelete = "";
			delete();
			Toast("已删除");
		}
		else
		{
			_armedDelete = id;
			_armedUntil = Time.GetTicksMsec() + 3000;
		}
		_favBuilt = "";
	}

	private void SendAnki(WordItem item)
	{
		string r = ThorDictionary.SendToAnki(item);
		if (r == "ok" || r == "duplicate")
		{
			item.Anki = true;
			ThorStore.SaveWords();
			Toast(r == "ok" ? "已发送到 AnkiDroid" : "AnkiDroid 里已经有这个词");
		}
		else if (r == "permission")
		{
			Toast("请在上屏弹出的窗口里允许访问 AnkiDroid，再点一次");
		}
		else if (r == "no_anki")
		{
			Toast("没有安装 AnkiDroid");
		}
		else
		{
			Toast("发送失败：" + r.Replace("error:", ""));
		}
		_favBuilt = "";
	}

	private Rect2 FavLinesRect => new(40f, ContentTop + 16f, 200f, GlassBtnSrc.Size.Y * Art);
	private Rect2 FavWordsRect => new(256f, ContentTop + 16f, 200f, GlassBtnSrc.Size.Y * Art);
	private Rect2 FavUpRect => new(Size.X - 64f, FavListTop, 48f, 72f);
	private Rect2 FavDownRect => new(Size.X - 64f, ListBottom - 72f, 48f, 72f);

	private void RelayoutFav()
	{
		HeaderTitle("title.fav.en", "title.fav");
		CenterText("fav.lines", FavLinesRect);
		CenterText("fav.words", FavWordsRect);
		SetText("fav.lines", $"名台词 {ThorStore.Lines.Count}");
		SetText("fav.words", $"生词本 {ThorStore.Words.Count}");
		CenterText("fav.lines", FavLinesRect);
		CenterText("fav.words", FavWordsRect);
		_texts["fav.lines"].Modulate = _favMode == FavMode.Lines ? ActiveYellow : TextWhite;
		_texts["fav.words"].Modulate = _favMode == FavMode.Words ? ActiveYellow : TextWhite;
		bool empty = _favMode == FavMode.Lines ? ThorStore.Lines.Count == 0 : ThorStore.Words.Count == 0;
		SetText("fav.hint", _favMode == FavMode.Lines
			? (empty ? "在文本记录里长按一句即可收藏" : "长按记录里的句子可以收藏或取消")
			: (empty ? "在文本记录里点一句日文，查词后加入生词本" : "点单词可以朗读"));
		Vector2 hs = TextSize(_texts["fav.hint"]);
		SetPos(_texts["fav.hint"], new Vector2(FavWordsRect.End.X + 24f, FavWordsRect.Position.Y + (FavWordsRect.Size.Y - hs.Y) / 2f));
		_hits.Add(new Hit(FavLinesRect.Grow(6f), "fav.lines", () => { _favMode = FavMode.Lines; _favList.SetScroll(0); }, true));
		_hits.Add(new Hit(FavWordsRect.Grow(6f), "fav.words", () => { _favMode = FavMode.Words; _favList.SetScroll(0); }, true));
		_favList.Layout(new Rect2(0, FavListTop, Size.X - 76f, ListBottom - FavListTop));
		if (_favRows.Count > 0)
		{
			AddScrollbarHits(_favList, FavUpRect, FavDownRect, "favscroll");
		}
		AddRowHits(_favList, _favRows);
	}

	private void DrawFavChrome()
	{
		DrawGlassButton(_favPage, FavLinesRect, "fav.lines", true);
		DrawGlassButton(_favPage, FavWordsRect, "fav.words", true);
		if (_favRows.Count > 0)
		{
			DrawScrollbar(_favPage, _favList, FavUpRect, FavDownRect, "favscroll");
		}
	}

	// ---- 查词 popup ---------------------------------------------------------------

	private Rect2 PopupRect => new(16f, ContentTop + 6f, Size.X - 32f, Size.Y - FooterH - ContentTop - 14f);
	private Rect2 PopupCloseRect => new(PopupRect.End.X - 24f - 140f, PopupRect.Position.Y + 16f, 140f, 48f);
	private Rect2 PopupUpRect => new(PopupRect.End.X - 64f, PopupRect.Position.Y + 80f, 48f, 72f);
	private Rect2 PopupDownRect => new(PopupRect.End.X - 64f, PopupRect.End.Y - 84f, 48f, 72f);

	/// <summary>Opens the word breakdown for a Japanese line.</summary>
	private void OpenPopup(string jp)
	{
		if (string.IsNullOrEmpty(jp))
		{
			return;
		}
		_popupSentence = jp;
		_popupOpen = true;
		_popupLoaded = false;
		_popupRetry = 0;
		ClearRows(_popupRows);
		_popupList.ContentHeight = 0;
		_popupList.SetScroll(0);
		Dirty = true;
	}

	public bool ClosePopup()
	{
		if (!_popupOpen)
		{
			return false;
		}
		_popupOpen = false;
		ClearRows(_popupRows);
		Dirty = true;
		return true;
	}

	private void LoadPopup()
	{
		if (!ThorDictionary.Ready)
		{
			return;
		}
		_popupLoaded = true;
		ClearRows(_popupRows);
		float right = PopupRect.Size.X - 90f;
		float y = 6f;
		// the sentence itself, with furigana
		var head = new SimpleRow { Y = y };
		float sh = JpBlock(_popupList, head, _popupSentence, y + 18f, 32, TextWhite, 40f, right);
		head.H = sh + 36f;
		head.Tap = new Rect2(20f, y, right, head.H);
		head.TapId = "say.sentence";
		head.TapAct = () => ThorSpeech.Speak(_popupSentence);
		_popupRows.Add(head);
		y += head.H + 14f;
		foreach (DictWord w in ThorDictionary.Breakdown(_popupSentence))
		{
			var row = new SimpleRow { Y = y };
			DictWord word = w;
			y = WordRow(_popupList, row, y, word.Head, word.Reading, word.Pos, word.Meaning, null, right);
			float by = y - 62f;
			WordItem saved = ThorStore.FindWord(word.Head);
			string key = "pw." + RuntimeHelpers.GetHashCode(row);
			ListButton add = RowButton(_popupList, row, key + ".add", saved != null ? "已加入生词本" : "加入生词本", right, by, () =>
			{
				ThorStore.AddWord(new WordItem
				{
					Word = word.Head, Reading = word.Reading, Pos = word.Pos,
					Meaning = string.IsNullOrEmpty(word.Zh) ? word.En : word.Zh + (string.IsNullOrEmpty(word.En) ? "" : "\n" + word.En),
					Sentence = _popupSentence,
				});
				Toast("已加入生词本");
				_popupLoaded = false;
			}, saved != null);
			RowButton(_popupList, row, key + ".anki", "发送到 Anki", add.Rect.Position.X - 14f, by, () =>
			{
				WordItem item = ThorStore.AddWord(new WordItem
				{
					Word = word.Head, Reading = word.Reading, Pos = word.Pos,
					Meaning = string.IsNullOrEmpty(word.Zh) ? word.En : word.Zh + (string.IsNullOrEmpty(word.En) ? "" : "\n" + word.En),
					Sentence = _popupSentence,
				});
				SendAnki(item);
				_popupLoaded = false;
			});
			_popupRows.Add(row);
			y += 14f;
		}
		_popupList.ContentHeight = y;
		_popupList.SetScroll(_popupList.Scroll);
		Dirty = true;
	}

	private void RelayoutPopup()
	{
		_popup.Visible = _popupOpen;
		if (!_popupOpen)
		{
			return;
		}
		_popup.Size = Size;
		Rect2 p = PopupRect;
		SetPos(_texts["popup.title"], new Vector2(p.Position.X + 30f, p.Position.Y + 22f));
		SetPos(_texts["popup.credit"], new Vector2(p.Position.X + 110f, p.Position.Y + 34f));
		CenterText("popup.close", PopupCloseRect);
		_texts["popup.wait"].Visible = !_popupLoaded;
		SetPos(_texts["popup.wait"], new Vector2(p.Position.X + 40f, p.Position.Y + 110f));
		_hits.Add(new Hit(PopupCloseRect.Grow(8f), "popup.close", () => ClosePopup(), true));
		_popupList.Layout(new Rect2(p.Position.X + 6f, p.Position.Y + 80f, p.Size.X - 80f, p.Size.Y - 90f));
		if (_popupList.MaxScroll > 0)
		{
			AddScrollbarHits(_popupList, PopupUpRect, PopupDownRect, "popscroll");
		}
		AddRowHits(_popupList, _popupRows);
		_popup.QueueRedraw();
	}

	private void DrawPopupChrome()
	{
		Rect2 p = PopupRect;
		_popup.DrawRect(new Rect2(0, ContentTop, Size.X, Size.Y - FooterH - ContentTop), new Color(0, 0, 0, 0.45f));
		var box = new StyleBoxFlat { BgColor = new Color(0.04f, 0.09f, 0.14f, 0.97f), BorderColor = new Color(Teal, 0.6f), AntiAliasing = true };
		box.SetCornerRadiusAll(16);
		box.SetBorderWidthAll(2);
		box.Draw(_popup.GetCanvasItem(), p);
		_popup.DrawRect(new Rect2(p.Position + new Vector2(16f, 24f), new Vector2(6f, 34f)), Teal);
		DrawGlassButton(_popup, PopupCloseRect, "popup.close", true);
		if (_popupList.MaxScroll > 0)
		{
			DrawScrollbar(_popup, _popupList, PopupUpRect, PopupDownRect, "popscroll");
		}
	}

	/// <summary>The game logo as a faint watermark in the empty lower part of a page.</summary>
	private void DrawLogoWatermark(float bottom)
	{
		if (_texLogo == null)
		{
			return;
		}
		Vector2 size = _texLogo.GetSize() * 2.2f;
		var rect = new Rect2((Size.X - size.X) / 2f, bottom - size.Y - 16f, size.X, size.Y);
		DrawTextureRect(_texLogo, rect, false, new Color(1, 1, 1, 0.42f));
	}
}
