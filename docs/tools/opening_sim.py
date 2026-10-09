"""怒号光明遭遇战的开局伤害，与原版荣耀（vanilla_glory.py）同一口径对照（引擎与口径见 sim_core.py）。

用法：
  python3 opening_sim.py          逐场列出 A0 / A10 的期望与最大值，并和原版荣耀同类型的均值对照
  python3 opening_sim.py --search 为每场重新挑起手位置（按下面的 TARGET 打分），改阵容或数值后用

这是设计稿阶段的模拟器：招式与数值是 docs/战斗设计.md 的提案，实现时以代码为准、改了要同步这里。
起手写在 OPENINGS 里，实现后必须与遭遇战的 SetOpenings 一致（值 = 招式序号）。
随机出招的怪第一招取 moves[偏移]，之后在其余招里等权随机、不连用（同苦难摇篮的 RandomMachine）。
只算攻击伤害：烈焚灼息（每回合结束扣血）、炮击标记延迟落地、国度这类伤害或削弱在 docs/战斗设计.md 里单独估算。
"""
import sys
from itertools import product

from sim_core import V, Move, cycle, random_kind, simulate, summarize
import vanilla_glory

A = Move  # 简写


def fixed(*moves):
    return ("cycle", moves)


def rand(*moves):
    return ("random", moves)


# 招式：(招式名, Move)。数值 (A0, A10)。
M = {
    # —— 纠察队 ——
    "纠察官": rand(("警棍", A(V(11, 12), 1)), ("射击", A(V(6, 7), 2)), ("搜捕", A())),
    "高级纠察官": fixed(("押送", A()), ("重击", A(V(15, 16), 1)), ("连击", A(V(7, 8), 2))),
    # —— 裂兽 ——
    "裂兽": rand(("撕咬", A(V(9, 10), 1)), ("扑击", A(V(4, 5), 2)), ("撕裂", A(V(6, 7), 1))),
    "裂兽族母": fixed(("嗥叫", A(self_str=V(2, 3))), ("撕咬", A(V(12, 13), 1)), ("扑击", A(V(5, 6), 2))),
    # —— 远程与术师 ——
    "突袭弩手": fixed(("上弦", A(self_str=V(2, 3))), ("弩箭", A(V(14, 15), 1)), ("速射", A(V(5, 6), 3))),
    "着铠术师": fixed(("源石法术", A(V(14, 15), 1)), ("法术连击", A(V(8, 9), 2)), ("铠化", A(self_str=V(2, 3)))),
    "高级着铠术师": fixed(("源石法术", A(V(16, 17), 1)), ("法术连击", A(V(9, 10), 2)), ("铠化", A(self_str=V(2, 3)))),
    # —— 重装与炮兵 ——
    "突击者": fixed(("冲锋", A(V(18, 20), 1)), ("重劈", A(V(9, 10), 2)), ("稳阵", A(self_str=V(2, 3)))),
    "突击者队长": fixed(("冲锋", A(V(22, 24), 1)), ("重劈", A(V(11, 12), 2)), ("压阵", A(V(10, 11), 1, self_str=V(3, 4)))),
    # 标定不出伤，下一招炮击（炮击标记让炮弹在标定者死后也会落地，见设计文档；这里按正常出手计）。
    "炮火先兆者": fixed(("标定", A()), ("炮击", A(V(28, 30), 1)), ("霰弹", A(V(6, 7), 2))),
    "炮火中枢先兆者": fixed(("标定", A()), ("炮击", A(V(30, 33), 1)), ("速射", A(V(6, 7), 3))),
    # 背水（生命低于一半加力量）只在被打后触发，开局模拟里玩家不出手，所以不计。
    "帝国前锋精锐": fixed(("远射", A(V(10, 11), 1)), ("突刺", A(V(18, 20), 1)), ("战意", A(V(8, 9), 1, self_str=V(3, 4)))),
    "帝国前锋百战精锐": fixed(("远射", A(V(10, 11), 1)), ("突刺", A(V(17, 19), 1)), ("战意", A(self_str=V(3, 4)))),
    # —— 宿主（第八章新单位，不与苦难摇篮重复） ——
    "萨卡兹宿主百夫长": fixed(("汲取", A(V(12, 13), 1)), ("群噬", A(V(6, 7), 2)), ("吟唱", A(self_str=V(2, 3)))),
    "宿主重装士兵": fixed(("盾击", A(V(14, 15), 1)), ("坚守", A()), ("重压", A(V(10, 11), 1))),
    # —— 精英：皇帝的利刃，追猎者（一形态；二形态在第一次倒下之后，开局 4 回合内一般打不到） ——
    "追猎者": fixed(("斩击", A(V(20, 22), 1)), ("坍缩恐惧", A(V(12, 13), 1)), ("巡猎", A(V(8, 9), 3))),
    # —— Boss：“不死的黑蛇”第一条命。点燃只在开局放一次（给玩家挂引燃），之后噬咬 → 焦土 → 引爆循环（见下方 KINDS 改写）；
    #      焦土另往手牌塞 2（3）张灼伤（每张留在手里的回合末受 2 点）、引燃让玩家打出的牌受伤，都取决于玩家怎么出牌，这里不计 ——
    #      点燃不攻击（开局意图只有侵蚀卡牌，用户 2026-10-09 要求）。
    "塔露拉": fixed(("点燃", A()), ("噬咬", A(V(11, 12), 2)), ("焦土", A(V(14, 15), 1)), ("引爆", A(V(18, 20), 1))),
}


def build_kinds() -> dict:
    return {name: (random_kind if mode == "random" else cycle)(list(moves)) for name, (mode, moves) in M.items()}


KINDS = build_kinds()
KINDS["塔露拉"].next["引爆"] = "噬咬"  # 点燃只在开局

# 阵容 = 种类列表，或种类列表的列表（随机阵容，等概率）。站位从左到右即出手顺序。
ENCOUNTERS = {
    "弱|纠察队巡逻": ["纠察官", "纠察官"],
    "弱|裂兽": ["裂兽", "裂兽"],
    "弱|着铠术师": ["着铠术师"],
    "普|谷壳村搜捕": ["纠察官", "纠察官", "高级纠察官"],
    "普|裂兽群": ["裂兽", "裂兽", "裂兽族母"],
    "普|突袭弩手": ["突袭弩手", "突袭弩手"],
    "普|术师小队": ["着铠术师", "高级着铠术师"],
    "普|突击者小队": ["突击者", "纠察官"],
    "普|牵引的炮火": ["纠察官", "炮火先兆者"],
    "普|沉默的屠夫": ["帝国前锋精锐"],
    "普|宿主百夫长": ["宿主重装士兵", "萨卡兹宿主百夫长"],
    "普|突击者队长": ["突击者队长"],
    "精|皇帝的利刃": ["追猎者"],
    "精|百战精锐": ["高级纠察官", "帝国前锋百战精锐"],
    "精|中枢先兆者": ["着铠术师", "炮火中枢先兆者"],
    "首|塔露拉": ["塔露拉"],
}
# 每场的起手偏移：固定阵容按站位；随机阵容按种类（{种类: 偏移}，未列出的为 0）。
OPENINGS = {key: tuple(0 for _ in (v if not isinstance(v[0], list) else v[0])) for key, v in ENCOUNTERS.items()}
OPENINGS.update({"普|沉默的屠夫": (2,), "普|突击者队长": (2,), "精|百战精锐": (1, 0), "弱|纠察队巡逻": (1, 2), "弱|裂兽": (0, 2), "弱|着铠术师": (1,), "普|谷壳村搜捕": (0, 2, 0), "普|裂兽群": (1, 2, 0), "普|突袭弩手": (0, 1), "普|术师小队": (0, 2), "普|突击者小队": (2, 0), "普|牵引的炮火": (0, 0), "普|宿主百夫长": (2, 2), "精|皇帝的利刃": (2,), "精|中枢先兆者": (1, 0),
})
# 固定起手（不参与搜索）：先兆者先标定（炮击前一回合预告）；塔露拉开场先点燃（施加烈焚灼息）。
FIXED = {"炮火先兆者": [0], "炮火中枢先兆者": [0], "塔露拉": [0]}


def lineups_of(key):
    v = ENCOUNTERS[key]
    return v if isinstance(v[0], list) else [v]


def start_state(kind, offset):
    moves = M[kind][1]
    return moves[offset % len(moves)][0]


def run(key, openings, asc):
    dists = []
    for lu in lineups_of(key):
        offs = [openings.get(k, 0) for k in lu] if isinstance(openings, dict) else openings
        dists.append(simulate(KINDS, [(k, start_state(k, o)) for k, o in zip(lu, offs)], asc))
    return summarize(dists)


# 按原版荣耀同类型遭遇战 A0 的期望值定目标（vanilla_glory.py）：
#   原版 A0 第 1 回合 / 4 回合总伤：弱 15.0 / 84.3、普 9.9 / 82.2、精 25.0 / 98.4、首 22.7 / 83.0。
#   弱怪刻意压在原版之下（苦难摇篮的教训：弱怪战是一幕里最先遇到的，难度主要来自机制而不是出伤）。
#   first：第 1 回合期望上限；total：前 4 回合总伤期望的目标；peak：任一回合最大值上限。
TARGET = {
    "弱": {"first": 16, "total": 70, "peak": 30},
    "普": {"first": 10, "total": 80, "peak": 38},
    "精": {"first": 26, "total": 95, "peak": 42},
    "首": {"first": 15, "total": 80, "peak": 40},
}


def score(kind, s):
    t = TARGET[kind]
    over_first = max(0.0, s["mean"][0] - t["first"])
    over_peak = sum(max(0, x - t["peak"]) for x in s["max"])
    return over_first * 3 + over_peak * 2 + abs(s["total_mean"] - t["total"])


def search(key):
    kind = key.split("|")[0]
    lus = lineups_of(key)
    randomized = len(lus) > 1
    names = sorted({n for lu in lus for n in lu}) if randomized else lus[0]
    best = None
    for combo in product(*(FIXED.get(n) or range(len(M[n][1])) for n in names)):
        if not randomized and any(names[a] == names[b] and combo[a] == combo[b]
                                  for a in range(len(names)) for b in range(a + 1, len(names))):
            continue  # 同一场里的同种怪起手必须错开
        openings = dict(zip(names, combo)) if randomized else combo
        s = run(key, openings, 0)
        sc = (round(score(kind, s), 1), sum(1 for c in combo if c))
        if best is None or sc < best[0]:
            best = (sc, openings, s)
    return best


def fmt(xs):
    return "/".join(f"{x:.1f}".rstrip("0").rstrip(".") for x in xs)


def describe(key, openings):
    lu = lineups_of(key)[0]
    return " ".join(f"{k}:{start_state(k, o)}" for k, o in zip(lu, openings))


def report():
    vanilla = {0: vanilla_glory.run(0), 1: vanilla_glory.run(1)}
    for asc, label in ((0, "A0"), (1, "A10")):
        print(f"== 怒号光明 {label}（前 4 个敌方回合：期望 | 最大 | 总伤期望/最大）")
        groups: dict = {}
        for key in ENCOUNTERS:
            kind, title = key.split("|")
            s = run(key, OPENINGS[key], asc)
            groups.setdefault(kind, []).append(s)
            print(f"{kind} {title:8s} 期望 {fmt(s['mean']):24s} 最大 {fmt(s['max']):14s} 总 {s['total_mean']:5.1f}/{s['total_max']}")
        print(f"-- 类型均值（怒号光明 vs 原版荣耀 {label}）：第 1 回合期望 / 总伤期望")
        for kind, ss in groups.items():
            vs = [s for (k, _), s in vanilla[asc].items() if k == kind]
            ours = (sum(s["mean"][0] for s in ss) / len(ss), sum(s["total_mean"] for s in ss) / len(ss))
            base = (sum(s["mean"][0] for s in vs) / len(vs), sum(s["total_mean"] for s in vs) / len(vs))
            print(f"{kind}  {ours[0]:5.1f} / {ours[1]:5.1f}    原版 {base[0]:5.1f} / {base[1]:5.1f}")


if __name__ == "__main__":
    if "--search" in sys.argv:
        for key in ENCOUNTERS:
            sc, openings, s = search(key)
            print(f"{key:14s} {openings}  分 {sc[0]}  期望 {fmt(s['mean'])} 总 {s['total_mean']:.1f}  {describe(key, openings)}")
    else:
        report()
