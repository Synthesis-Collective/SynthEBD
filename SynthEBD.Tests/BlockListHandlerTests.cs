using FluentAssertions;
using Mutagen.Bethesda.Skyrim;
using Xunit;

namespace SynthEBD.Tests;

/// <summary>
/// Unit tests for <see cref="BlockListHandler.MergeBlockedPlugins"/>, the per-NPC block-flag merge. Every
/// flag (including each per-head-part-type flag) must OR-aggregate across the block-list plugins in an NPC's
/// context chain. The head-part-type case is the regression guard for the old <c>else { ... = false; }</c>,
/// which let a later plugin in the chain clear a per-type block set by an earlier one.
/// </summary>
public class BlockListHandlerTests
{
    [Fact]
    public void HeadPartType_LaterPluginDoesNotClearEarlierBlock()
    {
        // Winning override blocks the Eyes head-part type; the source plugin blocks Head Parts generally but
        // not Eyes. With the old else-branch the source plugin cleared Eyes; it must now survive.
        var winning = new BlockedPlugin { HeadParts = true };
        winning.HeadPartTypes[HeadPart.TypeEnum.Eyes] = true;

        var source = new BlockedPlugin { HeadParts = true }; // all HeadPartTypes default to false

        var merged = BlockListHandler.MergeBlockedPlugins(new[] { winning, source });

        merged.HeadParts.Should().BeTrue();
        merged.HeadPartTypes[HeadPart.TypeEnum.Eyes].Should().BeTrue("an earlier plugin's per-type block must not be cleared by a later plugin");
        merged.HeadPartTypes[HeadPart.TypeEnum.Hair].Should().BeFalse("no contributing plugin blocked Hair");
    }

    [Fact]
    public void HeadPartType_OrAggregatesAcrossPlugins()
    {
        var a = new BlockedPlugin { HeadParts = true };
        a.HeadPartTypes[HeadPart.TypeEnum.Eyes] = true;
        var b = new BlockedPlugin { HeadParts = true };
        b.HeadPartTypes[HeadPart.TypeEnum.Hair] = true;

        var merged = BlockListHandler.MergeBlockedPlugins(new[] { a, b });

        merged.HeadPartTypes[HeadPart.TypeEnum.Eyes].Should().BeTrue();
        merged.HeadPartTypes[HeadPart.TypeEnum.Hair].Should().BeTrue();
        merged.HeadPartTypes[HeadPart.TypeEnum.Face].Should().BeFalse();
    }

    [Fact]
    public void TopLevelFlags_OrAggregate()
    {
        var a = new BlockedPlugin { Assets = false, Height = true, BodyShape = false, VanillaBodyPath = false };
        var b = new BlockedPlugin { Assets = true, Height = false, BodyShape = true, VanillaBodyPath = true };

        var merged = BlockListHandler.MergeBlockedPlugins(new[] { a, b });

        merged.Assets.Should().BeTrue();
        merged.Height.Should().BeTrue();
        merged.BodyShape.Should().BeTrue();
        merged.VanillaBodyPath.Should().BeTrue();
    }

    [Fact]
    public void SinglePlugin_PerTypeSelectionPassesThrough()
    {
        var only = new BlockedPlugin { HeadParts = true };
        only.HeadPartTypes[HeadPart.TypeEnum.Scars] = true;

        var merged = BlockListHandler.MergeBlockedPlugins(new[] { only });

        merged.HeadPartTypes[HeadPart.TypeEnum.Scars].Should().BeTrue();
        merged.HeadPartTypes[HeadPart.TypeEnum.Eyes].Should().BeFalse();
    }

    [Fact]
    public void NoContributingPlugins_NothingBlocked()
    {
        // Null slots model context-chain plugins that are not on the block list.
        var merged = BlockListHandler.MergeBlockedPlugins(new BlockedPlugin?[] { null, null });

        merged.Assets.Should().BeFalse();
        merged.HeadParts.Should().BeFalse();
        merged.HeadPartTypes[HeadPart.TypeEnum.Eyes].Should().BeFalse();
    }
}
