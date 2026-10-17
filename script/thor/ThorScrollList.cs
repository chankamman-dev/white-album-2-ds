// SPDX-License-Identifier: Apache-2.0
using Godot;

/// <summary>
/// Clipped vertical list for the lower screen: owners add their items to <see cref="Content"/>
/// (in content coordinates) and draw on Content's Draw signal. Scrolling is driven by the owner's
/// touch handling; this class keeps the offset, clamping and fling.
/// </summary>
public partial class ThorScrollList : Control
{
	public Control Content { get; }
	public float Scroll { get; private set; }
	public float Velocity;
	public float ContentHeight;

	public float MaxScroll => Mathf.Max(0, ContentHeight - Size.Y);

	public ThorScrollList()
	{
		ClipContents = true;
		MouseFilter = MouseFilterEnum.Ignore;
		Content = new Control { Name = "Content", MouseFilter = MouseFilterEnum.Ignore };
		AddChild(Content);
	}

	/// <summary>Returns true when the visible offset changed.</summary>
	public bool SetScroll(float value)
	{
		float clamped = Mathf.Clamp(value, 0, MaxScroll);
		if (clamped != value)
		{
			Velocity = 0;
		}
		if (Mathf.IsEqualApprox(clamped, Scroll))
		{
			return false;
		}
		Scroll = clamped;
		Content.Position = new Vector2(0, -Scroll);
		return true;
	}

	/// <summary>Continues a fling; returns true when the offset moved.</summary>
	public bool Fling(float delta)
	{
		if (Mathf.Abs(Velocity) <= 4f)
		{
			Velocity = 0;
			return false;
		}
		bool moved = SetScroll(Scroll + Velocity * delta);
		Velocity *= Mathf.Exp(-5f * delta);
		return moved;
	}

	public void Layout(Rect2 rect)
	{
		Position = rect.Position;
		Size = rect.Size;
		Content.Size = new Vector2(rect.Size.X, Mathf.Max(ContentHeight, rect.Size.Y));
		Content.Position = new Vector2(0, -Scroll);
		Content.QueueRedraw();
	}

	/// <summary>Converts a content-space rect to the owner's coordinates (null when scrolled out of view).</summary>
	public Rect2? ToOwner(Rect2 contentRect)
	{
		Rect2 r = contentRect;
		r.Position += Position - new Vector2(0, Scroll);
		if (r.End.Y < Position.Y || r.Position.Y > Position.Y + Size.Y)
		{
			return null;
		}
		return r;
	}

	public bool Contains(Vector2 ownerPos)
	{
		return Visible && IsVisibleInTree() && new Rect2(Position, Size).HasPoint(ownerPos);
	}
}
