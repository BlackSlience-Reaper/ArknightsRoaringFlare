using System.Reflection;
using ArknightsRoaringFlare.Acts;
using HarmonyLib;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;

namespace ArknightsRoaringFlare.Integration;

/// <summary>
/// ActLikeIt2（创意工坊 3808369281）是可选前置：装了它，本幕登记为第三幕开始前选幕界面的候选，与荣耀并列由玩家选择或联机投票；
/// 没装时本幕照旧经 RitsuLib 进原版随机幕列表，与荣耀随机出现（见 <see cref="RoaringFlareAct.AllowInRandomActList"/>）。
///
/// 全程反射、不在编译期引用 ActLikeIt2.dll：没装它的玩家加载本模组时不会碰到它的类型，构建也不需要它。
/// ActLikeIt2 的 fork 版用加载器按游戏版本载入实现程序集，程序集名不固定，所以按类型全名在已加载的程序集里找。
///
/// 登记放在 ModelDb.Init 之后：模组初始化时 ActLikeIt2 可能还没加载（清单里不声明依赖就没有加载顺序保证），
/// 而 ModelDb.Init 晚于全部模组初始化。用的是传入已解析幕模型的 Register(ActRegistration)，ActLikeIt2 收到即生效，
/// 选幕时按幕号实时查询，不依赖它自己的初始化时机。
/// </summary>
internal static class ActLikeIt2Bridge
{
	private const string RegistryTypeName = "ActLikeIt2.ActRegistry";
	private const string RegistrationTypeName = "ActLikeIt2.ActRegistration";

	private static Type? _registryType;
	private static bool _modelDbInitialized;
	private static bool _absentAfterInit;

	/// <summary>ActLikeIt2 已加载。全部模组初始化之后才准确，在那之前它可能还没轮到加载。</summary>
	public static bool IsLoaded => RegistryType != null;

	/// <summary>本幕在 ActLikeIt2 里登记的幕号（从 1 起算）。</summary>
	public static int ActNumber => RoaringFlareAct.ActIndex + 1;

	private static Type? RegistryType
	{
		get
		{
			// 找到就缓存；ModelDb.Init 之前没找到不下结论（可能只是还没加载），之后仍没有才认定没装、不再找。
			if (_registryType == null && !_absentAfterInit)
			{
				_registryType = FindType(RegistryTypeName);
				_absentAfterInit = _registryType == null && _modelDbInitialized;
			}

			return _registryType;
		}
	}

	public static void Install(Harmony harmony)
	{
		harmony.Patch(
			AccessTools.Method(typeof(ModelDb), nameof(ModelDb.Init)),
			postfix: new HarmonyMethod(typeof(ActLikeIt2Bridge), nameof(AfterModelDbInit)) { priority = Priority.High });
	}

	private static void AfterModelDbInit()
	{
		_modelDbInitialized = true;
		if (RegistryType is not { } registry)
		{
			Log.Info($"[{ModEntry.ModId}] ActLikeIt2 not loaded; act {ActNumber} stays in the vanilla random act list.");
			return;
		}

		try
		{
			Register(registry);
			Log.Info($"[{ModEntry.ModId}] Registered {nameof(RoaringFlareAct)} as an act {ActNumber} choice through ActLikeIt2.");
		}
		catch (Exception ex)
		{
			// ActLikeIt2 改了接口时只丢掉选幕入口；它自己的随机列表补丁仍会把本幕挡在外面，荣耀照常出现。
			Log.Error($"[{ModEntry.ModId}] Could not register with ActLikeIt2: {ex}");
		}
	}

	private static void Register(Type registry)
	{
		Type registrationType = registry.Assembly.GetType(RegistrationTypeName)
			?? throw new MissingMemberException(RegistrationTypeName);
		object registration = Activator.CreateInstance(registrationType)!;
		Set(registrationType, registration, "CanonicalAct", ModelDb.Act<RoaringFlareAct>());
		Set(registrationType, registration, "ActNumber", ActNumber);
		Set(registrationType, registration, "OptionDescription", new LocString("acts", "ARKNIGHTS_ROARING_FLARE_ACT_ROARING_FLARE_ACT.description"));
		MethodInfo register = registry.GetMethod("Register", BindingFlags.Public | BindingFlags.Static, [registrationType])
			?? throw new MissingMethodException(RegistryTypeName, "Register(ActRegistration)");
		register.Invoke(null, [registration]);
	}

	/// <summary>自检用：ActLikeIt2 第三幕候选里本幕的选项说明；没装或没登记时为 null。</summary>
	internal static string? RegisteredOptionDescription()
	{
		if (RegistryType is not { } registry)
		{
			return null;
		}

		MethodInfo query = registry.GetMethod("GetRegistrationsForSlot", BindingFlags.Public | BindingFlags.Static, [typeof(int)])
			?? throw new MissingMethodException(RegistryTypeName, "GetRegistrationsForSlot(int)");
		ActModel canonical = ModelDb.Act<RoaringFlareAct>();
		foreach (object registration in (System.Collections.IEnumerable)query.Invoke(null, [ActNumber])!)
		{
			Type type = registration.GetType();
			if (type.GetProperty("CanonicalAct")?.GetValue(registration) == canonical)
			{
				return (type.GetProperty("OptionDescription")?.GetValue(registration) as LocString)?.GetFormattedText() ?? "";
			}
		}

		return null;
	}

	private static void Set(Type type, object target, string property, object value)
	{
		PropertyInfo info = type.GetProperty(property) ?? throw new MissingMemberException(type.FullName, property);
		info.SetValue(target, value);
	}

	private static Type? FindType(string fullName)
	{
		foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
		{
			if (assembly.IsDynamic)
			{
				continue;
			}

			Type? type = assembly.GetType(fullName, throwOnError: false);
			if (type != null)
			{
				return type;
			}
		}

		return null;
	}
}
