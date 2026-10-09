# WD-8「大雪将至」与“皇帝的利刃”调研

调研日期：2026-10-07。数据来源：PRTS wiki 的 wikitext（api.php）、Kengxxiao/ArknightsGameData 的国服解包数据（`zh_CN/gamedata`，master 分支）、PRTS 的 Spine CDN。只读取公开页面，没有改代码。

## 结论摘要

- **活动名需要更正**：代号 **WD** 对应的是 SideStory「**遗尘漫步**」（英文副标题 **A WALK IN THE DUST**，2021-04-15 开放，活动关卡 id 前缀 `act18d0`），不是「生于黑夜」。「生于黑夜」的英文名是 **DARKNIGHTS MEMOIR**，代号是 DM，是 W 的故事。我检查了 PRTS「生于黑夜」页面，里面没有“皇帝的利刃”。
- **WD-8 = 「大雪将至」**（`act18d0_08`），Boss 是 **“皇帝的利刃”，追猎者**（`enemy_1520_empgrd`，PRTS 敌人编号 1149，图鉴序号 EMG，地位：领袖）。
- **核心机制**有以下几点：
  1. **【国度】**：已损失生命每达到最大生命的 50% 的整数倍（二阶段为 25%），就对一名干员脚下投放一块永久黑色地面。站在【国度】里的干员攻击速度 -50；Boss 自己站在【国度】里时，受到的物理和法术伤害 -50%。
  2. **【坍缩恐惧】**：每 20 秒对周围造成法术范围伤害，并**强制击倒**站在【国度】里的干员。
  3. **击倒后重生转阶段**：第一次被击倒时，用 7.2 秒回满生命，同时对全场释放一次【坍缩恐惧】，之后进入第二形态，获得 10 秒无敌，攻击力 +50%，并开始近战。
- **这一关没有限时撤退，也没有剧情无敌**。Boss 从地图中央出生，按脚本缓慢巡逻一圈后走向出口；漏过去扣 **2 点**目标耐久（本关共 3 点）。只有第八章 R8-11 里那两个“皇帝的利刃”是无敌的演出单位。
- **Spine 可以拿到，资源完整**：`https://torappu.prts.wiki/assets/enemy_spine/enemy_1520_empgrd/`，文件是 `enemy_1520_empgrd.{skel,atlas,png}`，版本 **Spine 3.8.99**。用 STS2 需要转换到 4.2.43（`sts2-spine-enemy-import` 技能的脚本会自动转）。共 10 个动画：`Attack01, Attack02, Default, Die, Idle, Move, Skill_01, Skill_02_Begin, Skill_02_Loop, Skill_02_End`。
- **BGM**：「**乌萨斯的影月**」，出自塞壬唱片专辑《遗尘漫步OST》第 3 轨，作曲 Robert Wolf。游戏内事件名是 `m_bat_empgrd`，用于 WD-8 和 WD-EX-8。

---

## 1. WD-8 关卡

| 项目 | 值 |
|---|---|
| 代号 / 名称 | WD-8 大雪将至（关卡 id `act18d0_08`，levelId `Activities/act18d0/level_act18d0_08`） |
| 所属活动 / 区域 | SideStory「遗尘漫步」/ 漫漫独行（主线活动关的最后一关） |
| 关卡描述 | 而冰凌漆黑如影。 |
| 推荐等级 | 精英2 LV.10 |
| 部署上限 / 初始费用 / 费用上限 | 9 / 10 / 99 |
| 目标点耐久 | 3 |
| 敌人数量 | 31 |
| 地图 | 13×9，左侧 5 个出怪点，右侧 3 个保护目标（同一入口区） |
| 突袭模式 | **无**（stage_table 中只有 `act18d0_08` 普通难度） |
| BGM 事件 | `m_bat_empgrd` |
| 场景（剧情） | 维多利亚，文森特伯爵的庄园（背景 `bg_manorindoor`、`bg_manorgarden`、`bg_manorgate`），时间在过去 |

全局规则方面，`runes`、`globalBuffs`、`predefines` 都是空的：**没有特殊地块、没有全局 buff、也没有预置单位**。这一关唯一的特殊机制就是 Boss 本身，以及它按脚本走的巡逻路线。活动层面有“可移动战术机库 / 空援无人机”机制，但只出现在 WD-7 等雇佣兵关卡，WD-8 的敌人列表里没有无人机。

### 地图（解包 `mapData`，上北下南；`row` 0 在最下方）

```
row8  forbi forbi forbi forbi forbi forbi forbi forbi forbi forbi forbi forbi forbi
row7  forbi start floor road  road  road  road  road  road  wall  forbi forbi forbi
row6  start floor floor road  wall  wall  forbi forbi road  wall  wall  wall  forbi
row5  forbi forbi wall  road  forbi forbi wall  wall  road  road  road  floor end
row4  start floor floor road  road  road  road  road  road  road  road  floor end
row3  forbi forbi wall  road  wall  forbi forbi wall  road  road  road  floor end
row2  start floor floor road  wall  forbi forbi wall  road  wall  wall  wall  forbi
row1  forbi start floor road  road  road  road  road  road  wall  forbi forbi forbi
row0  forbi ...
       c0    c1    c2    c3    c4    c5    c6    c7    c8    c9    c10   c11   c12
```

### 出兵顺序（解包 `waves`，1 个波次，6 个片段）

| 片段 | 片段前延迟 | 出兵 |
|---|---|---|
| 0 | 0s | **“皇帝的利刃”，追猎者 ×1**，开场即出，**出生点在地图中央 (4,4)**，不在出怪点（同时显示敌人情报与路线预览） |
| 1 | +4s | 帝国前锋精锐 ×4，各片段内 3 秒后从 4 个出怪点各出 1 个 |
| 2 | +4s | 感染者纠察官 ×8，4 条路线各 2 个，间隔 18 秒 |
| 3 | +22s | 感染者纠察官 ×8（4 条路线各 2 个，间隔 20 秒）；乌萨斯着铠术师 ×2（中路，片段内第 3 秒和第 23 秒） |
| 4 | +15s | 乌萨斯着铠术师 ×2（第 6 秒和第 26 秒）；感染者纠察官 ×2（中路，第 20 秒起，间隔 10 秒） |
| 5 | +20s | 乌萨斯着铠术师 ×4（4 条路线同时） |

合计 31 只：Boss 1、帝国前锋精锐 4、感染者纠察官 18、乌萨斯着铠术师 8，与 PRTS「敌方情报」一致。

### Boss 巡逻路线（route 1；路线检查点类型是 `WAIT_CURRENT_FRAGMENT_TIME`，下面按“等到片段计时到达 X 秒再走”理解，这个解读是推断）

出生于 (4,4) → 10s 走到 (4,5) → 17s 到 (4,6) → 25s 到 (4,7) → 60s 到 (4,8) → 113s 上行到 (7,8)，再沿上路向左到 (7,3) → 160s 沿 c3 纵向走到下路 (1,3) → 200s 沿下路向右到 (1,8)，再回到中路 (4,8) → (4,10) → 出口 (4,12)。

也就是说，它**先在中路一格一格地逼近，再沿上路、左侧、下路绕全图一圈，大约 200 秒后才冲向出口**。它被阻挡时会停下战斗（重量等级 6）。PRTS 显示的预览路线（route 2）用的是固定秒数等待，走法与此相同。

### 小怪数值（解包 `enemy_database`，级别 0）

| 敌人 | id | 地位 | 生命 | 攻击 | 防御 | 法抗 | 攻击方式 | 能力（PRTS） |
|---|---|---|---|---|---|---|---|---|
| 帝国前锋精锐 | enemy_1113_empace | 精英 | 12000 | 900 | 800 | 50 | 物理 近战/远程（半径 1.5），攻击间隔 4.5 秒 | 生命值降至一半以下时攻击力大幅提升 |
| 感染者纠察官 | enemy_1107_uoffcr | 普通 | 3300 | 330 | 150 | 20 | 物理 近战，攻击间隔 2.0 秒，移速 1.1 | 可用远程武器攻击<乌萨斯平民>与<斗士塔露拉>（这一关里无效） |
| 乌萨斯着铠术师 | enemy_1110_uamord | 普通 | 5000 | 340 | 500 | 50 | 法术 远程（半径 2.2），攻击间隔 2.8 秒 | PRTS 未列 |

---

## 2. WD-8 Boss：“皇帝的利刃”，追猎者

### 基本信息

| 项目 | 值 |
|---|---|
| 敌人 key | `enemy_1520_empgrd` |
| PRTS 编号 / 图鉴序号 | 1149 / EMG |
| 地位 | 领袖（解包 `levelType = BOSS`） |
| 图鉴描述 | 他曾盘剥北原邪魔，将异种隔绝于文明之外；亦手刃王公贵族，令荣光不蒙上叛乱尘埃。每一位内卫都是一个国度，他们脚下的大地皆是乌萨斯的国土。 |
| 关卡内简介 | 每损失一定生命值释放永久地面效果“国度”；释放法术“坍缩恐惧”，击中“国度”中的目标会将其强制击倒。 |
| 伤害类型 / 攻击方式 / 行动方式 | 物理（普攻）+ 法术（技能），近战与远程，地面 |
| 登场 | WD-8（级别 0）、WD-EX-8 如帝国之影（级别 1） |

### 数值

| 属性 | 级别 0（WD-8） | 级别 1（WD-EX-8） | WD-EX-8 突袭 |
|---|---|---|---|
| 最大生命 | 28000 | 40000 | 44000（突袭全体敌人生命 ×1.1） |
| 攻击力 | 600 | 700 | 700 |
| 防御力 | 400 | 400 | 400 |
| 法术抗性 | 40 | 40 | 40 |
| 攻击范围半径 | 2.0 | 2.0 | 2.0 |
| 攻击间隔 | 4.0 秒（攻速 100） | 同左 | 同左 |
| 移动速度 | 0.6 | 同左 | 同左 |
| 重量等级 | 6 | 同左 | 同左 |
| 漏怪扣耐久 | 2 | 同左 | 同左（突袭目标耐久只有 1） |
| 异常抗性 | 免疫晕眩、沉默、沉睡、冻结、浮空；不免疫战栗、恐惧、麻痹、诱导、传送、缚地 | 同左 | 同左 |

级别 1 只覆盖了生命和攻击，其余属性沿用级别 0。

### PRTS 能力概述（原文）

> **第一形态**
> ·每损失一定比例生命值释放永久地面效果【国度】，大幅度降低目标攻击速度并减少自身受到的物理或法术伤害（优先选择不在【国度】中且期间累计造成伤害最高的我方单位）
> ·【坍缩恐惧】对周围造成法术伤害，若目标在【国度】中会被强制击倒
> ·生命值首次降为0时对整个战场释放【坍缩恐惧】并进入第二形态
> **第二形态**
> ·攻击力提升，可进行近战攻击造成更高伤害
> ·每损失更少的生命比例就释放【国度】

### 技能（PRTS 原文 + 解包数值）

| 技能 | 初始冷却 | 冷却 | 效果（原文） | 解包 |
|---|---|---|---|---|
| 坍缩恐惧 [第一形态] | 20s | 20s | 对半径 2 范围内所有我方单位造成攻击力 100% 的法术溅射伤害<br>※强制击倒位于【国度】中可被选中的我方单位 | `BlackRingP1`，atk_scale 1.0 |
| 坍缩恐惧 [第二形态] | 20s | 20s | 对半径 2.5 范围内所有我方单位造成攻击力 100% 的法术溅射伤害<br>※强制击倒位于【国度】中可被选中的我方单位 | `BlackRingP2`，atk_scale 1.0 |

两个技能的半径（2 / 2.5）写在技能预制体里，解包 blackboard 中没有，取值以 PRTS 为准。

### 天赋（PRTS 原文）

> 【国度】（不同单位释放的【国度】实际上并非同名地面效果，该【国度】仅可通过“皇帝的利刃”，追猎者而出现）：位于其中的我方单位攻击速度-50
> 每次释放【国度】时，会在延迟1.45s后于目标周围一定范围内永久生成【国度】，并对其中心半径1.8范围内我方单位造成攻击力100%法术溅射伤害
> 自身位于【国度】中时，受到的物理/法术伤害-50%
> ※自身退场后，位于【国度】中的敌方单位也会受到相同的攻击速度降低效果
>
> **第一形态<初始形态>**
> 仅进行远程攻击
> 已损失生命值达到最大生命值50%的整数倍时，以特殊索敌逻辑释放【国度】，并清空伤害记录
> 被击倒时，进行持续7.2s的重生(回复100%生命值)；重生开始的短暂延迟后，对场上所有我方单位造成攻击力100%法术溅射伤害，并强制击倒位于【国度】中可被选中的我方单位
> 重生结束后切换至第二形态，清空伤害记录，并获得10s无敌
>
> **第二形态**
> 攻击力+50%
> 被阻挡时进行近战攻击，造成攻击力250%的物理伤害
> 已损失生命值达到最大生命值25%的整数倍时，以特殊索敌逻辑释放【国度】，并清空伤害记录

特殊索敌逻辑（PRTS 弹窗原文）：

> 优先选择不在国度中的我方单位。以上条件相同时：优先选择累计对自身造成伤害最高的我方单位。以上条件相同时：优先选择仇恨值最高的我方单位。不存在可选目标时：选择自身所在地块

【国度】的投放范围在 PRTS 页面上是一个范围图（`x-1`），具体格子形状**未核实**。

### 解包 talentBlackboard（原值）

| key | 值 | 含义 |
|---|---|---|
| BlackFogTriggerP1.hp_ratio | 0.5 | 一阶段每损失 50% 最大生命放一次【国度】 |
| BlackFogTriggerP2.hp_ratio | 0.25 | 二阶段每损失 25% 最大生命放一次【国度】 |
| Reborn.duration | 7.2 | 重生持续 7.2 秒 |
| Reborn.rest_duration | 3.0 | 用途未明（推测与“重生开始的短暂延迟”有关，**未核实**） |
| Reborn.atk | 0.5 | 二阶段攻击力 +50% |
| Reborn.reborn_invincible.duration | 10.0 | 重生后无敌 10 秒 |
| Reborn.atk_scale | 1.0 | 重生时全场【坍缩恐惧】的倍率为 100% |
| Combat.attack@combat_atkscale.atk_scale | 2.5 | 二阶段近战倍率 250% |
| BlackFog.attack_speed | -50 | 【国度】内干员攻速 -50 |
| BlackFog.damage_resistance | 0.5 | Boss 在【国度】内受到的伤害 -50% |
| BlackFog.range_radius | 1.8 | 【国度】生成时的伤害半径 |

### 关卡特殊规则核对

- **不是无敌 Boss，也不会限时撤退**：没有剧情无敌，也没有撤退计时。唯一的无敌是转阶段后的 10 秒。它是实际要击杀的 Boss，漏过去扣 2 点耐久。
- **有两个阶段**，第一形态被打到 0 时触发重生，所以实际有效生命是 2 × 最大生命（WD-8 为 56000）。
- **有“对我方的特殊效果”**：【国度】让干员攻速 -50 并成为强制击倒的目标；在国度里被【坍缩恐惧】命中就会被强制击倒，等于直接撤离。
- 其他关卡和难度：WD-8 没有突袭。WD-EX-8「如帝国之影」中，Boss 在第 0 波第 3 片段出现，同场有帝国炮火中枢先兆者、帝国前锋百战精锐、乌萨斯裂兽族母、感染者高级纠察官等，共 45 个敌人。它的突袭条件是：目标耐久改为 1、敌人生命 ×1.1、7 格不可部署（(2,3) 到 (2,9)）。

---

## 3. 各个“皇帝的利刃”单位（需要区分）

| 单位 | key | PRTS 编号 | 登场 | 数值 | 机制 |
|---|---|---|---|---|---|
| “皇帝的利刃”，追猎者 | `enemy_1520_empgrd` | 1149 / EMG | SideStory 遗尘漫步：WD-8、WD-EX-8 | 见上文 | 见上文，是可击杀的 Boss |
| “皇帝的利刃” | `enemy_1115_embald` | 226 / U15 | 主线第八章 **R8-11「落雪，浸黑国土」**（`main_08-13`，同时有突袭版），场上 **2 只** | 生命 50000 / 攻击 0 / 防御 3000 / 法抗 90 / 移速 0.4 / 重量 5；免疫麻痹 | **演出用敌人**：无敌、免疫失衡、免疫沉睡，不攻击，无法被阻挡；“无法被攻击、消失时不会扣除目标生命”。技能【削弱】（初始 7 秒，冷却 10000 秒，相当于只放一次）：使【斗士塔露拉】的防御力和法术抗性最终降至 50%，不可叠加。图鉴描述：“乌萨斯某种意志的体现……只需一个个体就足以屠剿数支小队。没有被战胜的可能。” |
| “皇帝的利刃”，折戟者 | `enemy_3011_dyegrd` | 1076 / BBM | 主线第十六章 **16-9「疯人的辩护」**（`main_16-08`），1 只 | 生命 40000 / 攻击 1200 / 防御 500 / 法抗 40 / 范围 2.0 / 攻击间隔 4.0 / 移速 0.5 / 重量 6；不免疫任何异常 | 演出用敌人，无法被阻挡，只攻击范围内的[矿工游击队]。技能【坍缩恐惧】（初始和冷却都是 10 秒）：对 1 名矿工游击队及其半径 2.0 内所有我方单位造成攻击力 200% 的物理伤害。出场 75 秒后获得无限时长的【自缚】，期间每秒受到 5% 最大生命的真实伤害（`DelayBloodDot.delay_duration=75`，`hp_ratio=0.05`）。图鉴描述：“他的行动本代表乌萨斯的意志。而利刃崩断，也许喻示着意志已摇摇欲坠。” |

PRTS 的「皇帝的利刃」页面是「“皇帝的利刃”」的重定向，内容相同。第八章各关和第十六章各关的解包数据中，只有上面这两关含有“皇帝的利刃”。其他活动、危机合约、集成战略、生息演算是否有同类单位，**未逐一核实**；PRTS 搜索只返回这三个敌人页面。

### 剧情定位（简述）

“皇帝的利刃”就是乌萨斯的**内卫**：直属皇帝的秘密处刑者，佩戴面具，与“邪魔”相关的黑色力量相伴。在遗尘漫步中，一名内卫追杀被控弑杀大公的凯尔希，在维多利亚的庄园与她和 Mon3tr 交手后暂时退去。在第八章，两名利刃旁观并削弱塔露拉。第十六章的“折戟者”象征乌萨斯意志的动摇。

---

## 4. Spine 资源

PRTS 的 `/spine` 子页面就是 SPINEDATA：

| 单位 | prefix | file |
|---|---|---|
| “皇帝的利刃”，追猎者 | `https://torappu.prts.wiki/assets/enemy_spine/enemy_1520_empgrd/` | `enemy_1520_empgrd` |
| “皇帝的利刃”（第八章） | `https://torappu.prts.wiki/assets/enemy_spine/enemy_1115_embald/` | `enemy_1115_embald` |
| “皇帝的利刃”，折戟者 | `https://torappu.prts.wiki/assets/enemy_spine/enemy_3011_dyegrd/` | `enemy_3011_dyegrd` |

每个单位只有一套皮肤：“默认 / 战斗”。

实际下载的文件放在 scratchpad 的 `wd8_spine/` 目录。这是临时目录，会被清理，接入时请用技能脚本重新下载。

| 文件 | 大小 | `strings | head -3` 中的版本 |
|---|---|---|
| enemy_1520_empgrd.skel | 242088 B | **3.8.99** |
| enemy_1520_empgrd.atlas | 5027 B | 单页 `enemy_1520_empgrd.png`，512×512 RGBA8888 |
| enemy_1520_empgrd.png | 85677 B | — |
| enemy_1115_embald.skel | 61302 B | 3.8.99 |
| enemy_3011_dyegrd.skel | 176856 B | 3.8.99（Spine 读取时有 “Nonessential unchecked” 警告，不影响读取） |

### 动画清单（用 Spine 3.8.99 CLI `-i` 读取；时长与事件来自导出的 JSON）

**enemy_1520_empgrd（追猎者，80 根骨骼，58 个槽位，1 个事件 `OnAttack`）**

| 动画 | 时长 | OnAttack 时刻 | 用途推测（**未核实**） |
|---|---|---|---|
| Attack01 | 1.067s | 0.600s | 一阶段远程攻击 |
| Attack02 | 1.867s | 0.767s | 二阶段近战（250%） |
| Default | 0 | — | 静态姿势 |
| Die | 2.500s | — | 死亡 |
| Idle | 1.667s | — | 待机 |
| Move | 1.400s | — | 移动 |
| Skill_01 | 1.767s | 1.067s | 坍缩恐惧或【国度】 |
| Skill_02_Begin | 1.800s | 1.167s | 重生开始，此时对全场释放坍缩恐惧 |
| Skill_02_Loop | 1.533s | — | 重生循环（7.2 秒） |
| Skill_02_End | 2.400s | — | 重生结束、转为第二形态 |

**enemy_1115_embald（第八章演出版，51 根骨骼）**：Default, Idle, Move, Skill。共 4 个动画，没有攻击和死亡动画，不适合做精英怪。

**enemy_3011_dyegrd（折戟者）**：骨骼和槽位与追猎者结构相同，动画名也相同，共 10 个（Attack01/02, Default, Die, Idle, Move, Skill_01, Skill_02_Begin/Loop/End）。

做精英怪推荐用 **enemy_1520_empgrd**：动作齐全，而且有 Begin/Loop/End 三段式的“重生”动画，可以直接对应转阶段演出。接入时注意两点：

- STS2 用的是 Spine 4.2.43 运行时，需要用 `sts2-spine-enemy-import` 技能的 `prepare_prts_spine_asset.py` 转换（流程是先用 3.8.99 导入，再用 4.2.43 导出）。
- 转换后要检查时间轴拼接问题，参考记忆 `sts2-spine-38to42-duplicated-timelines`。

---

## 5. BGM

| 项目 | 值 |
|---|---|
| 曲名 | **乌萨斯的影月** |
| 专辑 | 《遗尘漫步OST》（塞壬唱片-MSR，2023-04-30），第 3 轨，作曲 Robert Wolf，时长 03:20 |
| 游戏内事件 | `m_bat_empgrd`（`audio_data`：`Audio/Sound_Beta_2/Music/act18d0d0/m_bat_empgrd_intro` 和 `_loop`） |
| 使用关卡 | WD-8 大雪将至、WD-EX-8 如帝国之影；后来也用于生息演算部分 RA-NO 关卡 |
| 塞壬唱片 | https://monster-siren.hypergryph.com/music/048711 |
| 网易云 | https://music.163.com/song?id=2041818713 |
| PRTS 试听 | https://static.prts.wiki/music/music/act18d0d0/m_bat_empgrd_combine.mp3 |

同一张专辑里的其余曲目：01「走过漫漫时空」（活动主界面 `m_sys_act18d0d0`），02「兴衰旧篇」（活动普通战斗 `m_bat_walkinthedust`，WD-7 和 EX-6、EX-7 使用）。

---

## 未核实或有推断的部分

- 【国度】投放范围的具体格子形状（PRTS 只给了范围图编号 `x-1`）。
- `Reborn.rest_duration = 3.0` 的确切含义。
- 动画名与具体行为的对应关系（表中的“用途推测”）。
- Boss 路线检查点 `WAIT_CURRENT_FRAGMENT_TIME` 按“片段计时绝对时刻”理解，是根据数据推断的。
- 萌娘百科移动版这次返回了 Cloudflare 403（“Just a moment…”），没能用来交叉核对。
- 第八章、第十六章以外的关卡是否还有“皇帝的利刃”单位，没有全量扫描。

## 资料来源

- PRTS 关卡：https://prts.wiki/w/WD-8 （wikitext：`api.php?action=parse&page=WD-8&prop=wikitext`）、https://prts.wiki/w/WD-EX-8
- PRTS 活动：https://prts.wiki/w/遗尘漫步 、https://prts.wiki/w/生于黑夜
- PRTS 敌人：https://prts.wiki/w/“皇帝的利刃”，追猎者 、https://prts.wiki/w/“皇帝的利刃” 、https://prts.wiki/w/“皇帝的利刃”，折戟者，以及三者的 `/spine` 子页
- PRTS 音乐：https://prts.wiki/w/遗尘漫步OST 、https://prts.wiki/w/音乐鉴赏/游戏内音乐一览
- PRTS 剧情：https://prts.wiki/w/WD-8_大雪将至/BEG 、https://prts.wiki/w/WD-8_大雪将至/END
- 解包数据（Kengxxiao/ArknightsGameData，`zh_CN/gamedata`）：
  - `levels/activities/act18d0/level_act18d0_08.json`、`level_act18d0_ex08.json`
  - `levels/obt/main/level_main_08-13.json`、`level_main_16-08.json`
  - `levels/enemydata/enemy_database.json`、`excel/enemy_handbook_table.json`、`excel/stage_table.json`、`excel/audio_data.json`
- Spine CDN：https://torappu.prts.wiki/assets/enemy_spine/enemy_1520_empgrd/ （以及 `enemy_1115_embald/`、`enemy_3011_dyegrd/`）
