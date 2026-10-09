using ArknightsRoaringFlare.Vfx;
using ArknightsRoaringFlare.Powers;
using MegaCrit.Sts2.Core.Animation;
using MegaCrit.Sts2.Core.Bindings.MegaSpine;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.MonsterMoves.Intents;
using MegaCrit.Sts2.Core.MonsterMoves.MonsterMoveStateMachine;

namespace ArknightsRoaringFlare.Monsters;

/// <summary>
/// 炮火先兆者一族的公共部分：标定（给玩家挂弹着点）→ 炮击（按弹着点落弹并移除）→ 第三招。
/// 炮击预警：标定提前一回合告诉玩家要格挡多少；先兆者在这期间被击杀，炮弹照样在下个敌方回合结束时落地
/// （见 <see cref="ArtilleryMarkPower"/>，PRTS：弹道 3 秒后落地、使用缓存攻击力）。照原版火箭“蓄能 → 开火”的节奏。
/// 数值见 docs/设计文档.md。
/// </summary>
public abstract class HarbingerBase : FlareMonster
{
	public const string MarkMoveId = "MARK_MOVE";
	public const string BombardMoveId = "BOMBARD_MOVE";

	// 原作音效（audio_data.json：enemy_1112_emppnt.attack 标定收尾 e_atk_aim、开火 e_atk_energyshotcast、炮弹命中 e_imp_grenade_n）。
	protected override FlareSound SoundFor(string slot) => slot switch
	{
		RangedHitSfx => new(On: "e_atk_aim"),
		_ => new(On: "e_atk_energyshotcast", Hit: "e_imp_grenade_n"),
	};

	// 原作特效（enemy_1112_emppnt：开火 enemy_emppnt_attack_01_start_02 在炮口（第 11 根骨骼 C_Muzzle），炮弹
	// projectile_enemy_emppnt 拖尾 enemy_emppnt_attack_01_trail、落点 enemy_emppnt_attack_01_hit；预警准星与光柱
	// enemy_emppnt_attack_01_start 放在落点，自身的扫描 enemy_emppnt_scanning_buff）。Graphic 缩放 0.19×0.24（比士兵小，
	// 按纵向 0.24 换算），HitPoint (0, 0.6)。炮弹是抛射（原作弹道带高度）。
	protected override AkFxRig Rig => new(0.24f, new Godot.Vector2(0f, 0.6f));

	protected override IEnumerable<string> ExtraEffects => ["enemy_emppnt_scanning_buff", "enemy_emppnt_attack_01_start"];

	protected override FlareFx FxFor(string slot) => slot switch
	{
		RangedHitSfx => default,
		_ => new(
			// 原作挂点组件：_start_02 在单位根节点（无人机的根节点在它投下阴影的地面上），炮弹从 C_Muzzle（11 号）射出。
			Start: [new("enemy_emppnt_attack_01_start_02", FxAnchor.Feet, Delay: 0.3f)],
			Hit: [new("enemy_emppnt_attack_01_hit", FxAnchor.Feet)],
			Projectile: "enemy_emppnt_attack_01_trail",
			ProjectileBone: 11,
			ProjectileArc: 1.2f),
	};

	public override IReadOnlyList<string> RequiredAnimations => ["Idle", AttackAnimation, "Die"];

	// 攻击动画 OnAttack 事件的时刻（docs/spine_import.md）。
	protected override float AttackHitDelay => 0.333f;

	protected abstract int BombardDamage { get; }

	/// <summary>第三招（霰弹 / 速射）。</summary>
	protected abstract MoveState CreateThirdMove();

	protected override MonsterMoveStateMachine GenerateMoveStateMachine()
	{
		MoveState mark = new(MarkMoveId, MarkMove, new DebuffIntent());
		MoveState bombard = new(BombardMoveId, BombardMove, new SingleAttackIntent(BombardDamage));
		MoveState third = CreateThirdMove();
		mark.FollowUpState = bombard;
		bombard.FollowUpState = third;
		third.FollowUpState = mark;
		return Machine([mark, bombard, third], mark);
	}

	private async Task MarkMove(IReadOnlyList<Creature> targets)
	{
		PlaySound(RangedHitSfx);
		PlayFx(new FxCue("enemy_emppnt_scanning_buff", FxAnchor.Feet, Tilt: true, StopAfter: 2f));
		foreach (Creature target in targets)
		{
			PlayFxOn(target, new FxCue("enemy_emppnt_attack_01_start", FxAnchor.Feet, StopAfter: 2.6f, Tune: GroundDecal));
		}

		await ApplyPower<ArtilleryMarkPower>(targets, BombardDamage);
	}

	private async Task BombardMove(IReadOnlyList<Creature> targets)
	{
		await Strike(BombardDamage, vfx: HeavyBluntVfx, sfx: BombHitSfx);
		foreach (Creature target in targets)
		{
			if (target.GetPower<ArtilleryMarkPower>() is { } mark)
			{
				await PowerCmd.Remove(mark);
			}
		}
	}

	public override CreatureAnimator GenerateAnimator(MegaSprite controller) =>
		BuildAnimator(controller, "Idle", "Die", (CreatureAnimator.attackTrigger, AttackAnimation));
}

/// <summary>帝国炮火先兆者（PRTS enemy_1112_emppnt）：标定 → 炮击 → 霰弹（两段）。</summary>
public sealed class ArtilleryHarbinger : HarbingerBase
{
	public const string ScatterMoveId = "SCATTER_MOVE";

	private const int ScatterHits = 2;

	public override string SceneName => "artillery_harbinger";

	public override int MinInitialHp => ToughValue(126, 120);

	public override int MaxInitialHp => ToughValue(132, 126);

	protected override int BombardDamage => DeadlyValue(30, 28);

	private int ScatterDamage => DeadlyValue(7, 6);

	protected override MoveState CreateThirdMove() =>
		new(ScatterMoveId, targets => Strike(ScatterDamage, ScatterHits, BluntVfx, BombHitSfx), new MultiAttackIntent(ScatterDamage, ScatterHits));
}

/// <summary>帝国炮火中枢先兆者（PRTS enemy_1112_emppnt_2），先兆者的强化版：标定 → 炮击 → 速射（三段）。</summary>
public sealed class CoreHarbinger : HarbingerBase
{
	public const string RapidFireMoveId = "RAPID_FIRE_MOVE";

	private const int RapidHits = 3;

	public override string SceneName => "core_harbinger";

	public override int MinInitialHp => ToughValue(178, 170);

	public override int MaxInitialHp => ToughValue(184, 176);

	protected override int BombardDamage => DeadlyValue(33, 30);

	private int RapidDamage => DeadlyValue(7, 6);

	protected override MoveState CreateThirdMove() =>
		new(RapidFireMoveId, targets => Strike(RapidDamage, RapidHits, BluntVfx, BombHitSfx), new MultiAttackIntent(RapidDamage, RapidHits));
}
