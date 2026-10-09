using Godot;

namespace ArknightsRoaringFlare.Vfx;

/// <summary>
/// 原作特效用到的七个粒子着色器（Torappu/Particles/*）在 Godot 里的重写。公式照 [uc]shaders.ab 里 GLES3 程序的 GLSL 源码：
/// <list type="bullet">
/// <item>Additive / AlphaBlend：颜色 = 贴图（可只取一个通道）× 顶点色 × 色调 × 2。</item>
/// <item>Dissolve Add / AB：同上，alpha 再乘溶解遮罩 clamp((溶解图 − Amount + BorderWidth·[Amount ≤ 0]) / BorderWidth)。</item>
/// <item>Dissolve (VertexColor Major)：溶解阈值改由顶点色 alpha 驱动，色调只乘 alpha。</item>
/// <item>Disturb Origin：主贴图 UV 按扰动图的 R 偏移 (IntensityU, IntensityV)，颜色 = 贴图 × MainColor × 顶点色 × MainColor.a × 2。</item>
/// </list>
/// 混合：Additive 类是 SrcAlpha + One（Godot 的 blend_add），AlphaBlend 类是 SrcAlpha + OneMinusSrcAlpha（blend_mix）；
/// Disturb 的混合由材质给（默认 One + One，输出已预乘，这里写 alpha = 1 后走 blend_add）。
/// UV 保持 Unity 的约定（v 向上），采样时翻转；主贴图的平铺/偏移（_MainTex_ST）在着色器里做，原作动画会驱动它滚动。
/// </summary>
internal static class AkFxShaders
{
	private const string Body = """
		uniform int kind = 0;
		uniform int channel = 0;
		uniform vec4 tint = vec4(0.5);
		uniform vec4 main_st = vec4(1.0, 1.0, 0.0, 0.0);
		uniform sampler2D aux_tex : repeat_enable, filter_linear, hint_default_white;
		uniform vec4 aux_st = vec4(1.0, 1.0, 0.0, 0.0);
		uniform float amount = 0.5;
		uniform float border = 0.1;
		uniform float iu = 0.0;
		uniform float iv = 0.0;
		uniform bool premul = false;
		uniform vec2 main_clamp = vec2(0.0);
		uniform vec2 aux_clamp = vec2(0.0);

		// Godot 不允许同一个函数的采样器参数既传内置 TEXTURE 又传自定义贴图，所以主贴图与辅助贴图分开取样。
		vec2 unity_uv(vec2 uv, vec2 clamp_uv) {
			if (clamp_uv.x > 0.5) { uv.x = clamp(uv.x, 0.0, 1.0); }
			if (clamp_uv.y > 0.5) { uv.y = clamp(uv.y, 0.0, 1.0); }
			return vec2(uv.x, 1.0 - uv.y);
		}

		void fragment() {
			vec4 vc = COLOR;
			vec2 uv0 = UV;
			vec4 o;
			if (kind == 3) {
				float d = texture(aux_tex, unity_uv(uv0 * aux_st.xy + aux_st.zw, aux_clamp)).r;
				vec2 muv = (uv0 + d * vec2(iu, iv)) * main_st.xy + main_st.zw;
				o = texture(TEXTURE, unity_uv(muv, main_clamp)) * tint * vc * tint.a;
				o = o + o;
				if (premul) {
					o = vec4(o.rgb, 1.0);
				}
			} else {
				vec4 t = texture(TEXTURE, unity_uv(uv0 * main_st.xy + main_st.zw, main_clamp));
				if (kind == 0) {
					vec4 tc = channel == 0 ? t : vec4(channel == 1 ? t.r : channel == 2 ? t.g : channel == 3 ? t.b : t.a);
					o = tc * (vc * tint) * 2.0;
				} else if (kind == 1) {
					float dv = texture(aux_tex, unity_uv(uv0 * aux_st.xy + aux_st.zw, aux_clamp)).r;
					float edge = 1.0 - roundEven(amount + 0.5);
					float m = clamp((border * edge + dv - amount) / border, 0.0, 1.0);
					o = (vc * tint) * 2.0 * t;
					o.a = o.a * m;
				} else {
					float dv = texture(aux_tex, unity_uv(uv0 * aux_st.xy + aux_st.zw, aux_clamp)).r;
					float m = clamp((dv - (1.0 - vc.a) + vc.a * border) / border, 0.0, 1.0);
					o = vec4(t.rgb * vc.rgb * 2.0, t.a * m * tint.a);
				}
				o.a = clamp(o.a, 0.0, 1.0);
			}
			COLOR = o;
		}
		""";

	private static Shader? _add;
	private static Shader? _mix;

	private static Shader AddShader => _add ??= new Shader { Code = "shader_type canvas_item;\nrender_mode blend_add, unshaded;\n" + Body };

	private static Shader MixShader => _mix ??= new Shader { Code = "shader_type canvas_item;\nrender_mode blend_mix, unshaded;\n" + Body };

	public static ShaderMaterial Create(FxMaterial mat, Func<string?, Texture2D?> textures)
	{
		ShaderMaterial m = new() { Shader = mat.Blend == "add" ? AddShader : MixShader };
		m.SetShaderParameter("kind", mat.Kind switch
		{
			"dissolve" => 1,
			"dissolve_vc" => 2,
			"disturb" => 3,
			_ => 0,
		});
		m.SetShaderParameter("channel", mat.Channel);
		m.SetShaderParameter("tint", FxGradient.ToColor(mat.Tint));
		m.SetShaderParameter("main_st", ToVec4(mat.St));
		m.SetShaderParameter("aux_st", ToVec4(mat.AuxSt));
		m.SetShaderParameter("amount", mat.Amount);
		m.SetShaderParameter("border", Math.Max(1e-4f, mat.Border));
		m.SetShaderParameter("iu", mat.Iu);
		m.SetShaderParameter("iv", mat.Iv);
		m.SetShaderParameter("premul", mat.Premul);
		m.SetShaderParameter("main_clamp", new Vector2(Clamped(mat.Wrap, 0), Clamped(mat.Wrap, 1)));
		m.SetShaderParameter("aux_clamp", new Vector2(Clamped(mat.AuxWrap, 0), Clamped(mat.AuxWrap, 1)));
		if (textures(mat.Aux) is { } aux)
		{
			m.SetShaderParameter("aux_tex", aux);
		}

		return m;
	}

	private static int Clamped(int[] wrap, int axis) => wrap.Length > axis && wrap[axis] == 1 ? 1 : 0;

	public static Vector4 ToVec4(float[] v) => v.Length >= 4 ? new Vector4(v[0], v[1], v[2], v[3]) : new Vector4(1, 1, 0, 0);

	/// <summary>开战时用极淡的小面片把两个着色器各画一次，免得首次出招时现场编译管线卡顿。</summary>
	public static void Prewarm(Node parent)
	{
		foreach (Shader shader in new[] { AddShader, MixShader })
		{
			ColorRect rect = new()
			{
				Size = new Vector2(2, 2),
				Modulate = new Color(1, 1, 1, 0.02f),
				Material = new ShaderMaterial { Shader = shader },
				MouseFilter = Control.MouseFilterEnum.Ignore,
			};
			parent.AddChild(rect);
			rect.GetTree().CreateTimer(0.2).Timeout += rect.QueueFree;
		}
	}
}
