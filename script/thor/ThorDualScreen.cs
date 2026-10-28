// SPDX-License-Identifier: Apache-2.0
using Godot;
using System;
using System.Linq;

/// <summary>
/// AYN Thor dual-screen support (autoload).
/// Renders the lower-screen companion into an offscreen SubViewport and hands
/// finished frames to the Android Presentation bridge (Wa2ThorPlugin.java).
/// On desktop, "--thor-preview" opens the same UI in a second window.
/// </summary>
public partial class ThorDualScreen : Node
{
	public const int TouchDown = 0;
	public const int TouchMove = 1;
	public const int TouchUp = 2;
	public const int TouchCancel = 3;

	private static readonly Vector2I DefaultSize = new(1240, 1080);
	private const double MinPushInterval = 1.0 / 30.0;

	public static ThorDualScreen Instance { get; private set; }

	private GodotObject _plugin;
	private SubViewport _viewport;
	private ThorBottomUi _ui;
	private Vector2I _size = DefaultSize;
	private bool _surfaceReady;
	private bool _captureQueued;
	private double _sincePush = 1.0;
	private Window _previewWindow;
	private string _shotPath = "";
	private bool _mock;
	private int _startTab;
	private bool _verbose;
	private ThorGamepadNav _nav;
	private Rect2 _meterSent;
	private float _meterLevel = -1f;
	private bool _meterFrame;

	/// <summary>
	/// Live BGM meter, same formula as the game's BgmPlayer (spectrum analyser on the BGM bus).
	/// Only the level is sent; the bridge redraws just that strip, so the whole frame is not re-sent.
	/// </summary>
	private void UpdateMeter()
	{
		if (_plugin == null || !_surfaceReady)
		{
			return;
		}
		_meterFrame = !_meterFrame;
		if (!_meterFrame)
		{
			return;
		}
		Rect2 rect = _ui.MeterRect;
		if (rect != _meterSent)
		{
			Image img = _ui.MeterImage();
			_plugin.Call("setMeterSprite", img.GetData(), img.GetWidth(), img.GetHeight(), (int)rect.Position.X, (int)rect.Position.Y);
			_meterSent = rect;
			_meterLevel = -2f;
		}
		float level = -1f;
		if (_ui.BgmTitleShown && AudioServer.GetBusEffectInstance(1, 0) is AudioEffectSpectrumAnalyzerInstance spectrum)
		{
			float m = spectrum.GetMagnitudeForFrequencyRange(0, 3200, AudioEffectSpectrumAnalyzerInstance.MagnitudeMode.Average).Length();
			level = Mathf.Clamp((60f + Mathf.LinearToDb(m)) / 60f, 0f, 1f);
		}
		if (Mathf.Abs(level - _meterLevel) >= 0.01f)
		{
			_meterLevel = level;
			_plugin.Call("setMeterLevel", level);
		}
	}

	public override void _Ready()
	{
		Instance = this;
		ProcessMode = ProcessModeEnum.Always;
		ThorSpeech.Attach(this);
		string[] args = OS.GetCmdlineArgs().Concat(OS.GetCmdlineUserArgs()).ToArray();
		_mock = args.Contains("--thor-mock");
		ThorBottomUi.MockJapanese = args.Contains("--thor-jp");
		ThorBottomUi.MockWords = args.Contains("--thor-words");
		ThorDictionary.Mock = _mock && args.Contains("--thor-popup");
		ThorFurigana.MockReady = ThorBottomUi.MockJapanese && OS.GetName() != "Android";
		foreach (string arg in args)
		{
			if (arg.StartsWith("--thor-tab="))
			{
				_startTab = arg.Substring("--thor-tab=".Length).ToInt();
			}
			if (arg.StartsWith("--thor-shot="))
			{
				_shotPath = arg.Substring("--thor-shot=".Length);
			}
		}

		if (OS.GetName() == "Android" && Godot.Engine.HasSingleton("Wa2Thor"))
		{
			_plugin = Godot.Engine.GetSingleton("Wa2Thor");
			_plugin.Connect("bottom_surface", Callable.From<int, int>(OnSurface));
			_plugin.Connect("bottom_touch", Callable.From<int, float, float>(OnTouch));
			_plugin.Call("start");
			ThorFurigana.Plugin = _plugin;
			ThorSpeech.Plugin = _plugin;
			ThorDictionary.Plugin = _plugin;
		}
		else if (args.Contains("--thor-preview") || _mock || _shotPath != "")
		{
			CreatePreviewWindow();
			_surfaceReady = true;
		}

		var skip = new InputEventJoypadButton { ButtonIndex = JoyButton.RightShoulder };
		if (InputMap.HasAction("Skip"))
		{
			InputMap.ActionAddEvent("Skip", skip);
		}
		RenderingServer.FramePostDraw += OnFramePostDraw;
		_nav = new ThorGamepadNav { Name = "GamepadNav" };
		AddChild(_nav);
	}

	public override void _ExitTree()
	{
		RenderingServer.FramePostDraw -= OnFramePostDraw;
		if (_plugin != null)
		{
			_plugin.Call("stop");
		}
	}

	private void OnSurface(int width, int height)
	{
		_surfaceReady = width > 0 && height > 0;
		if (!_surfaceReady)
		{
			return;
		}
		_size = new Vector2I(width, height);
		if (_viewport != null)
		{
			_viewport.Size = _size;
			_ui.Size = _size;
		}
		_ui?.MarkDirty();
	}

	/// <summary>Touch from the lower screen, in surface pixels.</summary>
	public void OnTouch(int action, float x, float y)
	{
		if (_verbose)
		{
			GD.Print($"[thor] touch {action} {x:0} {y:0}");
		}
		_ui?.HandleTouch(action, new Vector2(x, y) * ((Vector2)_viewport.Size / (Vector2)_size));
	}

	private bool EngineReady()
	{
		Wa2EngineMain engine = Wa2EngineMain.Engine;
		return engine != null && engine.Prefs != null && Wa2Def.FontMap.Count > 0 && IsInstanceValid(engine);
	}

	public override void _Process(double delta)
	{
		_sincePush += delta;
		if (_ui == null)
		{
			if (!EngineReady())
			{
				return;
			}
			CreateUi();
		}
		_ui.Tick(delta);
		UpdateMeter();
		if (_ui.Dirty && _surfaceReady && !_captureQueued && _sincePush >= MinPushInterval)
		{
			_ui.Dirty = false;
			_ui.Relayout();
			_viewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Once;
			_captureQueued = true;
		}
	}

	private void CreateUi()
	{
		// diagnostics: put an empty "thor_verbose" file into the Wa2Res folder
		_verbose = OS.GetCmdlineArgs().Contains("--thor-verbose") || FileAccess.FileExists(Wa2Resource.ResPath + "thor_verbose");
		_viewport = new SubViewport
		{
			Name = "ThorBottomViewport",
			Size = _size,
			Disable3D = true,
			TransparentBg = false,
			GuiDisableInput = true,
			RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled,
			CanvasItemDefaultTextureFilter = Viewport.DefaultCanvasItemTextureFilter.Linear,
		};
		AddChild(_viewport);
		_ui = new ThorBottomUi { Name = "ThorBottomUi", Size = _size };
		_viewport.AddChild(_ui);
		_ui.NativeMeter = _plugin != null;
		if (_mock)
		{
			ThorBottomUi.FillMockBacklog(Wa2EngineMain.Engine);
		}
		for (int i = 0; i < _startTab; i++)
		{
			_ui.NextTab();
		}
		if (ThorDictionary.Mock)
		{
			_ui.MockOpenPopup();
		}
		if (_previewWindow != null)
		{
			_previewWindow.GetNode<TextureRect>("Frame").Texture = _viewport.GetTexture();
		}
	}

	private void OnFramePostDraw()
	{
		if (!_captureQueued)
		{
			return;
		}
		_captureQueued = false;
		_sincePush = 0.0;
		if (_plugin == null && _shotPath == "")
		{
			return;
		}
		Image image = _viewport.GetTexture().GetImage();
		if (image == null)
		{
			return;
		}
		if (image.GetFormat() != Image.Format.Rgba8)
		{
			image.Convert(Image.Format.Rgba8);
		}
		if (_plugin != null)
		{
			_plugin.Call("pushFrame", image.GetData(), image.GetWidth(), image.GetHeight());
		}
		if (_shotPath != "")
		{
			image.SavePng(_shotPath);
		}
	}

	private void CreatePreviewWindow()
	{
		_previewWindow = new Window
		{
			Title = "Thor lower screen",
			Size = _size / 2,
			Position = new Vector2I(40, 40),
			Unresizable = false,
		};
		var frame = new TextureRect
		{
			Name = "Frame",
			ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
			StretchMode = TextureRect.StretchModeEnum.Scale,
			MouseFilter = Control.MouseFilterEnum.Stop,
		};
		frame.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		frame.GuiInput += ev => OnPreviewInput(frame, ev);
		_previewWindow.AddChild(frame);
		_previewWindow.CloseRequested += () => _previewWindow.Hide();
		CallDeferred(Node.MethodName.AddChild, _previewWindow);
	}

	private void OnPreviewInput(Control frame, InputEvent ev)
	{
		Vector2 scale = (Vector2)_size / frame.Size;
		if (ev is InputEventMouseButton mb && mb.ButtonIndex == MouseButton.Left)
		{
			OnTouch(mb.Pressed ? TouchDown : TouchUp, mb.Position.X * scale.X, mb.Position.Y * scale.Y);
		}
		else if (ev is InputEventMouseMotion mm && (mm.ButtonMask & MouseButtonMask.Left) != 0)
		{
			OnTouch(TouchMove, mm.Position.X * scale.X, mm.Position.Y * scale.Y);
		}
		else if (ev is InputEventMouseButton wheel && wheel.Pressed && (wheel.ButtonIndex == MouseButton.WheelUp || wheel.ButtonIndex == MouseButton.WheelDown))
		{
			_ui?.ScrollBy(wheel.ButtonIndex == MouseButton.WheelUp ? -120 : 120);
		}
	}

	// ---- Thor gamepad ----------------------------------------------------

	public override void _Input(InputEvent ev)
	{
		if (_verbose && ev is InputEventJoypadButton jv)
		{
			GD.Print($"[thor] joy {jv.ButtonIndex} {jv.Pressed}");
		}
		if (!EngineReady())
		{
			return;
		}
		if ((ev is InputEventMouseButton mb && mb.Device != ThorGamepadNav.SyntheticDevice) || ev is InputEventScreenTouch)
		{
			_nav.OnPointer();
			return;
		}
		if (ev is InputEventJoypadMotion jm)
		{
			_nav.HandleAxis(jm);
			return;
		}
		if (ev is not InputEventJoypadButton jb)
		{
			return;
		}
		// B closes the lower screen's word popup first
		if (jb.Pressed && jb.ButtonIndex == JoyButton.B && _ui != null && _ui.ClosePopup())
		{
			GetViewport().SetInputAsHandled();
			return;
		}
		// left / right in the word popup: previous / next line
		if (jb.Pressed && _ui != null && _ui.PopupOpen && jb.ButtonIndex is JoyButton.DpadLeft or JoyButton.DpadRight)
		{
			_ui.PopupStep(jb.ButtonIndex == JoyButton.DpadLeft ? -1 : 1);
			GetViewport().SetInputAsHandled();
			return;
		}
		// pages and dialogs: move a cursor over their buttons
		if (_nav.HandleButton(jb))
		{
			GetViewport().SetInputAsHandled();
			_ui?.MarkDirty();
			return;
		}
		if (!jb.Pressed)
		{
			return;
		}
		Wa2EngineMain engine = Wa2EngineMain.Engine;
		switch (jb.ButtonIndex)
		{
			case JoyButton.A:
				Advance(engine);
				break;
			case JoyButton.B:
				engine.Back();
				break;
			case JoyButton.X:
				if (ThorActions.AtAdv(engine))
				{
					engine.AdvMain.OnAutoButtonDown();
				}
				break;
			case JoyButton.Y:
				ToggleWindow(engine);
				break;
			case JoyButton.LeftShoulder:
				_ui?.NextTab();
				break;
			case JoyButton.DpadUp:
				_ui?.ScrollBy(-160);
				break;
			case JoyButton.DpadDown:
				_ui?.ScrollBy(160);
				break;
			default:
				return;
		}
		GetViewport().SetInputAsHandled();
		_ui?.MarkDirty();
	}

	/// <summary>Same path as a tap on the top screen, without hitting any top-screen button.</summary>
	public static void Advance(Wa2EngineMain engine)
	{
		engine._GuiInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true });
		engine._GuiInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false });
	}

	public static void ToggleWindow(Wa2EngineMain engine)
	{
		if (!ThorActions.AtAdv(engine))
		{
			return;
		}
		if (engine.AdvMain.State == Wa2AdvMain.AdvState.HIDE)
		{
			Advance(engine);
		}
		else
		{
			engine.AdvMain.OnOffButtonDown();
		}
	}
}
