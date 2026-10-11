using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Xunit;

namespace DTech.OmniDebugger.SourceGenerator.Tests
{
	public sealed class CommandGeneratorTests
	{
		private const string Wallet = @"
using DTech.OmniDebugger;

namespace Game
{
	public enum Currency { Gold = 1, Gems = 2 }

	public class Wallet
	{
		public int Coins;
		public Currency LastCurrency;
		public string LastNote;
		public float LastScale;
		public int? LastBonus;

		[DebugCommand(""Economy/Coins""), DebugIcon(""coin""), DebugTags(""money"", ""cash"")]
		public void AddCoins([DebugRange(0, 10000)] int amount = 100) => Coins += amount;

		[DebugCommand(""Economy / Coins "", Name = ""Balance"", Order = 5, Description = ""How many"")]
		public int Balance => Coins;

		[DebugCommand(""World/Time""), DebugRange(0, 4, Step = 0.25)]
		public float TimeScale { get; set; } = 1f;

		[DebugCommand(""Economy"")]
		internal void Spend(Currency currency, string note = ""none"", float scale = 1.5f, int? bonus = null)
		{
			LastCurrency = currency;
			LastNote = note;
			LastScale = scale;
			LastBonus = bonus;
		}

		[DebugCommand(""Economy"")]
		public int PrivateSet { get; private set; }

		[DebugCommand(""Economy"")]
		public int InitOnly { get; init; }

		public class Nested
		{
			[DebugCommand(""Nested"")]
			public void @class() { }
		}
	}

	public class RichWallet : Wallet
	{
		[DebugCommand(""Economy/Rich"")]
		public void Double() => Coins *= 2;
	}
}";

		private const string Levels = @"
using System;
using System.Collections.Generic;
using DTech.OmniDebugger;

namespace Game
{
	public class Levels
	{
		public static string[] StaticIds = { ""s1"", ""s2"" };

		public List<string> Ids = new List<string> { ""a"", ""b"" };
		public Func<IEnumerable<int>> Numbers;
		public string LastId;

		public Func<IReadOnlyList<string>> NamesSource { get; set; } = () => new[] { ""x"" };

		[DebugCommand(""Levels"")]
		public void Load([DebugOptions(nameof(Ids))] string id, int times = 1) => LastId = id;

		[DebugCommand(""Levels""), DebugOptions(nameof(StaticIds))]
		public string Current { get; set; }

		[DebugCommand(""Levels"")]
		public void Pick([DebugOptions(nameof(Numbers))] int number) { }

		[DebugCommand(""Levels"")]
		public void Rename([DebugOptions(nameof(NamesSource))] string name) { }

		[DebugCommand(""Levels"")]
		public void Inspect([DebugOptions(nameof(ReadIds))] object id) { }

		[DebugCommand(""Levels"")]
		public void Scale([DebugOptions(nameof(Numbers))] double? value) { }

		public IEnumerable<string> ReadIds() => Ids;
	}
}";

		[Fact]
		public void GeneratedCode_CompilesWithoutErrors()
		{
			GeneratorResult result = GeneratorHarness.Run(Wallet);

			Assert.Empty(result.GeneratorDiagnostics);
			Assert.Empty(result.CompileErrors);
			Assert.Contains("CommandBinders.Add(typeof(global::Game.Wallet)", result.Generated);
			Assert.Contains("CommandBinders.Add(typeof(global::Game.Wallet.Nested)", result.Generated);

			string version = typeof(CommandGenerator).Assembly.GetName().Version.ToString(3);
			Assert.Contains($"GeneratedCode(\"DTech.OmniDebugger.SourceGenerator\", \"{version}\")", result.Generated);
		}

		[Fact]
		public void Binder_DescribesEveryMember()
		{
			Assembly assembly = GeneratorHarness.Run(Wallet).Load();
			object wallet = Activator.CreateInstance(assembly.GetType("Game.Wallet"));

			Dictionary<string, dynamic> commands = Collect(assembly, wallet)
				.ToDictionary(command => (string)Definition(command).Path, command => command);

			Assert.Equal(
				new[] { "Economy/Coins/AddCoins", "Economy/Coins/Balance", "Economy/InitOnly", "Economy/PrivateSet", "Economy/Spend", "World/Time/TimeScale" }
					.OrderBy(path => path, StringComparer.Ordinal),
				commands.Keys.OrderBy(path => path, StringComparer.Ordinal));

			dynamic add = Definition(commands["Economy/Coins/AddCoins"]);
			Assert.Equal("Action", add.Kind.ToString());
			Assert.Equal("coin", (string)add.IconKey);
			Assert.Equal(new[] { "money", "cash" }, ((IEnumerable<string>)add.Tags).ToArray());
			dynamic amount = add.Arguments[0];
			Assert.Equal(typeof(int), (Type)amount.Type);
			Assert.True((bool)amount.IsOptional);
			Assert.Equal(100, (int)amount.DefaultValue);
			Assert.Equal(10000.0, (double)amount.Range.Max);

			dynamic balance = Definition(commands["Economy/Coins/Balance"]);
			Assert.Equal("ReadonlyValue", balance.Kind.ToString());
			Assert.Equal(5, (int)balance.SortOrder);
			Assert.Equal("How many", (string)balance.Description);

			dynamic scale = Definition(commands["World/Time/TimeScale"]);
			Assert.Equal("Value", scale.Kind.ToString());
			Assert.Equal(0.25, (double)scale.Arguments[0].Range.Step);

			Assert.Equal("ReadonlyValue", Definition(commands["Economy/PrivateSet"]).Kind.ToString());
			Assert.Equal("ReadonlyValue", Definition(commands["Economy/InitOnly"]).Kind.ToString());

			dynamic spend = Definition(commands["Economy/Spend"]);
			Assert.Equal(1.5f, (float)spend.Arguments[2].DefaultValue);
			Assert.Null((object)spend.Arguments[3].DefaultValue);
			Assert.Equal("none", (string)spend.Arguments[1].DefaultValue);
		}

		[Fact]
		public void Binder_RunsAndReadsTheInstanceItWasGiven()
		{
			Assembly assembly = GeneratorHarness.Run(Wallet).Load();
			Type walletType = assembly.GetType("Game.Wallet");
			object wallet = Activator.CreateInstance(walletType);
			Dictionary<string, object> commands = Collect(assembly, wallet)
				.ToDictionary(command => (string)Definition(command).Path, command => command);

			Execute(commands["Economy/Coins/AddCoins"], 250);
			Assert.Equal(250, (int)walletType.GetField("Coins").GetValue(wallet));
			Assert.Equal(250, (int)GetValue(commands["Economy/Coins/Balance"]));

			Execute(commands["World/Time/TimeScale"], 2f);
			Assert.Equal(2f, (float)GetValue(commands["World/Time/TimeScale"]));

			Type currency = assembly.GetType("Game.Currency");
			Execute(commands["Economy/Spend"], Enum.ToObject(currency, 2), "note", 3f, 7);
			Assert.Equal("Gems", walletType.GetField("LastCurrency").GetValue(wallet).ToString());
			Assert.Equal(7, (int?)walletType.GetField("LastBonus").GetValue(wallet));
		}

		[Fact]
		public void DerivedType_GetsItsOwnCommandsAndItsBaseTypes()
		{
			Assembly assembly = GeneratorHarness.Run(Wallet).Load();
			object rich = Activator.CreateInstance(assembly.GetType("Game.RichWallet"));

			string[] paths = Collect(assembly, rich).Select(command => (string)Definition(command).Path).ToArray();

			Assert.Contains("Economy/Rich/Double", paths);
			Assert.Contains("Economy/Coins/AddCoins", paths);
		}

		[Theory]
		[InlineData("public static void Run() { }", "OMNI001")]
		[InlineData("private void Run() { }", "OMNI002")]
		[InlineData("protected void Run() { }", "OMNI002")]
		[InlineData("public int Run() => 1;", "OMNI003")]
		[InlineData("public void Run<T>() { }", "OMNI004")]
		[InlineData("public void Run(ref int value) { }", "OMNI005")]
		[InlineData("public void Run(out int value) { value = 0; }", "OMNI005")]
		[InlineData("public int Value { private get; set; }", "OMNI006")]
		[InlineData("public int this[int index] => index;", "OMNI007")]
		public void InvalidMember_IsACompileError(string member, string id)
		{
			GeneratorResult result = GeneratorHarness.Run($@"
using DTech.OmniDebugger;
public class Holder
{{
	[DebugCommand(""G"")]
	{member}
}}");

			Assert.Equal(new[] { id }, result.GeneratorIds);
			Assert.Empty(result.CompileErrors);
		}

		[Theory]
		[InlineData("public class Outer { private class Holder { [DebugCommand(\"G\")] public void Run() { } } }", "OMNI008")]
		[InlineData("public class Holder<T> { [DebugCommand(\"G\")] public void Run() { } }", "OMNI009")]
		[InlineData("public struct Holder { [DebugCommand(\"G\")] public void Run() { } }", "OMNI010")]
		[InlineData("public class Holder { [DebugCommand(\"G\"), DebugRange(5, 1)] public int Value { get; set; } }", "OMNI011")]
		[InlineData("public class Holder { [DebugCommand(\"G\")] public void Run([DebugRange(0, 1, Step = -1)] float value) { } }", "OMNI011")]
		[InlineData("public class Holder { [DebugCommand(\" / \")] public void Run() { } }", "OMNI012")]
		[InlineData("public class Holder { [DebugCommand(\"G\", Name = \"a/b\")] public void Run() { } }", "OMNI013")]
		public void InvalidType_IsACompileError(string source, string id)
		{
			GeneratorResult result = GeneratorHarness.Run("using DTech.OmniDebugger;\n" + source);

			Assert.Equal(new[] { id }, result.GeneratorIds);
		}

		[Fact]
		public void Options_AreGeneratedForEverySourceKind()
		{
			GeneratorResult result = GeneratorHarness.Run(Levels);

			Assert.Empty(result.GeneratorDiagnostics);
			Assert.Empty(result.CompileErrors);
			Assert.Contains("{ () => target.Ids, null }", result.Generated);
			Assert.Contains("() => global::Game.Levels.StaticIds", result.Generated);
			Assert.Contains("() => target.Numbers?.Invoke()", result.Generated);
			Assert.Contains("() => target.NamesSource?.Invoke()", result.Generated);
			Assert.Contains("() => target.ReadIds()", result.Generated);
		}

		[Fact]
		public void Options_AreReadFromTheInstanceItWasGiven()
		{
			Assembly assembly = GeneratorHarness.Run(Levels).Load();
			Type levelsType = assembly.GetType("Game.Levels");
			object levels = Activator.CreateInstance(levelsType);
			Dictionary<string, object> commands = Collect(assembly, levels)
				.ToDictionary(command => (string)Definition(command).Path, command => command);

			object load = commands["Levels/Load"];
			Assert.True((bool)Definition(load).Arguments[0].HasOptions);
			Assert.False((bool)Definition(load).Arguments[1].HasOptions);
			Assert.Equal(new object[] { "a", "b" }, Options(load, 0));

			Assert.True((bool)Definition(commands["Levels/Current"]).Arguments[0].HasOptions);
			Assert.Equal(new object[] { "s1", "s2" }, Options(commands["Levels/Current"], 0));

			Assert.Null(Options(commands["Levels/Pick"], 0));
			levelsType.GetField("Numbers").SetValue(levels, new Func<IEnumerable<int>>(() => new[] { 3, 4 }));
			Assert.Equal(new object[] { 3, 4 }, Options(commands["Levels/Pick"], 0));

			Assert.Equal(new object[] { "x" }, Options(commands["Levels/Rename"], 0));
		}

		[Theory]
		[InlineData("[DebugCommand(\"G\")] public void Run([DebugOptions(\"Nope\")] string id) { }", "OMNI014")]
		[InlineData("private List<string> Ids; [DebugCommand(\"G\")] public void Run([DebugOptions(nameof(Ids))] string id) { }", "OMNI015")]
		[InlineData("public List<string> Ids(int count) => null; [DebugCommand(\"G\")] public void Run([DebugOptions(nameof(Ids))] string id) { }", "OMNI015")]
		[InlineData("public int Ids; [DebugCommand(\"G\")] public void Run([DebugOptions(nameof(Ids))] string id) { }", "OMNI016")]
		[InlineData("public List<int> Ids; [DebugCommand(\"G\")] public void Run([DebugOptions(nameof(Ids))] string id) { }", "OMNI016")]
		[InlineData("public Func<List<int>> Ids; [DebugCommand(\"G\")] public void Run([DebugOptions(nameof(Ids))] string id) { }", "OMNI016")]
		[InlineData("public Func<int, List<string>> Ids; [DebugCommand(\"G\")] public void Run([DebugOptions(nameof(Ids))] string id) { }", "OMNI016")]
		[InlineData("public List<int> Ids; [DebugCommand(\"G\"), DebugOptions(nameof(Ids))] public string Value { get; set; }", "OMNI016")]
		public void InvalidOptions_AreACompileError(string members, string id)
		{
			GeneratorResult result = GeneratorHarness.Run($@"
using System;
using System.Collections.Generic;
using DTech.OmniDebugger;
public class Holder
{{
	{members}
}}");

			Assert.Equal(new[] { id }, result.GeneratorIds);
			Assert.Empty(result.CompileErrors);
		}

		[Fact]
		public void OneInvalidMember_LeavesTheOthersGenerated()
		{
			GeneratorResult result = GeneratorHarness.Run(@"
using DTech.OmniDebugger;
public class Holder
{
	[DebugCommand(""G"")] public static void Broken() { }
	[DebugCommand(""G"")] public void Fine() { }
}");

			Assert.Equal(new[] { "OMNI001" }, result.GeneratorIds);
			Assert.Contains("target.Fine()", result.Generated);
			Assert.DoesNotContain("Broken", result.Generated);
		}

		[Fact]
		public void AssemblyWithoutCommands_GetsNoCode()
		{
			GeneratorResult result = GeneratorHarness.Run("public class Plain { public void Run() { } }");

			Assert.Empty(result.GeneratorDiagnostics);
			Assert.Equal(string.Empty, result.Generated);
		}

		private static List<object> Collect(Assembly assembly, object target)
		{
			Type binders = assembly.GetType("DTech.OmniDebugger.CommandBinders");
			Type commandType = assembly.GetType("DTech.OmniDebugger.DebugCommand");
			IList list = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(commandType));

			bool found = (bool)binders.GetMethod("Collect", BindingFlags.NonPublic | BindingFlags.Static)
				.Invoke(null, new[] { target, list });

			Assert.True(found);
			return list.Cast<object>().ToList();
		}

		private static dynamic Definition(object command) =>
			command.GetType().GetProperty("Definition").GetValue(command);

		private static void Execute(object command, params object[] arguments) =>
			command.GetType().GetMethod("Execute", BindingFlags.NonPublic | BindingFlags.Instance)
				.Invoke(command, new object[] { arguments });

		private static object[] Options(object command, int argumentIndex)
		{
			IEnumerable options = (IEnumerable)command.GetType()
				.GetMethod("GetOptions", BindingFlags.NonPublic | BindingFlags.Instance)
				.Invoke(command, new object[] { argumentIndex });

			return options?.Cast<object>().ToArray();
		}

		private static object GetValue(object command) =>
			command.GetType().GetMethod("GetValue", BindingFlags.NonPublic | BindingFlags.Instance)
				.Invoke(command, null);
	}
}
