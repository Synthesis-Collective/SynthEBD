using Mutagen.Bethesda.Plugins;

namespace SynthEBD;

/// <summary>
/// A group of NPCs that should receive linked (consistent) assignments, anchored to a
/// <see cref="Primary"/> NPC whose choices the rest of the group inherits.
/// </summary>
public class LinkedNPCGroup
{
    public string GroupName { get; set; } = "";
    public HashSet<FormKey> NPCFormKeys { get; set; } = new();
    public FormKey Primary { get; set; } = new();
}