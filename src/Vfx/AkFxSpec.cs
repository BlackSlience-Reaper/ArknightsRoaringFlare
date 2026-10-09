using System.Text.Json;
using Godot;

namespace ArknightsRoaringFlare.Vfx;

// 明日方舟原作特效的描述数据，由 tools/ak_vfx/convert_ak_vfx.py 从原作 Unity 预制体转出（assets/vfx/ak/*.json）。
// 字段名与 JSON 键一一对应（驼峰），含义照 Unity 粒子系统；曲线与渐变已按归一化时间采样成定长数组。
// 坐标保持 Unity 的：x 右、y 上、z 纵深，长度单位是原作的世界单位。

/// <summary>Unity 的 MinMaxCurve：0 常量 a，1 曲线 ca，2 在 a、b 间随机，3 在曲线 ca、cb 间随机。</summary>
internal sealed class FxCurve
{
	public int M { get; set; }
	/// <summary>原始 scalar（曲线模式下 ca/cb 已乘过它）。</summary>
	public float S { get; set; }
	public float A { get; set; }
	public float B { get; set; }
	public float[]? Ca { get; set; }
	public float[]? Cb { get; set; }

	public static readonly FxCurve Zero = new();
	public static readonly FxCurve One = new() { A = 1f, S = 1f };

	/// <summary>原作动画改写了 scalar 时的取值：常量模式直接用新值，随机常量替换上限，曲线模式按新旧 scalar 的比例缩放。</summary>
	public float Eval(float t, float rand, float? scalar)
	{
		if (scalar is not { } s)
		{
			return Eval(t, rand);
		}

		return M switch
		{
			0 => s,
			2 => A + (s - A) * rand,
			_ => S == 0f ? 0f : Eval(t, rand) * (s / S),
		};
	}

	/// <param name="t">归一化时间 0..1。</param>
	/// <param name="rand">本粒子固定的随机数 0..1（随机模式用）。</param>
	public float Eval(float t, float rand) => M switch
	{
		0 => A,
		1 => Sample(Ca, t),
		2 => A + (B - A) * rand,
		3 => Mathf.Lerp(Sample(Ca, t), Sample(Cb, t), rand),
		_ => A,
	};

	internal static float Sample(float[]? samples, float t)
	{
		if (samples is not { Length: > 0 })
		{
			return 0f;
		}

		float f = Mathf.Clamp(t, 0f, 1f) * (samples.Length - 1);
		int i = Math.Min((int)f, samples.Length - 2);
		if (i < 0)
		{
			return samples[0];
		}

		return Mathf.Lerp(samples[i], samples[i + 1], f - i);
	}
}

/// <summary>Unity 的 MinMaxGradient：0 颜色 a，1 渐变 ga，2 在 a、b 间随机，3 在渐变 ga、gb 间随机，4 渐变上随机取一点。</summary>
internal sealed class FxGradient
{
	public int M { get; set; }
	public float[]? A { get; set; }
	public float[]? B { get; set; }
	public float[][]? Ga { get; set; }
	public float[][]? Gb { get; set; }

	public Color Eval(float t, float rand) => M switch
	{
		0 => ToColor(A),
		1 => Sample(Ga, t),
		2 => ToColor(A).Lerp(ToColor(B), rand),
		3 => Sample(Ga, t).Lerp(Sample(Gb, t), rand),
		4 => Sample(Ga, rand),
		_ => Colors.White,
	};

	internal static Color ToColor(float[]? c) => c is { Length: >= 4 } ? new Color(c[0], c[1], c[2], c[3]) : Colors.White;

	internal static Color Sample(float[][]? samples, float t)
	{
		if (samples is not { Length: > 0 })
		{
			return Colors.White;
		}

		float f = Mathf.Clamp(t, 0f, 1f) * (samples.Length - 1);
		int i = Math.Min((int)f, samples.Length - 2);
		if (i < 0)
		{
			return ToColor(samples[0]);
		}

		return ToColor(samples[i]).Lerp(ToColor(samples[i + 1]), f - i);
	}
}

internal sealed class FxBurst
{
	public float T { get; set; }
	public FxCurve N { get; set; } = FxCurve.Zero;
	public int Cycles { get; set; } = 1;
	public float Interval { get; set; }
	public float P { get; set; } = 1f;
}

internal sealed class FxShape
{
	public int Type { get; set; }
	public float Angle { get; set; }
	public float Length { get; set; }
	public float Radius { get; set; }
	public float Thick { get; set; } = 1f;
	public float Arc { get; set; } = 360f;
	public int ArcMode { get; set; }
	public float[] Box { get; set; } = [0, 0, 0];
	public float[] Pos { get; set; } = [0, 0, 0];
	public float[] Rot { get; set; } = [0, 0, 0];
	public float[] Scale { get; set; } = [1, 1, 1];
	public bool Align { get; set; }
	public float RandDir { get; set; }
	public float SphDir { get; set; }
	public float RandPos { get; set; }
}

internal sealed class FxXyz
{
	public FxCurve X { get; set; } = FxCurve.Zero;
	public FxCurve Y { get; set; } = FxCurve.Zero;
	public FxCurve Z { get; set; } = FxCurve.Zero;
	public bool World { get; set; }
}

internal sealed class FxVelocity
{
	public FxCurve X { get; set; } = FxCurve.Zero;
	public FxCurve Y { get; set; } = FxCurve.Zero;
	public FxCurve Z { get; set; } = FxCurve.Zero;
	public bool World { get; set; }
	public FxCurve OrbX { get; set; } = FxCurve.Zero;
	public FxCurve OrbY { get; set; } = FxCurve.Zero;
	public FxCurve OrbZ { get; set; } = FxCurve.Zero;
	public FxCurve Radial { get; set; } = FxCurve.Zero;
	public FxCurve SpeedMod { get; set; } = FxCurve.One;
}

internal sealed class FxLimit
{
	public bool Sep { get; set; }
	public FxCurve Mag { get; set; } = FxCurve.One;
	public FxCurve X { get; set; } = FxCurve.One;
	public FxCurve Y { get; set; } = FxCurve.One;
	public FxCurve Z { get; set; } = FxCurve.One;
	public float Dampen { get; set; }
	public FxCurve Drag { get; set; } = FxCurve.Zero;
	public bool DragBySize { get; set; }
	public bool DragByVel { get; set; }
}

internal sealed class FxSizeMod
{
	public bool Sep { get; set; }
	public FxCurve X { get; set; } = FxCurve.One;
	public FxCurve Y { get; set; } = FxCurve.One;
	public FxCurve Z { get; set; } = FxCurve.One;
}

/// <summary>按速度缩放大小：曲线横轴是速度落在 Range 区间里的位置。</summary>
internal sealed class FxSizeSpeedMod
{
	public bool Sep { get; set; }
	public FxCurve X { get; set; } = FxCurve.One;
	public FxCurve Y { get; set; } = FxCurve.One;
	public FxCurve Z { get; set; } = FxCurve.One;
	public float[] Range { get; set; } = [0, 1];
}

internal sealed class FxRotMod
{
	public bool Sep { get; set; }
	public FxCurve X { get; set; } = FxCurve.Zero;
	public FxCurve Y { get; set; } = FxCurve.Zero;
	public FxCurve Z { get; set; } = FxCurve.Zero;
}

internal sealed class FxNoise
{
	public bool Sep { get; set; }
	public FxCurve X { get; set; } = FxCurve.Zero;
	public FxCurve Y { get; set; } = FxCurve.Zero;
	public FxCurve Z { get; set; } = FxCurve.Zero;
	public float Freq { get; set; } = 0.5f;
	public FxCurve Scroll { get; set; } = FxCurve.Zero;
	public bool Damping { get; set; }
	public int Octaves { get; set; } = 1;
	public FxCurve Pos { get; set; } = FxCurve.One;
	public FxCurve RotAmt { get; set; } = FxCurve.Zero;
	public FxCurve SizeAmt { get; set; } = FxCurve.Zero;
}

internal sealed class FxSheet
{
	public int Tx { get; set; } = 1;
	public int Ty { get; set; } = 1;
	public int Type { get; set; }
	public int Row { get; set; }
	public int RowMode { get; set; } = 1;
	public FxCurve Frame { get; set; } = FxCurve.Zero;
	public FxCurve Start { get; set; } = FxCurve.Zero;
	public float Cycles { get; set; } = 1f;
	public int TimeMode { get; set; }
	public float Fps { get; set; } = 30f;
	public float FlipU { get; set; }
	public float FlipV { get; set; }
}

internal sealed class FxTrailMod
{
	public int Mode { get; set; }
	public float Ratio { get; set; } = 1f;
	public FxCurve Life { get; set; } = FxCurve.One;
	public float MinDist { get; set; }
	public int TexMode { get; set; }
	public bool World { get; set; }
	public bool DieWith { get; set; } = true;
	public bool SizeWidth { get; set; } = true;
	public bool SizeLife { get; set; }
	public bool Inherit { get; set; } = true;
	public FxGradient ColLife { get; set; } = new();
	public FxCurve Width { get; set; } = FxCurve.One;
	public FxGradient ColTrail { get; set; } = new();
}

internal sealed class FxSub
{
	public int Emitter { get; set; } = -1;
	public int Type { get; set; }
	public int Props { get; set; }
	public float P { get; set; } = 1f;
}

internal sealed class FxInherit
{
	public int Mode { get; set; }
	public FxCurve C { get; set; } = FxCurve.Zero;
}

internal sealed class FxMods
{
	public FxVelocity? Vel { get; set; }
	public FxXyz? Force { get; set; }
	public FxLimit? Limit { get; set; }
	public FxGradient? Col { get; set; }
	public FxSizeMod? Size { get; set; }
	public FxSizeSpeedMod? SizeSpeed { get; set; }
	public FxRotMod? Rot { get; set; }
	public FxNoise? Noise { get; set; }
	public FxSheet? Sheet { get; set; }
	public FxTrailMod? Trail { get; set; }
	public FxSub[]? Sub { get; set; }
	public FxInherit? Inherit { get; set; }
}

internal sealed class FxMaterial
{
	public string? Name { get; set; }
	public string? Shader { get; set; }
	/// <summary>plain / dissolve / dissolve_vc / disturb，对应原作着色器（公式见 AkFxShaders）。</summary>
	public string Kind { get; set; } = "plain";
	public string? Tex { get; set; }
	public float[] St { get; set; } = [1, 1, 0, 0];
	public string? Aux { get; set; }
	public float[] AuxSt { get; set; } = [1, 1, 0, 0];
	/// <summary>原作贴图的环绕方式（U、V）：0 重复，1 夹边。</summary>
	public int[] Wrap { get; set; } = [0, 0];
	public int[] AuxWrap { get; set; } = [0, 0];
	public float[] Tint { get; set; } = [0.5f, 0.5f, 0.5f, 0.5f];
	public string Blend { get; set; } = "add";
	public bool Premul { get; set; }
	public int Channel { get; set; }
	public float Amount { get; set; } = 0.5f;
	public float Border { get; set; } = 0.1f;
	public float Iu { get; set; }
	public float Iv { get; set; }
}

internal sealed class FxMesh
{
	public string? Name { get; set; }
	public float[][] V { get; set; } = [];
	public float[][] Uv { get; set; } = [];
	public float[][]? C { get; set; }
	public int[] I { get; set; } = [];
}

internal sealed class FxPsRender
{
	public bool Enabled { get; set; }
	/// <summary>0 面向镜头，1 沿速度拉伸，4 网格，5 不渲染。</summary>
	public int Mode { get; set; }
	public int SortMode { get; set; }
	public int Order { get; set; }
	public float Fudge { get; set; }
	public float LengthScale { get; set; } = 2f;
	public float VelScale { get; set; }
	public float CamVelScale { get; set; }
	public float MinSize { get; set; }
	public float MaxSize { get; set; } = 0.5f;
	/// <summary>0 面向镜头，1 世界，2 本地（面片落在粒子系统节点的 xy 平面上），3 朝向镜头，4 沿速度。</summary>
	public int Align { get; set; }
	public float[] Pivot { get; set; } = [0, 0, 0];
	public float[] Flip { get; set; } = [0, 0, 0];
	public FxMaterial? Mat { get; set; }
	public FxMaterial? TrailMat { get; set; }
	public FxMesh? Mesh { get; set; }
}

internal sealed class FxParticleSystem
{
	public float Duration { get; set; } = 1f;
	public bool Loop { get; set; }
	public bool Prewarm { get; set; }
	public FxCurve Delay { get; set; } = FxCurve.Zero;
	/// <summary>0 本地空间，1 世界空间。</summary>
	public int Space { get; set; }
	/// <summary>0 层级，1 本地，2 形状。</summary>
	public int Scaling { get; set; } = 1;
	public float SpeedMul { get; set; } = 1f;
	public bool PlayOnAwake { get; set; } = true;
	public FxCurve Life { get; set; } = FxCurve.One;
	public FxCurve Speed { get; set; } = FxCurve.Zero;
	public FxGradient Color { get; set; } = new();
	public FxCurve Size { get; set; } = FxCurve.One;
	public bool Size3d { get; set; }
	public FxCurve SizeY { get; set; } = FxCurve.One;
	public FxCurve SizeZ { get; set; } = FxCurve.One;
	public bool Rot3d { get; set; }
	public FxCurve RotX { get; set; } = FxCurve.Zero;
	public FxCurve RotY { get; set; } = FxCurve.Zero;
	public FxCurve Rot { get; set; } = FxCurve.Zero;
	public float RotFlip { get; set; }
	public FxCurve Gravity { get; set; } = FxCurve.Zero;
	public int Max { get; set; } = 1000;
	public FxCurve Rate { get; set; } = FxCurve.Zero;
	/// <summary>按发射器移动距离发射（每世界单位几颗）：黑蛇剑气的火焰就是旋转的发射器扫出来的。</summary>
	public FxCurve RateDist { get; set; } = FxCurve.Zero;
	public FxBurst[] Bursts { get; set; } = [];
	public FxShape? Shape { get; set; }
	public FxMods Mods { get; set; } = new();
	public FxPsRender Render { get; set; } = new();
}

internal sealed class FxMeshRenderer
{
	public bool Enabled { get; set; }
	public int Order { get; set; }
	public FxMaterial? Mat { get; set; }
	public FxMesh? Mesh { get; set; }
}

internal sealed class FxTrailRenderer
{
	public bool Enabled { get; set; }
	public int Order { get; set; }
	public float Time { get; set; }
	public float MinDist { get; set; }
	public float WidthMul { get; set; } = 1f;
	public float[] Width { get; set; } = [];
	public float[][] Grad { get; set; } = [];
	public int TexMode { get; set; }
	public bool Emitting { get; set; } = true;
	public FxMaterial? Mat { get; set; }
}

internal sealed class FxNodeSpec
{
	public string Name { get; set; } = "";
	public int Parent { get; set; } = -1;
	public string Path { get; set; } = "";
	public bool Active { get; set; } = true;
	public float[] Pos { get; set; } = [0, 0, 0];
	public float[] Rot { get; set; } = [0, 0, 0, 1];
	public float[] Scale { get; set; } = [1, 1, 1];
	public FxParticleSystem? Ps { get; set; }
	public FxMeshRenderer? Mesh { get; set; }
	public FxTrailRenderer? Trail { get; set; }
}

internal sealed class FxTrack
{
	public int Node { get; set; }
	/// <summary>Unity 类型号：1 GameObject，4 Transform，23/137/199 渲染器（材质属性），198 粒子系统。</summary>
	public int Type { get; set; }
	public string Prop { get; set; } = "";
	/// <summary>按 fps 采样的值（每帧一组分量）；只有一帧时是常量。</summary>
	public float[][] V { get; set; } = [];

	public float[] At(float time, float fps)
	{
		if (V.Length == 1)
		{
			return V[0];
		}

		float f = time * fps;
		int i = Math.Clamp((int)f, 0, V.Length - 1);
		int j = Math.Min(i + 1, V.Length - 1);
		float s = Math.Clamp(f - i, 0f, 1f);
		float[] a = V[i], b = V[j];
		float[] r = new float[a.Length];
		for (int k = 0; k < a.Length; k++)
		{
			r[k] = a[k] + (b[k] - a[k]) * s;
		}

		return r;
	}
}

internal sealed class FxClip
{
	public float Length { get; set; }
	public bool Loop { get; set; }
	public float Fps { get; set; } = 60f;
	public int Owner { get; set; }
	public FxTrack[] Tracks { get; set; } = [];
}

internal sealed class FxAttach
{
	public int A { get; set; }
	public int C { get; set; }
	public int D { get; set; }
}

internal sealed class FxSpec
{
	public string Name { get; set; } = "";
	public FxNodeSpec[] Nodes { get; set; } = [];
	public FxClip[] Clips { get; set; } = [];

	/// <summary>原作挂点组件（转换器读出，见 tools/ak_vfx/convert_ak_vfx.py 的 attach_mode）；没有组件的特效（命中、拖尾等由代码生成的）为 null。</summary>
	public FxAttach? Attach { get; set; }

	private static readonly JsonSerializerOptions Options = new()
	{
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
		PropertyNameCaseInsensitive = false,
	};

	public static FxSpec Parse(string json) => JsonSerializer.Deserialize<FxSpec>(json, Options)
		?? throw new InvalidDataException("empty effect spec");
}
