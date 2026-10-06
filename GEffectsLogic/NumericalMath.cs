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

    internal static readonly double[] RadauC = CreateRadauC();
    internal static readonly double[][] RadauA = CreateRadauA();

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
        if (!double.IsNaN(root) && !double.IsInfinity(root) && root > min && root < max &&
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

    internal static double[][] IdentityMatrix(int size)
    {
        var matrix = CreateMatrix(size);
        for (var i = 0; i < size; i++) matrix[i][i] = 1.0;
        return matrix;
    }

    internal static double[][] MultiplyMatrices(double[][] a, double[][] b)
    {
        var n = a.Length;
        var result = CreateMatrix(n);
        for (var i = 0; i < n; i++)
            for (var k = 0; k < n; k++)
            {
                var aik = a[i][k];
                if (aik == 0.0) continue;
                for (var j = 0; j < n; j++)
                    result[i][j] += aik * b[k][j];
            }
        return result;
    }

    private static double MaxAbsEntry(double[][] a)
    {
        var maximum = 0.0;
        for (var i = 0; i < a.Length; i++)
            for (var j = 0; j < a[i].Length; j++)
            {
                var magnitude = Math.Abs(a[i][j]);
                if (magnitude > maximum) maximum = magnitude;
            }
        return maximum;
    }

    private static double MaxRowSum(double[][] a)
    {
        var maximum = 0.0;
        for (var i = 0; i < a.Length; i++)
        {
            var rowSum = 0.0;
            for (var j = 0; j < a[i].Length; j++) rowSum += Math.Abs(a[i][j]);
            if (rowSum > maximum) maximum = rowSum;
        }
        return maximum;
    }

    // Scalar J_m(u*a) = integral_0^u exp((u - s) a) s^m ds for m = 0..2 in j.
    // Closed forms outside |a*u| < 0.5, Taylor series inside.
    internal static bool ScalarMoments(double a, double extent, double[] j)
    {
        var x = a * extent;
        if (!double.IsFinite(x) || !double.IsFinite(extent) || extent <= 0.0) return false;
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
            return j[0] == j[0] && j[1] == j[1] && j[2] == j[2];
        }
        if (a == 0.0) return false;
        var exponential = Math.Exp(x);
        if (double.IsNaN(exponential)) return false;
        var a2 = a * a;
        j[0] = (exponential - 1.0) / a;
        j[1] = (exponential - 1.0 - x) / a2;
        j[2] = (2.0 * (exponential - 1.0) - 2.0 * x - x * x) / (a2 * a);
        return double.IsFinite(j[0]) && double.IsFinite(j[1]) && double.IsFinite(j[2]);
    }

    private static double Factorial(int n)
    {
        var value = 1.0;
        for (var i = 2; i <= n; i++) value *= i;
        return value;
    }

    // J_m(u*A) = integral_0^u exp((u - s) A) s^m ds for m = 0..2, evaluated as a
    // series at extent u then doubled while ||u*A|| > 0.5 (max row sum norm);
    // arithmetic rescaling only, not physiological stepping. Returns null on any
    // nonfinite input, divergent series, or underflowed scaling.
    internal static double[][][]? MomentMatrices(double[][] a, double extent)
    {
        var n = a.Length;
        if (!double.IsFinite(extent) || extent <= 0.0) return null;
        var norm = MaxRowSum(a);
        if (!double.IsFinite(norm)) return null;

        var powered = new double[3][][];
        if (norm == 0.0)
        {
            var power = extent;
            for (var m = 0; m < 3; m++)
            {
                var j = CreateMatrix(n);
                for (var i = 0; i < n; i++) j[i][i] = power / (m + 1);
                powered[m] = j;
                power *= extent;
            }
            return powered;
        }

        var u = extent;
        for (var halvings = 0; u * norm > 0.5; halvings++)
        {
            if (halvings > 128 || u == 0.0) return null;
            u *= 0.5;
        }

        var powers = new[] { u, u * u, u * u * u };
        for (var m = 0; m < 3; m++)
        {
            var j = CreateMatrix(n);
            var term = CreateMatrix(n);
            for (var i = 0; i < n; i++)
            {
                j[i][i] = powers[m] / (m + 1);
                term[i][i] = powers[m] / (m + 1);
            }

            var converged = false;
            for (var iteration = 1; iteration <= 256; iteration++)
            {
                var next = MultiplyMatrices(term, a);
                var scale = 0.0;
                for (var i = 0; i < n; i++)
                    for (var k = 0; k < n; k++)
                    {
                        next[i][k] *= u / (m + iteration + 1);
                        var magnitude = Math.Abs(next[i][k]);
                        if (magnitude > scale) scale = magnitude;
                    }
                if (!double.IsFinite(scale)) return null;
                for (var i = 0; i < n; i++)
                    for (var k = 0; k < n; k++)
                        j[i][k] += next[i][k];
                term = next;
                if (scale <= 1e-18 * Math.Max(1.0, MaxAbsEntry(j)))
                {
                    converged = true;
                    break;
                }
            }
            if (!converged || !double.IsFinite(MaxRowSum(j))) return null;

            powered[m] = j;
        }

        var e = IdentityMatrix(n);
        var aj0 = MultiplyMatrices(a, powered[0]);
        for (var i = 0; i < n; i++)
            for (var k = 0; k < n; k++)
                e[i][k] += aj0[i][k];

        while (u < extent)
        {
            if (u == 0.0) return null;
            var eNew = MultiplyMatrices(e, e);
            var ej0 = MultiplyMatrices(e, powered[0]);
            var ej1 = MultiplyMatrices(e, powered[1]);
            var ej2 = MultiplyMatrices(e, powered[2]);
            var j0New = CreateMatrix(n);
            var j1New = CreateMatrix(n);
            var j2New = CreateMatrix(n);
            for (var i = 0; i < n; i++)
                for (var k = 0; k < n; k++)
                {
                    j0New[i][k] = ej0[i][k] + powered[0][i][k];
                    j1New[i][k] = ej1[i][k] + u * powered[0][i][k] + powered[1][i][k];
                    j2New[i][k] = ej2[i][k] + u * u * powered[0][i][k] +
                                  2.0 * u * powered[1][i][k] + powered[2][i][k];
                }
            e = eNew;
            powered[0] = j0New;
            powered[1] = j1New;
            powered[2] = j2New;
            if (!double.IsFinite(MaxRowSum(powered[0]))) return null;
            u *= 2.0;
        }

        return powered;
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
