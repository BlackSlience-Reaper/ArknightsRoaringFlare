using ArknightsRoaringFlare.Encounters;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Audio;
using STS2RitsuLib;
using STS2RitsuLib.Audio;

namespace ArknightsRoaringFlare.Audio;

/// <summary>
/// 本幕的战斗配乐（塞壬唱片）：
/// - 塔露拉 Boss 战：《交锋》（黑蛇第一条命），黑蛇第一次被击败时淡出，不息恶火重生时换《塔露拉·雅特利亚斯》，胜利后淡出。
/// 精英战（“皇帝的利刃”）不换曲，照原版精英战用本幕音乐（第一版放过「乌萨斯的影月」，用户要求去掉）。
/// 写法由苦难摇篮的 ReunionBossMusic 复制（那里有完整的来龙去脉）：
/// - 原版 EncounterModel.CustomBgm 只接受 FMOD Studio 事件，所以在 RitsuLib 的战斗开始事件里接管：只对音乐代理调 stop_music（环境音不动），
///   经 RitsuLib 音频服务流式播放模组里的原文件。胜利后只淡出，不恢复本幕音乐（照原版 Boss），之后由原版正常起曲。
/// - 不能用 NRunMusicController.StopMusic：它连环境音一起拆掉，之后进火堆会闪退（RPEFinale 的事故）；它被原版调用时（阵亡、退出）这里跟着停。
/// - 流式文件不走 bus:/master/music，每帧把音乐总线音量乘进句柄音量，跟随游戏的音乐滑条。
/// - 必须用 StreamingMusic 直接交 res:// 路径；StreamingResourceMusic 会把文件复制到带用户名的缓存目录，中文用户名下 FMOD 打不开并闪退。
/// - 文件以原文件打进包（assets/audio/music/*.import 为 keep）；句柄挂在房间作用域，离开房间时 RitsuLib 必定停掉。
/// </summary>
internal static class FlareMusic
{
	public const string TalulahTrackPath = RoaringFlareAssets.MusicRoot + "talulah_boss.mp3";
	public const string TalulahPhaseTwoTrackPath = RoaringFlareAssets.MusicRoot + "talulah_boss_phase2.mp3";

	// 总线之前的倍率：对齐原版 Boss 音乐的高潮段电平（3 秒窗口 RMS 的 90 分位，原版 -14.0 dBFS），
	// 原件分别是 -10.4 / -10.6 dBFS（tools/measure_music_level.py）。实际音量还要乘音乐总线音量。
	private static float VolumeOf(string trackPath) => trackPath switch
	{
		TalulahPhaseTwoTrackPath => 0.68f,
		_ => 0.66f,
	};

	private const double RebirthFadeSeconds = 2.0;
	private const double VictoryFadeSeconds = 3.0;
	private const double SwitchFadeSeconds = 0.5;

	private sealed class Track(AudioMusicHandle handle, float volume)
	{
		public AudioMusicHandle Handle { get; } = handle;
		public float Volume { get; } = volume;
		public double FadeSeconds { get; private set; }
		public double FadeElapsed { get; set; }
		public bool IsFading => FadeSeconds > 0;

		public void BeginFade(double seconds)
		{
			if (!IsFading)
			{
				FadeSeconds = seconds;
			}
		}

		public float Gain => IsFading ? (float)Math.Cos(Math.Min(1.0, FadeElapsed / FadeSeconds) * Math.PI / 2) : 1f;

		public void Stop()
		{
			Handle.TryStop(allowFadeOut: false);
			Handle.Dispose();
		}
	}

	private static readonly List<Track> Tracks = [];
	private static Track? _current;
	private static ulong _lastTickUsec;
	private static bool _ticking;
	private static bool _active;

	public static void Install(Harmony harmony)
	{
		RitsuLibFramework.SubscribeLifecycle<CombatStartingEvent>(OnCombatStarting, replayCurrentState: false);
		RitsuLibFramework.SubscribeLifecycle<CombatEndedEvent>(OnCombatEnded, replayCurrentState: false);
		harmony.Patch(
			AccessTools.Method(typeof(NRunMusicController), nameof(NRunMusicController.StopMusic)),
			postfix: new HarmonyMethod(typeof(FlareMusic), nameof(StopAll)));
	}

	/// <summary>黑蛇第一次被击败：淡出后静音，等不息恶火重生。</summary>
	public static void FadeOutForRebirth()
	{
		if (_active)
		{
			FadeCurrent(RebirthFadeSeconds);
		}
	}

	/// <summary>不息恶火重生：从头播放《塔露拉·雅特利亚斯》。</summary>
	public static void PlayPhaseTwo()
	{
		if (_active)
		{
			Play(TalulahPhaseTwoTrackPath);
		}
	}

	/// <summary>这场战斗的开场曲目；不是本模组接管的战斗时为 null。</summary>
	private static string? OpeningTrack(EncounterModel? encounter)
	{
		EncounterModel? canonical = encounter?.CanonicalInstance ?? encounter;
		return canonical switch
		{
			TalulahBoss => TalulahTrackPath,
			_ => null,
		};
	}

	private static string? LiveTrack(ICombatState? combatState)
	{
		return combatState is CombatState state && state.IsLiveCombat() ? OpeningTrack(state.Encounter) : null;
	}

	private static void OnCombatStarting(CombatStartingEvent e)
	{
		if (LiveTrack(e.CombatState) is not { } track)
		{
			return;
		}

		_active = true;
		// 与原版 PlayCustomMusic 相同，只对音乐代理（场景里的 Proxy 节点）调 stop_music，环境音继续播放。
		NRunMusicController.Instance?.GetNodeOrNull<Node>("Proxy")?.Call("stop_music");
		Play(track);
	}

	private static void OnCombatEnded(CombatEndedEvent e)
	{
		if (!_active || LiveTrack(e.CombatState) == null)
		{
			return;
		}

		_active = false;
		FadeCurrent(VictoryFadeSeconds);
	}

	private static void StopAll()
	{
		_active = false;
		_current = null;
		foreach (Track track in Tracks)
		{
			track.Stop();
		}

		Tracks.Clear();
		SetTicking(false);
	}

	private static void FadeCurrent(double seconds)
	{
		_current?.BeginFade(seconds);
		_current = null;
	}

	private static void Play(string trackPath)
	{
		FadeCurrent(SwitchFadeSeconds);
		float volume = VolumeOf(trackPath);
		AudioMusicHandle? handle = GameAudioService.Shared.PlayMusic(
			AudioSource.StreamingMusic(trackPath),
			new AudioPlaybackOptions { Volume = volume * MusicBusVolume(), Scope = AudioLifecycleScope.Room });
		if (handle == null)
		{
			Log.Warn($"[{ModEntry.ModId}] Could not start music {trackPath}");
			return;
		}

		_current = new Track(handle, volume);
		Tracks.Add(_current);
		SetTicking(true);
	}

	private static float MusicBusVolume()
	{
		// 找不到总线（音频库没加载）时按 1 处理；RitsuLib 的 TryGetVolume 在这种情况下返回 0，直接用会把音乐静音。
		return FmodStudioBusAccess.TryGetBus(FmodStudioRouting.MusicBus) == null
			? 1f
			: Math.Max(0f, FmodStudioBusAccess.TryGetVolume(FmodStudioRouting.MusicBus));
	}

	private static void SetTicking(bool on)
	{
		if (on == _ticking || Engine.GetMainLoop() is not SceneTree tree)
		{
			return;
		}

		_ticking = on;
		_lastTickUsec = Time.GetTicksUsec();
		if (on)
		{
			tree.ProcessFrame += Tick;
		}
		else
		{
			tree.ProcessFrame -= Tick;
		}
	}

	private static void Tick()
	{
		ulong now = Time.GetTicksUsec();
		double delta = (now - _lastTickUsec) / 1_000_000.0;
		_lastTickUsec = now;
		float bus = MusicBusVolume();
		for (int i = Tracks.Count - 1; i >= 0; i--)
		{
			Track track = Tracks[i];
			if (track.Handle.IsReleased)
			{
				Tracks.RemoveAt(i);
				continue;
			}

			if (track.IsFading)
			{
				track.FadeElapsed += delta;
				if (track.FadeElapsed >= track.FadeSeconds)
				{
					track.Stop();
					Tracks.RemoveAt(i);
					continue;
				}
			}

			track.Handle.TrySetVolume(track.Volume * track.Gain * bus);
		}

		if (_current is { } current && !Tracks.Contains(current))
		{
			_current = null;
		}

		if (Tracks.Count == 0)
		{
			SetTicking(false);
		}
	}
}
