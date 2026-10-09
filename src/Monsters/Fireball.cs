using ArknightsRoaringFlare.Vfx;
using Godot;
using MegaCrit.Sts2.Core.Animation;
using MegaCrit.Sts2.Core.Audio;
using MegaCrit.Sts2.Core.Bindings.MegaSpine;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.MonsterMoves.Intents;
using MegaCrit.Sts2.Core.MonsterMoves.MonsterMoveStateMachine;

namespace ArknightsRoaringFlare.Monsters;

/// <summary>
/// 火球：“不死的黑蛇”第一次被击败后的【不息恶火】连续三回合召唤，二阶段的火雨也召唤它（原作：一列火球从场地一侧横穿过来）。唯一的招式是爆炸：
/// 被召唤后的下个敌方回合对玩家造成伤害，然后自己死亡；在那之前击杀即可阻止。
/// 不挂爪牙（用户要求）：黑蛇最终倒下时场上剩下的火球不会随她退场，要打掉或等它爆炸，战斗才结束。
/// 照原版气态炸弹（GasBomb）的写法，外观是气态炸弹换成红色（creature_visuals/fireball.tscn 继承原版场景，只改着色，不复制原版素材）。
/// 五个火球在召唤位上排成一竖列，原版的站位放不下：场景里把立绘下移 40、点击框收紧到火球本身、意图从头顶挪到火球左侧，
/// 每个火球连同脚下的血条与能力行约高 160（乘镜头 0.85 约 136），召唤位每格 140。
/// </summary>
public sealed class Fireball : FlareMonster
{
	public const string ExplodeMoveId = "EXPLODE_MOVE";

	private const string ExplodeTrigger = "ExplodeTrigger";

	private bool _hasExploded;

	public override string SceneName => "fireball";

	// 原作音效（audio_data.json：不息恶火的火球命中 projectile_enemy_bsnake → e_skill_bsnake1to2_screenattack_imp）。
	protected override FlareSound SoundFor(string slot) => slot switch
	{
		_ => new(Hit: "e_skill_bsnake1to2_screenattack_imp"),
	};

	// 原作特效：不息恶火的火球落地 enemy_bsnake_reborn_01_trail_hit_01（黑蛇火雨投射物的命中）。
	protected override FlareFx FxFor(string slot) => new(Hit: [new("enemy_bsnake_reborn_01_trail_hit_01")]);

	public override IReadOnlyList<string> RequiredAnimations => ["spawn", "idle_loop", "hurt", "explode", "die"];

	// 数值调到第三幕水平：原版气态炸弹出自第一幕（血 7/8、伤害 8/9），第三幕原版召唤物（组装师的机器人）血 18–23、伤害 11–15。
	// 火球只爆一次、一批 5 个，伤害取机器人一档，血量略低于机器人。
	public override int MinInitialHp => ToughValue(15, 14);

	public override int MaxInitialHp => MinInitialHp;

	private int ExplodeDamage => DeadlyValue(12, 11);


	public override bool ShouldFadeAfterDeath => false;


	private bool HasExploded
	{
		get => _hasExploded;
		set
		{
			AssertMutable();
			_hasExploded = value;
		}
	}

	protected override MonsterMoveStateMachine GenerateMoveStateMachine()
	{
		MoveState explode = new(ExplodeMoveId, ExplodeMove, new DeathBlowIntent(() => ExplodeDamage));
		explode.FollowUpState = explode;
		return Machine([explode], explode);
	}

	private async Task ExplodeMove(IReadOnlyList<Creature> targets)
	{
		HasExploded = true;
		await Cued(DamageCmd.Attack(ExplodeDamage)
			.FromMonster(this)
			.WithAttackerAnim(ExplodeTrigger, 0.1f), BombHitSfx)
			.Execute(null);
		await CreatureCmd.Kill(Creature);
	}

	// 原版气态炸弹的状态机里有 attack，但骨骼里没有这个动画、也从不触发（爆炸走 ExplodeTrigger），这里不接。
	public override CreatureAnimator GenerateAnimator(MegaSprite controller)
	{
		AnimState idle = new("idle_loop", isLooping: true);
		AnimState spawn = new("spawn") { NextState = idle };
		AnimState hurt = new("hurt") { NextState = idle };
		CreatureAnimator animator = new(spawn, controller);
		animator.AddAnyState(CreatureAnimator.idleTrigger, idle);
		animator.AddAnyState(CreatureAnimator.deathTrigger, new AnimState("die"), () => !HasExploded);
		animator.AddAnyState(CreatureAnimator.hitTrigger, hurt);
		animator.AddAnyState(ExplodeTrigger, new AnimState("explode"));
		return animator;
	}
}
