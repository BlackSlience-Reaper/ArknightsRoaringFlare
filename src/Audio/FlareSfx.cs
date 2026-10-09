using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Audio.Debug;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.TestSupport;

namespace ArknightsRoaringFlare.Audio;

/// <summary>
/// 明日方舟原作的战斗音效（assets/audio/sfx/*.ogg，由 tools/ak_audio/ 从官方资源包解出、按原版音效响度对齐）。
/// 原版音效走 FMOD 事件，模组加不了 FMOD 事件，这里照原版 NDebugAudioManager 播临时音效的做法：Godot 的 AudioStreamPlayer
/// 挂在 SFX 总线上（游戏把音效音量同步到这条总线），播完自己释放。只是表现，联机各端各播各的，出错只记日志、不往外抛。
/// 片段名就是原作的 AudioClip 名；哪个怪用哪个照游戏数据 audio_data.json 的 soundFXBanks，见各怪物的 <c>SoundFor</c>。
/// </summary>
internal static class FlareSfx
{
	public const string Root = RoaringFlareAssets.Root + "audio/sfx/";

	private static readonly StringName SfxBus = new("SFX");

	private static readonly HashSet<string> MissingReported = [];

	/// <summary>
	/// 本模组怪物不用原版的音效：怪物被玩家打中时原版按 TakeDamageSfxType 播 FMOD 的敌人受击声（SfxCmd.PlayDamage），
	/// 这里对本模组的怪物跳过。原作没有“敌人受击”这类音效（命中声都挂在出手方身上），玩家自己出牌的攻击声照常。
	/// TakeDamageSfx 不能直接设成空串：原版把路径原样交给 FMOD 节点，空路径会每次命中都报错。
	/// </summary>
	public static void Install(Harmony harmony)
	{
		harmony.Patch(
			AccessTools.Method(typeof(SfxCmd), nameof(SfxCmd.PlayDamage)),
			prefix: new HarmonyMethod(typeof(FlareSfx), nameof(SkipVanillaImpact)));
	}

	private static bool SkipVanillaImpact(MonsterModel? monster) => monster is not Monsters.FlareMonster;

	/// <summary>立刻播放一个片段；<paramref name="clip"/> 为空时什么也不做。</summary>
	public static void Play(string? clip, float volume = 1f)
	{
		if (string.IsNullOrEmpty(clip) || TestMode.IsOn)
		{
			return;
		}

		try
		{
			if (Host() is not { } host)
			{
				return;
			}

			string path = Root + clip + ".ogg";
			if (ResourceLoader.Load<AudioStream>(path) is not { } stream)
			{
				if (MissingReported.Add(path))
				{
					Log.Warn($"[{ModEntry.ModId}] Missing sound {path}");
				}

				return;
			}

			// 用 Autoplay 在进树时开播：AddChildSafely 不在主线程或宿主没就绪时会延迟到下一帧才加进树，
			// 那时直接 Play() 会因为还不在树里而静默失败。
			AudioStreamPlayer player = new() { Stream = stream, Bus = SfxBus, VolumeLinear = volume, Autoplay = true };
			player.Finished += player.QueueFree;
			host.AddChildSafely(player);
		}
		catch (Exception ex)
		{
			Log.Warn($"[{ModEntry.ModId}] Could not play sound {clip}: {ex.Message}");
		}
	}

	/// <summary>按延迟（秒）依次播放，不阻塞招式（照原作 Spine 动画里音效事件的时间点）。</summary>
	public static void PlaySequence(params (string Clip, float Delay)[] cues)
	{
		if (TestMode.IsOn)
		{
			return;
		}

		// 计时器要在主线程上建。
		if (!NGame.IsMainThread())
		{
			Callable.From(() => PlaySequence(cues)).CallDeferred();
			return;
		}

		if (Host() is not { } host || !host.IsInsideTree())
		{
			return;
		}

		foreach ((string clip, float delay) in cues)
		{
			if (delay <= 0f)
			{
				Play(clip);
				continue;
			}

			string queued = clip;
			host.GetTree().CreateTimer(delay).Timeout += () => Play(queued);
		}
	}

	private static Node? Host() => (Node?)NDebugAudioManager.Instance ?? NGame.Instance;
}
