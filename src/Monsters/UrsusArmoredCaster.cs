using ArknightsRoaringFlare.Vfx;
using MegaCrit.Sts2.Core.Animation;
using MegaCrit.Sts2.Core.Bindings.MegaSpine;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.MonsterMoves.Intents;
using MegaCrit.Sts2.Core.MonsterMoves.MonsterMoveStateMachine;

namespace ArknightsRoaringFlare.Monsters;

/// <summary>
/// 着铠术师一族的公共部分：开场带原版“覆甲”（源石铠甲，PRTS 的高防高抗），
/// 源石法术（玩家虚弱）→ 法术连击（两段）→ 铠化（格挡 + 自身力量）。数值见 docs/设计文档.md。
/// </summary>
public abstract class ArmoredCasterBase : FlareMonster
{
	public const string ArtsBoltMoveId = "ARTS_BOLT_MOVE";
	public const string ArtsComboMoveId = "ARTS_COMBO_MOVE";
	public const string ArmorUpMoveId = "ARMOR_UP_MOVE";

	// 原作音效（audio_data.json：enemy_1110_uamord.attack 出手 e_atk_magic_n、法术命中 e_imp_magic_n）。
	protected override FlareSound SoundFor(string slot) => slot switch
	{
		_ => new(On: "e_atk_magic_n", Hit: "e_imp_magic_n"),
	};

	// 原作特效（enemy_1110_uamord：施法 enemy_uamord_attack_01_start 在枪口（第 28 根骨骼 Muzzle），法术弹
	// projectile_enemy_uamord 拖尾 enemy_uamord_attack_01_trail、命中 enemy_uamord_attack_01_hit）。_2 骨架的骨骼序号相同。
	protected override AkFxRig Rig => new(0.27f, new Godot.Vector2(0f, 1.6f));

	protected override FlareFx FxFor(string slot) => new(
		// 原作挂点组件：起手在单位根节点（脚下）；子弹从 Muzzle（本地 skel 28 号）射出。
		Start: [new("enemy_uamord_attack_01_start", FxAnchor.Feet)],
		Hit: [new("enemy_uamord_attack_01_hit")],
		Projectile: "enemy_uamord_attack_01_trail",
		ProjectileBone: 28);

	private const int ComboHits = 2;
	private const int BoltWeak = 1;

	public override IReadOnlyList<string> RequiredAnimations => ["Idle", AttackAnimation, "Die"];

	// 攻击动画 OnAttack 事件的时刻（docs/spine_import.md）。
	protected override float AttackHitDelay => 0.633f;

	protected abstract int PlatingAmount { get; }

	protected abstract int ArtsBoltDamage { get; }

	protected abstract int ArtsComboDamage { get; }

	protected abstract int ArmorUpBlock { get; }

	protected abstract int ArmorUpStrength { get; }

	public override async Task AfterAddedToRoom()
	{
		await base.AfterAddedToRoom();
		await ApplyPower<PlatingPower>([Creature], PlatingAmount);
	}

	protected override MonsterMoveStateMachine GenerateMoveStateMachine()
	{
		MoveState bolt = new(ArtsBoltMoveId, ArtsBoltMove, new SingleAttackIntent(ArtsBoltDamage), new DebuffIntent());
		MoveState combo = new(ArtsComboMoveId, ArtsComboMove, new MultiAttackIntent(ArtsComboDamage, ComboHits));
		MoveState armor = new(ArmorUpMoveId, ArmorUpMove, new DefendIntent(), new BuffIntent());
		bolt.FollowUpState = combo;
		combo.FollowUpState = armor;
		armor.FollowUpState = bolt;
		return Machine([bolt, combo, armor], bolt);
	}

	private async Task ArtsBoltMove(IReadOnlyList<Creature> targets)
	{
		await Strike(ArtsBoltDamage, vfx: ArtsVfx, sfx: ArtsHitSfx);
		await ApplyPower<WeakPower>(targets, BoltWeak);
	}

	private Task ArtsComboMove(IReadOnlyList<Creature> targets) => Strike(ArtsComboDamage, ComboHits, ArtsVfx, ArtsHitSfx);

	private async Task ArmorUpMove(IReadOnlyList<Creature> targets)
	{
		BuffCue();
		await GainBlock(ArmorUpBlock);
		await ApplyStrengthToSelf(ArmorUpStrength);
	}

	public override CreatureAnimator GenerateAnimator(MegaSprite controller) =>
		BuildAnimator(controller, "Idle", "Die", (CreatureAnimator.attackTrigger, AttackAnimation));
}

/// <summary>乌萨斯着铠术师（PRTS enemy_1110_uamord），法术远程、高防高抗。</summary>
public sealed class UrsusArmoredCaster : ArmoredCasterBase
{
	public override string SceneName => "ursus_armored_caster";

	public override int MinInitialHp => ToughValue(92, 88);

	public override int MaxInitialHp => ToughValue(96, 92);

	protected override int PlatingAmount => ToughValue(10, 8);

	protected override int ArtsBoltDamage => DeadlyValue(15, 14);

	protected override int ArtsComboDamage => DeadlyValue(9, 8);

	protected override int ArmorUpBlock => 12;

	protected override int ArmorUpStrength => DeadlyValue(3, 2);
}

/// <summary>乌萨斯高级着铠术师（PRTS enemy_1110_uamord_2），着铠术师的强化版。</summary>
public sealed class SeniorArmoredCaster : ArmoredCasterBase
{
	public override string SceneName => "senior_armored_caster";

	public override int MinInitialHp => ToughValue(104, 100);

	public override int MaxInitialHp => ToughValue(108, 104);

	protected override int PlatingAmount => ToughValue(12, 10);

	protected override int ArtsBoltDamage => DeadlyValue(17, 16);

	protected override int ArtsComboDamage => DeadlyValue(10, 9);

	protected override int ArmorUpBlock => 14;

	protected override int ArmorUpStrength => DeadlyValue(3, 2);
}
