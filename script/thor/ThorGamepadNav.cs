// SPDX-License-Identifier: Apache-2.0
using Godot;
using System.Collections.Generic;

/// <summary>
/// Gamepad cursor for every mouse-driven page (title, chapter select, save/load,
/// options, confirm dialogs, choices). D-pad / left stick moves between the visible
/// buttons of the page on top, A clicks the selected one, B goes back.
/// Clicks are delivered as real mouse events so the game's own handlers run.
/// </summary>
public partial class ThorGamepadNav : Node
{
	/// <summary>Device id stamped on the mouse events this class injects.</summary>
	public const int SyntheticDevice = 7701;

	private const double RepeatDelay = 0.38;
	private const double RepeatRate = 0.12;

	private Wa2EngineMain E => Wa2EngineMain.Engine;
	private CanvasLayer _layer;
	private Control _overlay;
	private StyleBoxFlat _frame;
	private Control _root;
	private BaseButton _selected;
	private bool _cursorVisible;
	private Vector2 _heldDir;
	private double _repeat;
	private Vector2 _stick;

	public override void _Ready()
	{
		_layer = new CanvasLayer { Layer = 120 };
		AddChild(_layer);
		_overlay = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
		_overlay.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		_overlay.Draw += DrawCursor;
		_layer.AddChild(_overlay);
		_frame = new StyleBoxFlat
		{
			DrawCenter = false,
			BorderColor = new Color(0.62f, 0.97f, 1f),
			ShadowColor = new Color(0.3f, 0.85f, 0.95f, 0.55f),
			ShadowSize = 10,
			AntiAliasing = true,
		};
		_frame.SetBorderWidthAll(3);
		_frame.SetCornerRadiusAll(8);
	}

	/// <summary>Page whose buttons the cursor walks, or null while the story text box has control.</summary>
	private Control NavRoot()
	{
		if (E.ErrorMessage != null && E.ErrorMessage.Visible)
		{
			return E.ErrorMessage;
		}
		if (E.VideoPlayer.IsPlaying)
		{
			return null;
		}
		if (ThorActions.ChoicesVisible(E))
		{
			return E.AdvMain.SelectMessageContainer;
		}
		Control top = ThorActions.TopUi(E);
		return top == null || top == E.AdvMain ? null : top;
	}

	public bool Active => E?.UiMgr != null && NavRoot() != null;

	/// <summary>Returns true when the button was used for menu navigation.</summary>
	public bool HandleButton(InputEventJoypadButton jb)
	{
		Control root = NavRoot();
		if (root == null)
		{
			_heldDir = Vector2.Zero;
			Hide();
			return false;
		}
		Vector2 dir = jb.ButtonIndex switch
		{
			JoyButton.DpadUp => Vector2.Up,
			JoyButton.DpadDown => Vector2.Down,
			JoyButton.DpadLeft => Vector2.Left,
			JoyButton.DpadRight => Vector2.Right,
			_ => Vector2.Zero,
		};
		if (dir != Vector2.Zero)
		{
			if (jb.Pressed)
			{
				Step(root, dir);
				_heldDir = dir;
				_repeat = RepeatDelay;
			}
			else if (_heldDir == dir)
			{
				_heldDir = Vector2.Zero;
			}
			return true;
		}
		if (!jb.Pressed)
		{
			return jb.ButtonIndex is JoyButton.A or JoyButton.B;
		}
		switch (jb.ButtonIndex)
		{
			case JoyButton.A:
				Confirm(root);
				return true;
			case JoyButton.B:
				Cancel(root);
				return true;
			default:
				return false;
		}
	}

	public void HandleAxis(InputEventJoypadMotion jm)
	{
		if (jm.Axis == JoyAxis.LeftX)
		{
			_stick.X = jm.AxisValue;
		}
		else if (jm.Axis == JoyAxis.LeftY)
		{
			_stick.Y = jm.AxisValue;
		}
		else
		{
			return;
		}
		Control root = NavRoot();
		if (root == null)
		{
			return;
		}
		Vector2 dir = Vector2.Zero;
		if (_stick.Length() > 0.6f)
		{
			dir = Mathf.Abs(_stick.X) > Mathf.Abs(_stick.Y)
				? new Vector2(Mathf.Sign(_stick.X), 0)
				: new Vector2(0, Mathf.Sign(_stick.Y));
		}
		else if (_stick.Length() > 0.3f)
		{
			return;
		}
		if (dir != _heldDir)
		{
			_heldDir = dir;
			_repeat = RepeatDelay;
			if (dir != Vector2.Zero)
			{
				Step(root, dir);
			}
		}
	}

	/// <summary>A touch or a real mouse click hides the cursor until the pad is used again.</summary>
	public void OnPointer()
	{
		if (_cursorVisible)
		{
			Hide();
		}
	}

	public override void _Process(double delta)
	{
		if (E?.UiMgr == null)
		{
			return;
		}
		Control root = NavRoot();
		if (root != _root)
		{
			_root = root;
			_selected = null;
			if (root == null)
			{
				Hide();
			}
		}
		if (_selected != null && !Usable(_selected))
		{
			_selected = null;
		}
		if (root != null && _heldDir != Vector2.Zero)
		{
			_repeat -= delta;
			if (_repeat <= 0)
			{
				_repeat = RepeatRate;
				Step(root, _heldDir);
			}
		}
		if (_cursorVisible && root != null && _selected == null)
		{
			Select(Default(root));
		}
		_overlay.Visible = _cursorVisible && _selected != null;
		if (_overlay.Visible)
		{
			_overlay.QueueRedraw();
		}
	}

	private void Hide()
	{
		_cursorVisible = false;
		_overlay.Visible = false;
	}

	private void DrawCursor()
	{
		if (_selected == null)
		{
			return;
		}
		Rect2 rect = _selected.GetGlobalRect().Grow(5f);
		_frame.Draw(_overlay.GetCanvasItem(), rect);
	}

	// ---- candidates ---------------------------------------------------------

	private static bool Usable(BaseButton b)
	{
		if (!IsInstanceValid(b) || !b.IsVisibleInTree() || b.Disabled || b.MouseFilter != Control.MouseFilterEnum.Stop)
		{
			return false;
		}
		Rect2 r = b.GetGlobalRect();
		return r.Size.X > 4 && r.Size.Y > 4 && r.Intersects(new Rect2(0, 0, 1280, 720));
	}

	private static void Collect(Node node, List<BaseButton> list)
	{
		foreach (Node child in node.GetChildren())
		{
			if (child is CanvasItem ci && !ci.Visible)
			{
				continue;
			}
			if (child is BaseButton b && Usable(b))
			{
				list.Add(b);
			}
			Collect(child, list);
		}
	}

	private static List<BaseButton> Candidates(Control root)
	{
		var list = new List<BaseButton>();
		Collect(root, list);
		return list;
	}

	private static Vector2 Center(Control c)
	{
		return c.GetGlobalRect().GetCenter();
	}

	private BaseButton Default(Control root)
	{
		BaseButton best = null;
		float bestScore = float.MaxValue;
		foreach (BaseButton b in Candidates(root))
		{
			Vector2 p = b.GetGlobalRect().Position;
			float score = p.Y * 3f + p.X;
			if (score < bestScore)
			{
				bestScore = score;
				best = b;
			}
		}
		return best;
	}

	private void Step(Control root, Vector2 dir)
	{
		List<BaseButton> list = Candidates(root);
		if (list.Count == 0)
		{
			ScrollFallback(root, dir);
			return;
		}
		if (!_cursorVisible || _selected == null || !list.Contains(_selected))
		{
			_cursorVisible = true;
			Select(_selected != null && list.Contains(_selected) ? _selected : Default(root));
			return;
		}
		Vector2 from = Center(_selected);
		BaseButton best = null;
		float bestScore = float.MaxValue;
		foreach (BaseButton b in list)
		{
			if (b == _selected)
			{
				continue;
			}
			Vector2 d = Center(b) - from;
			float along = d.Dot(dir);
			if (along <= 4f)
			{
				continue;
			}
			float perp = Mathf.Abs(d.Cross(dir));
			float score = along + perp * 2.5f;
			if (score < bestScore)
			{
				bestScore = score;
				best = b;
			}
		}
		if (best == null)
		{
			if (ScrollFallback(root, dir))
			{
				return;
			}
			// wrap around inside a vertical list such as the title menu
			if (dir.Y != 0)
			{
				float farthest = 4f;
				foreach (BaseButton b in list)
				{
					Vector2 d = Center(b) - from;
					float back = -d.Dot(dir);
					if (Mathf.Abs(d.X) < 40f && back > farthest)
					{
						farthest = back;
						best = b;
					}
				}
			}
		}
		if (best != null)
		{
			Select(best);
		}
	}

	/// <summary>Pages such as the backlog scroll with a VScrollBar instead of buttons.</summary>
	private static bool ScrollFallback(Control root, Vector2 dir)
	{
		if (dir.Y == 0)
		{
			return false;
		}
		ScrollBar bar = FindScrollBar(root);
		if (bar == null || !bar.IsVisibleInTree())
		{
			return false;
		}
		double before = bar.Value;
		bar.Value = Mathf.Clamp(bar.Value + dir.Y * Mathf.Max(1, bar.Step), bar.MinValue, bar.MaxValue);
		return bar.Value != before;
	}

	private static ScrollBar FindScrollBar(Node node)
	{
		foreach (Node child in node.GetChildren())
		{
			if (child is ScrollBar bar && bar.Visible)
			{
				return bar;
			}
			ScrollBar inner = FindScrollBar(child);
			if (inner != null)
			{
				return inner;
			}
		}
		return null;
	}

	private void Select(BaseButton b)
	{
		_selected = b;
		_cursorVisible = b != null;
		if (b != null)
		{
			// a hover move lights the button up the same way a mouse would
			SendMotion(Center(b));
		}
	}

	// ---- actions --------------------------------------------------------------

	private void Confirm(Control root)
	{
		if (!_cursorVisible || _selected == null || !Usable(_selected))
		{
			BaseButton first = Default(root);
			if (first == null)
			{
				// nothing to select (CG viewer, page animations): a plain click in the middle
				Click(new Vector2(640, 360));
				return;
			}
			Select(first);
			return;
		}
		Click(Center(_selected));
	}

	private void Cancel(Control root)
	{
		if (root == E.ErrorMessage)
		{
			BaseButton b = Default(root);
			if (b != null)
			{
				Click(Center(b));
			}
			return;
		}
		if (root is UIConfirm confirm)
		{
			if (confirm.ConfirmMessage.Visible && Usable(confirm.CancelBtn))
			{
				Click(Center(confirm.CancelBtn));
			}
			else
			{
				confirm.Close();
			}
			return;
		}
		if (root is TitleMenu title)
		{
			BaseButton back = title.DigitalNovel.Visible ? title.DigitalNovelBackButton
				: title.Special.Visible ? title.SpecialBackButton
				: title.InitalStart.Visible ? title.StartBackButton
				: null;
			if (back != null && Usable(back))
			{
				Click(Center(back));
			}
			return;
		}
		E.Back();
	}

	// ---- injected mouse events --------------------------------------------------

	private Vector2 ToWindow(Vector2 canvasPos)
	{
		return GetViewport().GetFinalTransform() * canvasPos;
	}

	private void SendMotion(Vector2 canvasPos)
	{
		Vector2 p = ToWindow(canvasPos);
		Input.ParseInputEvent(new InputEventMouseMotion { Position = p, GlobalPosition = p, Device = SyntheticDevice });
	}

	private void Click(Vector2 canvasPos)
	{
		Vector2 p = ToWindow(canvasPos);
		Input.ParseInputEvent(new InputEventMouseMotion { Position = p, GlobalPosition = p, Device = SyntheticDevice });
		Input.ParseInputEvent(new InputEventMouseButton
		{
			Position = p, GlobalPosition = p, ButtonIndex = MouseButton.Left, Pressed = true,
			ButtonMask = MouseButtonMask.Left, Device = SyntheticDevice,
		});
		Input.ParseInputEvent(new InputEventMouseButton
		{
			Position = p, GlobalPosition = p, ButtonIndex = MouseButton.Left, Pressed = false, Device = SyntheticDevice,
		});
	}
}
