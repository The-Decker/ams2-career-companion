using Companion.Core.Smgp;
using Companion.ViewModels.Briefing;
using Companion.ViewModels.Services;

namespace Companion.Tests.ViewModels;

/// <summary>
/// The rival-naming copy is GENDER-AWARE (Mike: "name HER as my rival for a female"). A female rival (Mika)
/// must never be called "him"/"his"; an unmarked driver keeps the he/him default. The copy properties read
/// the previewed/named rival's pronoun set, so we drive them directly.
/// </summary>
public sealed class SmgpRivalGenderTests
{
    private static SmgpRivalOption Rival(SmgpPronouns pronouns, bool offerOnWin = false, bool forfeitOnLoss = false) => new()
    {
        DriverId = "driver.mika_larssen", DriverName = "Mika Larssen", TeamId = "team.azalea", TeamName = "Azalea",
        MachineLine = "McLaren_MP45B", Quote = "IT'S INTERESTING.",
        OfferOnWin = offerOnWin, ForfeitOnLoss = forfeitOnLoss, Pronouns = pronouns,
    };

    private static BriefingViewModel NewVm() =>
        new(new FakeCareerSession { Briefing = BriefingComposer.Compose(
            ViewModelTestData.RealPack(), ViewModelTestData.RealPack().Season.Rounds.Single(r => r.Round == 3),
            ViewModelTestData.RealLibrary.Value) });

    [Fact]
    public void A_female_rival_is_named_with_her_not_him()
    {
        var vm = NewVm();
        vm.SelectedSmgpRival = Rival(SmgpPronouns.She);

        Assert.Equal("YES, name her as my rival", vm.SmgpNameButtonLabel);
        Assert.Contains("HER", vm.SmgpRivalPrompt);
        Assert.Contains("take her seat", vm.SmgpLadderLine);
        Assert.Contains("take her seat", vm.SmgpRivalIntro);
        Assert.DoesNotContain("him", vm.SmgpLadderLine);
        Assert.DoesNotContain("his", vm.SmgpLadderLine);
    }

    [Fact]
    public void A_female_named_line_uses_her()
    {
        var vm = NewVm();
        vm.NamedSmgpRival = Rival(SmgpPronouns.She, offerOnWin: false);
        Assert.Contains("Beat her this race", vm.SmgpNamedLine);
        Assert.Contains("take her seat", vm.SmgpNamedLine);
        Assert.DoesNotContain("him", vm.SmgpNamedLine);
    }

    [Fact]
    public void A_forfeit_line_uses_the_capitalised_subject()
    {
        var vm = NewVm();
        vm.SelectedSmgpRival = Rival(SmgpPronouns.She, forfeitOnLoss: true);
        Assert.StartsWith("She has beaten you once", vm.SmgpLadderLine);
    }

    [Fact]
    public void The_swap_accept_checkbox_label_is_gender_aware()
    {
        var vm = NewVm();
        vm.NamedSmgpRival = Rival(SmgpPronouns.She, offerOnWin: true);
        Assert.Equal("Join her team if the offer comes", vm.SmgpSwapAcceptLabel);

        vm.NamedSmgpRival = Rival(SmgpPronouns.Default, offerOnWin: true);
        Assert.Equal("Join his team if the offer comes", vm.SmgpSwapAcceptLabel);
    }

    [Fact]
    public void An_unmarked_rival_keeps_the_he_him_default()
    {
        var vm = NewVm();
        vm.SelectedSmgpRival = Rival(SmgpPronouns.Default); // he/him

        Assert.Equal("YES, name him as my rival", vm.SmgpNameButtonLabel);
        Assert.Contains("HIM", vm.SmgpRivalPrompt);
        Assert.Contains("take his seat", vm.SmgpLadderLine);
    }
}
