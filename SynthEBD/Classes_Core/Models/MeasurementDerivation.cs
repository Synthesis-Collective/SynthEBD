using System;
using System.Collections.Generic;

namespace SynthEBD;

/// <summary>
/// Computes measurements from other, already-cached measurements of the same (preset, weight) slice,
/// so adding a measurement whose ingredients are already cached needs no mesh work at all.
/// <para>Every key-vertex measurement is built from <b>distance terms</b> between two key vertices: the
/// absolute separation on one axis (<see cref="MeasurementKind.AxisDistance"/>, and the axis-projected
/// operands of <see cref="MeasurementKind.RatioDistance"/>) or the 3D length
/// (<see cref="MeasurementKind.PointDistance"/>, and a ratio operand with no axis). The signed kinds
/// add a direction. A cached distance measurement states one term outright; a cached ratio links its
/// numerator and denominator terms, so one of them known gives the other. <see cref="Derive"/> collects
/// every term the slice's cached values pin down (propagating through the ratio links until nothing new
/// is learned) and then evaluates each requested measurement from those terms. A measurement it cannot
/// assemble is simply not returned; the caller falls back to the geometry scan for it.</para>
/// <para>Soundness rests on the cache contract: a cached value is only kept while its fingerprint
/// matches the current definition, and that fingerprint covers the key vertices it reads. So every
/// cached value was computed from the <i>current</i> key-vertex definitions, and a term between two
/// named key vertices means the same thing in every measurement that reads them.</para>
/// <para>Values are computed in double from the cached floats, so a derived value can differ from a
/// geometry-evaluated one in the last float digit -- far below any rule threshold's resolution.</para>
/// </summary>
public static class MeasurementDerivation
{
    private const int LengthAxis = 3; // term axis slot for full 3D length (the ratio's "no axis")

    /// <summary>Unordered vertex pair + axis (0..2 = X/Y/Z, 3 = length): an absolute distance.</summary>
    private readonly record struct Term(string A, string B, int Axis)
    {
        public static Term Of(string a, string b, int axis) =>
            string.CompareOrdinal(a, b) <= 0 ? new Term(a, b, axis) : new Term(b, a, axis);
    }

    /// <summary>Ordered vertex pair + axis + kind: a signed quantity (A.axis - B.axis, or the length signed
    /// by it). Swapping A and B negates it.</summary>
    private readonly record struct SignedTerm(string A, string B, int Axis, bool IsPointDistance);

    /// <summary>Values for those of <paramref name="targetNames"/> that can be computed from
    /// <paramref name="knownValues"/> without geometry. <paramref name="knownValues"/> must hold only
    /// values current under the present definitions (null = the evaluator produced no number; such a
    /// value contributes nothing). Names that are unknown, not key-vertex based, or not derivable are
    /// left out of the result.</summary>
    public static Dictionary<string, float> Derive(
        IEnumerable<MeasurementDefinition> definitions,
        IReadOnlyDictionary<string, float?> knownValues,
        IEnumerable<string> targetNames)
    {
        var result = new Dictionary<string, float>(StringComparer.Ordinal);
        if (definitions == null || knownValues == null || targetNames == null) return result;

        var byName = new Dictionary<string, MeasurementDefinition>(StringComparer.Ordinal);
        foreach (var d in definitions)
            if (d != null && !string.IsNullOrEmpty(d.Name) && !byName.ContainsKey(d.Name)) byName[d.Name] = d;

        var terms = new Dictionary<Term, double>();
        var signed = new Dictionary<SignedTerm, double>();
        var links = new List<(Term Num, Term Den, double Ratio)>();

        // 1. Every term a cached value states or links.
        foreach (var kv in knownValues)
        {
            if (!kv.Value.HasValue || !byName.TryGetValue(kv.Key, out var def)) continue;
            double v = kv.Value.Value;
            if (double.IsNaN(v) || double.IsInfinity(v)) continue;
            if (!TryRefs(def, out var r)) continue;

            switch (def.Kind)
            {
                case MeasurementKind.AxisDistance:
                    terms[Term.Of(r[0], r[1], (int)def.Axis)] = Math.Abs(v);
                    break;
                case MeasurementKind.PointDistance:
                    terms[Term.Of(r[0], r[1], LengthAxis)] = Math.Abs(v);
                    break;
                case MeasurementKind.SignedAxisDistance:
                    terms[Term.Of(r[0], r[1], (int)def.Axis)] = Math.Abs(v);
                    signed[new SignedTerm(r[0], r[1], (int)def.Axis, false)] = v;
                    break;
                case MeasurementKind.SignedPointDistance:
                    terms[Term.Of(r[0], r[1], LengthAxis)] = Math.Abs(v);
                    signed[new SignedTerm(r[0], r[1], (int)def.Axis, true)] = v;
                    break;
                case MeasurementKind.RatioDistance:
                    links.Add((Term.Of(r[0], r[1], OperandAxis(def.NumeratorAxis)),
                               Term.Of(r[2], r[3], OperandAxis(def.DenominatorAxis)), v));
                    break;
            }
        }

        // 2. Propagate through the ratio links: num = ratio * den, den = num / ratio.
        bool learned = true;
        while (learned && links.Count > 0)
        {
            learned = false;
            foreach (var (num, den, ratio) in links)
            {
                bool hasNum = terms.TryGetValue(num, out var n);
                bool hasDen = terms.TryGetValue(den, out var d);
                if (hasDen && !hasNum) { terms[num] = ratio * d; learned = true; }
                else if (hasNum && !hasDen && Math.Abs(ratio) > 1e-12) { terms[den] = n / ratio; learned = true; }
            }
        }

        // 3. Evaluate the requested measurements from the known terms.
        foreach (var name in targetNames)
        {
            if (string.IsNullOrEmpty(name) || result.ContainsKey(name)) continue;
            if (!byName.TryGetValue(name, out var def) || !TryRefs(def, out var r)) continue;

            double? value = def.Kind switch
            {
                MeasurementKind.AxisDistance => Get(terms, Term.Of(r[0], r[1], (int)def.Axis)),
                MeasurementKind.PointDistance => Get(terms, Term.Of(r[0], r[1], LengthAxis)),
                MeasurementKind.SignedAxisDistance => GetSigned(signed, r[0], r[1], (int)def.Axis, false),
                MeasurementKind.SignedPointDistance => GetSigned(signed, r[0], r[1], (int)def.Axis, true),
                MeasurementKind.RatioDistance => Ratio(
                    Get(terms, Term.Of(r[0], r[1], OperandAxis(def.NumeratorAxis))),
                    Get(terms, Term.Of(r[2], r[3], OperandAxis(def.DenominatorAxis)))),
                _ => null, // RegionVolume and anything else reads geometry, not key-vertex distances
            };
            if (value.HasValue) result[name] = (float)value.Value;
        }
        return result;
    }

    private static int OperandAxis(MeasurementAxis? axis) => axis.HasValue ? (int)axis.Value : LengthAxis;

    private static bool TryRefs(MeasurementDefinition def, out List<string> refs)
    {
        refs = def.VertexRefNames;
        int needed = def.Kind == MeasurementKind.RatioDistance ? 4 : 2;
        if (def.Kind == MeasurementKind.RegionVolume || refs == null || refs.Count < needed) return false;
        for (int i = 0; i < needed; i++) if (string.IsNullOrEmpty(refs[i])) return false;
        return true;
    }

    private static double? Get(Dictionary<Term, double> terms, Term t) => terms.TryGetValue(t, out var v) ? v : null;

    private static double? GetSigned(Dictionary<SignedTerm, double> signed, string a, string b, int axis, bool isPoint)
    {
        if (signed.TryGetValue(new SignedTerm(a, b, axis, isPoint), out var v)) return v;
        if (signed.TryGetValue(new SignedTerm(b, a, axis, isPoint), out var w)) return -w;
        return null;
    }

    /// <summary>Mirrors MeasurementMath.TryEvaluate's guard: a denominator under 1e-6 does not evaluate.
    /// Such a slice is left to the geometry scan rather than guessed at.</summary>
    private static double? Ratio(double? num, double? den)
        => num.HasValue && den.HasValue && den.Value >= 1e-6 ? num.Value / den.Value : null;
}
