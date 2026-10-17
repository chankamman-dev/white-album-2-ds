// SPDX-License-Identifier: Apache-2.0
using Godot;

/// <summary>Game-state checks shared by the lower screen and the Thor gamepad mapping.</summary>
public static class ThorActions
{
	public static Control TopUi(Wa2EngineMain e)
	{
		return e.UiMgr != null && e.UiMgr.UiQueue.Count > 0 ? e.UiMgr.UiQueue.Peek() : null;
	}

	public static bool InGame(Wa2EngineMain e)
	{
		return e.State == Wa2EngineMain.GameState.GAME;
	}

	public static bool AtAdv(Wa2EngineMain e)
	{
		return InGame(e) && TopUi(e) == e.AdvMain && !e.VideoPlayer.IsPlaying;
	}

	/// <summary>Waiting for a click with nothing animating: safe to open menus.</summary>
	public static bool AdvIdle(Wa2EngineMain e)
	{
		return AtAdv(e) && e.AdvMain.State == Wa2AdvMain.AdvState.WAIT_CLICK && !e.WaitTimer.IsActive()
			&& !e.AnimatorMgr.WaitAnimation();
	}

	public static bool CanSaveLoad(Wa2EngineMain e)
	{
		return AtAdv(e) && !e.WaitTimer.IsActive()
			&& (e.AdvMain.State == Wa2AdvMain.AdvState.WAIT_CLICK || e.AdvMain.SelectMessageContainer.Visible);
	}

	// ---- quick save / load (a slot outside the 100 menu slots) ----------------

	public const int QuickSlot = 100;

	public static bool HasQuickSave(Wa2EngineMain e)
	{
		return FileAccess.FileExists(e.SavPath + string.Format("sav{0:D2}.sav", QuickSlot));
	}

	public static bool CanQuickLoad(Wa2EngineMain e)
	{
		if (!HasQuickSave(e))
		{
			return false;
		}
		bool atTitle = e.State == Wa2EngineMain.GameState.TITLE && TopUi(e) == e.UiMgr.TitleMenu;
		return atTitle || CanSaveLoad(e);
	}

	/// <summary>Saves like the save menu does, with the game's own tip dialog.</summary>
	public static void QuickSave(Wa2EngineMain e)
	{
		if (!CanSaveLoad(e) || e.Script == null)
		{
			return;
		}
		e.UiMgr.OpenConfirm("", "快速存档完成", false, () =>
		{
			e.GameSav.SaveData(QuickSlot);
			e.GetTree().CreateTimer(1).Timeout += () => e.UiMgr.UIConfirm.Close();
		});
	}

	/// <summary>Loads the quick slot, asking first when the game's "确认" option is on.</summary>
	public static void QuickLoad(Wa2EngineMain e)
	{
		if (!CanQuickLoad(e))
		{
			return;
		}
		e.UiMgr.OpenConfirm("读取快速存档。\n确定吗？", "存档读取成功", e.Prefs.GetConfig("yes_no") == 1, () =>
		{
			if (e.State == Wa2EngineMain.GameState.TITLE)
			{
				// same order as the load menu: drop the dialog, enter the game, then load
				e.UiMgr.UIConfirm.Close();
				e.SoundMgr.StopBgm();
				e.UiMgr.OpenGame();
				e.GameSav.LoadData(QuickSlot);
				return;
			}
			e.GameSav.LoadData(QuickSlot);
			e.GetTree().CreateTimer(1).Timeout += () => e.UiMgr.UIConfirm.Close();
		});
	}

	public static bool MenuOpen(Wa2EngineMain e)
	{
		return TopUi(e) is BasePage;
	}

	public static bool ChoicesVisible(Wa2EngineMain e)
	{
		return AtAdv(e) && e.AdvMain.SelectMessageContainer.Visible && e.SelectItems.Count > 0;
	}

	public static bool ChoiceEnabled(Wa2EngineMain e, int idx)
	{
		if (idx < 0 || idx >= e.SelectItems.Count || idx >= e.AdvMain.SelectMessageContainer.GetChildCount())
		{
			return false;
		}
		SelectMessage btn = e.AdvMain.SelectMessageContainer.GetChild<SelectMessage>(idx);
		return btn.Visible && !btn.Disabled;
	}

	public static string StatusText(Wa2EngineMain e)
	{
		if (e.VideoPlayer.IsPlaying)
		{
			return "影片播放中";
		}
		switch (e.State)
		{
			case Wa2EngineMain.GameState.TITLE:
				return MenuOpen(e) ? "菜单中" : "标题画面";
			case Wa2EngineMain.GameState.GAME:
				if (MenuOpen(e))
				{
					return "菜单中";
				}
				if (ChoicesVisible(e))
				{
					return "请选择";
				}
				if (e.SkipMode || e.Skipping)
				{
					return "快进中";
				}
				return e.AutoMode ? "自动播放中" : "游戏中";
			default:
				return "启动中";
		}
	}
}
