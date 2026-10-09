using Godot;
using MegaCrit.Sts2.Core.Logging;

namespace ArknightsRoaringFlare.Vfx;

/// <summary>
/// 播放一个原作特效（<see cref="FxSpec"/>）：按原作的节点树、Animator 动画与粒子系统参数逐帧模拟，投影到 2D 画布上绘制。
/// 本模组不带 Godot 的 C# 源码生成器，自定义节点子类的 _Process/_Draw 不会被调用，所以这里是普通对象：挂在
/// SceneTree.ProcessFrame 上推进，绘制直接往各图层节点的画布项里提交三角形（RenderingServer）。
///
/// 坐标系：E 是原作特效空间（Unity：x 右、y 上、z 纵深，单位是原作世界单位）；W 是画布上的特效空间（同样 y 向上、单位不变，
/// 原点在根节点）；W → 根节点本地像素 = (x·K, −y·K)。锚点（骨骼/受击点）给出 E → W 的变换：平移 + 两根轴（敌人立绘是镜像的，
/// 轴组可以是反射）。原作特效挂在立绘挂点上时 xy 平面就是屏幕平面；地面类特效（tilt）先绕 x 轴转 30°，还原原作 60° 俯视镜头。
/// 只是表现：随机数用本地 Random，不碰游戏随机源，联机各端各播各的。
/// </summary>
internal sealed class AkFxPlayer
{
	private const float MaxLifetime = 8f;

	private readonly FxSpec _spec;
	private readonly Node2D _root;
	private readonly float _unitPx;
	private readonly Func<Transform2D?>? _follow;
	private readonly float _stopAfter;
	private readonly Random _random = new();
	private readonly NodeState[] _nodes;
	private readonly List<Layer> _layers = [];
	private readonly List<PsState> _systems = [];
	private readonly List<TrailState> _trails = [];
	private readonly bool _tilted;
	private Basis _tilt;
	private readonly float _speed;

	private Transform3D _anchor;
	private float _time;
	private bool _stopped;
	// 到了 stopAfter：只停循环发射的部分（光环、拖尾），一次性的爆发照自己的时刻放（有的排在特效第 4 秒）。
	private bool _loopsStopped;
	private bool _done;
	private SceneTree? _tree;

	// 画在生物后面的那一半（原版的 BackCombatVfxContainer 里，与 _root 同一个全局变换）。原作透明物体先按 sortingOrder、同序再按离镜头远近排：
	// 排序号为负的部件（黑蛇的触手阵、贴地暗纹、剑后的暗光）与比立绘更远的部件都被立绘挡住。没有后层时全部画在前面。
	private readonly Node2D? _backRoot;
	// 挂在脚下（单位根节点）的特效才按纵深与立绘比：根节点就是原作的单位原点。挂在受击点、骨骼上的只看排序号。
	// 纵深比较的立绘平面（W 的 z）：脚下特效是 SpriteDepth，挂在立绘挂点上、按世界朝向摆放的特效是 0（挂点就在立绘平面上）；null 不比较。
	private readonly float? _depthPlane;

	/// <summary>
	/// 立绘平面在镜头纵深上的位置（W 的 z，越大越远）：原作 Graphic 挂在根节点 (0, −0.2, −0.06)、正对镜头，投影后 z ≈ −0.15。
	/// 本模组怪物的 Graphic 偏移都取这一组（原作敌人预制体的通用值）。
	/// </summary>
	internal const float SpriteDepth = -0.15f;

	/// <param name="root">特效根节点（已在树里、放在锚点处）。</param>
	/// <param name="unitPx">原作 1 个世界单位对应的像素（全局像素）。</param>
	/// <param name="anchor">E → W 的初始变换（W 原点就是根节点位置）。</param>
	/// <param name="follow">每帧取锚点的全局变换（骨骼跟随）；为空时锚点固定。</param>
	/// <param name="stopAfter">循环发射的粒子系统在多少秒后停止发射。</param>
	/// <param name="tilt">地面类特效：还原原作俯视镜头。</param>
	/// <param name="speed">播放倍速（FxCue.Speed：原作特效配的动作比本模组的长时整体压缩）。</param>
	/// <param name="backRoot">画在生物后面的部件放这里（与 root 同一全局变换）；为空时全画在前面。</param>
	public AkFxPlayer(FxSpec spec, Node2D root, float unitPx, Transform3D anchor, Func<Transform2D?>? follow, float stopAfter, bool tilt, float speed = 1f,
		Node2D? backRoot = null, float? depthPlane = null)
	{
		_speed = speed > 0f ? speed : 1f;
		_backRoot = backRoot;
		_depthPlane = tilt ? depthPlane : null;
		_spec = spec;
		_root = root;
		_unitPx = unitPx;
		_anchor = anchor;
		_follow = follow;
		_stopAfter = stopAfter;
		_tilted = tilt;
		_tilt = tilt ? new Basis(Vector3.Right, Mathf.DegToRad(FxTune.None.GroundTilt)) : Basis.Identity;
		_nodes = new NodeState[spec.Nodes.Length];
		for (int i = 0; i < spec.Nodes.Length; i++)
		{
			_nodes[i] = new NodeState(spec.Nodes[i]);
		}

		HashSet<int> subEmitters = [];
		foreach (FxNodeSpec n in spec.Nodes)
		{
			if (n.Ps?.Mods.Sub is { } subs)
			{
				foreach (FxSub s in subs)
				{
					subEmitters.Add(s.Emitter);
				}
			}
		}

		for (int i = 0; i < spec.Nodes.Length; i++)
		{
			FxNodeSpec n = spec.Nodes[i];
			if (n.Ps is { } ps)
			{
				PsState state = new(this, i, ps, subEmitters.Contains(i));
				_systems.Add(state);
				_nodes[i].System = state;
				if (ps.Render.Enabled && ps.Render.Mat is { } mat && ps.Render.Mode != 5)
				{
					state.Layer = AddLayer(mat, ps.Render.Order, i);
				}

				if (ps.Mods.Trail is not null && ps.Render.TrailMat is { } trailMat && ps.Render.Enabled)
				{
					state.TrailLayer = AddLayer(trailMat, ps.Render.Order, i);
				}
			}

			if (n.Mesh is { Mat: { } meshMat, Mesh: not null })
			{
				_nodes[i].MeshLayer = AddLayer(meshMat, n.Mesh.Order, i);
				_nodes[i].RendererEnabled = n.Mesh.Enabled;
			}

			if (n.Trail is { Mat: { } trMat } tr)
			{
				TrailState ts = new(i, tr) { Layer = AddLayer(trMat, tr.Order, i) };
				_trails.Add(ts);
				_nodes[i].TrailRenderer = ts;
			}
		}

		// 原作透明物体按 sortingOrder 排，同序按层级顺序。
		int index = 0;
		foreach (Layer layer in _layers.OrderBy(l => l.Order).ThenBy(l => l.Index))
		{
			root.MoveChild(layer.Node, index);
			if (layer.BackNode is { } back)
			{
				backRoot!.MoveChild(back, index);
			}

			index++;
		}
	}

	/// <summary>这一块画在生物后面吗：排序号为负的总在后面，为正的总在前面；同序（0）的贴地特效按纵深与立绘比。</summary>
	private bool Behind(int order, float depth) => _backRoot is not null && (order < 0 || order == 0 && _depthPlane is { } plane && depth > plane);

	public bool Done => _done;

	/// <summary>
	/// 忽略根节点自带的变换动画。原作有的投射物特效靠根节点的位移动画自己飞（火雨 reborn_01_trail：7 秒沿 x 飞 18.8 个单位），
	/// 本模组由 <see cref="AkFx.Projectile"/> 按起止点移动锚点，两者叠加时特效会冲到目标前面、飞出画面。
	/// </summary>
	public bool PinRoot { get; set; }

	/// <summary>手调参数（见 <see cref="FxTune"/>）：带状拖尾的取样区间、宽度与长度，地面类特效的倾角，隐藏的部件。要在 Start 之前设。</summary>
	public FxTune Tune
	{
		get => _tune;
		set
		{
			_tune = value;
			if (_tilted)
			{
				_tilt = new Basis(Vector3.Right, Mathf.DegToRad(value.GroundTilt));
			}

			for (int i = 0; i < _nodes.Length; i++)
			{
				_nodes[i].Hidden = value.Hide?.Contains(_spec.Nodes[i].Name) == true;
			}
		}
	}

	private FxTune _tune = FxTune.None;

	/// <param name="startAt">从特效自身的第几秒开始（预览里看特效中段用；之前的部分先在后台推进完，不绘制）。</param>
	public void Start(float startAt = 0f)
	{
		_tree = _root.GetTree();
		ApplyAnimation(0f);
		UpdateHierarchy(true);
		foreach (PsState ps in _systems)
		{
			if (ps.Prewarmable)
			{
				ps.Prewarm();
			}
		}

		for (float t = 0f; t < startAt - 1e-4f; t += 1f / 30f)
		{
			Step(Math.Min(1f / 30f, startAt - t));
		}

		_tree.ProcessFrame += Tick;
		Render();
	}

	/// <summary>停止发射（循环特效收尾用），已有的粒子照常消亡。</summary>
	public void Stop() => _stopped = true;

	private Layer AddLayer(FxMaterial mat, int order, int node)
	{
		Node2D n = new() { Name = $"L{_layers.Count}", TextureRepeat = CanvasItem.TextureRepeatEnum.Enabled };
		n.Material = AkFxShaders.Create(mat, AkFx.Texture);
		_root.AddChild(n);
		Node2D? back = null;
		if (_backRoot is not null)
		{
			back = new Node2D { Name = n.Name, TextureRepeat = CanvasItem.TextureRepeatEnum.Enabled, Material = n.Material };
			_backRoot.AddChild(back);
		}

		Layer layer = new(n, back, (ShaderMaterial)n.Material, AkFx.Texture(mat.Tex), mat, order, _layers.Count, node);
		_layers.Add(layer);
		return layer;
	}

	private void Tick()
	{
		if (_done)
		{
			return;
		}


		if (!GodotObject.IsInstanceValid(_root) || !_root.IsInsideTree())
		{
			Finish();
			return;
		}

		try
		{
			Step(Math.Clamp((float)_root.GetProcessDeltaTime(), 0f, 0.1f) * _speed);
			Render();
			if (IsFinished())
			{
				Finish();
			}
		}
		catch (Exception ex)
		{
			Log.Warn($"[{ModEntry.ModId}] effect {_spec.Name} failed: {ex}");
			Finish();
		}
	}

	/// <summary>推进一帧的模拟（不绘制）。</summary>
	private void Step(float dt)
	{
		_time += dt;
		if (_follow is not null)
		{
			if (_follow() is { } follow)
			{
				_anchor = AnchorFromGlobal(follow);
			}
			else if (float.IsPositiveInfinity(_stopAfter))
			{
				// 常驻特效跟随的立绘没了（死亡移除、换场景）：收尾，不再挂着。
				_stopped = true;
			}
		}

		if (_time >= _stopAfter)
		{
			_loopsStopped = true;
		}

		ApplyAnimation(_time);
		UpdateHierarchy(false);
		foreach (PsState ps in _systems)
		{
			ps.Update(dt);
		}

		foreach (TrailState tr in _trails)
		{
			UpdateTrailRenderer(tr);
		}
	}

	private bool IsFinished()
	{
		if (_time >= MaxLifetime && !float.IsPositiveInfinity(_stopAfter))
		{
			return true;
		}

		float clipEnd = _spec.Clips.Where(c => !c.Loop).Select(c => c.Length).DefaultIfEmpty(0f).Max();
		if (_time < clipEnd)
		{
			return false;
		}

		// 循环动画（原作挂在身上的光环）没有终点：停止发射后不再等它。
		if (_spec.Clips.Any(c => c.Loop) && !_stopped && !_loopsStopped)
		{
			return false;
		}

		// 投射物上的拖尾（只有 TrailRenderer 的特效）在飞行中一直保留，落地停止后才随拖尾淡出结束。
		if (!_stopped && !_loopsStopped && _trails.Count > 0 && _follow is not null)
		{
			return false;
		}

		return _systems.All(s => s.Finished) && _trails.All(t => t.Points.Count == 0 || !_nodes[t.Node].ActiveInHierarchy);
	}

	private void Finish()
	{
		if (_done)
		{
			return;
		}

		_done = true;
		if (_tree is not null)
		{
			_tree.ProcessFrame -= Tick;
		}

		if (GodotObject.IsInstanceValid(_root))
		{
			_root.QueueFree();
		}

		if (_backRoot is not null && GodotObject.IsInstanceValid(_backRoot))
		{
			_backRoot.QueueFree();
		}
	}

	/// <summary>全局变换（骨骼）→ E 到 W 的锚点变换：两根轴取方向（保留镜像造成的反射），原点换到根节点的 W 坐标。</summary>
	internal Transform3D AnchorFromGlobal(Transform2D global)
	{
		Vector2 local = _root.ToLocal(global.Origin);
		Vector2 ax = (_root.ToLocal(global.Origin + global.X) - local).Normalized();
		Vector2 ay = (_root.ToLocal(global.Origin + global.Y) - local).Normalized();
		float k = LocalUnitPx;
		// 画布 y 向下，W 的 y 向上：两根轴与原点都翻转 y。
		Basis b = new(new Vector3(ax.X, -ax.Y, 0), new Vector3(ay.X, -ay.Y, 0), Vector3.Back);
		return new Transform3D(b, new Vector3(local.X / k, -local.Y / k, 0));
	}

	internal void AnchorFromGlobalInto(Transform2D global) => _anchor = AnchorFromGlobal(global);

	private float LocalUnitPx
	{
		get
		{
			float s = Math.Abs(_root.GetGlobalTransform().Scale.Y);
			return s > 1e-4f ? _unitPx / s : _unitPx;
		}
	}

	// ------------------------------------------------------------------------------------------ 动画与层级

	private void ApplyAnimation(float time)
	{
		foreach (FxClip clip in _spec.Clips)
		{
			float t = clip.Loop && clip.Length > 0 ? time % clip.Length : Math.Min(time, clip.Length);
			foreach (FxTrack track in clip.Tracks)
			{
				if (track.Node < 0 || track.Node >= _nodes.Length)
				{
					continue;
				}

				NodeState n = _nodes[track.Node];
				float[] v = track.At(t, clip.Fps);
				switch (track.Type)
				{
					case 4 when track.Node == 0 && PinRoot:
						break;
					case 4:
						ApplyTransformTrack(n, track.Prop, v);
						break;
					case 1 when track.Prop == "m_IsActive":
						n.ActiveSelf = v[0] > 0.5f;
						break;
					case 198:
						if (n.System is { } ps)
						{
							ps.Overrides[track.Prop] = v[0];
						}

						break;
					default:
						if (track.Prop == "m_Enabled")
						{
							n.RendererEnabled = v[0] > 0.5f;
						}
						else
						{
							n.MaterialOverrides[track.Prop] = v[0];
						}

						break;
				}
			}
		}
	}

	private static void ApplyTransformTrack(NodeState n, string prop, float[] v)
	{
		switch (prop)
		{
			case "pos" when v.Length >= 3:
				n.LocalPos = new Vector3(v[0], v[1], v[2]);
				break;
			case "rot" when v.Length >= 4:
				n.LocalRot = new Quaternion(v[0], v[1], v[2], v[3]).Normalized();
				break;
			case "euler" when v.Length >= 3:
				n.LocalRot = UnityEuler(v[0], v[1], v[2]);
				break;
			case "scale" when v.Length >= 3:
				n.LocalScale = new Vector3(v[0], v[1], v[2]);
				break;
		}
	}

	/// <summary>Unity 的欧拉角（度）：先 z、再 x、再 y。</summary>
	internal static Quaternion UnityEuler(float x, float y, float z)
	{
		Quaternion qx = new(Vector3.Right, Mathf.DegToRad(x));
		Quaternion qy = new(Vector3.Up, Mathf.DegToRad(y));
		Quaternion qz = new(Vector3.Back, Mathf.DegToRad(z));
		return (qy * qx * qz).Normalized();
	}

	private void UpdateHierarchy(bool initial)
	{
		for (int i = 0; i < _nodes.Length; i++)
		{
			NodeState n = _nodes[i];
			int parent = _spec.Nodes[i].Parent;
			Transform3D local = new(new Basis(n.LocalRot) * Basis.FromScale(n.LocalScale), n.LocalPos);
			bool parentActive = parent < 0 || _nodes[parent].ActiveInHierarchy;
			bool active = parentActive && n.ActiveSelf && !n.Hidden;
			if (parent < 0)
			{
				n.World = new Transform3D(_tilt, Vector3.Zero) * local;
				n.WorldRot = (new Quaternion(_tilt) * n.LocalRot).Normalized();
			}
			else
			{
				n.World = _nodes[parent].World * local;
				n.WorldRot = (_nodes[parent].WorldRot * n.LocalRot).Normalized();
			}

			bool wasActive = n.ActiveInHierarchy;
			n.ActiveInHierarchy = active;
			if (n.System is { } ps)
			{
				if (active && (initial || !wasActive))
				{
					ps.OnEnable();
				}
				else if (!active && wasActive && !initial)
				{
					ps.OnDisable();
				}
			}

			if (n.TrailRenderer is { } tr && !active && wasActive)
			{
				tr.Points.Clear();
			}
		}
	}

	/// <summary>粒子系统用的矩阵：位置与朝向取完整层级，缩放只取本节点（原作全部用 Local 缩放模式）。</summary>
	private Transform3D SystemMatrix(int node)
	{
		NodeState n = _nodes[node];
		FxParticleSystem ps = _spec.Nodes[node].Ps!;
		Basis b = ps.Scaling == 0 ? n.World.Basis : new Basis(n.WorldRot) * Basis.FromScale(n.LocalScale);
		return new Transform3D(b, n.World.Origin);
	}

	// ------------------------------------------------------------------------------------------ 粒子系统

	private sealed class Particle
	{
		public Vector3 Pos;
		public Vector3 Vel;
		public Vector3 AnimVel;
		public float Age;
		public float Life;
		public Vector3 Size0;
		public Color Color0;
		public Vector3 Rot;
		public float RotSign;
		public float R0, R1, R2, R3, R4, R5;
		public int Id;
		public float SheetRow;
		public bool FlipU, FlipV;
		public Vector3 LastWorld;
	}

	private sealed class PsState
	{
		private readonly AkFxPlayer _p;
		private readonly FxParticleSystem _s;
		private readonly int _node;
		private readonly bool _isSub;
		private float _t;
		private float _delay;
		private bool _playing;
		private float _emitAcc;
		private float _distAcc;
		private Vector3? _lastEmitterW;
		private float _prevCycleT;
		private int _cycle;
		private int _nextId;

		public readonly List<Particle> Particles = [];
		public readonly Dictionary<string, float> Overrides = [];
		public Layer? Layer;
		public Layer? TrailLayer;

		public PsState(AkFxPlayer player, int node, FxParticleSystem s, bool isSub)
		{
			_p = player;
			_node = node;
			_s = s;
			_isSub = isSub;
			_seed = (float)player._random.NextDouble() * 100f;
		}

		/// <summary>噪声流场的相位（每个粒子系统一个，系统内所有粒子共用）。</summary>
		private readonly float _seed;

		public FxParticleSystem Spec => _s;

		public int Node => _node;

		public bool Prewarmable => _s.Prewarm && _s.Loop && _playing;

		public bool Finished => Particles.Count == 0 && (!_playing || !_s.Loop && _t > _s.Duration || _p._stopped || _s.Loop && _p._loopsStopped || _isSub);

		private float? Ov(string key) => Overrides.TryGetValue(key, out float v) ? v : null;

		public void OnEnable()
		{
			if (!_s.PlayOnAwake || _isSub)
			{
				return;
			}

			_playing = true;
			_delay = _s.Delay.Eval(0f, (float)_p._random.NextDouble());
			_t = -_delay;
			_emitAcc = 0f;
			_distAcc = 0f;
			_lastEmitterW = null;
			_prevCycleT = -1e-4f;
			_cycle = 0;
		}

		public void OnDisable()
		{
			_playing = false;
			_lastEmitterW = null;
			Particles.Clear();
		}

		public void Prewarm()
		{
			float step = 1f / 30f;
			for (float t = 0; t < _s.Duration; t += step)
			{
				Update(step);
			}
		}

		public void Update(float dt)
		{
			dt *= _s.SpeedMul;
			NodeState node = _p._nodes[_node];
			Transform3D m = _p.SystemMatrix(_node);
			if (_playing && node.ActiveInHierarchy && !_p._stopped && !(_s.Loop && _p._loopsStopped))
			{
				Emit(dt, m);
			}
			else if (_playing)
			{
				_t += dt;
			}

			Simulate(dt, m);
		}

		private void Emit(float dt, Transform3D m)
		{
			float prev = _t;
			_t += dt;
			// 按距离发射看的是发射器在 W 里走过的路程（原作是世界空间距离；W 与原作世界同单位）。
			Vector3 emitterW = (_p._anchor * m).Origin;
			Vector3? lastW = _lastEmitterW;
			_lastEmitterW = emitterW;
			if (_t < 0f)
			{
				return;
			}

			float duration = Math.Max(1e-3f, _s.Duration);
			if (!_s.Loop && prev >= duration)
			{
				return;
			}

			if (_s.RateDist.M != 0 || _s.RateDist.A > 0f)
			{
				EmitByDistance(lastW, emitterW, Math.Min(_t, duration) / duration, m);
			}

			float cycleT = _s.Loop ? _t % duration : Math.Min(_t, duration);
			int cycle = _s.Loop ? (int)(_t / duration) : 0;
			if (cycle != _cycle)
			{
				// 进入新一轮：先补完上一轮剩下的爆发。
				EmitBursts(_prevCycleT, duration + 1e-4f, m);
				_cycle = cycle;
				_prevCycleT = -1e-4f;
			}

			float norm = cycleT / duration;
			float rate = _s.Rate.Eval(norm, (float)_p._random.NextDouble());
			_emitAcc += rate * dt;
			int n = (int)_emitAcc;
			_emitAcc -= n;
			for (int i = 0; i < n; i++)
			{
				Spawn(m, norm, null);
			}

			EmitBursts(_prevCycleT, cycleT, m);
			_prevCycleT = cycleT;
		}

		/// <summary>按距离发射：这一帧走过的线段上均匀撒下粒子（快速旋转的发射器才拉得成连续的火焰弧）。</summary>
		private void EmitByDistance(Vector3? lastW, Vector3 curW, float norm, Transform3D m)
		{
			if (lastW is not { } from)
			{
				return;
			}

			float perUnit = _s.RateDist.Eval(norm, (float)_p._random.NextDouble());
			_distAcc += perUnit * (curW - from).Length();
			int n = (int)_distAcc;
			_distAcc -= n;
			for (int i = 0; i < n; i++)
			{
				float f = (i + (float)_p._random.NextDouble()) / n;
				Spawn(m, norm, null, (from - curW) * (1f - f));
			}
		}

		private void EmitBursts(float from, float to, Transform3D m)
		{
			foreach (FxBurst b in _s.Bursts)
			{
				int cycles = b.Cycles <= 0 ? 1000 : b.Cycles;
				for (int k = 0; k < cycles; k++)
				{
					float at = b.T + k * Math.Max(1e-3f, b.Interval);
					if (at > to)
					{
						break;
					}

					if (at > from && (b.P >= 1f || _p._random.NextDouble() < b.P))
					{
						int count = (int)Math.Round(b.N.Eval(at / Math.Max(1e-3f, _s.Duration), (float)_p._random.NextDouble()));
						for (int i = 0; i < count; i++)
						{
							Spawn(m, at / Math.Max(1e-3f, _s.Duration), null);
						}
					}
				}
			}
		}

		/// <summary>子发射器：在给定的 W 坐标处按自身的爆发数发射一批。</summary>
		public void EmitSub(Vector3 worldPos)
		{
			Transform3D m = _p.SystemMatrix(_node);
			int count = 0;
			foreach (FxBurst b in _s.Bursts)
			{
				count += (int)Math.Round(b.N.Eval(0f, (float)_p._random.NextDouble()));
			}

			if (count == 0)
			{
				count = Math.Max(1, (int)Math.Round(_s.Rate.Eval(0f, 0.5f) * 0.1f));
			}

			for (int i = 0; i < count; i++)
			{
				Spawn(m, 0f, worldPos);
			}
		}

		/// <param name="shiftW">W 空间里的整体平移（按距离发射时把粒子摆回本帧走过的线段上）。</param>
		private void Spawn(Transform3D m, float norm, Vector3? worldOrigin, Vector3 shiftW = default)
		{
			if (Particles.Count >= _s.Max)
			{
				return;
			}

			Random r = _p._random;
			Particle pt = new()
			{
				R0 = (float)r.NextDouble(),
				R1 = (float)r.NextDouble(),
				R2 = (float)r.NextDouble(),
				R3 = (float)r.NextDouble(),
				R4 = (float)r.NextDouble(),
				R5 = (float)r.NextDouble(),
				Id = _nextId++,
			};
			pt.Life = Math.Max(1e-3f, _s.Life.Eval(norm, (float)r.NextDouble()) * (_s.Mods.Trail is not null ? _p.Tune.TrailLife : 1f));
			(Vector3 pos, Vector3 dir) = ShapeSample(r);
			float speed = _s.Speed.Eval(norm, (float)r.NextDouble(), Ov("InitialModule.startSpeed.scalar")) * (_s.Mods.Trail is not null ? _p.Tune.TrailSpeed : 1f);
			Vector3 vel = dir * speed;
			float size = _s.Size.Eval(norm, (float)r.NextDouble());
			pt.Size0 = _s.Size3d
				? new Vector3(size, _s.SizeY.Eval(norm, (float)r.NextDouble()), _s.SizeZ.Eval(norm, (float)r.NextDouble()))
				: new Vector3(size, size, size);
			pt.Color0 = _s.Color.Eval(norm, (float)r.NextDouble());
			pt.RotSign = _s.RotFlip > 0f && r.NextDouble() < _s.RotFlip ? -1f : 1f;
			pt.Rot = new Vector3(
				_s.Rot3d ? _s.RotX.Eval(norm, (float)r.NextDouble()) : 0f,
				_s.Rot3d ? _s.RotY.Eval(norm, (float)r.NextDouble()) : 0f,
				_s.Rot.Eval(norm, (float)r.NextDouble())) * pt.RotSign;
			if (_s.Mods.Sheet is { } sheet)
			{
				pt.SheetRow = sheet.RowMode == 1 || sheet.Type == 0 ? sheet.Row : r.Next(Math.Max(1, sheet.Ty));
				pt.SheetRow = sheet.Type == 1 && sheet.RowMode == 0 ? r.Next(Math.Max(1, sheet.Ty)) : pt.SheetRow;
			}

			FxPsRender ren = _s.Render;
			pt.FlipU = ren.Flip.Length > 0 && r.NextDouble() < ren.Flip[0];
			pt.FlipV = ren.Flip.Length > 1 && r.NextDouble() < ren.Flip[1];
			if (_s.Space == 1)
			{
				// 世界空间：出生时就换到 W，之后不随节点移动。
				Transform3D w = _p._anchor * m;
				pt.Pos = (worldOrigin ?? w * pos) + shiftW;
				pt.Vel = w.Basis * vel;
			}
			else
			{
				Transform3D inv = (_p._anchor * m).AffineInverse();
				pt.Pos = worldOrigin is { } wo ? inv * (wo + shiftW) : pos + inv.Basis * shiftW;
				pt.Vel = vel;
			}

			pt.LastWorld = WorldPos(pt, _p._anchor * m);
			Particles.Add(pt);
		}

		private (Vector3 Pos, Vector3 Dir) ShapeSample(Random r)
		{
			if (_s.Shape is not { } sh)
			{
				return (Vector3.Zero, Vector3.Back);
			}

			Vector3 pos, dir;
			float arc = Mathf.DegToRad(sh.Arc <= 0 ? 360f : sh.Arc);
			switch (sh.Type)
			{
				case 0:
				case 1:
				case 2:
				case 3:
				{
					Vector3 u = RandomUnit(r);
					if (sh.Type is 2 or 3 && u.Z < 0)
					{
						u.Z = -u.Z;
					}

					float rad = sh.Radius * Mathf.Lerp(1f, MathF.Cbrt((float)r.NextDouble()), sh.Thick);
					pos = u * rad;
					dir = u;
					break;
				}
				case 4:
				case 7:
				case 8:
				case 9:
				{
					float phi = (float)r.NextDouble() * arc;
					float rr = Mathf.Lerp(1f, MathF.Sqrt((float)r.NextDouble()), sh.Thick);
					Vector2 disc = new(Mathf.Cos(phi) * rr, Mathf.Sin(phi) * rr);
					float a = Mathf.DegToRad(sh.Angle);
					pos = new Vector3(disc.X * sh.Radius, disc.Y * sh.Radius, 0f);
					if (sh.Radius < 1e-4f)
					{
						float phi2 = (float)r.NextDouble() * Mathf.Tau;
						float t = Mathf.Sqrt((float)r.NextDouble());
						disc = new Vector2(Mathf.Cos(phi2) * t, Mathf.Sin(phi2) * t);
					}

					dir = new Vector3(disc.X * Mathf.Sin(a), disc.Y * Mathf.Sin(a), Mathf.Cos(a)).Normalized();
					if (sh.Type is 8 or 9)
					{
						pos += dir * sh.Length * (float)r.NextDouble();
					}

					break;
				}
				case 10:
				case 11:
				{
					float phi = (float)r.NextDouble() * arc;
					float rr = sh.Radius * Mathf.Lerp(1f, MathF.Sqrt((float)r.NextDouble()), sh.Thick);
					Vector3 radial = new(Mathf.Cos(phi), Mathf.Sin(phi), 0f);
					pos = radial * rr;
					dir = radial;
					break;
				}
				case 12:
					pos = new Vector3(((float)r.NextDouble() * 2f - 1f) * sh.Radius, 0f, 0f);
					dir = Vector3.Up;
					break;
				default:
					// 盒体（含盒面、盒边）：单位立方体按形状缩放，朝 +z 发射。
					pos = new Vector3((float)r.NextDouble() - 0.5f, (float)r.NextDouble() - 0.5f, (float)r.NextDouble() - 0.5f);
					dir = Vector3.Back;
					break;
			}

			Quaternion q = UnityEuler(sh.Rot[0], sh.Rot[1], sh.Rot[2]);
			float sy = Ov("ShapeModule.m_Scale.y") ?? sh.Scale[1];
			float sz = Ov("ShapeModule.m_Scale.z") ?? sh.Scale[2];
			Vector3 scale = new(sh.Scale[0], sy, sz);
			if (Ov("ShapeModule.radius.value") is { } radiusOv && sh.Radius > 1e-5f)
			{
				pos *= radiusOv / sh.Radius;
			}

			pos = q * (pos * scale) + new Vector3(sh.Pos[0], sh.Pos[1], sh.Pos[2]);
			dir = (q * dir).Normalized();
			if (sh.SphDir > 0 && pos.LengthSquared() > 1e-8f)
			{
				dir = dir.Lerp(pos.Normalized(), sh.SphDir).Normalized();
			}

			if (sh.RandDir > 0)
			{
				dir = dir.Lerp(RandomUnit(r), sh.RandDir).Normalized();
			}

			if (sh.RandPos > 0)
			{
				pos += RandomUnit(r) * sh.RandPos * (float)r.NextDouble();
			}

			return (pos, dir);
		}

		private static Vector3 RandomUnit(Random r)
		{
			float z = (float)r.NextDouble() * 2f - 1f;
			float phi = (float)r.NextDouble() * Mathf.Tau;
			float s = Mathf.Sqrt(1f - z * z);
			return new Vector3(s * Mathf.Cos(phi), s * Mathf.Sin(phi), z);
		}

		private void Simulate(float dt, Transform3D m)
		{
			Transform3D toW = _p._anchor * m;
			FxMods mods = _s.Mods;
			Vector3 gravityE = new(0, -9.81f, 0);
			for (int i = Particles.Count - 1; i >= 0; i--)
			{
				Particle pt = Particles[i];
				pt.Age += dt;
				if (pt.Age >= pt.Life)
				{
					OnDeath(pt, toW);
					Particles.RemoveAt(i);
					continue;
				}

				float n = pt.Age / pt.Life;
				bool world = _s.Space == 1;
				// 手调的拖尾运动尺度（FxTune.TrailSpeed）：初速度（出生时）、力、限速与噪声一起乘，粒子轨迹整体等比缩放、形状不变。
				float reach = _s.Mods.Trail is not null ? _p.Tune.TrailSpeed : 1f;
				float g = _s.Gravity.Eval(n, pt.R0);
				if (g != 0f)
				{
					Vector3 gv = gravityE * g * dt;
					pt.Vel += world ? _p._anchor.Basis * gv : m.Basis.Inverse() * gv;
				}

				if (mods.Force is { } f)
				{
					Vector3 fv = new(f.X.Eval(n, pt.R1, Ov("ForceModule.x.scalar")), f.Y.Eval(n, pt.R2), f.Z.Eval(n, pt.R3));
					pt.Vel += ToSpace(fv * dt * reach, f.World, world, m);
				}

				pt.AnimVel = Vector3.Zero;
				if (mods.Vel is { } v)
				{
					Vector3 vv = new(v.X.Eval(n, pt.R1), v.Y.Eval(n, pt.R2), v.Z.Eval(n, pt.R3));
					pt.AnimVel = ToSpace(vv, v.World, world, m);
					float radial = v.Radial.Eval(n, pt.R4);
					if (radial != 0f && pt.Pos.LengthSquared() > 1e-8f)
					{
						pt.AnimVel += pt.Pos.Normalized() * radial;
					}

					float orbZ = v.OrbZ.Eval(n, pt.R5);
					if (orbZ != 0f)
					{
						pt.Pos = new Basis(Vector3.Back, orbZ * dt) * pt.Pos;
					}
				}

				if (mods.Limit is { } lim)
				{
					float cap = lim.Mag.Eval(n, pt.R2) * reach;
					Vector3 total = pt.Vel;
					float speed = total.Length();
					if (!lim.Sep && speed > cap && speed > 1e-6f)
					{
						float excess = speed - cap;
						float keep = 1f - Mathf.Pow(1f - Math.Clamp(lim.Dampen, 0f, 1f), dt * 30f);
						pt.Vel = total.Normalized() * (speed - excess * keep);
					}

					float drag = lim.Drag.Eval(n, pt.R3);
					if (drag > 0f)
					{
						pt.Vel *= Math.Max(0f, 1f - drag * dt);
					}
				}

				if (mods.Noise is { } noise)
				{
					// 原作的噪声是随空间位置连续变化的流场（加上随时间滚动），相邻粒子走向一致：
					// 黑蛇的“触手”就是一串粒子连成的拖尾，噪声若按粒子各自随机，连出来是乱折的锯齿线。
					float sx = noise.X.Eval(n, pt.R4);
					Vector3 strength = noise.Sep ? new Vector3(sx, noise.Y.Eval(n, pt.R4), noise.Z.Eval(n, pt.R4)) : new Vector3(sx, sx, sx);
					float freq = Math.Max(0.01f, noise.Freq);
					float scroll = noise.Scroll.Eval(n, pt.R5, Ov("NoiseModule.scrollSpeed.scalar"));
					Vector3 q = pt.Pos * (freq / Math.Max(0.05f, reach));
					float tt = _p._time * scroll + _seed;
					Vector3 nv = new(
						Mathf.Sin(q.Y * 1.31f + q.Z * 0.73f + tt * 1.7f) + Mathf.Sin(q.Z * 1.93f - q.X * 0.57f + tt * 1.1f + 1.3f),
						Mathf.Sin(q.Z * 1.17f + q.X * 0.89f + tt * 1.5f + 2.1f) + Mathf.Sin(q.X * 1.71f - q.Y * 0.61f + tt * 0.9f + 3.7f),
						Mathf.Sin(q.X * 1.23f + q.Y * 0.97f + tt * 1.3f + 4.3f) + Mathf.Sin(q.Y * 1.57f - q.Z * 0.69f + tt * 0.8f + 5.9f));
					pt.AnimVel += nv * 0.5f * strength * noise.Pos.Eval(n, 0.5f) * reach;
				}

				if (mods.Rot is { } rot)
				{
					pt.Rot += new Vector3(rot.Sep ? rot.X.Eval(n, pt.R1) : 0f, rot.Sep ? rot.Y.Eval(n, pt.R2) : 0f, rot.Z.Eval(n, pt.R3))
						* pt.RotSign * dt;
				}

				pt.Pos += (pt.Vel + pt.AnimVel) * dt;
				pt.LastWorld = WorldPos(pt, toW);
			}

			if (mods.Sub is not null)
			{
				foreach (Particle pt in Particles)
				{
					if (pt.Age <= dt)
					{
						FireSubs(0, pt.LastWorld);
					}
				}
			}
		}

		private Vector3 ToSpace(Vector3 v, bool moduleWorld, bool particlesWorld, Transform3D m)
		{
			if (moduleWorld == particlesWorld)
			{
				return particlesWorld ? _p._anchor.Basis * v : v;
			}

			return particlesWorld ? (_p._anchor * m).Basis * v : (_p._anchor.Basis.Inverse() * v);
		}

		private void OnDeath(Particle pt, Transform3D toW)
		{
			FireSubs(2, WorldPos(pt, toW));
		}

		private void FireSubs(int type, Vector3 world)
		{
			if (_s.Mods.Sub is not { } subs)
			{
				return;
			}

			foreach (FxSub sub in subs)
			{
				if (sub.Type == type && sub.Emitter >= 0 && _p._nodes[sub.Emitter].System is { } target
					&& (sub.P >= 1f || _p._random.NextDouble() < sub.P))
				{
					target.EmitSub(world);
				}
			}
		}

		public Vector3 WorldPos(Particle pt, Transform3D toW) => _s.Space == 1 ? pt.Pos : toW * pt.Pos;

		public Vector3 WorldVel(Particle pt, Transform3D toW)
		{
			Vector3 v = pt.Vel + pt.AnimVel;
			return _s.Space == 1 ? v : toW.Basis * v;
		}

		public Vector3 SizeOf(Particle pt)
		{
			float n = pt.Age / pt.Life;
			Vector3 size = pt.Size0;
			if (_s.Mods.Size is { } sm)
			{
				float sx = sm.X.Eval(n, pt.R4);
				size = sm.Sep ? new Vector3(size.X * sx, size.Y * sm.Y.Eval(n, pt.R4), size.Z * sm.Z.Eval(n, pt.R4)) : size * sx;
			}

			if (_s.Mods.SizeSpeed is { } ss && ss.Range.Length >= 2)
			{
				float span = ss.Range[1] - ss.Range[0];
				float k = span > 1e-5f ? Math.Clamp(((pt.Vel + pt.AnimVel).Length() - ss.Range[0]) / span, 0f, 1f) : 0f;
				float sx = ss.X.Eval(k, pt.R3);
				size = ss.Sep ? new Vector3(size.X * sx, size.Y * ss.Y.Eval(k, pt.R3), size.Z * ss.Z.Eval(k, pt.R3)) : size * sx;
			}

			return size;
		}

		public Color ColorOf(Particle pt)
		{
			Color c = pt.Color0;
			if (_s.Mods.Col is { } col)
			{
				c *= col.Eval(pt.Age / pt.Life, pt.R5);
			}

			return c;
		}

		/// <summary>贴图序列帧的 UV 矩形（Unity 约定：v 向上，第 0 帧在左上）。</summary>
		public Rect2 SheetRect(Particle pt)
		{
			if (_s.Mods.Sheet is not { } sh)
			{
				return new Rect2(0, 0, 1, 1);
			}

			int tx = Math.Max(1, sh.Tx), ty = Math.Max(1, sh.Ty);
			int frames = sh.Type == 1 ? tx : tx * ty;
			float n = pt.Age / pt.Life;
			float cyc = Math.Max(1f, sh.Cycles);
			float local = n * cyc % 1f;
			float f;
			if (sh.TimeMode == 2)
			{
				f = pt.Age * sh.Fps + sh.Start.Eval(0, pt.R3) ;
			}
			else
			{
				f = (sh.Frame.Eval(local, pt.R2) + sh.Start.Eval(0, pt.R3) / Math.Max(1, frames)) * frames;
			}

			int frame = Math.Clamp((int)Mathf.PosMod(MathF.Floor(f), frames), 0, frames - 1);
			int col, row;
			if (sh.Type == 1)
			{
				col = frame;
				row = (int)pt.SheetRow;
			}
			else
			{
				col = frame % tx;
				row = frame / tx;
			}

			float w = 1f / tx, h = 1f / ty;
			return new Rect2(col * w, 1f - (row + 1) * h, w, h);
		}
	}

	// ------------------------------------------------------------------------------------------ TrailRenderer

	private sealed class TrailState(int node, FxTrailRenderer spec)
	{
		public readonly int Node = node;
		public readonly FxTrailRenderer Spec = spec;
		public readonly List<(Vector3 Pos, float Time)> Points = [];
		public Layer? Layer;
	}

	private void UpdateTrailRenderer(TrailState tr)
	{
		NodeState n = _nodes[tr.Node];
		float life = Math.Max(1e-3f, tr.Spec.Time);
		tr.Points.RemoveAll(p => _time - p.Time > life);
		if (!n.ActiveInHierarchy || !tr.Spec.Emitting || _stopped || _loopsStopped)
		{
			return;
		}

		Vector3 pos = _anchor * n.World.Origin;
		if (tr.Points.Count == 0 || (tr.Points[^1].Pos - pos).Length() >= Math.Max(tr.Spec.MinDist * 0.5f, 1e-4f))
		{
			tr.Points.Add((pos, _time));
		}
	}

	// ------------------------------------------------------------------------------------------ 绘制

	private sealed class NodeState(FxNodeSpec spec)
	{
		public Vector3 LocalPos = new(spec.Pos[0], spec.Pos[1], spec.Pos[2]);
		public Quaternion LocalRot = new Quaternion(spec.Rot[0], spec.Rot[1], spec.Rot[2], spec.Rot[3]).Normalized();
		public Vector3 LocalScale = new(spec.Scale[0], spec.Scale[1], spec.Scale[2]);
		public bool ActiveSelf = spec.Active;
		public bool ActiveInHierarchy;

		/// <summary>手调隐藏（FxTune.Hide）：连同子节点一直不激活，不受激活动画影响。</summary>
		public bool Hidden;
		public Transform3D World = Transform3D.Identity;
		public Quaternion WorldRot = Quaternion.Identity;
		public bool RendererEnabled = true;
		public readonly Dictionary<string, float> MaterialOverrides = [];
		public PsState? System;
		public Layer? MeshLayer;
		public TrailState? TrailRenderer;
	}

	private sealed class Layer(Node2D node, Node2D? backNode, ShaderMaterial material, Texture2D? texture, FxMaterial mat, int order, int index, int ownerNode)
	{
		public readonly Node2D Node = node;
		public readonly Node2D? BackNode = backNode;
		public readonly ShaderMaterial Material = material;
		public readonly Texture2D? Texture = texture;
		public readonly FxMaterial Mat = mat;
		public readonly int Order = order;
		public readonly int Index = index;
		public readonly int OwnerNode = ownerNode;
		public readonly Geo Front = new();
		public readonly Geo Back = new();
		public readonly Dictionary<string, float> Applied = [];

		public Geo Pick(bool back) => back && BackNode is not null ? Back : Front;

		public void Clear()
		{
			Front.Clear();
			Back.Clear();
		}
	}

	/// <summary>一个画布项要提交的三角形（W 空间坐标，提交时再换成像素）。</summary>
	private sealed class Geo
	{
		public readonly List<Vector2> Points = [];
		public readonly List<Vector2> Uvs = [];
		public readonly List<Color> Colors = [];
		public readonly List<int> Indices = [];

		public void Clear()
		{
			Points.Clear();
			Uvs.Clear();
			Colors.Clear();
			Indices.Clear();
		}

		public void Add(Vector2 p, Vector2 uv, Color c)
		{
			Points.Add(p);
			Uvs.Add(uv);
			Colors.Add(c);
		}

		public void Quad(Vector2 a, Vector2 b, Vector2 c, Vector2 d, Rect2 uv, Color col, bool flipU, bool flipV)
		{
			// a 左下，b 右下，c 右上，d 左上（W 空间，y 向上）。
			float u0 = flipU ? uv.End.X : uv.Position.X, u1 = flipU ? uv.Position.X : uv.End.X;
			float v0 = flipV ? uv.End.Y : uv.Position.Y, v1 = flipV ? uv.Position.Y : uv.End.Y;
			int i = Points.Count;
			Points.Add(a);
			Points.Add(b);
			Points.Add(c);
			Points.Add(d);
			Uvs.Add(new Vector2(u0, v0));
			Uvs.Add(new Vector2(u1, v0));
			Uvs.Add(new Vector2(u1, v1));
			Uvs.Add(new Vector2(u0, v1));
			Colors.Add(col);
			Colors.Add(col);
			Colors.Add(col);
			Colors.Add(col);
			Indices.Add(i);
			Indices.Add(i + 1);
			Indices.Add(i + 2);
			Indices.Add(i);
			Indices.Add(i + 2);
			Indices.Add(i + 3);
		}
	}

	private void Render()
	{
		foreach (Layer layer in _layers)
		{
			layer.Clear();
		}

		foreach (PsState ps in _systems)
		{
			if (!_nodes[ps.Node].ActiveInHierarchy && ps.Particles.Count == 0)
			{
				continue;
			}

			if (ps.Layer is { } layer)
			{
				DrawParticles(ps, layer);
			}

			if (ps.TrailLayer is { } trailLayer)
			{
				DrawRibbon(ps, trailLayer);
			}
		}

		for (int i = 0; i < _nodes.Length; i++)
		{
			NodeState n = _nodes[i];
			if (n.MeshLayer is { } layer && n.ActiveInHierarchy && n.RendererEnabled)
			{
				DrawMesh(i, layer);
			}
		}

		foreach (TrailState tr in _trails)
		{
			if (tr.Layer is { } layer)
			{
				DrawTrailRenderer(tr, layer);
			}
		}

		foreach (Layer layer in _layers)
		{
			ApplyMaterial(layer);
			Submit(layer.Node, layer.Front, layer.Texture);
			if (layer.BackNode is { } back && GodotObject.IsInstanceValid(back))
			{
				Submit(back, layer.Back, layer.Texture);
			}
		}
	}

	private void Submit(Node2D node, Geo geo, Texture2D? texture)
	{
		Rid item = node.GetCanvasItem();
		RenderingServer.CanvasItemClear(item);
		if (geo.Indices.Count == 0)
		{
			return;
		}

		Vector2[] pts = [.. geo.Points.Select(Px2)];
		RenderingServer.CanvasItemAddTriangleArray(item, [.. geo.Indices], pts, [.. geo.Colors], [.. geo.Uvs], texture: texture?.GetRid() ?? default);
	}

	private Vector2 Px2(Vector2 w)
	{
		float k = LocalUnitPx;
		return new Vector2(w.X * k, -w.Y * k);
	}

	private void ApplyMaterial(Layer layer)
	{
		NodeState n = _nodes[layer.OwnerNode];
		if (n.MaterialOverrides.Count == 0)
		{
			return;
		}

		FxMaterial mat = layer.Mat;
		string colorKey = mat.Kind == "disturb" ? "_MainColor" : "_TintColor";
		if (Changed(layer, n, colorKey, out float[] c, 4, mat.Tint, "rgba"))
		{
			layer.Material.SetShaderParameter("tint", new Color(c[0], c[1], c[2], c[3]));
		}

		if (Changed(layer, n, "_MainTex_ST", out float[] st, 4, mat.St, "xyzw"))
		{
			layer.Material.SetShaderParameter("main_st", new Vector4(st[0], st[1], st[2], st[3]));
		}

		string auxKey = mat.Kind == "disturb" ? "_DisturTex_ST" : "_DissolveTex_ST";
		if (Changed(layer, n, auxKey, out float[] ast, 4, mat.AuxSt, "xyzw"))
		{
			layer.Material.SetShaderParameter("aux_st", new Vector4(ast[0], ast[1], ast[2], ast[3]));
		}

		if (n.MaterialOverrides.TryGetValue("_Amount", out float amount) && (!layer.Applied.TryGetValue("_Amount", out float prev) || prev != amount))
		{
			layer.Applied["_Amount"] = amount;
			layer.Material.SetShaderParameter("amount", amount);
		}
	}

	private static bool Changed(Layer layer, NodeState n, string key, out float[] values, int count, float[] defaults, string comps)
	{
		values = new float[count];
		bool any = false, changed = false;
		for (int i = 0; i < count; i++)
		{
			string k = $"{key}.{comps[i]}";
			if (n.MaterialOverrides.TryGetValue(k, out float v))
			{
				any = true;
				values[i] = v;
			}
			else
			{
				values[i] = i < defaults.Length ? defaults[i] : 0f;
			}

			if (!layer.Applied.TryGetValue(k, out float prev) || prev != values[i])
			{
				layer.Applied[k] = values[i];
				changed = true;
			}
		}

		return any && changed;
	}

	private void DrawParticles(PsState ps, Layer layer)
	{
		FxParticleSystem s = ps.Spec;
		FxPsRender ren = s.Render;
		int node = ps.Node;
		Transform3D toW = _anchor * SystemMatrix(node);
		bool mirrored = _anchor.Basis.Determinant() < 0;
		NodeState owner = _nodes[node];
		Vector3 sysScale = (s.Scaling == 0 ? owner.World.Basis.Scale : owner.LocalScale).Abs();
		float viewportUnits = _root.GetViewportRect().Size.Y / LocalUnitPx;
		float maxUnits = ren.MaxSize > 0f ? ren.MaxSize * viewportUnits : float.MaxValue;
		float minUnits = ren.MinSize * viewportUnits;
		IEnumerable<Particle> order = ren.SortMode switch
		{
			2 => ps.Particles.OrderBy(p => p.Id),
			3 => ps.Particles.OrderByDescending(p => p.Id),
			_ => ps.Particles,
		};
		foreach (Particle pt in order)
		{
			Vector3 size = ps.SizeOf(pt) * sysScale;
			// Unity 把粒子限制在视口高度的 MinParticleSize..MaxParticleSize 倍之间（原作压暗屏幕的大底板就靠上限收成半屏）。
			float largest = Math.Max(Math.Abs(size.X), Math.Abs(size.Y));
			if (largest > maxUnits && largest > 0f)
			{
				size *= maxUnits / largest;
			}
			else if (largest < minUnits && largest > 0f)
			{
				size *= minUnits / largest;
			}

			Color col = ps.ColorOf(pt);
			Rect2 uv = ps.SheetRect(pt);
			Vector3 center = ps.WorldPos(pt, toW);
			Vector2 c2 = new(center.X, center.Y);
			Geo geo = layer.Pick(Behind(layer.Order, center.Z));
			if (ren.Mode == 1)
			{
				Vector3 vel = ps.WorldVel(pt, toW);
				Vector2 v2 = new(vel.X, vel.Y);
				float speed = vel.Length();
				Vector2 dir = v2.LengthSquared() > 1e-10f ? v2.Normalized() : Vector2.Right;
				// 拉伸面片：x 是宽、y 乘 lengthScale 是长（原作“细线”就是 x 很小、y 为 1 的分轴大小，反过来会画成一个胖椭圆）。
				float len = size.Y * ren.LengthScale + speed * ren.VelScale;
				Vector2 perp = new Vector2(-dir.Y, dir.X) * size.X * 0.5f;
				Vector2 head = c2 + dir * len * 0.5f, tail = c2 - dir * len * 0.5f;
				// 贴图的 u 沿速度：u = 0 在前端。
				geo.Quad(head - perp, tail - perp, tail + perp, head + perp, uv, col, pt.FlipU, pt.FlipV);
			}
			else if (ren.Mode == 4 && ren.Mesh is { } mesh)
			{
				DrawMeshParticle(geo, mesh, toW, center, size, pt, col);
			}
			else
			{
				Vector2 half = new(size.X * 0.5f, (s.Size3d ? size.Y : size.X) * 0.5f);
				Vector2 pivot = ren.Pivot.Length >= 2 ? new Vector2(ren.Pivot[0] * size.X, ren.Pivot[1] * size.Y) : Vector2.Zero;
				if (ren.Align == 2)
				{
					// 本地对齐：面片落在粒子系统节点的 xy 平面上，随节点朝向透视缩短。
					Basis b = toW.Basis.Orthonormalized();
					float ang = -pt.Rot.Z;
					Vector3 ax = b * new Vector3(Mathf.Cos(ang), Mathf.Sin(ang), 0);
					Vector3 ay = b * new Vector3(-Mathf.Sin(ang), Mathf.Cos(ang), 0);
					Vector2 px = new Vector2(ax.X, ax.Y) * half.X, py = new Vector2(ay.X, ay.Y) * half.Y;
					Vector2 cc = c2 + new Vector2(ax.X, ax.Y) * pivot.X + new Vector2(ay.X, ay.Y) * pivot.Y;
					geo.Quad(cc - px - py, cc + px - py, cc + px + py, cc - px + py, uv, col, pt.FlipU, pt.FlipV);
				}
				else
				{
					// 面向镜头：原作的旋转正方向在屏幕上是顺时针；镜像特效里方向随之翻转。
					float ang = -pt.Rot.Z * (mirrored ? -1f : 1f);
					Vector2 ax = new(Mathf.Cos(ang), Mathf.Sin(ang));
					Vector2 ay = new(-ax.Y, ax.X);
					if (mirrored)
					{
						ax = -ax;
					}

					Vector2 cc = c2 + ax * pivot.X + ay * pivot.Y;
					Vector2 px = ax * half.X, py = ay * half.Y;
					geo.Quad(cc - px - py, cc + px - py, cc + px + py, cc - px + py, uv, col, pt.FlipU, pt.FlipV);
				}
			}
		}
	}

	private static void DrawMeshParticle(Geo layer, FxMesh mesh, Transform3D toW, Vector3 center, Vector3 size, Particle pt, Color col)
	{
		Basis rot = new Basis(UnityEuler(Mathf.RadToDeg(pt.Rot.X), Mathf.RadToDeg(pt.Rot.Y), Mathf.RadToDeg(pt.Rot.Z)));
		Basis b = toW.Basis.Orthonormalized() * rot * Basis.FromScale(size);
		int start = layer.Points.Count;
		for (int i = 0; i < mesh.V.Length; i++)
		{
			float[] v = mesh.V[i];
			Vector3 w = center + b * new Vector3(v[0], v[1], v[2]);
			layer.Points.Add(new Vector2(w.X, w.Y));
			layer.Uvs.Add(i < mesh.Uv.Length ? new Vector2(mesh.Uv[i][0], mesh.Uv[i][1]) : Vector2.Zero);
			layer.Colors.Add(mesh.C is { } mc && i < mc.Length ? col * new Color(mc[i][0], mc[i][1], mc[i][2], mc[i][3]) : col);
		}

		foreach (int idx in mesh.I)
		{
			layer.Indices.Add(start + idx);
		}
	}

	private void DrawMesh(int node, Layer layer)
	{
		FxMesh mesh = _spec.Nodes[node].Mesh!.Mesh!;
		Transform3D toW = _anchor * _nodes[node].World;
		bool split = layer.BackNode is not null;
		float[] depth = new float[mesh.V.Length];
		int startF = layer.Front.Points.Count, startB = layer.Back.Points.Count;
		for (int i = 0; i < mesh.V.Length; i++)
		{
			float[] v = mesh.V[i];
			Vector3 w = toW * new Vector3(v[0], v[1], v[2]);
			depth[i] = w.Z;
			Vector2 p = new(w.X, w.Y);
			Vector2 uv = i < mesh.Uv.Length ? new Vector2(mesh.Uv[i][0], mesh.Uv[i][1]) : Vector2.Zero;
			Color c = mesh.C is { } mc && i < mc.Length ? new Color(mc[i][0], mc[i][1], mc[i][2], mc[i][3]) : Colors.White;
			layer.Front.Add(p, uv, c);
			if (split)
			{
				layer.Back.Add(p, uv, c);
			}
		}

		// 按三角形的纵深分前后（贴地的大圆盘、剑气弧一部分在立绘后面）。
		for (int t = 0; t + 2 < mesh.I.Length; t += 3)
		{
			int a = mesh.I[t], b = mesh.I[t + 1], c = mesh.I[t + 2];
			bool back = split && Behind(layer.Order, (depth[a] + depth[b] + depth[c]) / 3f);
			List<int> idx = back ? layer.Back.Indices : layer.Front.Indices;
			int start = back ? startB : startF;
			idx.Add(start + a);
			idx.Add(start + b);
			idx.Add(start + c);
		}
	}

	private void DrawRibbon(PsState ps, Layer layer)
	{
		FxParticleSystem s = ps.Spec;
		FxTrailMod tm = s.Mods.Trail!;
		if (ps.Particles.Count < 2)
		{
			return;
		}

		Transform3D toW = _anchor * SystemMatrix(ps.Node);
		// 原作带状拖尾从最老的粒子算起：宽度曲线的 0 端在末梢、1 端在发射点（黑蛇的触手根部粗、末梢细，与原作录像一致）。
		List<Particle> ordered = [.. ps.Particles.OrderBy(p => p.Id)];
		int count = ordered.Count;
		Vector2[] pts = new Vector2[count];
		float[] depths = new float[count];
		float[] widths = new float[count];
		Color[] cols = new Color[count];
		float alphaOv = ps.Overrides.TryGetValue("TrailModule.colorOverLifetime.maxColor.a", out float a) ? a : -1f;
		for (int i = 0; i < count; i++)
		{
			Particle pt = ordered[i];
			Vector3 w = ps.WorldPos(pt, toW);
			pts[i] = new Vector2(w.X, w.Y);
			depths[i] = w.Z;
			float u = count == 1 ? 0f : (float)i / (count - 1);
			float su = Tune.TrailFrom + u * (Tune.TrailTo - Tune.TrailFrom);
			float width = tm.Width.Eval(su, 0.5f) * (tm.SizeWidth ? ps.SizeOf(pt).X : 1f) * Tune.TrailWidth;
			widths[i] = width;
			Color c = tm.ColTrail.Eval(su, 0.5f) * tm.ColLife.Eval(pt.Age / pt.Life, pt.R1);
			if (alphaOv >= 0f)
			{
				c.A *= alphaOv;
			}

			if (tm.Inherit)
			{
				c *= ps.ColorOf(pt);
			}

			cols[i] = c;
		}

		Strip(layer, pts, depths, widths, cols);
	}

	private void DrawTrailRenderer(TrailState tr, Layer layer)
	{
		int count = tr.Points.Count;
		if (count < 2)
		{
			return;
		}

		Vector2[] pts = new Vector2[count];
		float[] depths = new float[count];
		float[] widths = new float[count];
		Color[] cols = new Color[count];
		for (int i = 0; i < count; i++)
		{
			// u：0 在头（最新点），1 在尾。
			int src = count - 1 - i;
			Vector3 w = tr.Points[src].Pos;
			pts[i] = new Vector2(w.X, w.Y);
			depths[i] = w.Z;
			float u = (float)i / (count - 1);
			widths[i] = FxCurve.Sample(tr.Spec.Width, u) * tr.Spec.WidthMul;
			cols[i] = FxGradient.Sample(tr.Spec.Grad, u);
		}

		Strip(layer, pts, depths, widths, cols);
	}

	/// <summary>带状拖尾：每一段按中点纵深分到前层或后层（触手从立绘背后伸出，根部被她挡住）。</summary>
	private void Strip(Layer layer, Vector2[] pts, float[] depths, float[] widths, Color[] cols)
	{
		int count = pts.Length;
		bool split = layer.BackNode is not null;
		int startF = layer.Front.Points.Count, startB = layer.Back.Points.Count;
		for (int i = 0; i < count; i++)
		{
			Vector2 prev = pts[Math.Max(0, i - 1)], next = pts[Math.Min(count - 1, i + 1)];
			Vector2 t = next - prev;
			t = t.LengthSquared() > 1e-12f ? t.Normalized() : Vector2.Right;
			Vector2 nrm = new Vector2(-t.Y, t.X) * widths[i] * 0.5f;
			float u = count == 1 ? 0f : (float)i / (count - 1);
			layer.Front.Add(pts[i] + nrm, new Vector2(u, 1f), cols[i]);
			layer.Front.Add(pts[i] - nrm, new Vector2(u, 0f), cols[i]);
			if (split)
			{
				layer.Back.Add(pts[i] + nrm, new Vector2(u, 1f), cols[i]);
				layer.Back.Add(pts[i] - nrm, new Vector2(u, 0f), cols[i]);
			}
		}

		for (int i = 0; i < count - 1; i++)
		{
			bool back = split && Behind(layer.Order, (depths[i] + depths[i + 1]) * 0.5f);
			List<int> idx = back ? layer.Back.Indices : layer.Front.Indices;
			int a = (back ? startB : startF) + i * 2;
			idx.Add(a);
			idx.Add(a + 1);
			idx.Add(a + 3);
			idx.Add(a);
			idx.Add(a + 3);
			idx.Add(a + 2);
		}
	}
}
