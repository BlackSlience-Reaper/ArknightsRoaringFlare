你是游戏美术出图助手。只做一件事：用你的图像生成工具生成 1 张图片，并把结果文件复制到当前工作目录下的指定路径。不要修改任何其他文件，不要写代码。如果生成结果明显违反下面的约束（不是透明背景、写实/3D 渲染感、出现文字、主体贴边或被裁切），重新生成一次再保存。

## 要生成的图：《杀戮尖塔2》战斗怪物立绘“能量聚合体”（透明背景 PNG）

- **背景必须是真正的透明（alpha = 0）**，PNG 带 alpha 通道。不要白底、不要棋盘格、不要地面、不要投影、不要场景。
- 正方形 1:1（1024×1024 即可）。主体居中，四周留约 12% 空白；**主体下沿离画布底边留约 18%**（游戏会在下面垫悬浮阴影）。任何部分都不能碰到画布边缘。

### 内容
一块**悬浮在空中的源石能量结晶**（明日方舟的“源石”）：
- 本体是一簇**黑色、多面体的尖锐晶体**：中间一根粗大的主晶柱，周围斜插三四根较小的晶刺，整体像一颗竖立的、上下两端收尖的晶簇，约占画布高度的 55–60%。晶面是棱角分明的平面切面，颜色是近黑的深灰/墨黑，亮面带一点冷灰紫的反光。
- 晶体表面有几道**裂缝**，裂缝里透出**炽热的橙红色火光**（亮橙到红，裂缝中心接近黄白），像熔岩从内部透出来。
- 整颗晶体被一层**橙红色的火焰包裹**：火舌贴着晶体轮廓向上舔，顶部汇成两三束上扬的火尖；下半部火焰较薄，露出晶体的黑色切面。火焰是一片片边缘清楚的平涂形状（像卡通火焰），不是烟雾或粒子。
- 晶体周围漂浮两三块小碎晶（同样黑色、带橙色裂光），离主体不远。
- 这是即将自爆的能量装置：整体气质危险、躁动，但仍是一件“物体”，**没有眼睛、嘴巴、脸或肢体**。

### 画风（必须）
Match the attached Slay the Spire 2 reference monsters exactly in rendering style: flat 2D hand-painted game art, simplified graphic shapes, cel-shaded with only 2-3 value steps per material, crisp thick dark outline around the whole silhouette and major shapes, subtle painterly brush texture, limited palette. NOT a photo, NOT a 3D render, NOT realistic concept art, no lens flare, no bloom haze, no particles, no smoke, no glow gradient filling the background. Large clean color areas, no noise, no grain, no ripple/halftone texture.

参考图 `vanilla_monsters_style.png` 是《杀戮尖塔2》原版怪物的游戏内截图（从左到右：感染棱镜、活体盾牌、灵魂枢纽），**只参考它们的画风**（描边粗细、平涂明暗阶数、笔触质感），尤其是左边那只悬浮的红色晶体“感染棱镜”的处理方式；不要复制它们的造型和配色。

保存为：`generated/energy_crystal_source.png`

最后只回复一行：保存的路径、图片像素尺寸、是否带透明通道。
