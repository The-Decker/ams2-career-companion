using Companion.Core.Smgp;

namespace Companion.Tests.Career;

/// <summary>The gendered pronoun set for rival/driver copy: "female" → she/her, anything else defaults to
/// he/him (the safe legacy default, so only the female drivers need marking). Pure.</summary>
public sealed class SmgpPronounsTests
{
    [Theory]
    [InlineData("female", "she", "her", "her")]
    [InlineData("Female", "she", "her", "her")]
    [InlineData("f", "she", "her", "her")]
    [InlineData("male", "he", "him", "his")]
    [InlineData("", "he", "him", "his")]        // unmarked → he/him
    [InlineData("banana", "he", "him", "his")]  // unknown → he/him
    [InlineData("nonbinary", "they", "them", "their")]
    public void For_MapsGenderToPronouns(string gender, string subject, string obj, string possessive)
    {
        var p = SmgpPronouns.For(gender);
        Assert.Equal(subject, p.Subject);
        Assert.Equal(obj, p.Object);
        Assert.Equal(possessive, p.Possessive);
    }

    [Fact]
    public void Null_gender_defaults_to_he_him()
    {
        Assert.Equal(SmgpPronouns.He, SmgpPronouns.For(null));
        Assert.Equal(SmgpPronouns.He, SmgpPronouns.Default);
    }

    [Fact]
    public void Capitalised_forms_uppercase_the_first_letter()
    {
        Assert.Equal("She", SmgpPronouns.She.SubjectCap);
        Assert.Equal("Her", SmgpPronouns.She.ObjectCap);
        Assert.Equal("He", SmgpPronouns.He.SubjectCap);
    }

    [Fact]
    public void A_profile_resolves_its_pronouns_from_gender()
    {
        var mika = new SmgpDriverProfile { DriverId = "driver.mika_larssen", Name = "Mika Larssen", Gender = "female" };
        var male = new SmgpDriverProfile { DriverId = "driver.x", Name = "X" }; // unmarked
        Assert.Equal(SmgpPronouns.She, mika.Pronouns);
        Assert.Equal(SmgpPronouns.He, male.Pronouns);
    }

    [Fact]
    public void The_shipped_data_marks_Mika_female()
    {
        var profiles = SmgpDriverProfiles.Load(Companion.Tests.ViewModels.ViewModelTestData.RulesDirectory);
        var mika = profiles.ForDriver("driver.mika_larssen");
        Assert.NotNull(mika);
        Assert.Equal("female", mika!.Gender);
        Assert.Equal(SmgpPronouns.She, mika.Pronouns);
    }
}
