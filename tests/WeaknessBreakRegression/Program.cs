using ArknightsRoaringFlare.Monsters;
using ArknightsRoaringFlare.Powers;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.MonsterMoves.Intents;
using MegaCrit.Sts2.Core.MonsterMoves.MonsterMoveStateMachine;
using STS2RitsuLib.Combat.HealthBars;

int passed = 0;
int failed = 0;
async Task Check(string name, Func<Task> test)
{
    try { await test(); passed++; Console.WriteLine($"PASS {name}"); }
    catch (Exception error) { failed++; Console.Error.WriteLine($"FAIL {name}: {error.Message}"); }
}
void Equal<T>(T expected, T actual, string label)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException($"{label}: expected {expected}, got {actual}");
}
async Task<ImperialVanguard> Create(int ascension, int? maxHp = null)
{
    AscensionHelper.Level = ascension;
    ImperialVanguard monster = new();
    monster.SetUpForCombat(maxHp ?? monster.MaxInitialHp);
    await monster.AfterAddedToRoom();
    Equal(VanguardBase.LongShotMoveId, monster.OpeningStateId, "opening");
    if (monster.StartingPower is null) throw new InvalidOperationException("starting WeaknessBreakPower missing");
    Equal(ascension >= 9 ? 15 : 10, monster.StartingPower.Amount, "starting Vigor reward");
    return monster;
}
async Task ChangeHp(ImperialVanguard monster, int hp)
{
    int delta = hp - monster.Creature.CurrentHp;
    monster.Creature.CurrentHp = hp;
    await monster.StartingPower!.AfterCurrentHpChanged(monster.Creature, delta);
}
int Vigor(ImperialVanguard monster) => monster.Creature.GetPower<VigorPower>()?.Amount ?? 0;
int Threshold(ImperialVanguard monster, int numerator) => monster.Creature.MaxHp * numerator / 3;
void AssertThrust(ImperialVanguard monster, int basis, int effective)
{
    MoveState thrust = (MoveState)monster.MoveStateMachine.States[VanguardBase.ThrustMoveId];
    Equal(basis, thrust.Intents.OfType<SingleAttackIntent>().Single().Damage, "base intent");
    Equal((basis, effective, 1), monster.Creature.Attacks[^1], "executed Thrust");
    Equal(0, Vigor(monster), "Vigor consumed");
}

// 真正从生命变化触发生产 AfterCurrentHpChanged，随后调用生产突刺委托。
foreach (int ascension in new[] { 0, 8, 9, 10 })
{
    int basis = ascension >= 9 ? 20 : 19;
    int reward = ascension >= 9 ? 15 : 10;
    foreach (bool interruptResolve in new[] { false, true })
    {
        await Check($"A{ascension} single threshold, resume {(interruptResolve ? "Resolve" : "Long Shot")}", async () =>
        {
            ImperialVanguard monster = await Create(ascension);
            if (interruptResolve)
                Equal(VanguardBase.LongShotMoveId, await monster.TakeTurn(), "first Long Shot");
            string pending = monster.MoveStateMachine.Current.Id;
            await ChangeHp(monster, Threshold(monster, 2) + 1);
            Equal(0, monster.Creature.StunCalls, "above threshold");
            await ChangeHp(monster, Threshold(monster, 2));
            Equal(1, monster.StartingPower!.TriggeredThresholds, "first threshold recorded");
            Equal(1, monster.Creature.StunCalls, "Stun called");
            Equal(VanguardBase.ThrustMoveId, monster.Creature.StunReturnId, "inserted move");
            Equal(reward, Vigor(monster), "single reward");
            int attacksBeforeStun = monster.Creature.Attacks.Count;
            Equal("STUNNED", await monster.TakeTurn(), "stunned turn");
            Equal(attacksBeforeStun, monster.Creature.Attacks.Count, "no attack while stunned");
            Equal(reward, Vigor(monster), "Vigor survives Stun");
            Equal(VanguardBase.ThrustMoveId, await monster.TakeTurn(), "actual Thrust delegate");
            AssertThrust(monster, basis, basis + reward);
            Equal(pending, monster.MoveStateMachine.Current.Id, "resume interrupted intent");
            Equal(pending, await monster.TakeTurn(), "resumed move executes");
            string other = pending == VanguardBase.ResolveMoveId ? VanguardBase.LongShotMoveId : VanguardBase.ResolveMoveId;
            Equal(other, await monster.TakeTurn(), "regular cycle continues");
            Equal(1, monster.Creature.Attacks.Count(attack => attack.Base == basis), "only one inserted Thrust");
        });
    }
}

foreach (int ascension in new[] { 0, 10 })
{
    int basis = ascension == 10 ? 20 : 19;
    int reward = ascension == 10 ? 15 : 10;
    foreach (bool separateHits in new[] { false, true })
    {
        await Check($"A{ascension} both thresholds {(separateHits ? "during same Stun" : "in one hit")}", async () =>
        {
            ImperialVanguard monster = await Create(ascension);
            if (separateHits) await ChangeHp(monster, Threshold(monster, 2));
            await ChangeHp(monster, Threshold(monster, 1));
            Equal(3, monster.StartingPower!.TriggeredThresholds, "both recorded");
            Equal(separateHits ? 2 : 1, monster.Creature.StunCalls, "trigger command count");
            Equal(2 * reward, Vigor(monster), "both Vigor rewards");
            Equal("STUNNED", await monster.TakeTurn(), "one shared Stun turn");
            Equal(VanguardBase.ThrustMoveId, await monster.TakeTurn(), "one shared Thrust");
            AssertThrust(monster, basis, basis + 2 * reward);
            Equal(VanguardBase.LongShotMoveId, await monster.TakeTurn(), "resume opening shot");
            Equal(VanguardBase.ResolveMoveId, await monster.TakeTurn(), "resume regular cycle");
            Equal(1, monster.Creature.Attacks.Count(attack => attack.Base == basis), "shared Thrust count");
        });
    }
    await Check($"A{ascension} second threshold after completed Thrust, heal does not reset", async () =>
    {
        ImperialVanguard monster = await Create(ascension);
        await ChangeHp(monster, Threshold(monster, 2));
        await monster.TakeTurn();
        await monster.TakeTurn();
        await ChangeHp(monster, monster.Creature.MaxHp);
        await ChangeHp(monster, Threshold(monster, 2));
        Equal(1, monster.Creature.StunCalls, "healed first threshold stays consumed");
        await monster.TakeTurn(); // 远射，下一招战意。
        await ChangeHp(monster, Threshold(monster, 1));
        Equal(2, monster.Creature.StunCalls, "second threshold separately triggers");
        await monster.TakeTurn();
        Equal(VanguardBase.ThrustMoveId, await monster.TakeTurn(), "second inserted Thrust");
        AssertThrust(monster, basis, basis + reward);
        Equal(VanguardBase.ResolveMoveId, monster.MoveStateMachine.Current.Id, "second resume");
        await ChangeHp(monster, Threshold(monster, 1) - 1);
        Equal(2, monster.Creature.StunCalls, "used thresholds stay consumed");
        Equal(0, Vigor(monster), "no additional reward");
    });
}

await Check("existing Strength contributes to inserted Thrust", async () =>
{
    ImperialVanguard monster = await Create(10);
    await monster.TakeTurn();
    await monster.TakeTurn(); // 战意获得 2 力量。
    await ChangeHp(monster, Threshold(monster, 2));
    await monster.TakeTurn();
    Equal(VanguardBase.ThrustMoveId, await monster.TakeTurn(), "Thrust after Resolve");
    AssertThrust(monster, 20, 37);
    Equal(VanguardBase.LongShotMoveId, monster.MoveStateMachine.Current.Id, "resume shot after Resolve");
});

await Check("threshold floors actual max HP, including scaled HP", async () =>
{
    foreach (int hp in new[] { 133, 137, 140, 277 })
    {
        ImperialVanguard monster = await Create(0, hp);
        int[] markers = monster.StartingPower!.GetHealthBarForecastSegments(new(monster.Creature)).Select(segment => segment.Hp).ToArray();
        Equal($"{hp * 2 / 3},{hp / 3}", string.Join(',', markers), "forecast thresholds");
        await ChangeHp(monster, hp * 2 / 3 + 1);
        Equal(0, monster.Creature.StunCalls, "rounded boundary + 1");
        await ChangeHp(monster, hp * 2 / 3);
        Equal(1, monster.Creature.StunCalls, "rounded boundary");
        Equal(1, monster.StartingPower.GetHealthBarForecastSegments(new(monster.Creature)).Count(), "used marker removed");
    }
});

await Check("guard: other creature, healing, zero delta, dead owner, other monster", async () =>
{
    ImperialVanguard monster = await Create(0);
    WeaknessBreakPower power = monster.StartingPower!;
    monster.Creature.CurrentHp = Threshold(monster, 1);
    await power.AfterCurrentHpChanged(new Creature(), -100);
    await power.AfterCurrentHpChanged(monster.Creature, 1);
    await power.AfterCurrentHpChanged(monster.Creature, 0);
    monster.Creature.CurrentHp = 0;
    await power.AfterCurrentHpChanged(monster.Creature, -100);
    monster.Creature.CurrentHp = 1;
    monster.Creature.Monster = new VanguardVeteran();
    await power.AfterCurrentHpChanged(monster.Creature, -1);
    Equal(0, power.TriggeredThresholds, "guards keep threshold mask");
    Equal(0, monster.Creature.StunCalls, "guards do not insert Thrust");
    Equal(0, Vigor(monster), "guards do not reward Vigor");
});

await Check("thresholds recorded before command reentry", async () =>
{
    ImperialVanguard monster = await Create(0);
    monster.Creature.OnStun = () => monster.StartingPower!.AfterCurrentHpChanged(monster.Creature, -1).GetAwaiter().GetResult();
    await ChangeHp(monster, Threshold(monster, 1));
    Equal(3, monster.StartingPower!.TriggeredThresholds, "atomic mask");
    Equal(1, monster.Creature.StunCalls, "reentry does not insert another Stun");
    Equal(20, Vigor(monster), "reentry does not duplicate rewards");
});

Console.WriteLine($"Weakness Break regression: {passed} passed, {failed} failed (external game API doubles).");
return failed == 0 ? 0 : 1;
