using System.Linq;
using DAoCLogWatcher.UI.Services;
using FluentAssertions;
using Xunit;

namespace DAoCLogWatcher.Tests.UI.Services;

/// <summary>
/// Payloads are trimmed captures of the real Eden warmap socket (wss://ws.eden-daoc.net:60005).
/// A relic's location and owner come from the relic record's own site field, kept current by the
/// singular "relic" message; the pad list only supplies names and coordinates.
/// </summary>
public sealed class WarmapRelicTests
{
	private const string RelicsJson = """
	                                  {"relics":{"1":{"or":1,"t":1,"s":86,"r":2},"2":{"or":1,"t":0,"s":107,"r":3},"3":{"or":2,"t":1,"s":2,"r":2},"4":{"or":2,"t":0,"s":2,"r":2}}}
	                                  """;

	private const string PadsJson = """
	                                {"relicpads":[
	                                  {"n":"Castle Excalibur","reg":163,"x":673847,"y":590547,"rlm":1,"o":1,"pt":0,"h":1,"r":0,"rt":-1,"c":0,"d":0,"k":47},
	                                  {"n":"Mjollner Faste","reg":163,"x":610896,"y":303046,"rlm":2,"o":2,"pt":0,"h":1,"r":4,"rt":0,"c":0,"d":0,"k":35},
	                                  {"n":"Grallarhorn Faste","reg":163,"x":713065,"y":403739,"rlm":2,"o":2,"pt":1,"h":1,"r":3,"rt":1,"c":0,"d":0,"k":36},
	                                  {"n":"Glenlock Faste","reg":163,"x":609247,"y":377960,"rlm":2,"o":2,"pt":2,"h":0,"r":1,"rt":1,"c":0,"d":0,"k":86},
	                                  {"n":"Dun Crauchon","reg":163,"x":471769,"y":501281,"rlm":3,"o":3,"pt":2,"h":0,"r":2,"rt":0,"c":0,"d":0,"k":107},
	                                  {"n":"Bledmeer Faste","reg":163,"x":533784,"y":407943,"rlm":3,"o":2,"pt":2,"h":0,"r":0,"rt":-1,"c":0,"d":0,"k":82}
	                                ]}
	                                """;

	private static WarmapWebSocketService MakeService()
	{
		var service = new WarmapWebSocketService();
		service.ProcessMessage(RelicsJson);
		service.ProcessMessage(PadsJson);
		return service;
	}

	[Fact]
	public void RelicsSnapshot_ParsesOwnerOriginAndSite()
	{
		var service = MakeService();

		var relic = service.GetRelicsSnapshot().Single(r => r.Id == 1);
		relic.OriginRealm.Should().Be(1);
		relic.Type.Should().Be(1);
		relic.OwnerRealm.Should().Be(2);
		relic.Site.Should().Be(86);
		relic.IsMoving.Should().BeFalse();
		relic.KeepId.Should().Be(86);
	}

	[Fact]
	public void RelicPadsSnapshot_MarksHomePads()
	{
		var service = MakeService();

		var pads = service.GetRelicPadsSnapshot();
		pads.Should().HaveCount(6);
		pads.Single(p => p.Name == "Castle Excalibur").IsHomePad.Should().BeTrue();
		pads.Single(p => p.Name == "Glenlock Faste").IsHomePad.Should().BeFalse();
	}

	[Fact]
	public void GetRelicPlacements_ResolvesKeepSiteToThatKeep()
	{
		var service = MakeService();

		var stolen = service.GetRelicPlacements().Single(p => p.RelicId == 1);

		stolen.PadName.Should().Be("Glenlock Faste");
		stolen.OriginRealm.Should().Be(1);
		stolen.OwnerRealm.Should().Be(2);
		stolen.IsAtHome.Should().BeFalse();
		stolen.IsMoving.Should().BeFalse();
		stolen.DisplayName.Should().Be("Merlin's Staff");
		stolen.TypeName.Should().Be("Power");
	}

	[Fact]
	public void GetRelicPlacements_HomeSiteResolvesToTheMatchingHomePad()
	{
		var service = MakeService();

		var home = service.GetRelicPlacements().Single(p => p.RelicId == 4);

		home.PadName.Should().Be("Mjollner Faste");
		home.IsAtHome.Should().BeTrue();
		home.IsHomePad.Should().BeTrue();
		home.DisplayName.Should().Be("Thor's Hammer");
		home.TypeName.Should().Be("Strength");
	}

	[Fact]
	public void GetRelicPlacements_HomeSitePicksThePadMatchingTheRelicType()
	{
		var service = MakeService();

		// Relics 3 and 4 are both Midgard's and both report site 2: only the pad type separates them.
		service.GetRelicPlacements().Single(p => p.RelicId == 3).PadName.Should().Be("Grallarhorn Faste");
		service.GetRelicPlacements().Single(p => p.RelicId == 4).PadName.Should().Be("Mjollner Faste");
	}

	[Fact]
	public void RelicUpdate_MovesTheRelicAndChangesOwner()
	{
		var service = MakeService();

		// Midgard captures the Albion strength relic and puts it in Bledmeer Faste (keep 82).
		service.ProcessMessage("""{"relic":{"id":2,"s":82,"r":2}}""");

		var placement = service.GetRelicPlacements().Single(p => p.RelicId == 2);
		placement.PadName.Should().Be("Bledmeer Faste");
		placement.OwnerRealm.Should().Be(2);
		placement.OriginRealm.Should().Be(1);
		placement.IsAtHome.Should().BeFalse();
		placement.DisplayName.Should().Be("Scabbard of Excalibur");
	}

	[Fact]
	public void RelicUpdate_SiteZeroMarksTheRelicAsMoving()
	{
		var service = MakeService();

		service.ProcessMessage("""{"relic":{"id":1,"s":0,"r":0}}""");

		var placement = service.GetRelicPlacements().Single(p => p.RelicId == 1);
		placement.IsMoving.Should().BeTrue();
		placement.PadName.Should().Be("In transit");
		placement.GameX.Should().Be(0);
		placement.GameY.Should().Be(0);
	}

	[Fact]
	public void RelicUpdate_AcceptsAQuotedSite()
	{
		var service = MakeService();

		service.ProcessMessage("""{"relic":{"id":1,"s":"82","r":2}}""");

		service.GetRelicPlacements().Single(p => p.RelicId == 1).PadName.Should().Be("Bledmeer Faste");
	}

	[Fact]
	public void RelicUpdate_ReturningHomeClearsTheCapturedState()
	{
		var service = MakeService();

		service.ProcessMessage("""{"relic":{"id":1,"s":1,"r":1}}""");

		var placement = service.GetRelicPlacements().Single(p => p.RelicId == 1);
		placement.IsAtHome.Should().BeTrue();
		placement.OwnerRealm.Should().Be(1);
	}

	[Fact]
	public void RelicUpdate_UnknownIdIsIgnored()
	{
		var service = MakeService();

		service.ProcessMessage("""{"relic":{"id":99,"s":82,"r":2}}""");

		service.GetRelicsSnapshot().Should().HaveCount(4);
	}

	[Fact]
	public void GetRelicPlacements_UnknownSiteKeepStillReportsOwnership()
	{
		var service = new WarmapWebSocketService();
		service.ProcessMessage(RelicsJson);

		// No pad list has arrived, so nothing can name the keep, but the relic is still known.
		var placement = service.GetRelicPlacements().Single(p => p.RelicId == 1);
		placement.PadName.Should().Be("Unknown");
		placement.OwnerRealm.Should().Be(2);
	}
}
