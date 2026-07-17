using DAoCLogWatcher.UI.Services;
using FluentAssertions;

namespace DAoCLogWatcher.Tests.UI.Services;

public sealed class KWinOverlayRuleInstallerTests
{
	private static string Uuid()
	{
		return "11111111-2222-3333-4444-555555555555";
	}

	[Fact]
	public void EmptyConfig_AddsGeneralAndOverlayRule()
	{
		var added = KWinOverlayRuleInstaller.TryBuildWithOverlayRule(string.Empty, Uuid, out var updated);

		added.Should().BeTrue();
		updated.Should().Contain("[General]");
		updated.Should().Contain($"rules={Uuid()}");
		updated.Should().Contain("count=1");
		updated.Should().Contain($"[{Uuid()}]");
		updated.Should().Contain("layer=osd");
		updated.Should().Contain("layerrule=2");
		updated.Should().Contain("title=DAoC Overlay");
		updated.Should().Contain("wmclass=io.github.zzerker.DAoCLogWatcher");
	}

	[Fact]
	public void AlreadyPresent_IsIdempotent()
	{
		// First install, then re-run against the produced config: nothing should change.
		KWinOverlayRuleInstaller.TryBuildWithOverlayRule(string.Empty, Uuid, out var installed);

		var addedAgain = KWinOverlayRuleInstaller.TryBuildWithOverlayRule(installed, () => "should-not-be-used", out var second);

		addedAgain.Should().BeFalse();
		second.Should().Be(installed);
	}

	[Fact]
	public void ManuallyImportedRule_IsDetectedAndNotDuplicated()
	{
		// A rule matching wmclass + layer=osd but with a different uuid/title still counts as present.
		var existing =
				"[abc12345-0000-0000-0000-000000000000]\n" +
				"Description=my own\n" +
				"layer=osd\n" +
				"layerrule=2\n" +
				"wmclass=io.github.zzerker.DAoCLogWatcher\n" +
				"wmclassmatch=1\n" +
				"\n" +
				"[General]\n" +
				"count=1\n" +
				"rules=abc12345-0000-0000-0000-000000000000\n";

		var added = KWinOverlayRuleInstaller.TryBuildWithOverlayRule(existing, Uuid, out var updated);

		added.Should().BeFalse();
		updated.Should().Be(existing);
	}

	[Fact]
	public void ExistingUnrelatedRules_ArePreservedAndCountIncremented()
	{
		var existing =
				"[rule-a]\n" +
				"Description=unrelated game\n" +
				"below=true\n" +
				"belowrule=2\n" +
				"wmclass=steam_app_default\n" +
				"wmclassmatch=1\n" +
				"\n" +
				"[General]\n" +
				"count=1\n" +
				"rules=rule-a\n";

		var added = KWinOverlayRuleInstaller.TryBuildWithOverlayRule(existing, Uuid, out var updated);

		added.Should().BeTrue();
		// existing rule untouched
		updated.Should().Contain("[rule-a]");
		updated.Should().Contain("wmclass=steam_app_default");
		// general updated to include both rules
		updated.Should().Contain($"rules=rule-a,{Uuid()}");
		updated.Should().Contain("count=2");
		// our rule appended
		updated.Should().Contain("title=DAoC Overlay");
	}
}
