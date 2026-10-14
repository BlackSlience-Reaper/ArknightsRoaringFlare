using Godot;
using MegaCrit.Sts2.Core.Animation;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;

namespace ArknightsRoaringFlare.Vfx;

/// <summary>仅移动已有节点；结算与站位仍由 CreatureCmd 和遭遇槽位管理。</summary>
internal static class ReinforcementEntrance
{
	public const string RunTrigger = "ReinforcementRun";
	private const float OffscreenMargin = 260f;
	private const float RunDuration = 0.8f;

	public static async Task Enter(Creature creature)
	{
		NCreature? node = NCombatRoom.Instance?.GetCreatureNode(creature);
		if (node is null || !GodotObject.IsInstanceValid(node))
		{
			return;
		}

		Vector2 destination = node.GlobalPosition;
		node.GlobalPosition = new Vector2(RightEdge(node), destination.Y);
		node.SetAnimationTrigger(RunTrigger);
		Tween tween = node.CreateTween();
		tween.TweenProperty(node, "global_position", destination, RunDuration);
		await Cmd.Wait(RunDuration);
		if (GodotObject.IsInstanceValid(node) && node.IsInsideTree())
		{
			tween.Kill();
			node.GlobalPosition = destination;
			node.SetAnimationTrigger(CreatureAnimator.idleTrigger);
		}
	}

	public static async Task Exit(Creature creature, bool downed)
	{
		NCreature? node = NCombatRoom.Instance?.GetCreatureNode(creature);
		if (node is null || !GodotObject.IsInstanceValid(node))
		{
			return;
		}

		Tween tween = node.CreateTween();
		if (downed)
		{
			tween.TweenProperty(node, "modulate:a", 0f, RunDuration);
		}
		else
		{
			node.SetAnimationTrigger(RunTrigger);
			node.Visuals.Scale *= new Vector2(-1f, 1f);
			tween.TweenProperty(node, "global_position:x", RightEdge(node), RunDuration);
		}
		await Cmd.Wait(RunDuration);
		if (GodotObject.IsInstanceValid(node) && node.IsInsideTree())
		{
			tween.Kill();
			node.Visible = false;
		}
	}

	private static float RightEdge(NCreature node)
	{
		// CanvasTransform 的逆变换覆盖窗口缩放与战斗镜头偏移。
		Vector2 viewportEdge = new(node.GetViewportRect().End.X + OffscreenMargin, 0f);
		return Math.Max(node.GlobalPosition.X + OffscreenMargin, (node.GetCanvasTransform().AffineInverse() * viewportEdge).X);
	}
}
