using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using FluentAssertions;
using SynthEBD;
using Xunit;

namespace SynthEBD.Tests;

/// <summary>
/// Covers the Measurements-tab undo history's restore step (<see cref="VM_BodyTypeProfile.ApplyMeasurementSnapshot"/>)
/// and drag-and-drop reordering (<see cref="VM_BodyTypeProfile.MoveRows"/>). Rows are built without a parent
/// profile (their rename hooks are null-safe), so these run headless.
/// </summary>
public class MeasurementHistoryTests
{
    private static VM_MeasurementDefinition Row(string name, MeasurementKind kind = MeasurementKind.PointDistance)
        => new(new MeasurementDefinition { Name = name, Kind = kind, VertexRefNames = new() { "A", "B" } }, null!);

    private static VM_BodyTypeProfile.MeasurementSnapshot Snap(IEnumerable<VM_MeasurementDefinition> rows)
        => new(rows.Select(r => (r, VM_BodyTypeProfile.MeasurementRowState.Of(r))).ToList());

    [Fact]
    public void Restore_ReaddsDeletedRow_AtItsPosition_AndKeepsIdentity()
    {
        var a = Row("a"); var b = Row("b"); var c = Row("c");
        var rows = new ObservableCollection<VM_MeasurementDefinition> { a, b, c };
        var before = Snap(rows);

        rows.Remove(b);
        VM_BodyTypeProfile.ApplyMeasurementSnapshot(rows, before);

        rows.Should().Equal(a, b, c);
        Snap(rows).SameAs(before).Should().BeTrue();
    }

    [Fact]
    public void Restore_RevertsFieldEdits_AndOrder()
    {
        var a = Row("a"); var b = Row("b");
        var rows = new ObservableCollection<VM_MeasurementDefinition> { a, b };
        var before = Snap(rows);

        a.Kind = MeasurementKind.AxisDistance;
        a.Axis = MeasurementAxis.Y;
        b.Enabled = false;
        rows.Move(0, 1);
        VM_BodyTypeProfile.ApplyMeasurementSnapshot(rows, before);

        rows.Should().Equal(a, b);
        a.Kind.Should().Be(MeasurementKind.PointDistance);
        a.Axis.Should().Be(MeasurementAxis.X);
        b.Enabled.Should().BeTrue();
    }

    [Fact]
    public void Restore_UndoesNameSwap_WithoutMergingNames()
    {
        var a = Row("left"); var b = Row("right");
        var rows = new ObservableCollection<VM_MeasurementDefinition> { a, b };
        var before = Snap(rows);

        // Each rename passes through a (prev, new) pair, which is what drives the rule / cache cascade.
        var renames = new List<(string From, string To)>();
        foreach (var r in rows)
        {
            string last = r.Name;
            r.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName != nameof(VM_MeasurementDefinition.Name)) return;
                renames.Add((last, r.Name));
                last = r.Name;
            };
        }
        a.Name = "tmp"; b.Name = "left"; a.Name = "right"; // user swaps the two names
        renames.Clear();

        VM_BodyTypeProfile.ApplyMeasurementSnapshot(rows, before);

        a.Name.Should().Be("left");
        b.Name.Should().Be("right");
        // No rename ever goes straight from one real name to another row's real name.
        renames.Should().NotContain(r => (r.From == "right" && r.To == "left") || (r.From == "left" && r.To == "right"));
    }

    [Theory]
    // rows a b c d e; move {b, d} to insertion point 5 (end) -> a c e b d
    [InlineData(new[] { 1, 3 }, 5, "acebd")]
    // move {d} to the top
    [InlineData(new[] { 3 }, 0, "dabce")]
    // move {a, b} to between d and e (insertion point 4) -> c d a b e
    [InlineData(new[] { 0, 1 }, 4, "cdabe")]
    // drop onto its own position: unchanged
    [InlineData(new[] { 2 }, 2, "abcde")]
    [InlineData(new[] { 2 }, 3, "abcde")]
    public void MoveRows_PlacesBlockAtInsertionPoint(int[] picked, int insertIndex, string expected)
    {
        var rows = new ObservableCollection<string>("abcde".Select(ch => ch.ToString()));
        var moving = picked.Select(i => rows[i]).ToList();
        VM_BodyTypeProfile.MoveRows(rows, moving, insertIndex);
        string.Concat(rows).Should().Be(expected);
    }
}
