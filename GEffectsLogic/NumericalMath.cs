// GEffectsLogic
// Copyright (C) 2026 AMPW
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY, without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with this program.  If not, see <http://www.gnu.org/licenses/>.

namespace GEffectsLogic;

internal static class NumericalMath
{
    internal const int RadauStageCount = 3;
    private const int MomentPowerCacheSize = 32;

    internal static readonly double[] RadauC = CreateRadauC();
    internal static readonly double[][] RadauA = CreateRadauA();

    internal struct Matrix3
    {
        internal double M00;
        internal double M01;
        internal double M02;
        internal double M10;
        internal double M11;
        internal double M12;
        internal double M20;
        internal double M21;
        internal double M22;

        internal double this[int row, int column]
        {
            get => row switch
            {
                0 => column switch
                {
                    0 => M00,
                    1 => M01,
                    2 => M02,
                    _ => throw new ArgumentOutOfRangeException(nameof(column))
                },
                1 => column switch
                {
                    0 => M10,
                    1 => M11,
                    2 => M12,
                    _ => throw new ArgumentOutOfRangeException(nameof(column))
                },
                2 => column switch
                {
                    0 => M20,
                    1 => M21,
                    2 => M22,
                    _ => throw new ArgumentOutOfRangeException(nameof(column))
                },
                _ => throw new ArgumentOutOfRangeException(nameof(row))
            };
            set
            {
                switch (row)
                {
                    case 0:
                        switch (column)
                        {
                            case 0: M00 = value; break;
                            case 1: M01 = value; break;
                            case 2: M02 = value; break;
                            default: throw new ArgumentOutOfRangeException(nameof(column));
                        }
                        break;
                    case 1:
                        switch (column)
                        {
                            case 0: M10 = value; break;
                            case 1: M11 = value; break;
                            case 2: M12 = value; break;
                            default: throw new ArgumentOutOfRangeException(nameof(column));
                        }
                        break;
                    case 2:
                        switch (column)
                        {
                            case 0: M20 = value; break;
                            case 1: M21 = value; break;
                            case 2: M22 = value; break;
                            default: throw new ArgumentOutOfRangeException(nameof(column));
                        }
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(row));
                }
            }
        }
    }

    internal struct MatrixMoments
    {
        internal Matrix3 J0;
        internal Matrix3 J1;
        internal Matrix3 J2;

        internal double this[int moment, int row, int column]
        {
            get => moment switch
            {
                0 => J0[row, column],
                1 => J1[row, column],
                2 => J2[row, column],
                _ => throw new ArgumentOutOfRangeException(nameof(moment))
            };
            set
            {
                switch (moment)
                {
                    case 0: J0[row, column] = value; break;
                    case 1: J1[row, column] = value; break;
                    case 2: J2[row, column] = value; break;
                    default: throw new ArgumentOutOfRangeException(nameof(moment));
                }
            }
        }

        internal void Set(int moment, Matrix3 value)
        {
            switch (moment)
            {
                case 0: J0 = value; break;
                case 1: J1 = value; break;
                case 2: J2 = value; break;
                default: throw new ArgumentOutOfRangeException(nameof(moment));
            }
        }
    }

    internal sealed class MomentPowerCache
    {
        private readonly Matrix3[] powers;

        internal Matrix3 ScaledA { get; }
        internal double Norm { get; }
        internal int PowerCount => powers.Length;

        internal MomentPowerCache(Matrix3 scaledA, double norm, Matrix3[] powers)
        {
            ScaledA = scaledA;
            Norm = norm;
            this.powers = powers;
        }

        internal Matrix3 PowerAt(int exponent) => powers[exponent];
    }

    internal static bool IsFinite(double value) =>
        !double.IsNaN(value) && !double.IsInfinity(value);

    private static double[] CreateRadauC()
    {
        var r = Math.Sqrt(6.0);
        return [(4.0 - r) / 10.0, (4.0 + r) / 10.0, 1.0];
    }

    private static double[][] CreateRadauA()
    {
        var r = Math.Sqrt(6.0);
        return
        [
            [(88.0 - 7.0 * r) / 360.0, (296.0 - 169.0 * r) / 1800.0, (-2.0 + 3.0 * r) / 225.0],
            [(296.0 + 169.0 * r) / 1800.0, (88.0 + 7.0 * r) / 360.0, (-2.0 - 3.0 * r) / 225.0],
            [(16.0 - r) / 36.0, (16.0 + r) / 36.0, 1.0 / 9.0]
        ];
    }

    internal static double[][] CreateMatrix(int size)
    {
        var matrix = new double[size][];
        for (var row = 0; row < size; row++) matrix[row] = new double[size];
        return matrix;
    }

    internal static double Sigmoid(double x) =>
        x >= 0.0
            ? 1.0 / (1.0 + Math.Exp(-x))
            : Math.Exp(x) / (1.0 + Math.Exp(x));

    internal static double ExpMinusOne(double x) =>
        Math.Abs(x) < 1e-4
            ? x * (1.0 + x * (0.5 + x * (1.0 / 6.0 + x / 24.0)))
            : Math.Exp(x) - 1.0;

    internal static double LogOnePlus(double x) =>
        Math.Abs(x) < 1e-4
            ? x * (1.0 + x * (-0.5 + x * (1.0 / 3.0 + x * (-0.25 + x / 5.0))))
            : Math.Log(1.0 + x);

    internal static double ExponentialConvolution(double initial, double from, double to,
        double decayRate, Func<double, double> source)
    {
        var span = to - from;
        if (span <= 0.0) return initial;

        var x = decayRate * span;
        var decay = Math.Exp(-x);
        var w = -ExpMinusOne(-x);
        var mass = decayRate == 0.0 || x == 0.0 ? span : w / decayRate;
        var nodes = GaussLegendre16Nodes;
        var weights = GaussLegendre16Weights;
        var sum = 0.0;
        for (var node = 0; node < nodes.Length; node++)
        {
            var offset = decayRate == 0.0 || x == 0.0
                ? from + nodes[node] * span
                : to + (x >= 1.0
                    ? Math.Log(decay + w * nodes[node])
                    : LogOnePlus(-w * (1.0 - nodes[node]))) / decayRate;
            sum += weights[node] * source(offset);
        }

        return initial * decay + mass * sum;
    }

    internal static double[] GaussLegendre16Nodes { get; } =
    [
        0.005299532504175031, 0.0277124884633837, 0.06718439880608412, 0.1222977958224985,
        0.19106187779867811, 0.2709916111713863, 0.35919822461037054, 0.4524937450811813,
        0.5475062549188188, 0.6408017753896295, 0.7290083888286136, 0.8089381222013219,
        0.8777022041775016, 0.9328156011939159, 0.9722875115366163, 0.994700467495825
    ];

    internal static double[] GaussLegendre16Weights { get; } =
    [
        0.013576229705877029, 0.031126761969323888, 0.04757925584124645, 0.06231448562776697,
        0.07479799440828841, 0.08457825969750128, 0.0913017075224618, 0.09472530522753424,
        0.09472530522753424, 0.0913017075224618, 0.08457825969750128, 0.07479799440828841,
        0.06231448562776697, 0.04757925584124645, 0.031126761969323888, 0.013576229705877029
    ];

    internal static double[] CubicCoefficients(double[] nodes, double[] values)
    {
        var matrix = CreateMatrix(4);
        for (var row = 0; row < 4; row++)
        {
            var t = nodes[row];
            matrix[row][0] = 1.0;
            matrix[row][1] = t;
            matrix[row][2] = t * t;
            matrix[row][3] = t * t * t;
        }

        var coefficients = new double[4];
        if (!TrySolveLinear(matrix, values, coefficients))
            throw new InvalidOperationException("Cubic interpolation solve failed.");
        return coefficients;
    }

    internal static double EvaluateCubic(double[] coefficients, double t) =>
        coefficients[0] + t * (coefficients[1] + t * (coefficients[2] + t * coefficients[3]));

    internal static int QuadraticRootsInInterval(double a, double b, double c, double min, double max, double[] roots)
    {
        var count = 0;
        if (Math.Abs(a) <= 1e-300)
        {
            if (Math.Abs(b) > 1e-300)
            {
                TryAddRoot(roots, ref count, -c / b, min, max);
            }

            return count;
        }

        var discriminant = b * b - 4.0 * a * c;
        if (discriminant < 0.0)
        {
            return 0;
        }

        var sqrt = Math.Sqrt(discriminant);
        var q = -0.5 * (b + (b >= 0.0 ? sqrt : -sqrt));
        TryAddRoot(roots, ref count, q / a, min, max);
        TryAddRoot(roots, ref count, c / q, min, max);
        Array.Sort(roots, 0, count);
        return count;
    }

    private static void TryAddRoot(double[] roots, ref int count, double root, double min, double max)
    {
        if (IsFinite(root) && root > min && root < max &&
            (count == 0 || Math.Abs(root - roots[count - 1]) > 1e-12))
        {
            roots[count++] = root;
        }
    }

    internal static bool TrySolveLinear(double[][] matrix, double[] rhs, double[] x)
    {
        var n = rhs.Length;
        var a = new double[n][];
        for (var row = 0; row < n; row++) a[row] = (double[])matrix[row].Clone();
        var b = (double[])rhs.Clone();
        return TrySolveLinearInPlace(a, b, x);
    }

    internal static bool TrySolveLinearInPlace(double[][] matrix, double[] rhs, double[] x)
    {
        var n = rhs.Length;
        var a = matrix;
        var b = rhs;

        for (var column = 0; column < n; column++)
        {
            var pivot = column;
            var pivotMagnitude = Math.Abs(a[column][column]);
            for (var row = column + 1; row < n; row++)
            {
                var magnitude = Math.Abs(a[row][column]);
                if (magnitude > pivotMagnitude)
                {
                    pivotMagnitude = magnitude;
                    pivot = row;
                }
            }

            if (pivotMagnitude == 0.0) return false;

            if (pivot != column)
            {
                (a[pivot], a[column]) = (a[column], a[pivot]);
                (b[pivot], b[column]) = (b[column], b[pivot]);
            }

            for (var row = column + 1; row < n; row++)
            {
                var factor = a[row][column] / a[column][column];
                for (var k = column; k < n; k++) a[row][k] -= factor * a[column][k];
                b[row] -= factor * b[column];
            }
        }

        for (var row = n - 1; row >= 0; row--)
        {
            var sum = b[row];
            for (var k = row + 1; k < n; k++) sum -= a[row][k] * x[k];
            x[row] = sum / a[row][row];
        }

        return true;
    }

    private static Matrix3 IdentityMatrix(int size)
    {
        var matrix = default(Matrix3);
        if (size >= 1) matrix.M00 = 1.0;
        if (size >= 2) matrix.M11 = 1.0;
        if (size >= 3) matrix.M22 = 1.0;
        return matrix;
    }

    private static Matrix3 MultiplyMatrices(in Matrix3 a, in Matrix3 b, int size)
    {
        var result = default(Matrix3);
        if (size == 3)
        {
            result.M00 = a.M00 * b.M00 + a.M01 * b.M10 + a.M02 * b.M20;
            result.M01 = a.M00 * b.M01 + a.M01 * b.M11 + a.M02 * b.M21;
            result.M02 = a.M00 * b.M02 + a.M01 * b.M12 + a.M02 * b.M22;
            result.M10 = a.M10 * b.M00 + a.M11 * b.M10 + a.M12 * b.M20;
            result.M11 = a.M10 * b.M01 + a.M11 * b.M11 + a.M12 * b.M21;
            result.M12 = a.M10 * b.M02 + a.M11 * b.M12 + a.M12 * b.M22;
            result.M20 = a.M20 * b.M00 + a.M21 * b.M10 + a.M22 * b.M20;
            result.M21 = a.M20 * b.M01 + a.M21 * b.M11 + a.M22 * b.M21;
            result.M22 = a.M20 * b.M02 + a.M21 * b.M12 + a.M22 * b.M22;
        }
        else if (size == 2)
        {
            result.M00 = a.M00 * b.M00 + a.M01 * b.M10;
            result.M01 = a.M00 * b.M01 + a.M01 * b.M11;
            result.M10 = a.M10 * b.M00 + a.M11 * b.M10;
            result.M11 = a.M10 * b.M01 + a.M11 * b.M11;
        }
        else if (size == 1)
        {
            result.M00 = a.M00 * b.M00;
        }
        return result;
    }

    private static Matrix3 ScaleMatrix(in Matrix3 matrix, double scale, int size)
    {
        var result = default(Matrix3);
        if (size >= 1) result.M00 = matrix.M00 * scale;
        if (size >= 2)
        {
            result.M01 = matrix.M01 * scale;
            result.M10 = matrix.M10 * scale;
            result.M11 = matrix.M11 * scale;
        }
        if (size >= 3)
        {
            result.M02 = matrix.M02 * scale;
            result.M12 = matrix.M12 * scale;
            result.M20 = matrix.M20 * scale;
            result.M21 = matrix.M21 * scale;
            result.M22 = matrix.M22 * scale;
        }
        return result;
    }

    private static Matrix3 AddMatrices(in Matrix3 left, in Matrix3 right, int size,
        double rightScale = 1.0)
    {
        var result = default(Matrix3);
        if (size >= 1) result.M00 = left.M00 + right.M00 * rightScale;
        if (size >= 2)
        {
            result.M01 = left.M01 + right.M01 * rightScale;
            result.M10 = left.M10 + right.M10 * rightScale;
            result.M11 = left.M11 + right.M11 * rightScale;
        }
        if (size >= 3)
        {
            result.M02 = left.M02 + right.M02 * rightScale;
            result.M12 = left.M12 + right.M12 * rightScale;
            result.M20 = left.M20 + right.M20 * rightScale;
            result.M21 = left.M21 + right.M21 * rightScale;
            result.M22 = left.M22 + right.M22 * rightScale;
        }
        return result;
    }

    private static double MaxAbsEntry(in Matrix3 matrix)
    {
        if (!IsFinite(matrix.M00) || !IsFinite(matrix.M01) ||
            !IsFinite(matrix.M02) || !IsFinite(matrix.M10) ||
            !IsFinite(matrix.M11) || !IsFinite(matrix.M12) ||
            !IsFinite(matrix.M20) || !IsFinite(matrix.M21) ||
            !IsFinite(matrix.M22))
            return double.NaN;
        return Math.Max(
            Math.Max(Math.Max(Math.Abs(matrix.M00), Math.Abs(matrix.M01)),
                Math.Abs(matrix.M02)),
            Math.Max(Math.Max(Math.Max(Math.Abs(matrix.M10), Math.Abs(matrix.M11)),
                    Math.Abs(matrix.M12)),
                Math.Max(Math.Max(Math.Abs(matrix.M20), Math.Abs(matrix.M21)),
                    Math.Abs(matrix.M22))));
    }

    private static double MaxRowSum(in Matrix3 matrix, int size)
    {
        if (size == 3)
        {
            var row0 = Math.Abs(matrix.M00) + Math.Abs(matrix.M01) +
                       Math.Abs(matrix.M02);
            var row1 = Math.Abs(matrix.M10) + Math.Abs(matrix.M11) +
                       Math.Abs(matrix.M12);
            var row2 = Math.Abs(matrix.M20) + Math.Abs(matrix.M21) +
                       Math.Abs(matrix.M22);
            return IsFinite(row0) && IsFinite(row1) && IsFinite(row2)
                ? Math.Max(row0, Math.Max(row1, row2))
                : double.NaN;
        }
        if (size == 2)
        {
            var row0 = Math.Abs(matrix.M00) + Math.Abs(matrix.M01);
            var row1 = Math.Abs(matrix.M10) + Math.Abs(matrix.M11);
            return IsFinite(row0) && IsFinite(row1)
                ? Math.Max(row0, row1)
                : double.NaN;
        }
        if (size == 1)
        {
            var row = Math.Abs(matrix.M00);
            return IsFinite(row) ? row : double.NaN;
        }
        return 0.0;
    }

    // Scalar J_m(u*a) = integral_0^u exp((u - s) a) s^m ds for m = 0..2 in j.
    // Closed forms outside |a*u| < 0.5, Taylor series inside.
    internal static bool ScalarMoments(double a, double extent, double[] j)
    {
        if (!IsFinite(a) || !IsFinite(extent) || extent < 0.0) return false;
        if (extent == 0.0)
        {
            j[0] = 0.0;
            j[1] = 0.0;
            j[2] = 0.0;
            return true;
        }
        var x = a * extent;
        if (!IsFinite(x)) return false;
        if (Math.Abs(x) < 0.5)
        {
            var extentPower = extent;
            for (var m = 0; m < 3; m++)
            {
                var sum = 0.0;
                var term = 1.0 / Factorial(m + 1);
                for (var k = 0; k < 64; k++)
                {
                    var previous = sum;
                    sum += term;
                    if (Math.Abs(term) <= 1e-18 * Math.Max(1.0, Math.Abs(sum))) break;
                    if (sum == previous) break;
                    term *= x / (m + k + 2);
                }
                j[m] = extentPower * Factorial(m) * sum;
                extentPower *= extent;
            }
            return IsFinite(j[0]) && IsFinite(j[1]) && IsFinite(j[2]);
        }
        if (a == 0.0) return false;
        var exponential = Math.Exp(x);
        if (!IsFinite(exponential)) return false;
        var a2 = a * a;
        j[0] = (exponential - 1.0) / a;
        j[1] = (exponential - 1.0 - x) / a2;
        j[2] = (2.0 * (exponential - 1.0) - 2.0 * x - x * x) / (a2 * a);
        return IsFinite(j[0]) && IsFinite(j[1]) && IsFinite(j[2]);
    }

    private static double Factorial(int n)
    {
        var value = 1.0;
        for (var i = 2; i <= n; i++) value *= i;
        return value;
    }

    // Test wrapper; production callers use the fixed-size value result directly.
    internal static double[][][]? MomentMatrices(double[][] a, double extent)
    {
        if (!TryMomentMatrices(a, 1.0, extent, out var moments)) return null;
        var result = new double[3][][];
        for (var moment = 0; moment < 3; moment++)
        {
            result[moment] = CreateMatrix(a.Length);
            for (var row = 0; row < a.Length; row++)
                for (var column = 0; column < a.Length; column++)
                    result[moment][row][column] = moments[moment, row, column];
        }
        return result;
    }

    private static bool TryScaleMatrix(double[][] baseMatrix, int size, double scale,
        out Matrix3 scaledMatrix)
    {
        scaledMatrix = default;
        if (size > 3 || !IsFinite(scale)) return false;
        for (var row = 0; row < size; row++)
        {
            if (baseMatrix[row] == null || baseMatrix[row].Length < size) return false;
            for (var column = 0; column < size; column++)
            {
                var value = baseMatrix[row][column];
                if (!IsFinite(value)) return false;
                value *= scale;
                if (!IsFinite(value)) return false;
                scaledMatrix[row, column] = value;
            }
        }
        return true;
    }

    internal static MomentPowerCache? CreateMomentPowerCache(double[][] baseMatrix,
        double scale)
    {
        if (baseMatrix.Length != 3 ||
            !TryScaleMatrix(baseMatrix, 3, scale, out var scaledA))
            return null;

        var norm = MaxRowSum(in scaledA, 3);
        if (!IsFinite(norm) || norm <= 0.0 || norm > 8.0) return null;

        var powers = new Matrix3[MomentPowerCacheSize];
        powers[0] = IdentityMatrix(3);
        for (var exponent = 1; exponent < powers.Length; exponent++)
        {
            var previous = powers[exponent - 1];
            powers[exponent] = MultiplyMatrices(in previous, in scaledA, 3);
            if (!IsFinite(MaxAbsEntry(in powers[exponent]))) return null;
        }
        return new MomentPowerCache(scaledA, norm, powers);
    }

    // J_m(u*A) = integral_0^u exp((u - s) A) s^m ds for m = 0..2, evaluated as a
    // series at extent u then doubled while ||u*A|| > 0.5 (max row sum norm);
    // arithmetic rescaling only, not physiological stepping.
    internal static bool TryMomentMatrices(double[][] baseMatrix, double scale,
        double extent, out MatrixMoments moments) =>
        TryMomentMatricesCore(baseMatrix, scale, extent, null, out moments);

    internal static bool TryMomentMatrices(MomentPowerCache cache, double extent,
        out MatrixMoments moments) =>
        TryMomentMatricesCore(null, 1.0, extent, cache, out moments);

    private static bool TryMomentMatricesCore(double[][]? baseMatrix, double scale,
        double extent, MomentPowerCache? cache, out MatrixMoments moments)
    {
        moments = default;
        var size = cache == null ? baseMatrix!.Length : 3;
        if (size > 3 || !IsFinite(scale) || !IsFinite(extent) || extent < 0.0)
            return false;

        Matrix3 a;
        if (cache == null)
        {
            if (!TryScaleMatrix(baseMatrix!, size, scale, out a)) return false;
        }
        else
        {
            a = cache.ScaledA;
        }

        if (extent == 0.0 || size == 0) return true;
        var norm = MaxRowSum(a, size);
        if (!IsFinite(norm)) return false;
        if (norm == 0.0)
        {
            var power = extent;
            for (var moment = 0; moment < 3; moment++)
            {
                var diagonal = power / (moment + 1);
                if (!IsFinite(diagonal)) return false;
                for (var i = 0; i < size; i++)
                    moments[moment, i, i] = diagonal;
                power *= extent;
            }
            return true;
        }

        var u = extent;
        for (var halvings = 0; u * norm > 0.5; halvings++)
        {
            if (halvings > 128 || u == 0.0) return false;
            u *= 0.5;
        }

        var useCachedPowers = cache != null && extent <= 1.0;
        var cachedPowerScale = u;
        var scaledA = ScaleMatrix(in a, u, size);
        if (!IsFinite(MaxAbsEntry(in scaledA))) return false;
        var powerMatrix = IdentityMatrix(size);
        var coefficient0 = u;
        var coefficient1 = u * u / 2.0;
        var coefficient2 = u * u * u / 3.0;
        if (!IsFinite(coefficient0) || !IsFinite(coefficient1) ||
            !IsFinite(coefficient2))
            return false;

        var j0 = default(Matrix3);
        var j1 = default(Matrix3);
        var j2 = default(Matrix3);
        for (var i = 0; i < size; i++)
        {
            j0[i, i] = coefficient0;
            j1[i, i] = coefficient1;
            j2[i, i] = coefficient2;
        }

        var converged = false;
        for (var iteration = 1; iteration <= 256; iteration++)
        {
            if (useCachedPowers && cache != null && iteration < cache.PowerCount)
            {
                var cachedPower = cache.PowerAt(iteration);
                powerMatrix = ScaleMatrix(in cachedPower, cachedPowerScale, size);
                cachedPowerScale *= u;
            }
            else
            {
                powerMatrix = MultiplyMatrices(in powerMatrix, in scaledA, size);
            }
            var powerMagnitude = MaxAbsEntry(in powerMatrix);
            if (!IsFinite(powerMagnitude)) return false;
            coefficient0 /= iteration + 1;
            coefficient1 /= iteration + 2;
            coefficient2 /= iteration + 3;

            var termMagnitude0 = powerMagnitude * Math.Abs(coefficient0);
            var termMagnitude1 = powerMagnitude * Math.Abs(coefficient1);
            var termMagnitude2 = powerMagnitude * Math.Abs(coefficient2);
            if (!IsFinite(termMagnitude0) || !IsFinite(termMagnitude1) ||
                !IsFinite(termMagnitude2))
                return false;

            j0 = AddMatrices(in j0, in powerMatrix, size, coefficient0);
            j1 = AddMatrices(in j1, in powerMatrix, size, coefficient1);
            j2 = AddMatrices(in j2, in powerMatrix, size, coefficient2);
            var maximum0 = MaxAbsEntry(in j0);
            var maximum1 = MaxAbsEntry(in j1);
            var maximum2 = MaxAbsEntry(in j2);
            if (!IsFinite(maximum0) || !IsFinite(maximum1) || !IsFinite(maximum2))
                return false;

            if (termMagnitude0 <= 1e-18 * Math.Max(1.0, maximum0) &&
                termMagnitude1 <= 1e-18 * Math.Max(1.0, maximum1) &&
                termMagnitude2 <= 1e-18 * Math.Max(1.0, maximum2))
            {
                converged = true;
                break;
            }
        }
        if (!converged || !IsFinite(MaxRowSum(in j0, size)) ||
            !IsFinite(MaxRowSum(in j1, size)) || !IsFinite(MaxRowSum(in j2, size)))
            return false;
        moments.J0 = j0;
        moments.J1 = j1;
        moments.J2 = j2;

        var exponential = IdentityMatrix(size);
        var aj0 = MultiplyMatrices(in a, in moments.J0, size);
        exponential = AddMatrices(in exponential, in aj0, size);

        while (u < extent)
        {
            if (u == 0.0) return false;
            var eNew = MultiplyMatrices(in exponential, in exponential, size);
            var ej0 = MultiplyMatrices(in exponential, in moments.J0, size);
            var ej1 = MultiplyMatrices(in exponential, in moments.J1, size);
            var ej2 = MultiplyMatrices(in exponential, in moments.J2, size);
            var j0New = AddMatrices(in ej0, in moments.J0, size);
            var scaledJ0 = ScaleMatrix(in moments.J0, u, size);
            var j1Sum = AddMatrices(in ej1, in scaledJ0, size);
            var j1New = AddMatrices(in j1Sum, in moments.J1, size);
            var scaledJ0Squared = ScaleMatrix(in moments.J0, u * u, size);
            var scaledJ1Twice = ScaleMatrix(in moments.J1, 2.0 * u, size);
            var j2First = AddMatrices(in ej2, in scaledJ0Squared, size);
            var j2Second = AddMatrices(in j2First, in scaledJ1Twice, size);
            var j2New = AddMatrices(in j2Second, in moments.J2, size);
            exponential = eNew;
            moments.J0 = j0New;
            moments.J1 = j1New;
            moments.J2 = j2New;
            if (!IsFinite(MaxRowSum(in moments.J0, size))) return false;
            u *= 2.0;
        }

        return IsFinite(MaxRowSum(in moments.J0, size)) &&
               IsFinite(MaxRowSum(in moments.J1, size)) &&
               IsFinite(MaxRowSum(in moments.J2, size));
    }

    // basis[s][m] is the degree-m coefficient of the Lagrange polynomial for Radau
    // node s on [0, 1]: l_s(tau) = sum_m basis[s][m] tau^m.
    internal static double[][] LagrangeBasis()
    {
        var count = RadauC.Length;
        var basis = new double[count][];
        for (var s = 0; s < count; s++)
        {
            var polynomial = new[] { 1.0 };
            var degree = 0;
            for (var k = 0; k < count; k++)
            {
                if (k == s) continue;
                var next = new double[degree + 2];
                for (var m = 0; m <= degree; m++)
                {
                    next[m] -= RadauC[k] * polynomial[m];
                    next[m + 1] += polynomial[m];
                }
                for (var m = 0; m < next.Length; m++)
                    next[m] /= RadauC[s] - RadauC[k];
                polynomial = next;
                degree++;
            }
            basis[s] = polynomial;
        }
        return basis;
    }
}
