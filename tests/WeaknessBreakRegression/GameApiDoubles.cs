// 外部游戏 API 的最小测试替身；阈值、位掩码、奖励与常规循环来自链接的生产源码。
// 击晕排程、攻击修正和活力消耗在这里建模，仍须在真实游戏中验证。
using ArknightsRoaringFlare.Powers;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.MonsterMoves.MonsterMoveStateMachine;

namespace Godot
{
    public readonly record struct Vector2(float X, float Y);
    public readonly record struct Color(float R, float G, float B);
}
namespace MegaCrit.Sts2.Core.Animation { public class CreatureAnimator { public const string attackTrigger = "Attack"; } }
namespace MegaCrit.Sts2.Core.Bindings.MegaSpine { public class MegaSprite; }
namespace MegaCrit.Sts2.Core.Combat { public enum CombatSide { Player, Enemy } }
namespace MegaCrit.Sts2.Core.Entities.Ascension { public enum AscensionLevel { ToughEnemies = 8, DeadlyEnemies = 9 } }
namespace MegaCrit.Sts2.Core.Helpers
{
    public static class AscensionHelper
    {
        public static int Level { get; set; }
        public static int GetValueIfAscension(MegaCrit.Sts2.Core.Entities.Ascension.AscensionLevel level, int ascension, int normal)
            => Level >= (int)level ? ascension : normal;
    }
}
namespace MegaCrit.Sts2.Core.Entities.Cards { public enum PileType { Draw, Discard } }
namespace MegaCrit.Sts2.Core.Entities.Powers { public enum PowerType { Buff } public enum PowerStackType { Counter } }
namespace MegaCrit.Sts2.Core.GameActions.Multiplayer
{
    public class PlayerChoiceContext;
    public class ThrowingPlayerChoiceContext : PlayerChoiceContext;
}
namespace MegaCrit.Sts2.Core.Models.Cards { public class Dazed; }
namespace MegaCrit.Sts2.Core.Models.Powers
{
    public class TestPower { public int Amount { get; set; } }
    public class VigorPower : TestPower;
    public class StrengthPower : TestPower;
    public class EnragePower : TestPower;
    public class VulnerablePower : TestPower;
}
namespace MegaCrit.Sts2.Core.Entities.Creatures
{
    public sealed class Creature
    {
        public object? Monster { get; set; }
        public int MaxHp { get; set; }
        public int CurrentHp { get; set; }
        public bool IsDead => CurrentHp <= 0;
        public bool Stunned { get; set; }
        public string? StunReturnId { get; set; }
        public int StunCalls { get; set; }
        public Action? OnStun { get; set; }
        public List<(int Base, int Effective, int Hits)> Attacks { get; } = [];
        private readonly Dictionary<Type, TestPower> _powers = [];
        public T? GetPower<T>() where T : TestPower => _powers.GetValueOrDefault(typeof(T)) as T;
        public void AddPower<T>(int amount) where T : TestPower, new()
        {
            if (!_powers.TryGetValue(typeof(T), out TestPower? power))
                _powers[typeof(T)] = power = new T();
            power.Amount += amount;
        }
        public void Attack(int damage, int hits)
        {
            int vigor = GetPower<VigorPower>()?.Amount ?? 0;
            int strength = GetPower<StrengthPower>()?.Amount ?? 0;
            Attacks.Add((damage, damage + vigor + strength, hits));
            if (GetPower<VigorPower>() is { } power) power.Amount = 0;
        }
    }
}
namespace MegaCrit.Sts2.Core.MonsterMoves.Intents
{
    public sealed record SingleAttackIntent(int Damage);
    public sealed record MultiAttackIntent(int Damage, int Hits);
    public class DefendIntent;
    public class BuffIntent;
    public class DebuffIntent;
    public sealed record StatusIntent(int Count);
}
namespace MegaCrit.Sts2.Core.MonsterMoves.MonsterMoveStateMachine
{
    public abstract class MonsterState(string id) { public string Id { get; } = id; }
    public sealed class MoveState(string id, Func<IReadOnlyList<Creature>, Task> move, params object[] intents) : MonsterState(id)
    {
        public Func<IReadOnlyList<Creature>, Task> Move { get; } = move;
        public object[] Intents { get; } = intents;
        public MonsterState? FollowUpState { get; set; }
    }
    public sealed class ConditionalBranchState(string id) : MonsterState(id)
    {
        private readonly List<(MonsterState State, Func<bool> Condition)> _branches = [];
        public void AddState(MonsterState state, Func<bool> condition) => _branches.Add((state, condition));
        public MonsterState Resolve() => _branches.First(branch => branch.Condition()).State;
    }
    public sealed class MonsterMoveStateMachine(IEnumerable<MonsterState> states, MonsterState start)
    {
        public Dictionary<string, MonsterState> States { get; } = states.ToDictionary(state => state.Id);
        public MonsterState Current { get; set; } = start;
    }
}
namespace MegaCrit.Sts2.Core.Commands
{
    public static class CreatureCmd
    {
        public static Task Stun(Creature creature, string nextMove)
        {
            creature.StunCalls++;
            creature.Stunned = true;
            creature.StunReturnId = nextMove;
            creature.OnStun?.Invoke();
            return Task.CompletedTask;
        }
    }
    public static class PowerCmd
    {
        public static Task Apply<T>(object context, Creature owner, int amount, Creature source, object? card)
            where T : TestPower, new() { owner.AddPower<T>(amount); return Task.CompletedTask; }
        public static Task ModifyAmount(object context, TestPower power, int delta, Creature source, object? card)
            { power.Amount += delta; return Task.CompletedTask; }
    }
    public static class CardPileCmd
    {
        public static Task AddToCombatAndPreview<T>(IReadOnlyList<Creature> targets, MegaCrit.Sts2.Core.Entities.Cards.PileType pile, int count, object? card)
            => Task.CompletedTask;
    }
    public static class DamageCmd { public static AttackBuilder Attack(int damage) => new(damage); }
    public sealed class AttackBuilder(int damage)
    {
        private Creature _attacker = null!;
        private int _hits = 1;
        public AttackBuilder FromMonster(ArknightsRoaringFlare.Monsters.FlareMonster monster) { _attacker = monster.Creature; return this; }
        public AttackBuilder WithHitCount(int hits) { _hits = hits; return this; }
        public AttackBuilder WithAttackerAnim(string animation, float delay) => this;
        public AttackBuilder WithHitFx(string effect) => this;
        public Task Execute(object? context) { _attacker.Attack(damage, _hits); return Task.CompletedTask; }
    }
}
namespace MegaCrit.Sts2.Core.HoverTips
{
    public interface IHoverTip;
    public sealed class TestHoverTip : IHoverTip;
    public static class HoverTipFactory { public static IHoverTip FromPower<T>(int amount) => new TestHoverTip(); }
}
namespace MegaCrit.Sts2.Core.Localization.DynamicVars
{
    public class DynamicVar(string name, int amount)
    {
        protected object? _owner;
        protected virtual decimal GetBaseValueForIConvertible() => amount;
        public string Name { get; } = name;
    }
}
namespace STS2RitsuLib.Scaffolding.Content
{
    public sealed class PowerAssetProfile;
    public abstract class ModPowerTemplate : TestPower
    {
        public Creature Owner { get; set; } = null!;
        public bool IsMutable => true;
        public abstract PowerAssetProfile AssetProfile { get; }
        public abstract MegaCrit.Sts2.Core.Entities.Powers.PowerType Type { get; }
        public abstract MegaCrit.Sts2.Core.Entities.Powers.PowerStackType StackType { get; }
        protected virtual IEnumerable<MegaCrit.Sts2.Core.HoverTips.IHoverTip> AdditionalHoverTips => [];
        protected virtual IEnumerable<MegaCrit.Sts2.Core.Localization.DynamicVars.DynamicVar> CanonicalVars => [];
        protected void AssertMutable() { }
        protected void Flash() { }
        public virtual Task AfterCurrentHpChanged(Creature creature, decimal delta) => Task.CompletedTask;
    }
}
namespace STS2RitsuLib.Combat.HealthBars
{
    public enum HealthBarForecastGrowthDirection { FromLeft }
    public enum HealthBarForecastLeftOriginLayout { OverlapFromOrigin }
    public sealed record HealthBarForecastContext(Creature Creature);
    public sealed record HealthBarForecastSegment(int Hp, Godot.Color Color, HealthBarForecastGrowthDirection Growth,
        int Index, object? First, object? Second, HealthBarForecastLeftOriginLayout Layout, bool AffectsHpLabel);
    public interface IHealthBarForecastSource { IEnumerable<HealthBarForecastSegment> GetHealthBarForecastSegments(HealthBarForecastContext context); }
}
namespace ArknightsRoaringFlare
{
    public static class RoaringFlareAssets
    {
        public static STS2RitsuLib.Scaffolding.Content.PowerAssetProfile PowerIcon(string icon) => new();
    }
}
namespace ArknightsRoaringFlare.Powers { public class BattleHardenedPower : TestPower; }
namespace ArknightsRoaringFlare.Vfx
{
    public enum FxAnchor { Feet, Bone, HitPoint }
    public readonly record struct AkFxRig(float Scale, Godot.Vector2 HitPoint);
    public readonly record struct FxCue(string Name, FxAnchor Anchor = FxAnchor.HitPoint, int Bone = -1,
        float Delay = 0, bool Spin = true, bool Flip = false);
}
namespace ArknightsRoaringFlare.Monsters
{
    public abstract partial class FlareMonster
    {
        protected const string RangedHitSfx = "ranged", HeavyHitSfx = "heavy", SlashVfx = "slash", BluntVfx = "blunt";
        protected readonly record struct FlareSound(string? Start = null, string? On = null, string? Hit = null);
        protected readonly record struct FlareFx(ArknightsRoaringFlare.Vfx.FxCue[]? Start = null,
            ArknightsRoaringFlare.Vfx.FxCue[]? Hit = null, string? Projectile = null, int ProjectileBone = -1);
        protected virtual FlareSound SoundFor(string slot) => new();
        protected virtual ArknightsRoaringFlare.Vfx.AkFxRig Rig => new();
        protected virtual FlareFx FxFor(string slot) => new();
        protected virtual ArknightsRoaringFlare.Vfx.FxCue[]? BuffFx => null;
        protected virtual string AttackAnimation => "Attack";
        protected virtual float AttackHitDelay => 0;
        public virtual IReadOnlyList<string> RequiredAnimations => [];
        public abstract string SceneName { get; }
        public abstract int MinInitialHp { get; }
        public abstract int MaxInitialHp { get; }
        public Creature Creature { get; } = new();
        public WeaknessBreakPower? StartingPower { get; private set; }
        public MonsterMoveStateMachine MoveStateMachine { get; private set; } = null!;
        public int OpeningOffset { get; set; }
        public string? OpeningStateId { get; private set; }
        public virtual Task AfterAddedToRoom() => Task.CompletedTask;
        public virtual MegaCrit.Sts2.Core.Animation.CreatureAnimator GenerateAnimator(MegaCrit.Sts2.Core.Bindings.MegaSpine.MegaSprite controller) => new();
        public virtual Task AfterSideTurnEnd(MegaCrit.Sts2.Core.GameActions.Multiplayer.PlayerChoiceContext context,
            MegaCrit.Sts2.Core.Combat.CombatSide side, IEnumerable<Creature> participants) => Task.CompletedTask;
        protected abstract MonsterMoveStateMachine GenerateMoveStateMachine();
        public void SetUpForCombat(int maxHp)
        {
            Creature.Monster = this;
            Creature.MaxHp = Creature.CurrentHp = maxHp;
            MoveStateMachine = GenerateMoveStateMachine();
        }
        protected void AssertMutable() { }
        protected void BuffCue() { }
        protected Task GainBlock(int amount) => Task.CompletedTask;
        protected Task ApplyStrengthToSelf(int amount) { Creature.AddPower<StrengthPower>(amount); return Task.CompletedTask; }
        protected Task ApplyPower<T>(IEnumerable<Creature> targets, int amount) where T : TestPower, new()
        {
            foreach (Creature target in targets)
            {
                if (typeof(T) == typeof(WeaknessBreakPower))
                    StartingPower = new WeaknessBreakPower { Owner = target, Amount = amount };
                else target.AddPower<T>(amount);
            }
            return Task.CompletedTask;
        }
        protected Task Strike(int damage, string? vfx = null, string? sfx = null) { Creature.Attack(damage, 1); return Task.CompletedTask; }
        protected MegaCrit.Sts2.Core.Commands.AttackBuilder Cued(MegaCrit.Sts2.Core.Commands.AttackBuilder builder, string sound) => builder;
        protected static Task AddStatusCards<T>(IReadOnlyList<Creature> targets, int count) => Task.CompletedTask;
        protected static MegaCrit.Sts2.Core.Animation.CreatureAnimator BuildAnimator(MegaCrit.Sts2.Core.Bindings.MegaSpine.MegaSprite sprite,
            string idle, string die, params (string, string)[] triggers) => new();

        // 仅调度外部状态机；每个 Move 和条件分支的内容均来自生产源码。
        public async Task<string> TakeTurn()
        {
            if (Creature.Stunned)
            {
                Creature.Stunned = false;
                MoveStateMachine.Current = MoveStateMachine.States[Creature.StunReturnId!];
                return "STUNNED";
            }
            MoveState move = (MoveState)MoveStateMachine.Current;
            await move.Move([]);
            MonsterState next = move.FollowUpState!;
            MoveStateMachine.Current = next is ConditionalBranchState branch ? branch.Resolve() : next;
            return move.Id;
        }
    }
}
