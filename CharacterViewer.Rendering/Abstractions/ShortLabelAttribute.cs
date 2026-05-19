using System;

namespace CharacterViewer.Rendering;

/// <summary>Short, human-friendly label for an enum field — paired with the long-form
/// [Description] tooltip. Defined as a custom attribute because <see cref="System.ComponentModel.DisplayNameAttribute"/>
/// is not valid on Field targets (which is what enum members are), so it can't be applied
/// per-enum-value.
/// <para>Lives in CharacterViewer.Rendering so enums in either project — the persisted
/// <c>BoundingBoxCriterion</c> over in SynthEBD and the authoring-time
/// <see cref="BoxCriterionSelection"/> here — can both annotate their values.</para></summary>
[AttributeUsage(AttributeTargets.Field, AllowMultiple = false)]
public sealed class ShortLabelAttribute : Attribute
{
    public string Label { get; }
    public ShortLabelAttribute(string label) { Label = label; }
}
