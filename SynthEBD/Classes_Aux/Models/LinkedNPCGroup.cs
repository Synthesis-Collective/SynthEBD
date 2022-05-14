using Mutagen.Bethesda.Plugins;

namespace SynthEBD;

public class LinkedNPCGroup
{
    public string GroupName { get; set; } = "";
    public HashSet<FormKey> NPCFormKeys { get; set; } = new();
    public FormKey Primary { get; set; } = new();
}