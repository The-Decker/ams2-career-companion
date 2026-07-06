using System.Windows;
using Companion.App.Views;
using Companion.Core.Character;
using Companion.ViewModels.Wizard;

namespace Companion.RenderHarness.Tests;

/// <summary>Off-screen render of the wizard's character step over a real <see cref="CharacterViewModel"/>.
/// Constructing and laying out the REAL <see cref="CharacterView"/> resolves every StaticResource it
/// uses (Panel, Faint, ErrorBrush, …) and realises the archetype list, stat sliders, and perk shelf —
/// the view-layer wiring a pure VM test can't exercise. Self-skips on a non-Windows / non-STA host.</summary>
public sealed class CharacterViewRenderTests
{
    private const string RulesJson = """
        {
          "version": 2,
          "characterPoints": { "creationBudget": 10, "minBudgetAfterSpend": 0, "maxRefundHeadroom": 6 },
          "stats": {
            "talentStats": [
              { "id": "pace", "mapsTo": ["raceSkill"] },
              { "id": "oneLap", "mapsTo": ["qualifyingSkill"] },
              { "id": "craft", "mapsTo": ["avoidanceOfMistakes"] },
              { "id": "racecraft", "mapsTo": ["aggression"] },
              { "id": "adaptability", "mapsTo": ["wetSkill"] }
            ],
            "metaStats": [ { "id": "marketability", "default": 0.5 }, { "id": "durability", "default": 0.5 } ]
          },
          "levels": {
            "xpCurve": { "baseXpToLevel2": 100, "growth": 1.35, "maxLevel": 30 },
            "xpSources": { "perRound": {}, "perSeason": {} },
            "levelGrants": {}
          },
          "creation": { "archetypes": [
            { "id": "a1", "name": "The Racer", "description": "Fast and fragile.",
              "startStats": { "pace": 0.6, "oneLap": 0.55, "craft": 0.5, "racecraft": 0.5, "adaptability": 0.5 },
              "startMeta": { "marketability": 0.6, "durability": 0.5 }, "perkIds": ["p_pace"] },
            { "id": "a2", "name": "The Survivor", "description": "Durable and calm.",
              "startStats": { "pace": 0.4, "oneLap": 0.4, "craft": 0.6, "racecraft": 0.45, "adaptability": 0.55 },
              "startMeta": { "marketability": 0.45, "durability": 0.65 }, "perkIds": ["p_craft"] }
          ] },
          "perks": [
            { "id": "p_pace", "name": "Quick Hands", "category": "pace", "cost": 1, "effects": [] },
            { "id": "p_craft", "name": "Careful", "category": "mental", "cost": 0, "effects": [] }
          ]
        }
        """;

    [Fact]
    public void CharacterView_RendersOverTheViewModel()
    {
        if (!WpfRenderHarness.IsSupported)
            return;

        WpfRenderHarness.RunSta(() =>
        {
            var vm = new CharacterViewModel(CharacterRules.Parse(RulesJson));
            // The default archetype makes a complete, valid build without any interaction.
            Assert.NotNull(vm.SelectedArchetype);
            Assert.True(vm.IsValid);

            // Constructing the real view resolves every StaticResource; laying it out realises the
            // templated archetype list / stat sliders / perk shelf without throwing.
            var view = new CharacterView { DataContext = vm };
            view.Measure(new Size(900, 600));
            view.Arrange(new Rect(0, 0, 900, 600));
            view.UpdateLayout();

            Assert.True(view.ActualWidth > 0);
            Assert.True(view.ActualHeight > 0);
        });
    }
}
