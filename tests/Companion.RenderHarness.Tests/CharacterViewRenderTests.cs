using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;
using Companion.App.Converters;
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
    private static readonly Size CreatorViewport = new(920, 620);

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

    [Fact]
    public void CharacterView_RendersProgressionV2RacingDna_AndPreservesLegacyGate()
    {
        if (!WpfRenderHarness.IsSupported)
            return;

        WpfRenderHarness.RunSta(() =>
        {
            var vm = CreateVersionTwoViewModel();
            vm.SelectedRacingDna = vm.RacingDnaCards.Single(card => card.Id == "dna_circuit_specialist");
            vm.RacingDnaChoiceValue = vm.RacingDnaChoiceOptions[0].Value;

            var view = new CharacterView { DataContext = vm };
            Arrange(view);

            Assert.True(vm.IsProgressionV2);
            Assert.Equal(30, vm.RacingDnaCards.Count);
            Assert.Equal(9, vm.MasteryPreviewFamilies.Count);
            Assert.Equal(90, vm.MasteryPreviewSkillCount);
            Assert.Equal(7, vm.MasteryPreviewAttributeRailCount);
            Assert.Equal(119, vm.MasteryPreviewAttributeNodeCount);
            Assert.Equal(119, vm.MasteryPreviewAttributeCost);
            Assert.Equal(Visibility.Visible, ((FrameworkElement)view.FindName("CharacterRacingDnaPanel")).Visibility);
            Assert.Equal(Visibility.Collapsed, ((FrameworkElement)view.FindName("CharacterLegacyArchetypePanel")).Visibility);
            Assert.Equal(Visibility.Visible, ((FrameworkElement)view.FindName("CharacterRacingDnaChoice")).Visibility);
            Assert.Equal(30, ((ListBox)view.FindName("CharacterRacingDnaList")).Items.Count);
            Assert.Same(vm.SelectedRacingDna,
                Assert.IsType<ContentControl>(view.FindName("CharacterRacingDnaDetail")).Content);
            AssertVisibleText(view, vm.SelectedRacingDna.Name);
        });
    }

    [Fact]
    public void CharacterView_ProgressionV2_CountryDropdownBindsAllFlagsSelectionAndReadiness()
    {
        if (!WpfRenderHarness.IsSupported)
            return;

        WpfRenderHarness.RunSta(() =>
        {
            using var bindingErrors = new BindingErrorScope();
            var vm = CreateVersionTwoViewModel();
            var view = new CharacterView { DataContext = vm };
            Arrange(view);

            Assert.Equal(200, vm.CountryOptions.Count);
            Assert.Null(vm.SelectedCountry);
            Assert.False(vm.IsCountryValid);
            Assert.Equal("COUNTRY NOT SELECTED", vm.CountrySummary);

            FrameworkElement selector = Assert.IsAssignableFrom<FrameworkElement>(
                view.FindName("CharacterCountrySelector"));
            var dropdown = Assert.IsType<ComboBox>(view.FindName("CharacterCountryDropdown"));
            var summary = Assert.IsType<TextBlock>(view.FindName("CharacterCountrySummary"));
            var readiness = Assert.IsType<TextBlock>(view.FindName("CharacterCountryReadiness"));
            FrameworkElement validationBadge = Assert.IsAssignableFrom<FrameworkElement>(
                view.FindName("CharacterCountryValidationBadge"));

            Assert.Equal(Visibility.Visible, selector.Visibility);
            Assert.Equal(vm.CountryOptions.Count, dropdown.Items.Count);
            Assert.Equal("Name", TextSearch.GetTextPath(dropdown));
            Assert.True(dropdown.IsTextSearchEnabled);
            Assert.Equal(ScrollBarVisibility.Auto, ScrollViewer.GetVerticalScrollBarVisibility(dropdown));
            Assert.True(ScrollViewer.GetCanContentScroll(dropdown));
            Assert.True(ScrollViewer.GetIsDeferredScrollingEnabled(dropdown));
            Assert.True(VirtualizingPanel.GetIsVirtualizing(dropdown));
            Assert.Equal(VirtualizationMode.Recycling, VirtualizingPanel.GetVirtualizationMode(dropdown));
            var configuredItemsPanel = Assert.IsType<VirtualizingStackPanel>(dropdown.ItemsPanel.LoadContent());
            Assert.True(VirtualizingPanel.GetIsVirtualizing(configuredItemsPanel));
            Assert.Equal(
                VirtualizationMode.Recycling,
                VirtualizingPanel.GetVirtualizationMode(configuredItemsPanel));
            Assert.Equal(244, dropdown.Width);
            Assert.Equal(36, dropdown.MinHeight);
            Assert.Equal(HorizontalAlignment.Left, dropdown.HorizontalAlignment);
            Assert.InRange(dropdown.MaxDropDownHeight, 280, 340);
            dropdown.ApplyTemplate();
            var popup = Assert.IsType<Popup>(dropdown.Template.FindName("PART_Popup", dropdown));
            var popupRoot = Assert.IsAssignableFrom<DependencyObject>(popup.Child);
            ScrollViewer popupScroller = Assert.Single(Descendants<ScrollViewer>(popupRoot));
            Assert.Equal(ScrollBarVisibility.Auto, popupScroller.VerticalScrollBarVisibility);
            Assert.Equal(ScrollBarVisibility.Disabled, popupScroller.HorizontalScrollBarVisibility);
            Assert.True(popupScroller.CanContentScroll);
            Assert.True(ScrollViewer.GetIsDeferredScrollingEnabled(popupScroller));
            Assert.Equal(vm.CountrySummary, summary.Text);
            Assert.Equal("Choose the country and flag shown on your driver profile.", readiness.Text);
            Assert.Contains("CHOOSE COUNTRY", VisibleText(validationBadge, view), StringComparison.Ordinal);

            BindingExpression itemsBinding = Assert.IsType<BindingExpression>(
                BindingOperations.GetBindingExpression(dropdown, ItemsControl.ItemsSourceProperty));
            Assert.Equal("CountryOptions", itemsBinding.ParentBinding.Path.Path);
            Assert.Equal(BindingMode.OneWay, itemsBinding.ParentBinding.Mode);

            BindingExpression selectionBinding = Assert.IsType<BindingExpression>(
                BindingOperations.GetBindingExpression(dropdown, Selector.SelectedItemProperty));
            Assert.Equal("SelectedCountry", selectionBinding.ParentBinding.Path.Path);
            Assert.Equal(BindingMode.TwoWay, selectionBinding.ParentBinding.Mode);

            // Every catalog entry remains reachable through the same two-way selector contract,
            // including catalogs much larger than the original 14-flag prototype.
            var assetConverter = new KeyedAssetImageConverter();
            foreach (CharacterCountryOption option in vm.CountryOptions)
            {
                Assert.IsAssignableFrom<ImageSource>(assetConverter.Convert(
                    option.FlagKey, typeof(ImageSource), "smgp/flags", CultureInfo.InvariantCulture));
                dropdown.SelectedItem = option;
                Assert.Same(option, vm.SelectedCountry);
            }

            CharacterCountryOption selected = vm.CountryOptions[^1];
            dropdown.SelectedItem = selected;
            DrainDispatcher();
            Arrange(view);

            Assert.Same(selected, vm.SelectedCountry);
            Assert.True(vm.IsCountryValid);
            Assert.Equal(selected.Name.ToUpperInvariant(), vm.CountrySummary);
            Assert.Equal(vm.CountrySummary, summary.Text);
            Assert.Equal("Choose the country and flag shown on your driver profile.", readiness.Text);
            Assert.Contains("COUNTRY SET", VisibleText(validationBadge, view), StringComparison.Ordinal);
            AssertVisibleText(dropdown, selected.Name);
            Assert.DoesNotContain(Descendants<TextBlock>(dropdown), text =>
                ReferenceEquals(text.DataContext, selected) &&
                string.Equals(text.Text, selected.Code, StringComparison.Ordinal));

            Image selectedFlag = Descendants<Image>(dropdown).Single(image =>
                ReferenceEquals(image.DataContext, selected));
            BindingExpression flagBinding = Assert.IsType<BindingExpression>(
                BindingOperations.GetBindingExpression(selectedFlag, Image.SourceProperty));
            Assert.Equal("FlagKey", flagBinding.ParentBinding.Path.Path);
            Assert.Equal(BindingMode.OneWay, flagBinding.ParentBinding.Mode);
            Assert.Equal("smgp/flags", flagBinding.ParentBinding.ConverterParameter);
            Assert.NotNull(selectedFlag.Source);
            Assert.Equal(30, selectedFlag.Width);
            Assert.Equal(20, selectedFlag.Height);
            var selectedFlagRow = Assert.IsType<Grid>(VisualTreeHelper.GetParent(selectedFlag));
            Assert.DoesNotContain(selectedFlagRow.Children.OfType<Border>(), _ => true);

            // Exercise the actual popup with a real off-screen HWND: 200 rows must overflow the
            // capped viewport, and opening on the last selected country must realize its row.
            var popupWindow = new Window
            {
                Content = view,
                Width = CreatorViewport.Width,
                Height = CreatorViewport.Height,
                WindowStyle = WindowStyle.None,
                ShowInTaskbar = false,
                ShowActivated = false,
                Left = -10000,
                Top = -10000,
            };
            try
            {
                popupWindow.Show();
                popupWindow.UpdateLayout();
                WpfRenderHarness.Pump(DispatcherPriority.Loaded);
                dropdown.IsDropDownOpen = true;
                DrainDispatcher();
                popupWindow.UpdateLayout();
                WpfRenderHarness.Pump(DispatcherPriority.Render);

                Assert.True(popup.IsOpen);
                Assert.True(popupScroller.ScrollableHeight > 0,
                    "The 200-country popup did not expose a scrollable viewport.");
                var selectedContainer = Assert.IsType<ComboBoxItem>(
                    dropdown.ItemContainerGenerator.ContainerFromItem(selected));
                selectedContainer.BringIntoView();
                DrainDispatcher();
                AssertVisibleText(selectedContainer, selected.Name);
                Assert.DoesNotContain(Descendants<TextBlock>(selectedContainer), text =>
                    string.Equals(text.Text, selected.Code, StringComparison.Ordinal));
                Assert.Contains(Descendants<Image>(selectedContainer), image =>
                    ReferenceEquals(image.DataContext, selected) && image.Source is not null);
            }
            finally
            {
                dropdown.IsDropDownOpen = false;
                popupWindow.Content = null;
                popupWindow.Close();
            }

            // Legacy creation keeps its original compact identity strip; the v2-only dropdown
            // contributes no height or interaction surface when the feature gate is off.
            var legacyVm = new CharacterViewModel(CharacterRules.Parse(RulesJson));
            var legacyView = new CharacterView { DataContext = legacyVm };
            Arrange(legacyView);
            Assert.Equal(
                Visibility.Collapsed,
                Assert.IsAssignableFrom<FrameworkElement>(
                    legacyView.FindName("CharacterCountrySelector")).Visibility);

            DrainDispatcher();
            bindingErrors.AssertNone();
        });
    }

    [Fact]
    public void CharacterView_ProgressionV2_CountryDropdownDefersOffscreenFlagRows()
    {
        if (!WpfRenderHarness.IsSupported)
            return;

        WpfRenderHarness.RunSta(() =>
        {
            using var bindingErrors = new BindingErrorScope();
            var vm = CreateVersionTwoViewModel();
            var view = new CharacterView { DataContext = vm };
            Arrange(view);

            var dropdown = Assert.IsType<ComboBox>(view.FindName("CharacterCountryDropdown"));
            var popupWindow = new Window
            {
                Content = view,
                Width = CreatorViewport.Width,
                Height = CreatorViewport.Height,
                WindowStyle = WindowStyle.None,
                ShowInTaskbar = false,
                ShowActivated = false,
                Left = -10000,
                Top = -10000,
            };

            try
            {
                popupWindow.Show();
                popupWindow.UpdateLayout();
                WpfRenderHarness.Pump(DispatcherPriority.Loaded);
                dropdown.IsDropDownOpen = true;
                DrainDispatcher();
                popupWindow.UpdateLayout();
                WpfRenderHarness.Pump(DispatcherPriority.Render);

                dropdown.ApplyTemplate();
                var popup = Assert.IsType<Popup>(dropdown.Template.FindName("PART_Popup", dropdown));
                var popupRoot = Assert.IsAssignableFrom<DependencyObject>(popup.Child);
                ScrollViewer popupScroller = Assert.Single(Descendants<ScrollViewer>(popupRoot));
                VirtualizingStackPanel itemsHost = Assert.Single(
                    Descendants<VirtualizingStackPanel>(popupRoot));

                Assert.True(popup.IsOpen);
                Assert.True(popupScroller.CanContentScroll);
                Assert.True(ScrollViewer.GetIsDeferredScrollingEnabled(popupScroller));
                Assert.True(VirtualizingPanel.GetIsVirtualizing(itemsHost));
                Assert.Equal(
                    VirtualizationMode.Recycling,
                    VirtualizingPanel.GetVirtualizationMode(itemsHost));

                int realizedRows = Enumerable.Range(0, dropdown.Items.Count)
                    .Count(index => dropdown.ItemContainerGenerator.ContainerFromIndex(index) is not null);
                Assert.InRange(realizedRows, 1, 40);
                Assert.True(realizedRows < dropdown.Items.Count);
                Assert.Null(dropdown.ItemContainerGenerator.ContainerFromIndex(dropdown.Items.Count - 1));

                int decodedVisibleFlags = Descendants<Image>(popupRoot)
                    .Count(image => image.Source is not null);
                Assert.InRange(decodedVisibleFlags, 1, realizedRows);
            }
            finally
            {
                dropdown.IsDropDownOpen = false;
                popupWindow.Content = null;
                popupWindow.Close();
            }

            DrainDispatcher();
            bindingErrors.AssertNone();
        });
    }

    [Fact]
    public void CharacterView_ProgressionV2_RealizesTraitsCareerPreviewAdvancedAndFooterContract()
    {
        if (!WpfRenderHarness.IsSupported)
            return;

        WpfRenderHarness.RunSta(() =>
        {
            using var bindingErrors = new BindingErrorScope();
            var vm = CreateVersionTwoViewModel();
            var view = new CharacterView { DataContext = vm };
            Arrange(view);

            var workbench = Assert.IsType<TabControl>(view.FindName("CharacterWorkbench"));
            Assert.Equal(5, workbench.Items.Count);

            // Every supplied creation-only trait remains reachable through the family selector,
            // and selecting one realizes its full prose plus the explicit effect-layer badge.
            workbench.SelectedIndex = 2;
            Arrange(view);
            var categoryPicker = Assert.IsType<ComboBox>(view.FindName("CharacterV2TraitCategory"));
            var traitList = Assert.IsType<ListBox>(view.FindName("CharacterV2TraitList"));
            Assert.Equal(vm.PerkCategories.Count, categoryPicker.Items.Count);
            Assert.Equal(244, categoryPicker.Width);
            Assert.Equal(36, categoryPicker.MinHeight);
            Assert.Equal(HorizontalAlignment.Left, categoryPicker.HorizontalAlignment);
            PerkCategory initiallySelectedCategory = Assert.IsType<PerkCategory>(categoryPicker.SelectedItem);
            string selectedFamilyText = VisibleText(categoryPicker, view);
            Assert.Contains(initiallySelectedCategory.DisplayName, selectedFamilyText, StringComparison.Ordinal);
            Assert.DoesNotContain("PerkCategory {", selectedFamilyText, StringComparison.Ordinal);
            Assert.DoesNotContain("System.Collections", selectedFamilyText, StringComparison.Ordinal);

            int reachedTraits = 0;
            foreach (PerkCategory category in vm.PerkCategories)
            {
                categoryPicker.SelectedItem = category;
                Arrange(view);
                Assert.Equal(category.Perks.Count, traitList.Items.Count);
                foreach (PerkOption trait in category.Perks)
                {
                    traitList.SelectedItem = trait;
                    traitList.ScrollIntoView(trait);
                    Arrange(view);
                    Assert.NotNull(traitList.ItemContainerGenerator.ContainerFromItem(trait));
                    reachedTraits++;
                }
            }
            Assert.Equal(vm.CreationTraitCount, reachedTraits);

            PerkOption detailedTrait = vm.Perks.First(trait =>
                trait.Benefits.Count > 0 && trait.Drawbacks.Count > 0 && trait.Effects.Count > 0);
            categoryPicker.SelectedItem = vm.PerkCategories.Single(category =>
                category.Perks.Contains(detailedTrait));
            traitList.SelectedItem = detailedTrait;
            traitList.ScrollIntoView(detailedTrait);
            Arrange(view);
            ContentControl traitDetail = FindVisibleDetail(view, detailedTrait, detailedTrait.Description);
            AssertVisibleText(traitDetail, detailedTrait.Description);
            AssertVisibleText(traitDetail, detailedTrait.Benefits[0]);
            AssertVisibleText(traitDetail, detailedTrait.Drawbacks[0]);
            AssertEffectRendered(traitDetail, detailedTrait.Effects[0]);

            // Career Progression previews all authored mastery and attribute content without exposing an
            // acquisition command. The selected item details surface authoritative effect classes.
            workbench.SelectedIndex = 3;
            Arrange(view);
            var familyPicker = Assert.IsType<ComboBox>(view.FindName("CharacterMasteryFamilyPicker"));
            Assert.Equal(9, familyPicker.Items.Count);
            MasteryPreviewFamily initiallySelectedFamily =
                Assert.IsType<MasteryPreviewFamily>(familyPicker.SelectedItem);
            familyPicker.ApplyTemplate();
            var familyContentSite = Assert.IsType<ContentPresenter>(
                familyPicker.Template.FindName("ContentSite", familyPicker));
            Assert.NotNull(familyPicker.ItemTemplateSelector);
            Assert.Same(familyPicker.ItemTemplateSelector, familyContentSite.ContentTemplateSelector);
            Assert.Equal(familyPicker.SelectionBoxItemStringFormat, familyContentSite.ContentStringFormat);
            string selectedMasteryFamilyText = VisibleText(familyPicker, view);
            Assert.Contains(initiallySelectedFamily.Name, selectedMasteryFamilyText, StringComparison.Ordinal);
            Assert.DoesNotContain("MasteryPreviewFamily {", selectedMasteryFamilyText, StringComparison.Ordinal);
            Assert.DoesNotContain("System.Collections", selectedMasteryFamilyText, StringComparison.Ordinal);
            int realizedSkills = 0;
            foreach (MasteryPreviewFamily family in vm.MasteryPreviewFamilies)
            {
                familyPicker.SelectedItem = family;
                Arrange(view);
                ListBox graph = FindVisibleNamedDescendant<ListBox>(view, "CharacterMasterySkillGraph");
                Assert.Equal(10, graph.Items.Count);
                graph.SelectedItem = family.Skills[0];
                graph.ScrollIntoView(family.Skills[^1]);
                Arrange(view);
                Assert.NotNull(graph.ItemContainerGenerator.ContainerFromItem(family.Skills[^1]));
                realizedSkills += graph.Items.Count;
            }
            Assert.Equal(90, realizedSkills);

            MasteryPreviewFamily firstFamily = vm.MasteryPreviewFamilies[0];
            familyPicker.SelectedItem = firstFamily;
            Arrange(view);
            ListBox masteryGraph = FindVisibleNamedDescendant<ListBox>(view, "CharacterMasterySkillGraph");
            MasteryPreviewSkill selectedSkill = firstFamily.Skills[0];
            masteryGraph.SelectedItem = selectedSkill;
            Arrange(view);
            ContentControl skillDetail = FindVisibleDetail(view, selectedSkill, selectedSkill.Description);
            AssertEffectRendered(skillDetail, selectedSkill.Effects[0]);

            TabControl careerTabs = Descendants<TabControl>(workbench)
                .Single(control => !ReferenceEquals(control, workbench) && control.Items.Count == 2);
            careerTabs.SelectedIndex = 1;
            Arrange(view);
            var railPicker = Assert.IsType<ComboBox>(view.FindName("CharacterAttributeRailPicker"));
            Assert.Equal(7, railPicker.Items.Count);
            int realizedAttributeSteps = 0;
            foreach (MasteryPreviewAttributeRail rail in vm.MasteryPreviewAttributeRails)
            {
                railPicker.SelectedItem = rail;
                Arrange(view);
                ListBox graph = FindVisibleNamedDescendant<ListBox>(view, "CharacterAttributeNodeGraph");
                Assert.Equal(17, graph.Items.Count);
                graph.SelectedItem = rail.Nodes[0];
                graph.ScrollIntoView(rail.Nodes[^1]);
                Arrange(view);
                Assert.NotNull(graph.ItemContainerGenerator.ContainerFromItem(rail.Nodes[^1]));
                realizedAttributeSteps += graph.Items.Count;
            }
            Assert.Equal(119, realizedAttributeSteps);

            MasteryPreviewAttributeRail firstRail = vm.MasteryPreviewAttributeRails[0];
            railPicker.SelectedItem = firstRail;
            Arrange(view);
            ListBox attributeGraph = FindVisibleNamedDescendant<ListBox>(view, "CharacterAttributeNodeGraph");
            MasteryPreviewAttributeNode selectedAttribute = firstRail.Nodes[0];
            attributeGraph.SelectedItem = selectedAttribute;
            Arrange(view);
            ContentControl attributeDetail = FindVisibleDetail(
                view, selectedAttribute, selectedAttribute.Effects[0].Text);
            AssertEffectRendered(attributeDetail, selectedAttribute.Effects[0]);

            // Advanced consumes CODE's explicit team-first projection verbatim.
            workbench.SelectedIndex = 4;
            Arrange(view);
            Assert.Equal(100, vm.ExpectedPerformanceComponents.Sum(component => component.Percent));
            Assert.Equal([60, 30, 10], vm.ExpectedPerformanceComponents.Select(component => component.Percent));
            AssertVisibleText(view, vm.ExpectedPerformanceBasisSummary);
            AssertVisibleText(view, vm.ExpectedPerformanceCalibrationSummary);
            ItemsControl expectationComponents = Descendants<ItemsControl>(view).Single(control =>
                IsEffectivelyVisible(control, view) && control.Items.Count == 3 &&
                control.Items.Cast<object>().All(item => item is CharacterExpectationComponent));
            Assert.Equal(3, expectationComponents.Items.Count);
            foreach (CharacterExpectationComponent component in vm.ExpectedPerformanceComponents)
            {
                AssertVisibleText(expectationComponents, component.Label);
                AssertVisibleText(expectationComponents, component.Description);
                AssertVisibleText(expectationComponents, $"{component.Percent}%");
            }

            // The visible v2 summary must never leak the legacy CP vocabulary hidden underneath it.
            FrameworkElement footer = Assert.IsAssignableFrom<FrameworkElement>(view.FindName("CharacterBudgetBar"));
            string footerText = VisibleText(footer, view);
            Assert.Contains("DNA", footerText, StringComparison.Ordinal);
            Assert.Contains("STARTING TRAITS & CREATION POINTS", footerText, StringComparison.Ordinal);
            Assert.Contains("STARTING ATTRIBUTES", footerText, StringComparison.Ordinal);
            Assert.Contains("READY TO CONTINUE", footerText, StringComparison.Ordinal);
            Assert.DoesNotMatch(new Regex(@"\bCP\b", RegexOptions.CultureInvariant), footerText);

            DrainDispatcher();
            bindingErrors.AssertNone();
        });
    }

    [Fact]
    public void CharacterView_ProgressionV2_ExplainsChoicesWithAccessiblePlainLanguage()
    {
        if (!WpfRenderHarness.IsSupported)
            return;

        WpfRenderHarness.RunSta(() =>
        {
            using var bindingErrors = new BindingErrorScope();
            var vm = CreateVersionTwoViewModel();
            var view = new CharacterView { DataContext = vm };
            Arrange(view);

            var workbench = Assert.IsType<TabControl>(view.FindName("CharacterWorkbench"));
            var randomBuild = Assert.IsType<Button>(view.FindName("CharacterRandomBuildButton"));
            Assert.Equal("SURPRISE ME", randomBuild.Content);
            AssertAccessibleHint(randomBuild);
            AssertAccessibleHint(Assert.IsType<ComboBox>(view.FindName("CharacterCountryDropdown")));
            AssertVisibleText(view, "CHOOSE YOUR PERMANENT IDENTITY");
            Assert.DoesNotMatch(new Regex(@"\bV1\b", RegexOptions.CultureInvariant), VisibleText(view, view));

            workbench.SelectedIndex = 1;
            Arrange(view);
            AssertVisibleText(view, "DRIVING ATTRIBUTES");
            AssertVisibleText(view, "CAREER ATTRIBUTES");
            AssertVisibleText(view, "STARTING VALUE");
            Slider[] startingSliders = Descendants<Slider>(workbench)
                .Where(slider => IsEffectivelyVisible(slider, view))
                .ToArray();
            Assert.Equal(7, startingSliders.Length);
            foreach (Slider slider in startingSliders)
                AssertAccessibleHint(slider);
            Assert.DoesNotContain("TUNE EXPECTATION", VisibleText(workbench, view), StringComparison.Ordinal);

            workbench.SelectedIndex = 2;
            Arrange(view);
            AssertVisibleText(view, "YOUR STARTING TRAITS");
            AssertVisibleText(view, "WHAT THIS AFFECTS");
            string traitsText = VisibleText(workbench, view);
            Assert.Contains("EXPECTATION", traitsText, StringComparison.Ordinal);
            Assert.Contains("CAREER", traitsText, StringComparison.Ordinal);
            Assert.Contains("CAR", traitsText, StringComparison.Ordinal);
            AssertAccessibleHint(Assert.IsType<ComboBox>(view.FindName("CharacterV2TraitCategory")));
            AssertAccessibleHint(Assert.IsType<ListBox>(view.FindName("CharacterV2TraitList")));

            workbench.SelectedIndex = 3;
            Arrange(view);
            string progressionText = VisibleText(workbench, view);
            Assert.Contains("CAREER PROGRESSION", progressionText, StringComparison.Ordinal);
            Assert.Contains("Skill Points (SP)", progressionText, StringComparison.Ordinal);
            Assert.DoesNotContain("Career SP", progressionText, StringComparison.OrdinalIgnoreCase);
            AssertAccessibleHint(Assert.IsType<ComboBox>(view.FindName("CharacterMasteryFamilyPicker")));
            AssertAccessibleHint(FindVisibleNamedDescendant<ListBox>(view, "CharacterMasterySkillGraph"));

            TabControl progressionTabs = Descendants<TabControl>(workbench)
                .Single(control => !ReferenceEquals(control, workbench) && control.Items.Count == 2);
            progressionTabs.SelectedIndex = 1;
            Arrange(view);
            AssertAccessibleHint(Assert.IsType<ComboBox>(view.FindName("CharacterAttributeRailPicker")));
            AssertAccessibleHint(FindVisibleNamedDescendant<ListBox>(view, "CharacterAttributeNodeGraph"));

            workbench.SelectedIndex = 4;
            Arrange(view);
            AssertVisibleText(view, "HOW YOUR RESULT TARGET IS SET");
            AssertVisibleText(view, "WHAT SETS THE TARGET");
            AssertVisibleText(view, "HOW YOUR RACE RECORD CHANGES IT");
            AssertVisibleText(view, "RATINGS WRITTEN TO AMS2");

            foreach (string statusName in new[]
                     {
                         "CharacterDnaStatus", "CharacterTraitStatus",
                         "CharacterAttributeStatus", "CharacterReadyStatus",
                     })
            {
                AssertAccessibleHint(Assert.IsAssignableFrom<FrameworkElement>(view.FindName(statusName)));
            }

            string xaml = File.ReadAllText(Path.Combine(
                FindRepositoryRoot(), "src", "Companion.App", "Views", "CharacterView.xaml"));
            Assert.DoesNotContain("ToolTip=\"{Binding IconKey", xaml, StringComparison.Ordinal);

            DrainDispatcher();
            bindingErrors.AssertNone();
        });
    }

    [Theory]
    [InlineData(0.90)]
    [InlineData(1.00)]
    [InlineData(1.10)]
    [InlineData(1.25)]
    [InlineData(1.30)]
    public void CharacterView_ProgressionV2_AllTabsRemainUsableAtCompactViewport(double scale)
    {
        if (!WpfRenderHarness.IsSupported)
            return;

        WpfRenderHarness.RunSta(() =>
        {
            using var bindingErrors = new BindingErrorScope();
            var vm = CreateVersionTwoViewModel();
            var view = new CharacterView
            {
                DataContext = vm,
                LayoutTransform = new ScaleTransform(scale, scale),
            };
            Arrange(view);

            var transform = Assert.IsType<ScaleTransform>(view.LayoutTransform);
            Assert.Equal(scale, transform.ScaleX, 3);
            Assert.Equal(scale, transform.ScaleY, 3);

            var workbench = Assert.IsType<TabControl>(view.FindName("CharacterWorkbench"));
            TabItem[] outerTabs = workbench.Items.Cast<TabItem>()
                .Where(tab => tab.Visibility == Visibility.Visible)
                .ToArray();
            Assert.Equal(5, outerTabs.Length);

            foreach (TabItem tab in outerTabs)
            {
                workbench.SelectedItem = tab;
                Arrange(view);
                FrameworkElement content = Assert.IsAssignableFrom<FrameworkElement>(tab.Content);
                Assert.True(content.ActualWidth > 0, $"Outer tab {tab.Header} did not realize at {scale:P0}.");
                Assert.True(content.ActualHeight > 0, $"Outer tab {tab.Header} did not realize at {scale:P0}.");
                Assert.True(content.ActualWidth <= workbench.ActualWidth + 2,
                    $"Outer tab {tab.Header} overflowed its workbench at {scale:P0}.");
                Assert.True(content.ActualHeight <= workbench.ActualHeight + 2,
                    $"Outer tab {tab.Header} overflowed its workbench vertically at {scale:P0}.");
                AssertNoDisabledHorizontalOverflow(content, view, $"outer tab {tab.Header}", scale);

                if (ReferenceEquals(tab, outerTabs[3]))
                {
                    TabControl careerTabs = Descendants<TabControl>(content)
                        .Single(control => control.Items.Count == 2);
                    foreach (TabItem careerTab in careerTabs.Items.Cast<TabItem>())
                    {
                        careerTabs.SelectedItem = careerTab;
                        Arrange(view);
                        FrameworkElement careerContent = Assert.IsAssignableFrom<FrameworkElement>(careerTab.Content);
                        Assert.True(careerContent.ActualWidth > 0,
                            $"Career tab {careerTab.Header} did not realize at {scale:P0}.");
                        Assert.True(careerContent.ActualHeight > 0,
                            $"Career tab {careerTab.Header} did not realize at {scale:P0}.");
                        AssertNoDisabledHorizontalOverflow(
                            careerContent, view, $"career tab {careerTab.Header}", scale);
                    }
                }
            }

            FrameworkElement identity = Assert.IsAssignableFrom<FrameworkElement>(view.FindName("CharacterIdentityStrip"));
            FrameworkElement footer = Assert.IsAssignableFrom<FrameworkElement>(view.FindName("CharacterBudgetBar"));
            AssertWithinBounds(identity, view, "identity strip", scale);
            AssertWithinBounds(footer, view, "creator footer", scale);
            foreach (TabItem tab in outerTabs)
                AssertWithinBounds(tab, workbench, $"tab header {tab.Header}", scale);

            DrainDispatcher();
            bindingErrors.AssertNone();
        });
    }

    private static CharacterViewModel CreateVersionTwoViewModel()
    {
        string root = FindRepositoryRoot();
        string rulesDirectory = Path.Combine(root, "data", "rules");
        var rules = CharacterRules.Parse(File.ReadAllText(Path.Combine(rulesDirectory, "perks.json")));
        var racingDna = RacingDnaCatalog.Parse(
            File.ReadAllText(Path.Combine(rulesDirectory, "racing-dna-v2.json")), rules);
        var mastery = MasterySkillCatalog.Parse(
            File.ReadAllText(Path.Combine(rulesDirectory, "mastery-skills-v2.json")), rules, racingDna);
        var vm = new CharacterViewModel(
            rules, "Nova Reyes", racingDna,
            progressionVersion: CharacterLevelProgression.Level300Version,
            masterySkillCatalog: mastery);
        if (vm.HasRacingDnaChoice && vm.RacingDnaChoiceOptions.Count > 0)
            vm.RacingDnaChoiceValue = vm.RacingDnaChoiceOptions[0].Value;
        return vm;
    }

    private static void AssertAccessibleHint(FrameworkElement element)
    {
        Assert.False(string.IsNullOrWhiteSpace(AutomationProperties.GetName(element)),
            $"{element.Name} has no accessible name.");
        Assert.False(string.IsNullOrWhiteSpace(AutomationProperties.GetHelpText(element)),
            $"{element.Name} has no accessible help text.");
        Assert.NotNull(element.ToolTip);

        if (element.ToolTip is FrameworkElement tooltip && !double.IsPositiveInfinity(tooltip.MaxWidth))
        {
            Assert.InRange(tooltip.MaxWidth, 1, 340);
            if (tooltip is TextBlock text)
                Assert.Equal(TextWrapping.Wrap, text.TextWrapping);
        }
    }

    private static void Arrange(FrameworkElement view)
    {
        view.Measure(CreatorViewport);
        view.Arrange(new Rect(new Point(), CreatorViewport));
        view.UpdateLayout();
        WpfRenderHarness.Pump(DispatcherPriority.Loaded);
        WpfRenderHarness.Pump(DispatcherPriority.Render);
        view.UpdateLayout();
    }

    private static void DrainDispatcher()
    {
        WpfRenderHarness.Pump(DispatcherPriority.Background);
        WpfRenderHarness.Pump();
    }

    private static T FindVisibleNamedDescendant<T>(DependencyObject root, string name)
        where T : FrameworkElement =>
        Descendants<T>(root).Single(element =>
            string.Equals(element.Name, name, StringComparison.Ordinal) &&
            IsEffectivelyVisible(element, root));

    private static ContentControl FindVisibleDetail(
        DependencyObject root,
        object content,
        string expectedText) =>
        Descendants<ContentControl>(root).First(control =>
            ReferenceEquals(control.Content, content) &&
            IsEffectivelyVisible(control, root) &&
            Descendants<TextBlock>(control).Any(text =>
                string.Equals(text.Text, expectedText, StringComparison.Ordinal)));

    private static void AssertEffectRendered(DependencyObject detail, CharacterEffectLine effect)
    {
        AssertVisibleText(detail, effect.ClassificationLabel);
        AssertVisibleText(detail, effect.Text);
    }

    private static void AssertVisibleText(DependencyObject root, string expected)
    {
        Assert.Contains(Descendants<TextBlock>(root), text =>
            IsEffectivelyVisible(text, root) &&
            string.Equals(text.Text, expected, StringComparison.Ordinal));
    }

    private static string VisibleText(DependencyObject root, DependencyObject visibilityRoot)
    {
        var text = new StringBuilder();
        foreach (TextBlock block in Descendants<TextBlock>(root).Where(block =>
                     IsEffectivelyVisible(block, visibilityRoot)))
        {
            if (text.Length > 0)
                text.AppendLine();
            text.Append(block.Text);
        }
        return text.ToString();
    }

    private static void AssertNoDisabledHorizontalOverflow(
        DependencyObject content,
        DependencyObject visibilityRoot,
        string label,
        double scale)
    {
        foreach (ScrollViewer scroller in Descendants<ScrollViewer>(content).Where(scroller =>
                     IsEffectivelyVisible(scroller, visibilityRoot) &&
                     scroller.ActualWidth > 0 &&
                     scroller.HorizontalScrollBarVisibility == ScrollBarVisibility.Disabled))
        {
            scroller.UpdateLayout();
            if (scroller.ViewportWidth <= 0)
                continue;
            Assert.True(
                scroller.ExtentWidth <= scroller.ViewportWidth + 2,
                $"{label} has disabled horizontal scrolling but an extent of " +
                $"{scroller.ExtentWidth:0.##} over viewport {scroller.ViewportWidth:0.##} at {scale:P0}.");
        }
    }

    private static void AssertWithinBounds(
        FrameworkElement element,
        FrameworkElement ancestor,
        string label,
        double scale)
    {
        Rect bounds = element.TransformToAncestor(ancestor)
            .TransformBounds(new Rect(new Point(), element.RenderSize));
        Assert.True(bounds.Left >= -2,
            $"{label} starts outside its host at {scale:P0}: {bounds}.");
        Assert.True(bounds.Right <= ancestor.ActualWidth + 2,
            $"{label} ends outside its host at {scale:P0}: {bounds}, host {ancestor.ActualWidth:0.##}.");
        Assert.True(bounds.Top >= -2,
            $"{label} starts above its host at {scale:P0}: {bounds}.");
        Assert.True(bounds.Bottom <= ancestor.ActualHeight + 2,
            $"{label} ends below its host at {scale:P0}: {bounds}, host {ancestor.ActualHeight:0.##}.");
    }

    private static bool IsEffectivelyVisible(DependencyObject element, DependencyObject root)
    {
        for (DependencyObject? current = element; current is not null; current = VisualTreeHelper.GetParent(current))
        {
            if (current is FrameworkElement frameworkElement && frameworkElement.Visibility != Visibility.Visible)
                return false;
            if (ReferenceEquals(current, root))
                return true;
        }
        return false;
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        int count = VisualTreeHelper.GetChildrenCount(root);
        for (int index = 0; index < count; index++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(root, index);
            if (child is T typed)
                yield return typed;
            foreach (T descendant in Descendants<T>(child))
                yield return descendant;
        }
    }

    private sealed class BindingErrorScope : IDisposable
    {
        private readonly TraceSource _source = PresentationTraceSources.DataBindingSource;
        private readonly CollectingTraceListener _listener = new();
        private readonly SourceLevels _previousLevel;

        public BindingErrorScope()
        {
            _previousLevel = _source.Switch.Level;
            _source.Switch.Level = SourceLevels.Warning;
            _source.Listeners.Add(_listener);
        }

        public void AssertNone()
        {
            _source.Flush();
            Assert.True(_listener.Messages.Count == 0,
                "WPF binding errors were emitted:\n" + string.Join("\n", _listener.Messages));
        }

        public void Dispose()
        {
            _source.Listeners.Remove(_listener);
            _source.Switch.Level = _previousLevel;
            _listener.Dispose();
        }
    }

    private sealed class CollectingTraceListener : TraceListener
    {
        private readonly StringBuilder _current = new();
        public List<string> Messages { get; } = [];

        public override void Write(string? message) => _current.Append(message);

        public override void WriteLine(string? message)
        {
            _current.Append(message);
            string line = _current.ToString().Trim();
            if (line.Length > 0)
                Messages.Add(line);
            _current.Clear();
        }
    }

    private static string FindRepositoryRoot()
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Companion.slnx")))
                return directory.FullName;
        }

        throw new DirectoryNotFoundException(
            $"Could not find Companion.slnx above '{AppContext.BaseDirectory}'.");
    }
}
