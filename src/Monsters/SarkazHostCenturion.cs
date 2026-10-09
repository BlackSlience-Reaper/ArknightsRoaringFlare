using ArknightsRoaringFlare.Vfx;
using ArknightsRoaringFlare.Powers;
using MegaCrit.Sts2.Core.Animation;
using MegaCrit.Sts2.Core.Bindings.MegaSpine;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Commands.Builders;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.MonsterMoves.Intents;
using MegaCrit.Sts2.Core.MonsterMoves.MonsterMoveStateMachine;

namespace ArknightsRoaringFlare.Monsters;

/// <summary>
/// 萨卡兹宿主百夫长（PRTS enemy_1114_rgrdmn），梅菲斯特牧群的萨卡兹头目（第八章 M8 线）。
/// 汲取（<see cref="SiphonPower"/>）：汲取这一招造成的未被格挡伤害，按 150% 治疗自己（PRTS：造成伤害后治疗自身，治疗量为伤害量的 150%）。
/// 招式与数值见 docs/设计文档.md：汲取 → 群噬（两段）→ 吟唱（自身力量）。站最右、最后出手。
/// </summary>
public sealed class SarkazHostCenturion : FlareMonster
{
	public const string SiphonMoveId = "SIPHON_MOVE";
	public const string DevourMoveId = "DEVOUR_MOVE";
	public const string ChantMoveId = "CHANT_MOVE";

	private const int DevourHits = 2;
	private const int SiphonPercent = 150;

	// 攻击动画 OnAttack 事件的时刻（docs/spine_import.md）。
	protected override float AttackHitDelay => 1.133f;

	public override string SceneName => "sarkaz_host_centurion";

	// 原作音效（audio_data.json：enemy_1114_rgrdmn.attack 命中 e_imp_bigaxe_h，法术弹 projectile_enemy_rgrdmn 命中 e_imp_magic_s）。
	protected override FlareSound SoundFor(string slot) => slot switch
	{
		ArtsHitSfx => new(Hit: "e_imp_magic_s"),
		_ => new(Hit: "e_imp_bigaxe_h"),
	};

	// 原作特效（enemy_1114_rgrdmn：起手 enemy_rgrdmn_attack_01_start 在脚下，法术弹 projectile_enemy_rgrdmn 拖尾
	// enemy_rgrdmn_attack_01_trail 从第 28 根骨骼 muzzle 射出、命中 enemy_rgrdmn_attack_01_hit；汲取回血对应天赋 enemy_rgrdmn_passive_buff）。
	protected override AkFxRig Rig => new(0.29f, new Godot.Vector2(0f, 2.2f));

	protected override IEnumerable<string> ExtraEffects => ["enemy_rgrdmn_passive_buff"];

	protected override FlareFx FxFor(string slot) => slot switch
	{
		ArtsHitSfx => new(
			Start: [new("enemy_rgrdmn_attack_01_start", FxAnchor.Feet)],
			Hit: [new("enemy_rgrdmn_attack_01_hit")],
			Projectile: "enemy_rgrdmn_attack_01_trail",
			ProjectileBone: 28),
		_ => new(Hit: [new("enemy_rgrdmn_attack_01_hit")]),
	};

	public override IReadOnlyList<string> RequiredAnimations => ["Idle", AttackAnimation, "Die"];

	public override int MinInitialHp => ToughValue(100, 96);

	public override int MaxInitialHp => ToughValue(104, 100);

	private int SiphonDamage => DeadlyValue(13, 12);

	private int DevourDamage => DeadlyValue(7, 6);

	private int ChantStrength => DeadlyValue(3, 2);

	public override async Task AfterAddedToRoom()
	{
		await base.AfterAddedToRoom();
		await ApplyPower<SiphonPower>([Creature], SiphonPercent);
	}

	protected override MonsterMoveStateMachine GenerateMoveStateMachine()
	{
		MoveState siphon = new(SiphonMoveId, SiphonMove, new SingleAttackIntent(SiphonDamage), new HealIntent());
		MoveState devour = new(DevourMoveId, targets => Strike(DevourDamage, DevourHits, ArtsVfx, ArtsHitSfx), new MultiAttackIntent(DevourDamage, DevourHits));
		MoveState chant = new(ChantMoveId, ChantMove, new BuffIntent());
		siphon.FollowUpState = devour;
		devour.FollowUpState = chant;
		chant.FollowUpState = siphon;
		return Machine([siphon, devour, chant], siphon);
	}

	private async Task SiphonMove(IReadOnlyList<Creature> targets)
	{
		AttackCommand attack = await Strike(SiphonDamage, vfx: ArtsVfx, sfx: ArtsHitSfx);
		int unblocked = attack.Results.SelectMany(hits => hits).Sum(result => result.UnblockedDamage);
		if (unblocked > 0 && Creature.IsAlive)
		{
			PlayFx(new FxCue("enemy_rgrdmn_passive_buff"));
			await CreatureCmd.Heal(Creature, unblocked * SiphonPercent / 100);
		}
	}

	private async Task ChantMove(IReadOnlyList<Creature> targets)
	{
		BuffCue();
		await ApplyStrengthToSelf(ChantStrength);
	}

	public override CreatureAnimator GenerateAnimator(MegaSprite controller) =>
		BuildAnimator(controller, "Idle", "Die", (CreatureAnimator.attackTrigger, AttackAnimation));
}
