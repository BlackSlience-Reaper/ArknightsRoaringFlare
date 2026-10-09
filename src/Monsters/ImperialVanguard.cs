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
/// 帝国前锋一族的公共部分：开场带背水（<see cref="LastStandPower"/>，生命第一次降到一半以下时获得力量；
/// PRTS：“生命值低于 50% 时攻击力 +100%”），远射 → 突刺 → 战意。数值见 docs/设计文档.md。
/// </summary>
public abstract class VanguardBase : FlareMonster
{
	public const string LongShotMoveId = "LONG_SHOT_MOVE";
	public const string ThrustMoveId = "THRUST_MOVE";
	public const string ResolveMoveId = "RESOLVE_MOVE";

	// 原作音效（audio_data.json：enemy_1113_empace.combat 出手 e_atk_bigaxe_n、命中 e_imp_bigaxe_n，.attack 起手 e_imp_mousek_n、远射命中 e_imp_reinforcement）。
	protected override FlareSound SoundFor(string slot) => slot switch
	{
		RangedHitSfx => new(Start: "e_imp_mousek_n", Hit: "e_imp_reinforcement"),
		_ => new(On: "e_atk_bigaxe_n", Hit: "e_imp_bigaxe_n"),
	};

	// 原作特效（enemy_1113_empace：近战 Combat 起手 enemy_empace_attack_01_start、武器闪光 _start_02、命中 enemy_empace_attack_01_hit；
	// 远射 Attack 起手 enemy_empace_attack_02_start、子弹 projectile_enemy_empace 拖尾 enemy_empace_attack_02_trail；
	// 背水天赋的 enemy_empace_eye_buff 在眼睛）。Graphic 缩放 0.30。刺击与闪光对到出手瞬间（近战 Attack_2 的 OnAttack 1.033 秒、
	// 远射 Attack_1_Loop 的 0.5 秒）。挂点照原作挂点组件（tools/ak_vfx/attach.py）：两个起手在单位根节点（脚下），
	// _start_02 在 SpecialPoint0 即武器骨骼 C_L_Weapon_2，朝向取单位朝向（挂点组件 A=10，与根节点特效同一模式；跟着骨骼转时刺光反向朝后）；子弹从枪口 C_Muzzle 射出。两套骨架的骨骼序号不同，由子类给。
	protected override AkFxRig Rig => new(0.30f, new Godot.Vector2(0f, 1.6f));

	/// <summary>枪口骨骼 C_Muzzle 在本地 skel 里的序号。</summary>
	protected abstract int MuzzleBone { get; }

	/// <summary>武器骨骼 C_L_Weapon_2（原作 SpecialPoint0）在本地 skel 里的序号。</summary>
	protected abstract int WeaponBone { get; }

	/// <summary>眼睛骨骼 C_Eye 在本地 skel 里的序号。</summary>
	protected abstract int EyeBone { get; }

	protected override FlareFx FxFor(string slot) => slot switch
	{
		RangedHitSfx => new(
			Start: [new("enemy_empace_attack_02_start", FxAnchor.Feet, Delay: 0.45f), new("enemy_empace_attack_01_start_02", FxAnchor.Bone, WeaponBone, 0.45f, Spin: false)],
			Hit: [new("enemy_empace_attack_01_hit")],
			Projectile: "enemy_empace_attack_02_trail",
			ProjectileBone: MuzzleBone),
		_ => new(
			// 近战起手的刺光几何在 −x（刺尖 −0.38、爆点 −1.4），与同怪射击起手（火光在 +0.46）及其他怪的起手相反，镜像取反才在身前；
			// 原作数据里找不到区分两者的字段（都是挂点模式 A=10、带 rotation_y），是对照预览逐个定的。
			Start: [new("enemy_empace_attack_01_start", FxAnchor.Feet, Delay: 0.98f, Flip: true), new("enemy_empace_attack_01_start_02", FxAnchor.Bone, WeaponBone, 0.98f, Spin: false)],
			Hit: [new("enemy_empace_attack_01_hit")]),
	};

	protected override FxCue[]? BuffFx => [new("enemy_empace_eye_buff", FxAnchor.Bone, EyeBone)];

	// 前锋的骨骼没有 Attack：近战用单段的 Attack_2（OnAttack 1.033 秒），远射用 Attack_1_Loop（0.5 秒）。
	private const string ShotTrigger = "Shot";

	protected override string AttackAnimation => "Attack_2";

	protected override float AttackHitDelay => 1.033f;

	public override IReadOnlyList<string> RequiredAnimations => ["Idle", AttackAnimation, "Attack_1_Loop", "Die"];

	protected abstract int LastStandStrength { get; }

	protected abstract int LongShotDamage { get; }

	protected abstract int ThrustDamage { get; }

	protected abstract MoveState CreateResolveMove();

	public override async Task AfterAddedToRoom()
	{
		await base.AfterAddedToRoom();
		await ApplyPower<LastStandPower>([Creature], LastStandStrength);
	}

	protected override MonsterMoveStateMachine GenerateMoveStateMachine()
	{
		MoveState shot = new(LongShotMoveId, LongShotMove, new SingleAttackIntent(LongShotDamage));
		MoveState thrust = new(ThrustMoveId, targets => Strike(ThrustDamage, vfx: SlashVfx, sfx: HeavyHitSfx), new SingleAttackIntent(ThrustDamage));
		MoveState resolve = CreateResolveMove();
		shot.FollowUpState = thrust;
		thrust.FollowUpState = resolve;
		resolve.FollowUpState = shot;
		return Machine([shot, thrust, resolve], shot);
	}

	private Task LongShotMove(IReadOnlyList<Creature> targets) =>
		Cued(DamageCmd.Attack(LongShotDamage).FromMonster(this)
			.WithAttackerAnim(ShotTrigger, 0.5f)
			.WithHitFx(BluntVfx), RangedHitSfx)
			.Execute(null);

	public override CreatureAnimator GenerateAnimator(MegaSprite controller) =>
		BuildAnimator(controller, "Idle", "Die", (CreatureAnimator.attackTrigger, AttackAnimation), (ShotTrigger, "Attack_1_Loop"));
}

/// <summary>帝国前锋精锐（PRTS enemy_1113_empace），单独成一场普通战“沉默的屠夫”。战意：攻击 + 自身力量。</summary>
public sealed class ImperialVanguard : VanguardBase
{
	public override string SceneName => "imperial_vanguard";

	protected override int MuzzleBone => 24;

	protected override int WeaponBone => 23;

	protected override int EyeBone => 36;

	public override int MinInitialHp => ToughValue(176, 168);

	public override int MaxInitialHp => ToughValue(182, 174);

	protected override int LastStandStrength => DeadlyValue(5, 4);

	protected override int LongShotDamage => DeadlyValue(11, 10);

	protected override int ThrustDamage => DeadlyValue(20, 18);

	private int ResolveDamage => DeadlyValue(9, 8);

	private int ResolveStrength => DeadlyValue(4, 3);

	protected override MoveState CreateResolveMove() =>
		new(ResolveMoveId, ResolveMove, new SingleAttackIntent(ResolveDamage), new BuffIntent());

	private async Task ResolveMove(IReadOnlyList<Creature> targets)
	{
		await Strike(ResolveDamage);
		PlayFx(BuffFx);
		await ApplyStrengthToSelf(ResolveStrength);
	}
}

/// <summary>帝国前锋百战精锐（PRTS enemy_1113_empace_2），前锋精锐的强化版，精英战“百战精锐”。战意：自身力量 + 格挡。</summary>
public sealed class VanguardVeteran : VanguardBase
{
	private const int ResolveBlock = 12;

	public override string SceneName => "vanguard_veteran";

	protected override int MuzzleBone => 25;

	protected override int WeaponBone => 24;

	protected override int EyeBone => 8;

	public override int MinInitialHp => ToughValue(206, 196);

	public override int MaxInitialHp => ToughValue(212, 202);

	protected override int LastStandStrength => DeadlyValue(7, 6);

	protected override int LongShotDamage => DeadlyValue(11, 10);

	protected override int ThrustDamage => DeadlyValue(19, 17);

	private int ResolveStrength => DeadlyValue(4, 3);

	protected override MoveState CreateResolveMove() =>
		new(ResolveMoveId, ResolveMove, new BuffIntent(), new DefendIntent());

	private async Task ResolveMove(IReadOnlyList<Creature> targets)
	{
		BuffCue();
		await ApplyStrengthToSelf(ResolveStrength);
		await GainBlock(ResolveBlock);
	}
}
