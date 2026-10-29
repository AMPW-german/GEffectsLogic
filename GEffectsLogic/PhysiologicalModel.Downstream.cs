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


using System.Globalization;
using System.Runtime.CompilerServices;

namespace GEffectsLogic;

public partial class PhysiologicalModel
{
    // constrained algebraically to a time-varying target (instantaneous tau
    // limit). Instant coordinates carry a zero row/column in M; their target is
    // folded into B of the free rows by the builder.
    internal sealed class AffineSystemSpec
    {
        internal double[][] M = [];
        internal double[] B = [];
        internal bool[] Instant = [];
        internal Func<double, double>?[] InstantTargets = [];
    }

    internal sealed class AffineSolution
    {
        internal bool Valid;
        internal double[][] M0 = [];
        internal double[] B0 = [];
        internal double[] BaseSource = [];
        internal double[] Y0 = [];
        internal double[][] ResidualBasis = [];
        internal double Duration;
        internal NumericalMath.MomentPowerCache? MomentPowers;
        internal double[]? DenseMomentCoefficients;
        internal double DenseMomentExtent;
        internal double DenseMomentNorm;
        internal int DenseMomentDegree;
        internal double[][] Stages = [];
        internal double[][] Residuals = [];
        internal Func<double, double>?[] InstantTargets = [];
        internal bool LoCCeilingZeroOnPiece;
        internal ScalarLagSolution? ScalarOverride;
        private readonly Dictionary<double, double[]> cache = new(32);

        [MethodImpl(AggressiveOptimization)]
        internal double[] Evaluate(double c)
        {
            if (cache.TryGetValue(c, out var cached)) return cached;
            var n = Y0.Length;
            var y = new double[n];
            if (ScalarOverride != null)
            {
                y[0] = ScalarOverride.Evaluate(c);
            }
            else
            {
                var allInstant = InstantTargets.Length == n;
                for (var i = 0; i < n && allInstant; i++)
                    allInstant &= InstantTargets[i] != null;
                if (!allInstant && !EvaluateFree(c, y))
                    for (var i = 0; i < n; i++)
                        y[i] = double.NaN;
                for (var i = 0; i < n; i++)
                    if (i < InstantTargets.Length && InstantTargets[i] != null)
                        y[i] = InstantTargets[i]!(c);
            }
            cache[c] = y;
            return y;
        }

        [MethodImpl(AggressiveOptimization)]
        internal double EvaluateFirst(double c)
        {
            if (cache.TryGetValue(c, out var cached)) return cached[0];
            var denseMomentCoefficients = DenseMomentCoefficients;
            if (Y0.Length == 3 && denseMomentCoefficients != null && c >= 0.0 &&
                c <= DenseMomentExtent &&
                (InstantTargets.Length == 0 || InstantTargets[0] == null) &&
                ScalarOverride == null)
            {
                var polynomial =
                    denseMomentCoefficients[(DenseMomentDegree - 1) * 3];
                for (var coefficient = DenseMomentDegree - 2; coefficient >= 0;
                     coefficient--)
                    polynomial = denseMomentCoefficients[coefficient * 3] +
                                 c * polynomial;
                var value = Y0[0] + c * polynomial;
                if (IsFiniteNumber(value)) return value;
            }
            return Evaluate(c)[0];
        }

        [MethodImpl(AggressiveOptimization)]
        private bool EvaluateFree(double c, double[] y)
        {
            var n = Y0.Length;
            var stageCount = NumericalMath.RadauStageCount;
            if (n == 1)
            {
                var scalar = new double[3];
                if (!NumericalMath.ScalarMoments(M0[0][0] * Duration, c, scalar))
                    return false;
                var value = Y0[0] + Duration * scalar[0] * BaseSource[0];
                for (var j = 0; j < stageCount; j++)
                {
                    var basis = LagrangePolynomialBasis[j];
                    var weight = basis[0] * scalar[0] + basis[1] * scalar[1] +
                                 basis[2] * scalar[2];
                    value += Duration * weight * Residuals[j][0];
                }
                y[0] = value;
                return true;
            }

            var denseMomentCoefficients = DenseMomentCoefficients;
            if (n == 3 && denseMomentCoefficients != null && c >= 0.0 &&
                c <= DenseMomentExtent)
            {
                var finite = true;
                for (var row = 0; row < 3; row++)
                {
                    var polynomial =
                        denseMomentCoefficients[(DenseMomentDegree - 1) * 3 + row];
                    for (var coefficient = DenseMomentDegree - 2;
                         coefficient >= 0; coefficient--)
                        polynomial = denseMomentCoefficients[coefficient * 3 + row] +
                                     c * polynomial;
                    var value = Y0[row] + c * polynomial;
                    if (!IsFiniteNumber(value))
                    {
                        finite = false;
                        break;
                    }
                    y[row] = value;
                }
                if (finite)
                    return true;
            }

            var momentPowers = MomentPowers;
            NumericalMath.MatrixMoments moments;
            var momentsValid = momentPowers != null && c >= 0.0 && c <= 1.0
                ? NumericalMath.TryMomentMatrices(momentPowers, c, out moments)
                : NumericalMath.TryMomentMatrices(M0, Duration, c, out moments);
            if (!momentsValid)
                return false;
            if (n > 1)
            {
                if (ResidualBasis.Length != 3) return false;
                for (var moment = 0; moment < 3; moment++)
                    if (ResidualBasis[moment] == null || ResidualBasis[moment].Length < n)
                        return false;
            }
            for (var row = 0; row < n; row++)
            {
                var value = Y0[row];
                for (var k = 0; k < n; k++)
                    value += Duration * moments[0, row, k] * BaseSource[k];
                if (n > 1)
                    for (var moment = 0; moment < 3; moment++)
                        for (var k = 0; k < n; k++)
                            value += Duration * moments[moment, row, k] *
                                     ResidualBasis[moment][k];
                y[row] = value;
            }
            return true;
        }

        [MethodImpl(AggressiveOptimization)]
        internal double[] Derivative(double c)
        {
            var n = Y0.Length;
            var y = Evaluate(c);
            var f = new double[n];
            if (ScalarOverride != null)
            {
                f[0] = ScalarOverride.Derivative(c);
                return f;
            }

            for (var i = 0; i < n; i++)
            {
                double value;
                if (i < InstantTargets.Length && InstantTargets[i] != null)
                {
                    var delta = Math.Min(1e-6, Math.Max(1e-9, 0.5 * (1.0 - c)));
                    var slope = (InstantTargets[i]!(c + delta) - y[i]) / delta;
                    value = IsFiniteNumber(slope) ? slope / Duration : 0.0;
                }
                else
                {
                    value = B0[i];
                    for (var k = 0; k < n; k++) value += M0[i][k] * y[k];
                    for (var j = 0; j < NumericalMath.RadauStageCount; j++)
                    {
                        var basis = LagrangePolynomialBasis[j];
                        var l = basis[0] +
                            c * (basis.Length > 1 ? basis[1] + c * basis[2] : 0.0);
                        value += l * Residuals[j][i];
                    }
                }
                f[i] = value;
            }
            return f;
        }
    }

    private static readonly double[][] LagrangePolynomialBasis = NumericalMath.LagrangeBasis();

    internal sealed class ScalarLagSolution
    {
        private readonly double initial;
        private readonly double duration;
        private readonly double tau;
        private readonly Func<double, double>? targetAt;
        private readonly Func<double, double>? decayRateAt;
        private readonly bool hasConstantTarget;
        private readonly double constantTarget;
        private readonly Dictionary<double, double> cache = new(16);

        internal ScalarLagSolution(double initial, double duration, double tau,
            Func<double, double> targetAt)
        {
            this.initial = initial;
            this.duration = duration;
            this.tau = tau;
            this.targetAt = targetAt;
            decayRateAt = null;
            hasConstantTarget = false;
            constantTarget = 0.0;
        }

        internal ScalarLagSolution(double initial, double duration, double tau,
            double constantTarget)
        {
            if (constantTarget is not (0.0 or 1.0))
                throw new ArgumentOutOfRangeException(nameof(constantTarget));
            this.initial = initial;
            this.duration = duration;
            this.tau = tau;
            targetAt = null;
            decayRateAt = null;
            hasConstantTarget = true;
            this.constantTarget = constantTarget;
        }

        internal ScalarLagSolution(double initial, double duration,
            Func<double, double> targetAt, Func<double, double> decayRateAt)
        {
            this.initial = initial;
            this.duration = duration;
            tau = double.NaN;
            this.targetAt = targetAt;
            this.decayRateAt = decayRateAt;
            hasConstantTarget = false;
            constantTarget = 0.0;
        }

        [MethodImpl(AggressiveOptimization)]
        internal double Evaluate(double c)
        {
            if (cache.TryGetValue(c, out var cached)) return cached;
            double value;
            if (hasConstantTarget)
            {
                value = tau <= 1e-9
                    ? constantTarget
                    : c == 0.0
                        ? initial
                        : constantTarget + (initial - constantTarget) *
                          Math.Exp(-duration * c / tau);
            }
            else if (decayRateAt != null)
            {
                if (c == 0.0)
                {
                    value = initial;
                }
                else
                {
                    var span = duration * c;
                    var weightedRate = 0.0;
                    var valid = IsFiniteNumber(span) && span > 0.0;
                    for (var node = 0;
                         node < NumericalMath.GaussLegendre16Nodes.Length && valid;
                         node++)
                    {
                        var sample = c * NumericalMath.GaussLegendre16Nodes[node];
                        var target = targetAt!(sample);
                        var rate = decayRateAt(sample);
                        if (!IsFiniteNumber(target) || target != 0.0 ||
                            !IsFiniteNumber(rate) || rate <= 0.0)
                        {
                            valid = false;
                            break;
                        }
                        weightedRate += NumericalMath.GaussLegendre16Weights[node] * rate;
                    }
                    value = valid
                        ? initial * Math.Exp(-span * weightedRate)
                        : double.NaN;
                }
            }
            else
            {
                value = tau <= 1e-9
                    ? targetAt!(c)
                    : c == 0.0
                        ? initial
                        : initial + NumericalMath.ExponentialConvolution(0.0, 0.0,
                            duration * c, 1.0 / tau,
                            (targetAt, initial, duration, tau),
                            static (s, time) =>
                                (s.targetAt!(time / s.duration) - s.initial) / s.tau);
            }
            cache[c] = value;
            return value;
        }

        [MethodImpl(AggressiveOptimization)]
        internal double Derivative(double c)
        {
            var value = Evaluate(c);
            if (hasConstantTarget)
                return tau <= 1e-9 ? 0.0 : (constantTarget - value) / tau;
            if (decayRateAt != null)
            {
                var rate = decayRateAt(c);
                return IsFiniteNumber(rate) && rate > 0.0
                    ? -rate * value
                    : double.NaN;
            }
            if (tau <= 1e-9)
            {
                var delta = Math.Min(1e-6, Math.Max(1e-9, 0.5 * (1.0 - c)));
                var slope = (targetAt!(c + delta) - value) / delta;
                return IsFiniteNumber(slope) ? slope / duration : 0.0;
            }
            return (targetAt!(c) - value) / tau;
        }
    }

    // Fits a cubic to the target at the four third-points of a piece and
    // validates it at three more. The surrogate replaces the target for
    // convolution quadrature only when the fit matches within 1e-6;
    // otherwise the exact function is returned. The surrogate is clamped to
    // the sampled range so interpolation cannot overshoot a bounded target.
    [MethodImpl(AggressiveOptimization)]
    private static Func<double, double> TargetSurrogate(
        Func<double, double> target)
    {
        var sampled0 = target(0.0);
        var sampled1 = target(1.0 / 3.0);
        var sampled2 = target(2.0 / 3.0);
        var sampled3 = target(1.0);
        var first1 = 3.0 * (sampled1 - sampled0);
        var first2 = 3.0 * (sampled2 - sampled1);
        var first3 = 3.0 * (sampled3 - sampled2);
        var second1 = 1.5 * (first2 - first1);
        var second2 = 1.5 * (first3 - first2);
        var third = second2 - second1;
        var low = Math.Min(Math.Min(sampled0, sampled1), Math.Min(sampled2, sampled3));
        var high = Math.Max(Math.Max(sampled0, sampled1), Math.Max(sampled2, sampled3));
        double Surrogate(double c) => Clamp(
            sampled0 + c * (first1 + (c - 1.0 / 3.0) *
                          (second1 + (c - 2.0 / 3.0) * third)), low, high);
        return Math.Abs(Surrogate(1.0 / 6.0) - target(1.0 / 6.0)) <= 1e-6 &&
               Math.Abs(Surrogate(0.5) - target(0.5)) <= 1e-6 &&
               Math.Abs(Surrogate(5.0 / 6.0) - target(5.0 / 6.0)) <= 1e-6
            ? Surrogate
            : target;
    }

    // Reusable assembly buffers for the hot n == 3 collocation solve. The
    // pooled path sets InUse for the duration of one solve; a nested solve on
    // the same thread falls back to a fresh instance rather than corrupting
    // the outer one's buffers.
    private sealed class AffineSolveScratch
    {
        internal bool InUse;
        internal readonly double[][] lhs = NumericalMath.CreateMatrix(9);
        internal readonly double[][] lhsWork = NumericalMath.CreateMatrix(9);
        internal readonly double[] rhs = new double[9];
        internal readonly double[] rhsWork = new double[9];
        internal readonly double[] flat = new double[9];
        internal readonly double[] p = new double[9];
        internal readonly double[] w = new double[81];
        internal readonly double[] dm = new double[27];
        internal readonly double[] deltaF = new double[9];
    }

    [ThreadStatic]
    private static AffineSolveScratch? affineScratch;

    [MethodImpl(AggressiveOptimization)]
    internal static AffineSolution SolveAffineCollocation(double[] y0, double h,
        AffineSystemSpec s0,
        Func<double, AffineSystemSpec> stageSystem,
        bool prepareDenseTaylor = true)
    {
        var n = y0.Length;
        var stageCount = NumericalMath.RadauStageCount;
        var initial = (double[])y0.Clone();
        for (var row = 0; row < n; row++)
            if (row < s0.Instant.Length && s0.Instant[row])
                initial[row] = s0.InstantTargets[row]!(0.0);

        var solution = new AffineSolution
        {
            M0 = s0.M,
            B0 = s0.B,
            Y0 = initial,
            Duration = h,
            InstantTargets = s0.InstantTargets
        };

        var m0 = s0.M;
        var b0 = s0.B;
        var baseSource = new double[n];
        for (var row = 0; row < n; row++)
        {
            var source = b0[row];
            for (var k = 0; k < n; k++) source += m0[row][k] * initial[k];
            baseSource[row] = source;
        }
        solution.BaseSource = baseSource;

        if (n == 3 && stageCount == 3)
        {
            var scratch = affineScratch;
            if (scratch == null)
            {
                scratch = new AffineSolveScratch();
                affineScratch = scratch;
            }
            if (scratch.InUse) scratch = new AffineSolveScratch();
            scratch.InUse = true;
            try
            {
                for (var row = 0; row < 9; row++)
                {
                    Array.Clear(scratch.lhs[row], 0, scratch.lhs[row].Length);
                    Array.Clear(scratch.lhsWork[row], 0,
                        scratch.lhsWork[row].Length);
                }
                Array.Clear(scratch.rhs, 0, scratch.rhs.Length);
                Array.Clear(scratch.rhsWork, 0, scratch.rhsWork.Length);
                Array.Clear(scratch.flat, 0, scratch.flat.Length);
                Array.Clear(scratch.p, 0, scratch.p.Length);
                Array.Clear(scratch.w, 0, scratch.w.Length);
                Array.Clear(scratch.dm, 0, scratch.dm.Length);
                Array.Clear(scratch.deltaF, 0, scratch.deltaF.Length);

                for (var i = 0; i < stageCount; i++)
                {
                    var c = NumericalMath.RadauC[i];
                    var moments = default(NumericalMath.MatrixMoments);
                    if (!NumericalMath.TryMomentMatrices(m0, h, c, out moments))
                        return solution;

                    for (var row = 0; row < n; row++)
                    {
                        var value = 0.0;
                        for (var k = 0; k < n; k++)
                            value += h * moments[0, row, k] * baseSource[k];
                        scratch.p[i * n + row] = value;
                    }

                    var specJ = stageSystem(c);
                    for (var row = 0; row < n; row++)
                    {
                        for (var k = 0; k < n; k++)
                            scratch.dm[(i * n + row) * n + k] =
                                specJ.M[row][k] - m0[row][k];
                        var initialStageRate = specJ.B[row];
                        for (var k = 0; k < n; k++)
                            initialStageRate += specJ.M[row][k] * initial[k];
                        scratch.deltaF[i * n + row] =
                            initialStageRate - baseSource[row];
                    }

                    for (var j = 0; j < stageCount; j++)
                        for (var m = 0; m < 3; m++)
                        {
                            var coefficient = LagrangePolynomialBasis[j][m];
                            for (var row = 0; row < n; row++)
                                for (var k = 0; k < n; k++)
                                    scratch.w[((i * stageCount + j) * n + row) *
                                              n + k] +=
                                        coefficient * moments[m, row, k];
                        }
                }

                const int solveSize = 9;
                for (var i = 0; i < stageCount; i++)
                    for (var row = 0; row < n; row++)
                    {
                        var blockRow = i * n + row;
                        scratch.lhs[blockRow][blockRow] = 1.0;
                        scratch.rhs[blockRow] = scratch.p[blockRow];
                        for (var j = 0; j < stageCount; j++)
                            for (var k = 0; k < n; k++)
                            {
                                var wij = scratch.w[
                                    ((i * stageCount + j) * n + row) * n + k];
                                scratch.rhs[blockRow] +=
                                    h * wij * scratch.deltaF[j * n + k];
                                for (var l = 0; l < n; l++)
                                    scratch.lhs[blockRow][j * n + l] -=
                                        h * wij * scratch.dm[(j * n + k) * n + l];
                            }
                    }

                for (var i = 0; i < stageCount; i++)
                    for (var row = 0; row < n; row++)
                    {
                        if (row >= s0.Instant.Length || !s0.Instant[row])
                            continue;
                        var blockRow = i * n + row;
                        for (var column = 0; column < solveSize; column++)
                            scratch.lhs[blockRow][column] = 0.0;
                        scratch.lhs[blockRow][blockRow] = 1.0;
                        scratch.rhs[blockRow] =
                            s0.InstantTargets[row]!(NumericalMath.RadauC[i]) -
                            initial[row];
                    }

                for (var i = 0; i < solveSize; i++)
                    Array.Copy(scratch.lhs[i], scratch.lhsWork[i], solveSize);
                Array.Copy(scratch.rhs, scratch.rhsWork, solveSize);
                if (!NumericalMath.TrySolveLinearInPlace(scratch.lhsWork,
                        scratch.rhsWork, scratch.flat))
                    return solution;

                var peakResidual = 0.0;
                var peakRhs = 1.0;
                for (var i = 0; i < solveSize; i++)
                {
                    var sum = 0.0;
                    for (var k = 0; k < solveSize; k++)
                        sum += scratch.lhs[i][k] * scratch.flat[k];
                    var error = Math.Abs(sum - scratch.rhs[i]);
                    if (error > peakResidual) peakResidual = error;
                    if (Math.Abs(scratch.rhs[i]) > peakRhs)
                        peakRhs = Math.Abs(scratch.rhs[i]);
                }
                if (peakResidual > ScaledResidualTolerance * peakRhs)
                    return solution;

                solution.Stages = new double[stageCount][];
                for (var i = 0; i < stageCount; i++)
                {
                    solution.Stages[i] = new double[n];
                    for (var row = 0; row < n; row++)
                        solution.Stages[i][row] =
                            initial[row] + scratch.flat[i * n + row];
                }

                solution.Residuals = new double[stageCount][];
                for (var j = 0; j < stageCount; j++)
                {
                    solution.Residuals[j] = new double[n];
                    for (var row = 0; row < n; row++)
                    {
                        var value = scratch.deltaF[j * n + row];
                        for (var k = 0; k < n; k++)
                            value += scratch.dm[(j * n + row) * n + k] *
                                     scratch.flat[j * n + k];
                        solution.Residuals[j][row] = value;
                    }
                }

                if (n > 1)
                {
                    var residualBasis = new double[3][];
                    for (var moment = 0; moment < 3; moment++)
                    {
                        residualBasis[moment] = new double[n];
                        for (var k = 0; k < n; k++)
                        {
                            var value = 0.0;
                            for (var j = 0; j < stageCount; j++)
                                value += LagrangePolynomialBasis[j][moment] *
                                         solution.Residuals[j][k];
                            if (!IsFiniteNumber(value)) return solution;
                            residualBasis[moment][k] = value;
                        }
                    }
                    solution.ResidualBasis = residualBasis;
                }

                solution.Valid = true;
                foreach (var stage in solution.Stages)
                    foreach (var value in stage)
                        if (!IsFiniteNumber(value)) solution.Valid = false;
                if (solution.Valid && n == 3)
                {
                    solution.MomentPowers =
                        NumericalMath.CreateMomentPowerCache(solution.M0, h);
                    if (prepareDenseTaylor && solution.MomentPowers != null)
                        PrepareDenseMomentTaylor(solution);
                }
                return solution;
            }
            finally
            {
                scratch.InUse = false;
            }
        }

        var p = new double[stageCount][];
        var w = new double[stageCount * stageCount * n * n];
        var dm = new double[stageCount][][];
        var deltaF = new double[stageCount][];
        for (var i = 0; i < stageCount; i++)
        {
            var c = NumericalMath.RadauC[i];
            var moments = default(NumericalMath.MatrixMoments);
            if (n == 1)
            {
                var scalar = new double[3];
                if (!NumericalMath.ScalarMoments(m0[0][0] * h, c, scalar))
                    return solution;
                for (var moment = 0; moment < 3; moment++)
                    moments[moment, 0, 0] = scalar[moment];
            }
            else if (!NumericalMath.TryMomentMatrices(m0, h, c, out moments))
            {
                return solution;
            }

            p[i] = new double[n];
            for (var row = 0; row < n; row++)
            {
                var value = 0.0;
                for (var k = 0; k < n; k++)
                    value += h * moments[0, row, k] * baseSource[k];
                p[i][row] = value;
            }

            var specJ = stageSystem(c);
            dm[i] = NumericalMath.CreateMatrix(n);
            deltaF[i] = new double[n];
            for (var row = 0; row < n; row++)
            {
                for (var k = 0; k < n; k++) dm[i][row][k] = specJ.M[row][k] - m0[row][k];
                var initialStageRate = specJ.B[row];
                for (var k = 0; k < n; k++)
                    initialStageRate += specJ.M[row][k] * initial[k];
                deltaF[i][row] = initialStageRate - baseSource[row];
            }

            for (var j = 0; j < stageCount; j++)
                for (var m = 0; m < 3; m++)
                {
                    var coefficient = LagrangePolynomialBasis[j][m];
                    for (var row = 0; row < n; row++)
                        for (var k = 0; k < n; k++)
                            w[((i * stageCount + j) * n + row) * n + k] +=
                                coefficient * moments[m, row, k];
                }
        }

        var size = stageCount * n;
        var lhs = NumericalMath.CreateMatrix(size);
        var rhs = new double[size];
        for (var i = 0; i < stageCount; i++)
            for (var row = 0; row < n; row++)
            {
                var blockRow = i * n + row;
                lhs[blockRow][blockRow] = 1.0;
                rhs[blockRow] = p[i][row];
                for (var j = 0; j < stageCount; j++)
                    for (var k = 0; k < n; k++)
                    {
                        var wij = w[((i * stageCount + j) * n + row) * n + k];
                        rhs[blockRow] += h * wij * deltaF[j][k];
                        for (var l = 0; l < n; l++)
                            lhs[blockRow][j * n + l] -= h * wij * dm[j][k][l];
                    }
            }

        for (var i = 0; i < stageCount; i++)
            for (var row = 0; row < n; row++)
            {
                if (row >= s0.Instant.Length || !s0.Instant[row]) continue;
                var blockRow = i * n + row;
                for (var column = 0; column < size; column++) lhs[blockRow][column] = 0.0;
                lhs[blockRow][blockRow] = 1.0;
                rhs[blockRow] =
                    s0.InstantTargets[row]!(NumericalMath.RadauC[i]) - initial[row];
            }

        var flat = new double[size];
        if (!NumericalMath.TrySolveLinear(lhs, rhs, flat)) return solution;

        var maxResidual = 0.0;
        var maxRhs = 1.0;
        for (var i = 0; i < size; i++)
        {
            var sum = 0.0;
            for (var k = 0; k < size; k++) sum += lhs[i][k] * flat[k];
            var error = Math.Abs(sum - rhs[i]);
            if (error > maxResidual) maxResidual = error;
            if (Math.Abs(rhs[i]) > maxRhs) maxRhs = Math.Abs(rhs[i]);
        }
        if (maxResidual > ScaledResidualTolerance * maxRhs) return solution;

        solution.Stages = new double[stageCount][];
        for (var i = 0; i < stageCount; i++)
        {
            solution.Stages[i] = new double[n];
            for (var row = 0; row < n; row++)
                solution.Stages[i][row] = initial[row] + flat[i * n + row];
        }

        solution.Residuals = new double[stageCount][];
        for (var j = 0; j < stageCount; j++)
        {
            solution.Residuals[j] = new double[n];
            for (var row = 0; row < n; row++)
            {
                var value = deltaF[j][row];
                for (var k = 0; k < n; k++)
                    value += dm[j][row][k] * flat[j * n + k];
                solution.Residuals[j][row] = value;
            }
        }

        if (n > 1)
        {
            var residualBasis = new double[3][];
            for (var moment = 0; moment < 3; moment++)
            {
                residualBasis[moment] = new double[n];
                for (var k = 0; k < n; k++)
                {
                    var value = 0.0;
                    for (var j = 0; j < stageCount; j++)
                        value += LagrangePolynomialBasis[j][moment] *
                                 solution.Residuals[j][k];
                    if (!IsFiniteNumber(value)) return solution;
                    residualBasis[moment][k] = value;
                }
            }
            solution.ResidualBasis = residualBasis;
        }

        solution.Valid = true;
        foreach (var stage in solution.Stages)
            foreach (var value in stage)
                if (!IsFiniteNumber(value)) solution.Valid = false;
        if (solution.Valid && n == 3)
        {
            solution.MomentPowers =
                NumericalMath.CreateMomentPowerCache(solution.M0, h);
            if (prepareDenseTaylor && solution.MomentPowers != null)
                PrepareDenseMomentTaylor(solution);
        }
        return solution;
    }

    [MethodImpl(AggressiveOptimization)]
    private static void PrepareDenseMomentTaylor(AffineSolution solution)
    {
        const int coefficientCount = 64;
        var powerCache = solution.MomentPowers;
        if (powerCache == null || solution.Y0.Length != 3 ||
            solution.BaseSource.Length < 3 || solution.ResidualBasis.Length != 3)
            return;
        for (var moment = 0; moment < 3; moment++)
            if (solution.ResidualBasis[moment] == null ||
                solution.ResidualBasis[moment].Length < 3)
                return;

        var norm = powerCache.Norm;
        var h = solution.Duration;
        if (!IsFiniteNumber(norm) || norm <= 0.0 || !IsFiniteNumber(h)) return;
        const double extent = 1.0;
        if (!IsFiniteNumber(extent) || extent <= 0.0) return;

        [MethodImpl(AggressiveOptimization)]
        double InfinityNorm(double[] values)
        {
            var maximum = 0.0;
            for (var i = 0; i < 3; i++)
            {
                if (!IsFiniteNumber(values[i])) return double.NaN;
                maximum = Math.Max(maximum, Math.Abs(values[i]));
            }
            return maximum;
        }

        var source0 = new double[3];
        for (var coordinate = 0; coordinate < 3; coordinate++)
        {
            source0[coordinate] = solution.BaseSource[coordinate] +
                                  solution.ResidualBasis[0][coordinate];
            if (!IsFiniteNumber(source0[coordinate])) return;
        }
        var u0 = Math.Abs(h) * InfinityNorm(source0);
        var u1 = Math.Abs(h) * InfinityNorm(solution.ResidualBasis[1]);
        var u2 = Math.Abs(h) * InfinityNorm(solution.ResidualBasis[2]);
        if (!IsFiniteNumber(u0) || !IsFiniteNumber(u1) || !IsFiniteNumber(u2))
            return;

        var coefficients = new double[coefficientCount * 3];
        for (var coordinate = 0; coordinate < 3; coordinate++)
        {
            coefficients[coordinate] = h * source0[coordinate];
            if (!IsFiniteNumber(coefficients[coordinate])) return;
        }

        var scaledA = powerCache.ScaledA;
        for (var degree = 2; degree <= 3; degree++)
        {
            var previous = (degree - 2) * 3;
            var current = (degree - 1) * 3;
            for (var row = 0; row < 3; row++)
            {
                var value = 0.0;
                for (var column = 0; column < 3; column++)
                    value += scaledA[row, column] * coefficients[previous + column];
                value += h * solution.ResidualBasis[degree - 1][row];
                var result = value / degree;
                if (!IsFiniteNumber(result)) return;
                coefficients[current + row] = result;
            }
        }

        var bound1 = u0;
        var bound2 = (norm * bound1 + u1) / 2.0;
        var bound3 = (norm * bound2 + u2) / 3.0;
        if (!IsFiniteNumber(bound1) || !IsFiniteNumber(bound2) ||
            !IsFiniteNumber(bound3))
            return;

        var exponential = Math.Exp(norm * extent);
        var cPower = extent * extent * extent * extent;
        if (!IsFiniteNumber(exponential) || !IsFiniteNumber(cPower)) return;

        var currentDegree = 3;
        var bound = bound3;
        var certifiedDegree = 0;
        while (currentDegree <= coefficientCount)
        {
            var nextDegree = currentDegree + 1;
            var nextBound = norm * bound / nextDegree;
            var tail = nextBound * cPower * exponential;
            if (!IsFiniteNumber(nextBound) || !IsFiniteNumber(tail)) return;
            if (tail <= 1e-15)
            {
                certifiedDegree = currentDegree;
                break;
            }
            if (currentDegree == coefficientCount) return;

            var previous = (currentDegree - 1) * 3;
            var current = (nextDegree - 1) * 3;
            for (var row = 0; row < 3; row++)
            {
                var value = 0.0;
                for (var column = 0; column < 3; column++)
                    value += scaledA[row, column] * coefficients[previous + column];
                var result = value / nextDegree;
                if (!IsFiniteNumber(result)) return;
                coefficients[current + row] = result;
            }
            bound = nextBound;
            cPower *= extent;
            if (!IsFiniteNumber(cPower)) return;
            currentDegree = nextDegree;
        }
        if (certifiedDegree == 0) return;

        solution.DenseMomentCoefficients = coefficients;
        solution.DenseMomentExtent = extent;
        solution.DenseMomentNorm = norm;
        solution.DenseMomentDegree = certifiedDegree;
    }

    private struct DownstreamHold
    {
        internal bool BrainDepleting;
        internal bool HeadPinned;
        internal bool LowerPinned;
        internal bool CorePinned;
        internal bool ConsciousnessLosing;
        internal double ConsciousnessTarget;
        internal double ConsciousnessMargin;
        internal double ConsciousnessMarginRightSlope;
        internal double ConsciousnessTau;
        internal bool[] Increasing;
    }

    private sealed class DownstreamSolution
    {
        internal bool Valid;
        internal string Failure = "";
        internal AffineSolution Oxygen = new();
        internal AffineSolution Consciousness = new();
        internal ScalarLagSolution[] Visuals = [];
        internal Func<int, double, double>? VisualTargetAt;
        internal bool HasHeadRange;
        internal double MinimumHead;
        internal double MaximumHead;
        internal double? TunnelVisionConstantTarget;
        internal double? RedoutConstantTarget;
        internal double? GrayscaleConstantTarget;
        internal List<string> InitialEvents = [];

        [MethodImpl(AggressiveOptimization)]
        internal double? ConstantVisualTarget(int channel) => channel switch
        {
            0 => TunnelVisionConstantTarget,
            1 => RedoutConstantTarget,
            2 => GrayscaleConstantTarget,
            _ => null
        };
    }

    private sealed class LoCTrajectory
    {
        internal bool Contact;
        internal double StartValue;
        internal double Rate;
        internal AffineSolution Consciousness = new();
        internal double PieceStart;
        internal LogicSettings Settings = LogicSettings.Default;
        internal double FirstEventTime = double.PositiveInfinity;
        internal string FirstEventKind = "";
        internal bool Pinned;

        [MethodImpl(AggressiveOptimization)]
        internal double CeilingAt(double t)
        {
            return LoCCeiling(Consciousness.Evaluate(
                (t - PieceStart) / Consciousness.Duration)[0],
                Settings);
        }

        [MethodImpl(AggressiveOptimization)]
        internal double ValueAt(double t)
        {
            if (Pinned) return 1.0;
            if (t >= FirstEventTime) t = FirstEventTime;
            if (Contact) return CeilingAt(t);
            return Clamp(StartValue + Rate * (t - PieceStart), 0.0, 1.0);
        }
    }

    [MethodImpl(AggressiveOptimization)]
    private static double CeilingDerivative(AffineSolution consciousness, double c,
        LogicSettings settings)
    {
        var level = consciousness.Evaluate(c)[0];
        var range = Math.Max(
            settings.ConsciousnessRecoveryThreshold - settings.ConsciousnessLossThreshold, 1e-9);
        var fraction = (settings.ConsciousnessRecoveryThreshold - level) / range;
        if (fraction is <= 0.0 or >= 1.0) return 0.0;
        var dcDc = -settings.VisualLoCConsciousnessExponent *
            Math.Pow(fraction, settings.VisualLoCConsciousnessExponent - 1.0) / range;
        return dcDc * consciousness.Derivative(c)[0];
    }

    [MethodImpl(AggressiveOptimization)]
    private static double LungEffectiveness(IntervalTrialState trial,
        double gxMagnitude, LogicSettings settings) =>
        Clamp(1.0 - trial.Compression * settings.GyLungCompressionSeverity -
              GxLungImpairment(gxMagnitude, settings) - 0.5 * trial.Respiratory,
            0.1, 1.0);

    [MethodImpl(AggressiveOptimization)]
    private static AffineSystemSpec BuildOxygenSystem(
        Func<double, IntervalTrialState> trialAt, double c,
        bool depleting, bool headPinned, bool lowerPinned, bool corePinned,
        double gxMagnitude, LogicSettings settings)
    {
        var spec = new AffineSystemSpec
        {
            M = NumericalMath.CreateMatrix(3),
            B = new double[3],
            Instant = new bool[3],
            InstantTargets = new Func<double, double>?[3]
        };
        var trial = trialAt(c);
        var transport = settings.OxygenTransportBaseRate *
            (1.0 + settings.OxygenTransportHeartRateSensitivity * (trial.HeartRate - 1.0));
        var kH = 1.8 * transport * trial.Head / settings.RestingBloodHead;
        var kL = 1.8 * transport * trial.Lower / settings.RestingBloodLower;
        var delivery = EffectiveDelivery(ShapedPerfusion(trial.Head, settings),
            trial.HeartRate, trial.Arterial, settings);
        var target = BrainO2Target(delivery, settings);
        var tau = depleting
            ? BrainDepletionTau(delivery, settings)
            : settings.BrainO2RecoveryTau;
        var headInstant = !headPinned && tau <= 1e-9;
        var coreInstant = !corePinned && settings.LungOxygenationRate <= 1e-9;
        var inverseTau = headInstant ? 0.0 : 1.0 / tau;
        var k = coreInstant ? 0.0 : 1.0 / settings.LungOxygenationRate;

        var m = spec.M;
        var b = spec.B;
        m[0][0] = -kH - inverseTau;
        m[0][2] = kH;
        m[1][1] = -kL;
        m[1][2] = kL;
        m[2][0] = trial.Head * kH;
        m[2][1] = trial.Lower * kL;
        m[2][2] = -k - trial.Head * kH - trial.Lower * kL;
        b[0] = target * inverseTau - settings.OxygenConsumptionRateHead;
        b[1] = -settings.OxygenConsumptionRateLower;
        b[2] = k * settings.CoreBloodO2Resting *
               LungEffectiveness(trial, gxMagnitude, settings) -
               settings.OxygenConsumptionRateCore;
        if (headInstant)
        {
            spec.Instant[0] = true;
            spec.InstantTargets[0] = node =>
            {
                var at = trialAt(node);
                return Clamp(BrainO2Target(EffectiveDelivery(
                        ShapedPerfusion(at.Head, settings), at.HeartRate, at.Arterial,
                        settings), settings), settings.BrainO2Floor, 1.0);
            };
        }
        if (coreInstant)
        {
            spec.Instant[2] = true;
            spec.InstantTargets[2] = node =>
                settings.CoreBloodO2Resting *
                LungEffectiveness(trialAt(node), gxMagnitude, settings);
        }
        for (var a = 0; a < 3; a++)
        {
            if (!spec.Instant[a]) continue;
            var targetValue = spec.InstantTargets[a]!(c);
            for (var f = 0; f < 3; f++)
            {
                if (f == a || spec.Instant[f]) continue;
                b[f] += m[f][a] * targetValue;
                m[f][a] = 0.0;
            }
            for (var column = 0; column < 3; column++) m[a][column] = 0.0;
            b[a] = 0.0;
        }
        if (headPinned) { m[0][0] = 0.0; m[0][2] = 0.0; b[0] = 0.0; }
        if (lowerPinned) { m[1][1] = 0.0; m[1][2] = 0.0; b[1] = 0.0; }
        if (corePinned)
        {
            m[2][0] = 0.0; m[2][1] = 0.0; m[2][2] = 0.0; b[2] = 0.0;
        }
        return spec;
    }

    [MethodImpl(AggressiveOptimization)]
    private static AffineSystemSpec BuildConsciousnessSystem(
        Func<double, IntervalTrialState> trialAt, Func<double, double> headO2At,
        double c, bool losing, bool pinned, LogicSettings settings)
    {
        var spec = new AffineSystemSpec
        {
            M = NumericalMath.CreateMatrix(1),
            B = new double[1],
            Instant = new bool[1],
            InstantTargets = new Func<double, double>?[1]
        };
        if (pinned) return spec;
        var trial = trialAt(c);
        var (target, lossTau) = ConsciousnessTargetAndTau(
            O2Normalized(headO2At(c), settings),
            PerfusionNormalized(trial.Head, settings), trial.Pressure, settings);
        var tau = losing ? lossTau : settings.ConsciousnessRecoveryTau;
        if (tau <= 1e-9)
        {
            spec.Instant[0] = true;
            spec.InstantTargets[0] = node =>
                ConsciousnessTargetAt(trialAt, headO2At, node, settings);
            return spec;
        }
        var k = 1.0 / tau;
        spec.M[0][0] = -k;
        spec.B[0] = target * k;
        return spec;
    }

    [MethodImpl(AggressiveOptimization)]
    private static double ConsciousnessTargetAt(
        Func<double, IntervalTrialState> trialAt, Func<double, double> headO2At,
        double c, LogicSettings settings)
    {
        var trial = trialAt(c);
        return ConsciousnessTargetValue(O2Normalized(headO2At(c), settings),
            PerfusionNormalized(trial.Head, settings), trial.Pressure, settings);
    }

    [MethodImpl(AggressiveOptimization)]
    private static double BisectSign(Func<double, double> f, double lo, double hi,
        int loSign, out int direction,
        double toleranceSeconds = PressureBoundRootToleranceSeconds)
    {
        direction = -loSign;
        for (var i = 0; i < 200 && hi - lo > toleranceSeconds; i++)
        {
            var mid = 0.5 * (lo + hi);
            var fMid = f(mid);
            if (!IsFiniteNumber(fMid)) return double.NaN;
            if (fMid == 0.0) return mid;
            if ((fMid > 0.0) == (loSign > 0)) lo = mid;
            else hi = mid;
        }
        return 0.5 * (lo + hi);
    }

    private static readonly string[] VisualTransitionNames =
        ["VisualTunnelTransition", "VisualRedoutTransition", "VisualGrayscaleTransition"];

    private static readonly string[] VisualInstantNames =
    [
        "InstantaneousTunnelVisionLimit", "InstantaneousRedoutLimit",
        "InstantaneousGrayscaleLimit"
    ];

    private static readonly string[] OxygenCoordinateNames = ["Head", "Lower", "Core"];

    private static readonly double[] ScanProbes =
    [
        0.5 * NumericalMath.RadauC[0], NumericalMath.RadauC[0],
        0.5 * (NumericalMath.RadauC[0] + NumericalMath.RadauC[1]),
        NumericalMath.RadauC[1],
        0.5 * (NumericalMath.RadauC[1] + NumericalMath.RadauC[2])
    ];

    // Downstream defect probes: every other Gauss-Legendre node halves the
    // per-piece probe cost while still spanning the interval.
    private static readonly double[] DownstreamDefectProbes =
    [
        NumericalMath.GaussLegendre16Nodes[0], NumericalMath.GaussLegendre16Nodes[2],
        NumericalMath.GaussLegendre16Nodes[4], NumericalMath.GaussLegendre16Nodes[6],
        NumericalMath.GaussLegendre16Nodes[8], NumericalMath.GaussLegendre16Nodes[10],
        NumericalMath.GaussLegendre16Nodes[12], NumericalMath.GaussLegendre16Nodes[14]
    ];

    // Held-bound release probes: the collocation nodes alone leave the first
    // ~7% of a span unsampled, where fast releases can hide.
    private static readonly double[] BoundReleaseScanFractions =
        NumericalMath.GaussLegendre16Nodes.Concat(NumericalMath.RadauC)
            .OrderBy(fraction => fraction).ToArray();

    [MethodImpl(AggressiveOptimization)]
    private static double ScanCrossing(Func<double, double> f, double lo, double hi,
        out int direction) =>
        ScanCrossing(f, lo, hi, (CrossingSamples?)null, out direction);

    [MethodImpl(AggressiveOptimization)]
    private static double ScanCrossing(Func<double, double> f, double lo, double hi,
        CrossingSamples samples, out int direction,
        double toleranceSeconds = PressureBoundRootToleranceSeconds) =>
        ScanCrossing(f, lo, hi, (CrossingSamples?)samples, out direction,
            toleranceSeconds);

    [MethodImpl(AggressiveOptimization)]
    private static double ScanCrossing(Func<double, double> f, double lo, double hi,
        CrossingSamples? samples, out int direction,
        double toleranceSeconds = PressureBoundRootToleranceSeconds,
        bool safeguardedRefine = false)
    {
        direction = 0;
        var span = hi - lo;
        if (span <= 0.0) return double.PositiveInfinity;

        var lastSign = 0;
        var lastT = lo;
        var lastValue = 0.0;
        var zeroStart = double.NaN;
        var startZero = false;
        var crossingDirection = 0;

        [MethodImpl(AggressiveOptimization)]
        double? Refine(double a, double b, double fa, double fb)
        {
            if (!safeguardedRefine)
                return BisectSign(f, a, b, fa > 0.0 ? 1 : -1, out crossingDirection,
                    toleranceSeconds);
            crossingDirection = fa > 0.0 ? -1 : 1;
            return NumericalMath.BrentDekkerSign(f, a, b, fa, fb,
                toleranceSeconds);
        }

        [MethodImpl(AggressiveOptimization)]
        double? Process(double t, double value)
        {
            if (!IsFiniteNumber(value))
            {
                lastSign = 0;
                zeroStart = double.NaN;
                return null;
            }
            if (value == 0.0)
            {
                if (double.IsNaN(zeroStart))
                {
                    zeroStart = t;
                    startZero = lastSign == 0;
                }
                return null;
            }
            var sign = value > 0.0 ? 1 : -1;
            if (!double.IsNaN(zeroStart))
            {
                double? result = null;
                if (startZero)
                {
                    crossingDirection = sign;
                    result = lo;
                }
                else if (lastSign != 0 && sign == -lastSign)
                    result = Refine(lastT, t, lastValue, value);
                zeroStart = double.NaN;
                lastSign = sign;
                lastT = t;
                lastValue = value;
                return result;
            }
            if (lastSign != 0 && sign == -lastSign)
                return Refine(lastT, t, lastValue, value);
            lastSign = sign;
            lastT = t;
            lastValue = value;
            return null;
        }

        var first = Process(lo, samples.HasValue ? samples.Value.ValueAt(0) : f(lo));
        if (first.HasValue)
        {
            direction = crossingDirection;
            return double.IsNaN(first.Value) ? double.PositiveInfinity : first.Value;
        }
        var sampleIndex = 1;
        foreach (var probe in ScanProbes)
        {
            var t = lo + probe * span;
            var value = samples.HasValue
                ? samples.Value.ValueAt(sampleIndex)
                : f(t);
            var hit = Process(t, value);
            if (hit.HasValue)
            {
                direction = crossingDirection;
                return double.IsNaN(hit.Value) ? double.PositiveInfinity : hit.Value;
            }
            sampleIndex++;
        }
        var endHit = Process(hi, samples.HasValue ? samples.Value.ValueAt(6) : f(hi));
        if (endHit.HasValue)
        {
            direction = crossingDirection;
            return double.IsNaN(endHit.Value) ? double.PositiveInfinity : endHit.Value;
        }
        if (!double.IsNaN(zeroStart) && lastSign != 0)
        {
            direction = -lastSign;
            return hi;
        }
        return double.PositiveInfinity;
    }

    private const double RightProbeFraction = 1e-4;

    [MethodImpl(AggressiveOptimization)]
    private static double RightSlope(Func<double, double> f)
    {
        var slope = (f(RightProbeFraction) - f(0.0)) / RightProbeFraction;
        return IsFiniteNumber(slope) ? slope : 0.0;
    }

    [MethodImpl(AggressiveOptimization)]
    private static bool TargetRises(double target0, double level,
        Func<double, double> targetAt) =>
        target0 > level || (target0 == level && RightSlope(targetAt) > 0.0);

    [MethodImpl(AggressiveOptimization)]
    private static DownstreamHold ComputeHold(IntegrationState state,
        Func<double, IntervalTrialState> trialAt, double span, double gxMagnitude,
        LogicSettings settings)
    {
        var hold = new DownstreamHold { Increasing = new bool[3] };
        var trial = trialAt(0.0);
        var delivery = EffectiveDelivery(ShapedPerfusion(trial.Head, settings),
            trial.HeartRate, trial.Arterial, settings);
        var headO2 = state.BloodO2Head;
        var margin0 = BrainO2Target(delivery, settings) - headO2;
        var probe = BuildOxygenSystem(trialAt, 0.0, margin0 < 0.0, false, false,
            false, gxMagnitude, settings);
        var m = probe.M;
        var b = probe.B;
        var hDot = m[0][0] * headO2 + m[0][2] * state.BloodO2Core + b[0];
        var lDot = m[1][1] * state.BloodO2Lower + m[1][2] * state.BloodO2Core + b[1];
        var cDot = m[2][0] * headO2 + m[2][1] * state.BloodO2Lower +
                   m[2][2] * state.BloodO2Core + b[2];
        hold.BrainDepleting = margin0 < 0.0 ||
            (margin0 == 0.0 && RightSlope(c => BrainO2Target(EffectiveDelivery(
                    ShapedPerfusion(trialAt(c).Head, settings), trialAt(c).HeartRate,
                    trialAt(c).Arterial, settings), settings)) - hDot * span < 0.0);
        [MethodImpl(AggressiveOptimization)]
        double NaturalOxygenRateAt(int coordinate, double c)
        {
            var spec = BuildOxygenSystem(trialAt, c, hold.BrainDepleting, false,
                false, false, gxMagnitude, settings);
            var values = new[]
            {
                headO2 + hDot * c * span,
                state.BloodO2Lower + lDot * c * span,
                state.BloodO2Core + cDot * c * span
            };
            var rate = spec.B[coordinate];
            for (var i = 0; i < values.Length; i++)
                rate += spec.M[coordinate][i] * values[i];
            return rate;
        }

        var headRateSlope = hDot == 0.0 ? RightSlope(c => NaturalOxygenRateAt(0, c)) : 0.0;
        var lowerRateSlope = lDot == 0.0 ? RightSlope(c => NaturalOxygenRateAt(1, c)) : 0.0;
        var coreRateSlope = cDot == 0.0 ? RightSlope(c => NaturalOxygenRateAt(2, c)) : 0.0;
        hold.HeadPinned =
            (headO2 <= settings.BrainO2Floor &&
             (hDot < 0.0 || (hDot == 0.0 && headRateSlope <= 0.0))) ||
            (headO2 >= 1.0 &&
             (hDot > 0.0 || (hDot == 0.0 && headRateSlope >= 0.0)));
        hold.LowerPinned =
            (state.BloodO2Lower <= 0.0 &&
             (lDot < 0.0 || (lDot == 0.0 && lowerRateSlope <= 0.0))) ||
            (state.BloodO2Lower >= 1.0 &&
             (lDot > 0.0 || (lDot == 0.0 && lowerRateSlope >= 0.0)));
        hold.CorePinned =
            (state.BloodO2Core <= 0.0 &&
             (cDot < 0.0 || (cDot == 0.0 && coreRateSlope <= 0.0))) ||
            (state.BloodO2Core >= 1.0 &&
             (cDot > 0.0 || (cDot == 0.0 && coreRateSlope >= 0.0)));

        [MethodImpl(AggressiveOptimization)]
        double HeadO2Probe(double c) => headO2 + hDot * c * span;
        var consciousnessTarget = ConsciousnessTargetAndTau(
            O2Normalized(headO2, settings),
            PerfusionNormalized(trial.Head, settings), trial.Pressure,
            settings);
        var cMargin = consciousnessTarget.Target - state.ConsciousnessLevel;
        hold.ConsciousnessTarget = consciousnessTarget.Target;
        hold.ConsciousnessMargin = cMargin;
        [MethodImpl(AggressiveOptimization)]
        double ConsciousnessTargetProbe(double c) => ConsciousnessTargetAt(trialAt,
            cProbe => HeadO2Probe(cProbe), c, settings);
        var targetSlope = RightSlope(ConsciousnessTargetProbe);
        hold.ConsciousnessLosing = cMargin < 0.0 ||
            (cMargin == 0.0 && targetSlope < 0.0);
        var consciousnessTau = hold.ConsciousnessLosing
            ? consciousnessTarget.LossTau
            : settings.ConsciousnessRecoveryTau;
        hold.ConsciousnessTau = consciousnessTau;
        hold.ConsciousnessMarginRightSlope = consciousnessTau > 1e-9
            ? targetSlope / span - cMargin / consciousnessTau
            : double.NaN;

        [MethodImpl(AggressiveOptimization)]
        double PhysTargetAt(double c) => PhysiologicalVisualTarget(trialAt(c).Head,
            O2Normalized(HeadO2Probe(c), settings), settings);
        [MethodImpl(AggressiveOptimization)]
        double RedoutAt(double c) => RedoutTarget(Math.Max(
            (trialAt(c).Head - settings.RestingBloodHead) / settings.RestingBloodHead,
            0.0), settings);
        hold.Increasing[0] = TargetRises(PhysTargetAt(0.0),
            state.VisualTunnelVisionLevel, PhysTargetAt);
        hold.Increasing[1] = TargetRises(RedoutAt(0.0), state.VisualRedoutLevel,
            RedoutAt);
        hold.Increasing[2] = TargetRises(PhysTargetAt(0.0),
            state.VisualGrayscaleLevel, PhysTargetAt);
        return hold;
    }

    [MethodImpl(AggressiveOptimization)]
    private static bool TryCreateHomogeneousZeroTargetLoss(
        IntegrationState state, IntervalUpstream upstream, double cursor, double span,
        DownstreamSolution piece, bool zeroTargetBranchAtEnd, LogicSettings settings,
        out AffineSolution? solution, out string failure)
    {
        solution = null;
        failure = "";
        if (state.IsDead) return false;
        if (!(span > 0.0) || !IsFiniteNumber(span))
        {
            failure = $"invalid target-zero piece span {span:R}";
            return false;
        }

        [MethodImpl(AggressiveOptimization)]
        IntervalTrialState TrialAt(double c) =>
            upstream.EvaluateHeadPressure(cursor + c * span);
        [MethodImpl(AggressiveOptimization)]
        double HeadO2At(double c) => piece.Oxygen.EvaluateFirst(c);
        [MethodImpl(AggressiveOptimization)]
        double TargetAt(double c)
        {
            var target = ConsciousnessTargetAt(TrialAt, HeadO2At, c, settings);
            return target <= ZeroTargetTolerance ? 0.0 : target;
        }
        [MethodImpl(AggressiveOptimization)]
        double LossRateAt(double c)
        {
            var trial = TrialAt(c);
            var lossTau = ConsciousnessTargetAndTau(
                O2Normalized(HeadO2At(c), settings),
                PerfusionNormalized(trial.Head, settings), trial.Pressure,
                settings).LossTau;
            return IsFiniteNumber(lossTau) && lossTau > 0.0
                ? 1.0 / lossTau
                : double.NaN;
        }

        var startTarget = TargetAt(0.0);
        if (!IsFiniteNumber(startTarget))
        {
            failure = $"nonfinite target at target-zero piece start: {startTarget:R}";
            return false;
        }
        if (startTarget != 0.0)
        {
            if (zeroTargetBranchAtEnd)
                failure = $"named target-zero branch prefix starts with target " +
                    $"{startTarget:R}, not zero";
            return false;
        }
        var startRate = LossRateAt(0.0);
        if (!IsFiniteNumber(startRate) || startRate <= 0.0)
        {
            failure = $"invalid homogeneous loss rate {startRate:R} at piece start";
            return false;
        }
        for (var i = 0; i < NumericalMath.GaussLegendre16Nodes.Length; i++)
        {
            var c = NumericalMath.GaussLegendre16Nodes[i];
            var target = TargetAt(c);
            var rate = LossRateAt(c);
            if (!IsFiniteNumber(target) || target != 0.0)
            {
                failure = $"target-zero verification failed at quadrature node " +
                    $"c={c:R}: target={target:R}";
                return false;
            }
            if (!IsFiniteNumber(rate) || rate <= 0.0)
            {
                failure = $"invalid homogeneous loss rate {rate:R} at quadrature " +
                    $"node c={c:R}";
                return false;
            }
        }

        var endpointTarget = TargetAt(1.0);
        if (!IsFiniteNumber(endpointTarget) || endpointTarget < 0.0 ||
            endpointTarget > 1.0)
        {
            failure = $"invalid target-zero piece endpoint target {endpointTarget:R}";
            return false;
        }
        if (zeroTargetBranchAtEnd)
        {
            var bracketDuration = Math.Min(PressureBoundRootToleranceSeconds, span);
            var beforeEndpoint = Math.Max(0.0, 1.0 - bracketDuration / span);
            var beforeTarget = TargetAt(beforeEndpoint);
            var endpointSlope = (endpointTarget - beforeTarget) / bracketDuration;
            var endpointAllowance =
                Math.Abs(endpointSlope) * PressureBoundRootToleranceSeconds + 1e-12;
            if (beforeTarget != 0.0 || !IsFiniteNumber(endpointSlope) ||
                !IsFiniteNumber(endpointAllowance) || endpointTarget > endpointAllowance)
            {
                failure = $"target-zero branch endpoint target {endpointTarget:R} " +
                    $"outside root-bracket allowance {endpointAllowance:R}; " +
                    $"preceding target={beforeTarget:R}, slope={endpointSlope:R}";
                return false;
            }
        }
        else if (endpointTarget != 0.0)
        {
            failure = $"target-zero piece endpoint target {endpointTarget:R} is not zero";
            return false;
        }

        var scalar = new ScalarLagSolution(state.ConsciousnessLevel, span,
            TargetAt, LossRateAt);
        foreach (var c in (double[])[0.0, .. NumericalMath.RadauC, 1.0])
        {
            var value = scalar.Evaluate(c);
            var derivative = scalar.Derivative(c);
            if (!IsFiniteNumber(value) || value < 0.0 || value > 1.0 ||
                !IsFiniteNumber(derivative))
            {
                failure = $"homogeneous consciousness invalid at c={c:R}: " +
                    $"value={value:R}, derivative={derivative:R}";
                return false;
            }
        }

        solution = new AffineSolution
        {
            Valid = true,
            Y0 = [state.ConsciousnessLevel],
            Duration = span,
            ScalarOverride = scalar
        };
        return true;
    }

    [MethodImpl(AggressiveOptimization)]
    private static DownstreamSolution SolveDownstream(IntegrationState state,
        IntervalUpstream upstream, double cursor, double span, DownstreamHold hold,
        LogicSettings settings, bool allowHomogeneousZeroTargetLoss = false,
        bool zeroTargetBranchAtEnd = false, bool oxygenOnly = false)
    {
        var piece = new DownstreamSolution();
        [MethodImpl(AggressiveOptimization)]
        IntervalTrialState TrialAt(double c) =>
            upstream.EvaluateOxygen(cursor + c * span);
        [MethodImpl(AggressiveOptimization)]
        IntervalTrialState TargetTrialAt(double c) =>
            upstream.EvaluateHeadPressure(cursor + c * span);
        var oxygenY0 = new[] { state.BloodO2Head, state.BloodO2Lower, state.BloodO2Core };
        var oxygenSpec0 = BuildOxygenSystem(TrialAt, 0.0, hold.BrainDepleting,
            hold.HeadPinned, hold.LowerPinned, hold.CorePinned, upstream.GxMagnitude,
            settings);
        for (var coord = 0; coord < 3; coord++)
            if (oxygenSpec0.Instant[coord] &&
                oxygenY0[coord] != oxygenSpec0.InstantTargets[coord]!(0.0))
                piece.InitialEvents.Add(
                    $"Instantaneous{OxygenCoordinateNames[coord]}OxygenLimit");
        piece.Oxygen = SolveAffineCollocation(oxygenY0, span, oxygenSpec0,
            c => BuildOxygenSystem(TrialAt, c, hold.BrainDepleting, hold.HeadPinned,
                hold.LowerPinned, hold.CorePinned, upstream.GxMagnitude, settings),
            prepareDenseTaylor: !oxygenOnly);
        if (!piece.Oxygen.Valid) return piece;
        if (oxygenOnly)
        {
            piece.Valid = true;
            return piece;
        }

        var dead = state.IsDead;
        [MethodImpl(AggressiveOptimization)]
        double HeadO2At(double c) => piece.Oxygen.EvaluateFirst(c);
        var consciousnessInitial = dead ? 0.0 : state.ConsciousnessLevel;
        var cSpec0 = BuildConsciousnessSystem(TargetTrialAt, HeadO2At, 0.0,
            hold.ConsciousnessLosing, dead, settings);
        if (!dead && cSpec0.Instant[0] &&
            state.ConsciousnessLevel != cSpec0.InstantTargets[0]!(0.0))
            piece.InitialEvents.Add("InstantaneousConsciousnessLimit");

        [MethodImpl(AggressiveOptimization)]
        double ConsciousnessTargetAtNode(double c) =>
            ConsciousnessTargetAt(TargetTrialAt, HeadO2At, c, settings);

        var homogeneousZeroTargetLoss = false;
        if (!dead && hold.ConsciousnessLosing && allowHomogeneousZeroTargetLoss)
        {
            if (TryCreateHomogeneousZeroTargetLoss(state, upstream, cursor, span,
                    piece, zeroTargetBranchAtEnd, settings, out var homogeneous,
                    out _) && homogeneous != null)
            {
                homogeneousZeroTargetLoss = true;
                piece.Consciousness = homogeneous;
            }
        }
        if (!homogeneousZeroTargetLoss)
        {
            if (!dead && !hold.ConsciousnessLosing &&
                settings.ConsciousnessRecoveryTau > 1e-9)
            {
                piece.Consciousness = new AffineSolution
                {
                    Valid = true,
                    Y0 = [consciousnessInitial],
                    Duration = span,
                    ScalarOverride = new ScalarLagSolution(consciousnessInitial, span,
                        settings.ConsciousnessRecoveryTau,
                        TargetSurrogate(ConsciousnessTargetAtNode)),
                    LoCCeilingZeroOnPiece =
                        IsFiniteNumber(consciousnessInitial) &&
                        IsFiniteNumber(span) && span >= 0.0 &&
                        IsFiniteNumber(settings.ConsciousnessRecoveryTau) &&
                        IsFiniteNumber(settings.ConsciousnessRecoveryThreshold) &&
                        IsFiniteNumber(settings.VisualLoCConsciousnessExponent) &&
                        settings.VisualLoCConsciousnessExponent > 0.0 &&
                        consciousnessInitial *
                        Math.Exp(-span / settings.ConsciousnessRecoveryTau) >
                        settings.ConsciousnessRecoveryThreshold + 1e-6
                };
            }
            else
            {
                piece.Consciousness = SolveAffineCollocation(
                    new[] { consciousnessInitial }, span, cSpec0,
                    c => BuildConsciousnessSystem(TargetTrialAt, HeadO2At, c,
                        hold.ConsciousnessLosing, dead, settings));
            }
        }
        if (piece.Consciousness.ScalarOverride != null &&
            !homogeneousZeroTargetLoss)
        {
            if (!IsFiniteNumber(piece.Consciousness.Evaluate(0.0)[0]) ||
                !IsFiniteNumber(piece.Consciousness.Evaluate(1.0)[0]))
                piece.Consciousness.Valid = false;
            for (var stage = 0; stage < NumericalMath.RadauStageCount; stage++)
            {
                var value = piece.Consciousness.Evaluate(
                    NumericalMath.RadauC[stage])[0];
                if (!IsFiniteNumber(value) || value < 0.0 || value > 1.0)
                    piece.Consciousness.Valid = false;
            }
        }
        if (!piece.Consciousness.Valid) return piece;

        var visualTaus = new (double In, double Out)[]
        {
            (settings.VisualTunnelVisionInTau, settings.VisualTunnelVisionOutTau),
            (settings.VisualRedoutInTau, settings.VisualRedoutOutTau),
            (settings.VisualGrayscaleInTau, settings.VisualGrayscaleOutTau)
        };
        Func<int, double, double> visualTarget = (channel, c) =>
        {
            var head = upstream.HeadAt(cursor + c * span);
            if (channel == 1)
                return RedoutTarget(Math.Max(
                    (head - settings.RestingBloodHead) / settings.RestingBloodHead,
                    0.0), settings);
            if (head >= settings.RestingBloodHead) return 0.0;
            return PhysiologicalVisualTarget(head,
                O2Normalized(piece.Oxygen.EvaluateFirst(c), settings), settings);
        };
        piece.VisualTargetAt = visualTarget;
        piece.HasHeadRange = upstream.TryHeadRange(cursor, cursor + span,
            out var minimumHead, out var maximumHead);
        if (piece.HasHeadRange)
        {
            piece.MinimumHead = minimumHead;
            piece.MaximumHead = maximumHead;
            var restingHead = settings.RestingBloodHead;
            if (minimumHead > restingHead)
            {
                piece.TunnelVisionConstantTarget = 0.0;
                piece.GrayscaleConstantTarget = 0.0;
            }

            var redoutOnsetHead = restingHead *
                (1.0 + settings.VisualRedoutOnsetHeadBloodOverfill);
            var redoutFullHead = restingHead *
                (1.0 + settings.VisualRedoutFullHeadBloodOverfill);
            if (IsFiniteNumber(redoutOnsetHead) && IsFiniteNumber(redoutFullHead) &&
                redoutFullHead > redoutOnsetHead)
            {
                if (maximumHead < redoutOnsetHead)
                    piece.RedoutConstantTarget = 0.0;
                else if (minimumHead > redoutFullHead)
                    piece.RedoutConstantTarget = 1.0;
            }
        }
        var visualInitial = new[]
        {
            state.VisualTunnelVisionLevel, state.VisualRedoutLevel,
            state.VisualGrayscaleLevel
        };
        piece.Visuals = new ScalarLagSolution[3];
        for (var channel = 0; channel < 3; channel++)
        {
            var index = channel;
            var tau = hold.Increasing[index]
                ? visualTaus[index].In
                : visualTaus[index].Out;
            var constantTarget = piece.ConstantVisualTarget(index);
            if (constantTarget.HasValue)
            {
                if (tau <= 1e-9 && visualInitial[index] != constantTarget.Value)
                    piece.InitialEvents.Add(VisualInstantNames[index]);
                piece.Visuals[index] = new ScalarLagSolution(visualInitial[index],
                    span, tau, constantTarget.Value);
            }
            else
            {
                // The convolution probes the target at many positions; a
                // validated cubic surrogate keeps them cheap, with exact
                // evaluation retained whenever the fit is poor.
                var targetAt = tau > 1e-9
                    ? TargetSurrogate(c => visualTarget(index, c))
                    : (Func<double, double>)(c => visualTarget(index, c));
                if (tau <= 1e-9 && visualInitial[index] != targetAt(0.0))
                    piece.InitialEvents.Add(VisualInstantNames[index]);
                piece.Visuals[index] = new ScalarLagSolution(
                    visualInitial[index], span, tau, targetAt);
            }
            var initialValue = piece.Visuals[index].Evaluate(0.0);
            if (!IsFiniteNumber(initialValue) || initialValue < 0.0 || initialValue > 1.0)
                return piece;
            for (var stage = 0; stage < NumericalMath.RadauStageCount; stage++)
            {
                var value = piece.Visuals[index].Evaluate(NumericalMath.RadauC[stage]);
                if (!IsFiniteNumber(value) || value < 0.0 || value > 1.0) return piece;
            }
            var finalValue = piece.Visuals[index].Evaluate(1.0);
            if (!IsFiniteNumber(finalValue) || finalValue < 0.0 || finalValue > 1.0)
                return piece;
        }

        piece.Valid = true;
        return piece;
    }

    [MethodImpl(AggressiveOptimization)]
    private static LoCTrajectory ChaseLoC(IntegrationState state,
        AffineSolution consciousness, double cursor, double span, LogicSettings settings)
    {
        var trajectory = new LoCTrajectory
        {
            Consciousness = consciousness,
            PieceStart = cursor,
            Settings = settings,
            StartValue = state.VisualLoCLevel
        };
        if (state.IsUnconscious || state.IsDead)
        {
            trajectory.Pinned = true;
            return trajectory;
        }

        var end = cursor + span;
        var ceiling0 = consciousness.LoCCeilingZeroOnPiece
            ? 0.0
            : trajectory.CeilingAt(cursor);
        var ceilingDerivative0 = consciousness.LoCCeilingZeroOnPiece
            ? 0.0
            : CeilingDerivative(consciousness, 0.0, settings);
        var increase = settings.VisualLoCIncreaseRate;
        var decrease = settings.VisualLoCDecreaseRate;
        trajectory.Contact = Math.Abs(state.VisualLoCLevel - ceiling0) <= 1e-9 &&
                             ceilingDerivative0 <= increase &&
                             ceilingDerivative0 >= -decrease;
        if (trajectory.Contact)
        {
            var exitUp = double.PositiveInfinity;
            var exitDown = double.PositiveInfinity;
            if (!consciousness.LoCCeilingZeroOnPiece)
            {
                exitUp = ScanCrossing(
                    t => CeilingDerivative(consciousness, (t - cursor) / span, settings) -
                         increase,
                    cursor, end, (CrossingSamples?)null, out _,
                    PressureBoundRootToleranceSeconds, safeguardedRefine: true);
                exitDown = ScanCrossing(
                    t => -decrease -
                         CeilingDerivative(consciousness, (t - cursor) / span, settings),
                    cursor, end, (CrossingSamples?)null, out _,
                    PressureBoundRootToleranceSeconds, safeguardedRefine: true);
            }
            var exit = Math.Min(exitUp, exitDown);
            if (exit <= end)
            {
                trajectory.FirstEventTime = exit;
                trajectory.FirstEventKind = "LoCContactExit";
            }
        }
        else
        {
            trajectory.Rate = state.VisualLoCLevel < ceiling0 ? increase : -decrease;
            var v0 = state.VisualLoCLevel;
            var rate = trajectory.Rate;
            var contact = double.PositiveInfinity;
            var contactWithinPiece = false;
            // A level already on the ceiling needs no entry event; it arises only when
            // the ceiling derivative is untrackable, and scanning would re-detect the
            // same contact at the piece start forever.
            var hasGapToClose = Math.Abs(ceiling0 - v0) > 1e-9;
            if (hasGapToClose &&
                consciousness.LoCCeilingZeroOnPiece && rate < 0.0 &&
                IsFiniteNumber(rate) && IsFiniteNumber(decrease) && decrease > 0.0)
            {
                contact = cursor + state.VisualLoCLevel / decrease;
                contactWithinPiece = IsFiniteNumber(contact) &&
                                     contact >= cursor && contact <= end;
            }
            if (!contactWithinPiece && hasGapToClose)
                contact = ScanCrossing(
                    t => v0 + rate * (t - cursor) - trajectory.CeilingAt(t),
                    cursor, end, (CrossingSamples?)null, out _,
                    PressureBoundRootToleranceSeconds, safeguardedRefine: true);
            if (contact <= end)
            {
                trajectory.FirstEventTime = contact;
                trajectory.FirstEventKind = "LoCContactEntry";
            }
        }
        return trajectory;
    }

    // Collocation piece evaluations are polynomial approximations of bounded
    // trajectories; a stage can land a fit-error distance outside [0,1]. Snap
    // only within this allowance so genuine violations still surface.
    private const double DownstreamBoundSnapTolerance = 1e-5;

    [MethodImpl(AggressiveOptimization)]
    private static double SnapUnitBound(double value) => value switch
    {
        > 1.0 and <= 1.0 + DownstreamBoundSnapTolerance => 1.0,
        < 0.0 and >= -DownstreamBoundSnapTolerance => 0.0,
        _ => value
    };

    [MethodImpl(AggressiveOptimization)]
    private static bool TryFullStateAt(double t, IntegrationState baseState,
        IntervalUpstream upstream, DownstreamSolution piece, LoCTrajectory loc,
        double cursor, double span, BloodBounds activeBloodBounds, LogicSettings settings,
        out IntegrationState accepted, out double correction)
    {
        var circ = upstream.CirculationStateAt(t);
        var c = span > 0.0 ? (t - cursor) / span : 0.0;
        var oxygen = piece.Oxygen.Evaluate(c);
        var consciousness = baseState.IsDead
            ? 0.0
            : SnapUnitBound(piece.Consciousness.Evaluate(c)[0]);
        var locValue = baseState.IsUnconscious || baseState.IsDead
            ? 1.0
            : SnapUnitBound(loc.ValueAt(t));
        var candidate = new IntegrationState(circ.BloodHead, circ.BloodLower,
            circ.HeartRateMultiplier, circ.CardioFatigue,
            SnapUnitBound(upstream.PressureAt(t)), circ.RespiratoryFatigue)
        {
            StrainingLevel = circ.StrainingLevel,
            StrainingFatigue = circ.StrainingFatigue,
            GSuitFatigue = circ.GSuitFatigue,
            BloodO2Head = SnapUnitBound(oxygen[0]),
            BloodO2Lower = SnapUnitBound(oxygen[1]),
            BloodO2Core = SnapUnitBound(oxygen[2]),
            ArterialOxygenation = SnapUnitBound(upstream.ArterialAt(t)),
            ConsciousnessLevel = consciousness,
            LungCompressionLevel = SnapUnitBound(upstream.CompressionAt(t)),
            PainLevel = SnapUnitBound(upstream.PainAt(t)),
            GyNeckFatigue = SnapUnitBound(upstream.NeckAt(t)),
            GyNeckFatigueDeathDwell = SnapUnitBound(upstream.NeckDwellAt(t)),
            SuddenLoCAccumulator = SnapUnitBound(upstream.SuddenAt(t)),
            VisualTunnelVisionLevel = SnapUnitBound(piece.Visuals[0].Evaluate(c)),
            VisualRedoutLevel = SnapUnitBound(piece.Visuals[1].Evaluate(c)),
            VisualGrayscaleLevel = SnapUnitBound(piece.Visuals[2].Evaluate(c)),
            VisualLoCLevel = locValue,
            IsUnconscious = baseState.IsUnconscious,
            InSuddenLoC = baseState.InSuddenLoC,
            IsDead = baseState.IsDead
        };
        return TryProjectActiveBloodState(in candidate, activeBloodBounds, settings,
            ActiveManifoldCorrectionTolerance, out accepted, out correction);
    }

    [MethodImpl(AggressiveOptimization)]
    private static int EventRank(string kind) => kind switch
    {
        "NeckDeathExit" => 0,
        "Death" => 1,
        "SuddenTrigger" => 2,
        "SuddenRecovery" => 3,
        "ConsciousnessLost" => 4,
        "ConsciousnessRecovered" => 5,
        _ => 6
    };

    [MethodImpl(AggressiveOptimization)]
    private static string DownstreamViolation(IntegrationState stage,
        LogicSettings settings, BloodBounds activeBloodBounds = BloodBounds.None)
    {
        if (!IsFiniteNumber(stage.BloodHead) || !IsFiniteNumber(stage.BloodLower) ||
            !IsFiniteNumber(stage.BloodCore) || !IsFiniteNumber(stage.HeartRateMultiplier) ||
            !IsFiniteNumber(stage.CardioFatigue) ||
            !IsFiniteNumber(stage.CerebralPressureImpairment) ||
            !IsFiniteNumber(stage.RespiratoryFatigue) ||
            !IsFiniteNumber(stage.StrainingLevel) ||
            !IsFiniteNumber(stage.StrainingFatigue) ||
            !IsFiniteNumber(stage.GSuitFatigue) ||
            !IsFiniteNumber(stage.BloodO2Head) || !IsFiniteNumber(stage.BloodO2Core) ||
            !IsFiniteNumber(stage.BloodO2Lower) ||
            !IsFiniteNumber(stage.ArterialOxygenation) ||
            !IsFiniteNumber(stage.ConsciousnessLevel) ||
            !IsFiniteNumber(stage.LungCompressionLevel) ||
            !IsFiniteNumber(stage.PainLevel) ||
            !IsFiniteNumber(stage.GyNeckFatigue) ||
            !IsFiniteNumber(stage.GyNeckFatigueDeathDwell) ||
            !IsFiniteNumber(stage.SuddenLoCAccumulator) ||
            !IsFiniteNumber(stage.VisualTunnelVisionLevel) ||
            !IsFiniteNumber(stage.VisualRedoutLevel) ||
            !IsFiniteNumber(stage.VisualGrayscaleLevel) ||
            !IsFiniteNumber(stage.VisualLoCLevel))
            return "nonfinite full state";

        const double activeManifoldTolerance = 1e-12;
        var headTolerance = (activeBloodBounds & BloodBounds.Head) != 0
            ? activeManifoldTolerance
            : 0.0;
        var lowerTolerance = (activeBloodBounds & BloodBounds.Lower) != 0
            ? activeManifoldTolerance
            : 0.0;
        var coreTolerance = (activeBloodBounds & BloodBounds.Core) != 0
            ? activeManifoldTolerance
            : 0.0;
        var bloodSumError = Math.Abs(
            stage.BloodHead + stage.BloodCore + stage.BloodLower - 1.0);
        if (bloodSumError > activeManifoldTolerance)
            return $"blood sum error {bloodSumError:R}";
        if (stage.BloodHead < settings.MinHeadBloodFraction - headTolerance ||
            stage.BloodHead > 1.0 ||
            stage.BloodLower < -lowerTolerance || stage.BloodLower > 1.0 ||
            stage.BloodCore < -coreTolerance || stage.BloodCore > 1.0 ||
            stage.HeartRateMultiplier < 0.0 ||
            stage.HeartRateMultiplier > settings.MaxHeartRateMultiplier)
            return string.Format(CultureInfo.InvariantCulture,
                "blood or heart-rate state outside physical bounds: head={0:R}; core={1:R}; " +
                "lower={2:R}; heartRateMultiplier={3:R}; headFloor={4:R}; " +
                "maxHeartRateMultiplier={5:R}", stage.BloodHead, stage.BloodCore,
                stage.BloodLower, stage.HeartRateMultiplier,
                settings.MinHeadBloodFraction, settings.MaxHeartRateMultiplier);
        if (stage.CardioFatigue is < 0.0 or > 1.0 ||
            stage.CerebralPressureImpairment is < 0.0 or > 1.0 ||
            stage.RespiratoryFatigue is < 0.0 or > 1.0 ||
            stage.StrainingLevel is < 0.0 or > 1.0 ||
            stage.StrainingFatigue is < 0.0 or > 1.0 ||
            stage.GSuitFatigue is < 0.0 or > 1.0 ||
            stage.BloodO2Head < settings.BrainO2Floor || stage.BloodO2Head > 1.0 ||
            stage.BloodO2Core is < 0.0 or > 1.0 ||
            stage.BloodO2Lower is < 0.0 or > 1.0 ||
            stage.ArterialOxygenation is < 0.0 or > 1.0 ||
            stage.ConsciousnessLevel is < 0.0 or > 1.0 ||
            stage.LungCompressionLevel is < 0.0 or > 1.0 ||
            stage.PainLevel is < 0.0 or > 1.0 ||
            stage.GyNeckFatigue is < 0.0 or > 1.0 ||
            stage.GyNeckFatigueDeathDwell is < 0.0 or > 1.0 ||
            stage.SuddenLoCAccumulator is < 0.0 or > 1.0)
            return $"full state outside physical bounds: " +
                   $"cardio={stage.CardioFatigue:R}; pressure={stage.CerebralPressureImpairment:R}; " +
                   $"respiratory={stage.RespiratoryFatigue:R}; straining={stage.StrainingLevel:R}; " +
                   $"strainFatigue={stage.StrainingFatigue:R}; gSuitFatigue={stage.GSuitFatigue:R}; " +
                   $"oxygen=[{stage.BloodO2Head:R},{stage.BloodO2Core:R},{stage.BloodO2Lower:R}]; " +
                   $"arterial={stage.ArterialOxygenation:R}; consciousness={stage.ConsciousnessLevel:R}; " +
                   $"compression={stage.LungCompressionLevel:R}; pain={stage.PainLevel:R}; " +
                   $"neck={stage.GyNeckFatigue:R}; dwell={stage.GyNeckFatigueDeathDwell:R}; " +
                   $"sudden={stage.SuddenLoCAccumulator:R}";
        if (stage.VisualTunnelVisionLevel is < 0.0 or > 1.0 ||
            stage.VisualRedoutLevel is < 0.0 or > 1.0 ||
            stage.VisualGrayscaleLevel is < 0.0 or > 1.0 ||
            stage.VisualLoCLevel is < 0.0 or > 1.0)
            return $"visual state outside [0,1]: " +
                   $"tunnel={stage.VisualTunnelVisionLevel:R}; " +
                   $"redout={stage.VisualRedoutLevel:R}; " +
                   $"grayscale={stage.VisualGrayscaleLevel:R}; " +
                   $"loc={stage.VisualLoCLevel:R}";
        if (stage.IsDead && (!stage.IsUnconscious ||
                             stage.ConsciousnessLevel != 0.0 ||
                             stage.VisualLoCLevel != 1.0 ||
                             stage.GyNeckFatigueDeathDwell != 1.0))
            return "dead state is not permanently pinned";
        return "";
    }

    [MethodImpl(AggressiveOptimization)]
    private static double OxygenCoordinateValue(in IntegrationState state, int coordinate) =>
        coordinate switch
        {
            0 => state.BloodO2Head,
            1 => state.BloodO2Lower,
            2 => state.BloodO2Core,
            _ => double.NaN
        };

    [MethodImpl(AggressiveOptimization)]
    private static IntegrationState SetOxygenCoordinate(IntegrationState state,
        int coordinate, double level) =>
        coordinate switch
        {
            0 => state with { BloodO2Head = level },
            1 => state with { BloodO2Lower = level },
            2 => state with { BloodO2Core = level },
            _ => state
        };

    [MethodImpl(AggressiveOptimization)]
    private static IntegrationState ApplyEvent(IntegrationState state, string kind,
        LogicSettings settings)
    {
        switch (kind)
        {
            case "Death":
                return state with
                {
                    IsDead = true,
                    ConsciousnessLevel = 0.0,
                    IsUnconscious = true,
                    VisualLoCLevel = 1.0,
                    GyNeckFatigueDeathDwell = 1.0
                };
            case "SuddenTrigger":
                {
                    var dropped = Math.Max(0.0,
                        state.ConsciousnessLevel - settings.SuddenLoCConsciousnessDrop);
                    var unconscious = state.IsUnconscious ||
                                      dropped <= settings.ConsciousnessLossThreshold;
                    return state with
                    {
                        InSuddenLoC = true,
                        SuddenLoCAccumulator = Math.Abs(state.SuddenLoCAccumulator - 0.8) <= 1e-6
                            ? 0.8
                            : state.SuddenLoCAccumulator,
                        ConsciousnessLevel = dropped,
                        IsUnconscious = unconscious,
                        VisualLoCLevel = unconscious ? 1.0 : state.VisualLoCLevel
                    };
                }
            case "SuddenRecovery":
                return state with
                {
                    InSuddenLoC = false,
                    SuddenLoCAccumulator = Math.Abs(state.SuddenLoCAccumulator - 0.3) <= 1e-6
                        ? 0.3
                        : state.SuddenLoCAccumulator
                };
            case "ConsciousnessLost":
                return state with
                {
                    IsUnconscious = true,
                    VisualLoCLevel = 1.0,
                    ConsciousnessLevel =
                        Math.Abs(state.ConsciousnessLevel -
                                 settings.ConsciousnessLossThreshold) <= 1e-6
                            ? settings.ConsciousnessLossThreshold
                            : state.ConsciousnessLevel
                };
            case "ConsciousnessRecovered":
                return state with
                {
                    IsUnconscious = false,
                    ConsciousnessLevel =
                        Math.Abs(state.ConsciousnessLevel -
                                 settings.ConsciousnessRecoveryThreshold) <= 1e-6
                            ? settings.ConsciousnessRecoveryThreshold
                            : state.ConsciousnessLevel
                };
            case "NeckDeathExit":
                return state with { GyNeckFatigueDeathDwell = 0.0 };
            default:
                return state;
        }
    }


    [MethodImpl(AggressiveOptimization)]
    private static bool TryProjectIncomingMode(IntervalLoopContext ctx, double span,
        ref DownstreamHold hold)
    {
        var margin = hold.ConsciousnessMargin;
        var marginSlope = hold.ConsciousnessMarginRightSlope;
        var correctionLimit = Math.Abs(marginSlope) *
            PressureBoundRootToleranceSeconds + 1e-12;
        var projectedRootTime = marginSlope != 0.0
            ? -margin / marginSlope
            : double.NaN;
        var incomingModeCrossing =
            (margin < 0.0 && marginSlope > 0.0) ||
            (margin > 0.0 && marginSlope < 0.0);
        if (!ctx.state.IsDead && incomingModeCrossing &&
            IsFiniteNumber(hold.ConsciousnessTarget) &&
            hold.ConsciousnessTarget is >= 0.0 and <= 1.0 &&
            IsFiniteNumber(margin) && IsFiniteNumber(marginSlope) &&
            IsFiniteNumber(hold.ConsciousnessTau) &&
            hold.ConsciousnessTau > 1e-9 &&
            IsFiniteNumber(correctionLimit) &&
            Math.Abs(margin) <= correctionLimit &&
            IsFiniteNumber(projectedRootTime) && projectedRootTime >= 0.0 &&
            projectedRootTime <= PressureBoundRootToleranceSeconds)
        {
            var projectedState = ctx.state with
            {
                ConsciousnessLevel = hold.ConsciousnessTarget
            };
            var activeBloodBounds = BloodBounds.None;
            foreach (var blood in ctx.circulation.Segments)
                if (blood.StartOffset <= ctx.cursor)
                    activeBloodBounds = blood.Bounds;
            var projectionViolation = DownstreamViolation(projectedState, ctx.settings,
                activeBloodBounds);
            if (projectionViolation.Length != 0)
            {
                ctx.violations.Add($"immediate consciousness mode projection at " +
                    $"{ctx.cursor:R}: {projectionViolation}");
                return false;
            }

            ctx.state = projectedState;
            if (ctx.segments.Count > 0)
            {
                var lastSegmentIndex = ctx.segments.Count - 1;
                var lastSegment = ctx.segments[lastSegmentIndex];
                if (Math.Abs(lastSegment.StartOffset + lastSegment.Duration -
                             ctx.cursor) <= 1e-12)
                {
                    var updatedStages = (IntegrationState[])lastSegment.Stages.Clone();
                    if (updatedStages.Length > 0)
                    {
                        var lastStageIndex = updatedStages.Length - 1;
                        updatedStages[lastStageIndex] = updatedStages[lastStageIndex] with
                        {
                            ConsciousnessLevel = ctx.state.ConsciousnessLevel
                        };
                        ctx.segments[lastSegmentIndex] = lastSegment with
                        {
                            Final = ctx.state,
                            Stages = updatedStages
                        };
                        if (ctx.stages.Count > 0)
                            ctx.stages[ctx.stages.Count - 1] = updatedStages[lastStageIndex];
                    }
                }
            }
            ctx.events.Add(new IntegrationEvent("ConsciousnessModeTransition", ctx.cursor,
                ctx.state));
            hold = ComputeHold(ctx.state,
                c => ctx.upstream.Evaluate(ctx.cursor + c * span), span,
                ctx.upstream.GxMagnitude, ctx.settings);
        }

        return true;
    }

    [MethodImpl(AggressiveOptimization)]
    private bool ScanAndAcceptPiece(IntervalLoopContext ctx,
        DownstreamSolution piece, LoCTrajectory loc, DownstreamHold hold,
        double span, double end)
    {
        ctx.candidates.Clear();
        [MethodImpl(AggressiveOptimization)]
        void Offer(double t, string eventKind, int coordinate = -1, double level = 0.0,
            int direction = 0)
        {
            if (t > ctx.cursor + 1e-12 && t <= end)
                ctx.candidates.Add((t, eventKind, coordinate, level, direction));
        }

        Offer(ctx.upstream.NeckClampTime, "NeckCeilingClamp");
        if (!ctx.state.IsDead)
        {
            Offer(ctx.upstream.NeckDeathEntry, "NeckDeathEntry");
            Offer(ctx.upstream.NeckDeathExit, "NeckDeathExit");
        }
        if (!ctx.state.IsDead) Offer(ctx.upstream.DeathCompletionTime(), "Death");
        Offer(ctx.upstream.SuddenTriggerTime(ctx.state.InSuddenLoC, ctx.state.IsDead),
            "SuddenTrigger");
        if (!ctx.state.IsDead)
            Offer(ctx.upstream.SuddenRecoveryTime(ctx.state.InSuddenLoC), "SuddenRecovery");
        Offer(ctx.upstream.RespiratoryBoundTime, "RespiratoryCeiling");
        Offer(ctx.upstream.SuddenBoundTime, "SuddenAccumulatorBound");
        foreach (var blood in ctx.circulation.Segments)
            if (blood.StartOffset > ctx.cursor)
                Offer(blood.StartOffset, "BloodSegmentBoundary");
        foreach (var (entry, release) in ctx.upstream.PressureEpisodes)
        {
            Offer(entry, "PressureBoundBoundary");
            Offer(release, "PressureBoundBoundary");
        }
        if (loc.FirstEventTime > ctx.cursor)
            Offer(loc.FirstEventTime, loc.FirstEventKind);

        var coordPins = new[] { hold.HeadPinned, hold.LowerPinned, hold.CorePinned };
        var restHead = ctx.settings.RestingBloodHead;
        var softMin = ctx.settings.ConsciousnessPerfusionSoftMinRatio;

        ctx.eventProbes[0] = new FullIntervalEventProbe
        {
            Time = ctx.cursor
        };
        for (var probe = 0; probe < ScanProbes.Length; probe++)
        {
            var time = ctx.cursor + ScanProbes[probe] * span;
            ctx.eventProbes[probe + 1] = new FullIntervalEventProbe
            {
                Time = time
            };
        }
        ctx.eventProbes[ScanProbes.Length + 1] = new FullIntervalEventProbe
        {
            Time = end
        };

        var activeHeadLevel = 0.0;
        Func<double, double>? activeHeadMarginAtHead = null;
        [MethodImpl(AggressiveOptimization)]
        double ActiveHeadMarginAtTime(double time)
        {
            var head = ctx.upstream.HeadAt(time);
            return activeHeadMarginAtHead == null
                ? head - activeHeadLevel
                : activeHeadMarginAtHead(head);
        }
        Func<double, double> activeHeadMarginAtTime = ActiveHeadMarginAtTime;

        [MethodImpl(AggressiveOptimization)]
        double ScanHeadCrossing(double level, out int direction,
            double toleranceSeconds = PressureBoundRootToleranceSeconds)
        {
            if (piece.HasHeadRange &&
                (piece.MinimumHead > level || piece.MaximumHead < level))
            {
                direction = 0;
                return double.PositiveInfinity;
            }

            activeHeadLevel = level;
            activeHeadMarginAtHead = null;
            var samples = new CrossingSamples(
                ctx.eventProbes[0].Head - level,
                ctx.eventProbes[1].Head - level,
                ctx.eventProbes[2].Head - level,
                ctx.eventProbes[3].Head - level,
                ctx.eventProbes[4].Head - level,
                ctx.eventProbes[5].Head - level,
                ctx.eventProbes[6].Head - level);
            return ScanCrossing(activeHeadMarginAtTime, ctx.cursor, end, samples,
                out direction, toleranceSeconds);
        }

        [MethodImpl(AggressiveOptimization)]
        double ScanHeadDerivedCrossing(Func<double, double> marginAtHead,
            out int direction,
            double toleranceSeconds = PressureBoundRootToleranceSeconds)
        {
            activeHeadMarginAtHead = marginAtHead;
            var samples = new CrossingSamples(
                marginAtHead(ctx.eventProbes[0].Head),
                marginAtHead(ctx.eventProbes[1].Head),
                marginAtHead(ctx.eventProbes[2].Head),
                marginAtHead(ctx.eventProbes[3].Head),
                marginAtHead(ctx.eventProbes[4].Head),
                marginAtHead(ctx.eventProbes[5].Head),
                marginAtHead(ctx.eventProbes[6].Head));
            return ScanCrossing(activeHeadMarginAtTime, ctx.cursor, end, samples,
                out direction, toleranceSeconds);
        }

        [MethodImpl(AggressiveOptimization)]
        double NaturalOxygenDerivative(FullIntervalEventProbe probe, int coordinate)
        {
            var trial = probe.Trial;
            var natural = BuildOxygenSystem(_ => trial, 0.0,
                hold.BrainDepleting, false, false, false,
                ctx.upstream.GxMagnitude, ctx.settings);
            var derivative = natural.B[coordinate];
            for (var k = 0; k < 3; k++)
                derivative += natural.M[coordinate][k] * probe.OxygenAt(k);
            return derivative;
        }

        [MethodImpl(AggressiveOptimization)]
        FullIntervalEventProbe SampleAt(double time,
            FullIntervalEventProbeNeeds needs, int coordinate)
        {
            var normalizedTime = (time - ctx.cursor) / span;
            var probe = new FullIntervalEventProbe
            {
                Time = time
            };
            if ((needs & FullIntervalEventProbeNeeds.NaturalOxygenDerivative) != 0)
                needs |= FullIntervalEventProbeNeeds.Trial |
                         FullIntervalEventProbeNeeds.Oxygen;
            if ((needs & FullIntervalEventProbeNeeds.Trial) != 0)
            {
                probe.Trial = ctx.upstream.Evaluate(time);
                probe.Head = probe.Trial.Head;
            }
            else if ((needs & FullIntervalEventProbeNeeds.Head) != 0)
            {
                probe.Head = ctx.upstream.HeadAt(time);
            }

            if ((needs & FullIntervalEventProbeNeeds.Oxygen) != 0)
            {
                var oxygen = piece.Oxygen.Evaluate(normalizedTime);
                probe.HeadOxygen = oxygen[0];
                probe.LowerOxygen = oxygen[1];
                probe.CoreOxygen = oxygen[2];
            }
            if ((needs & FullIntervalEventProbeNeeds.Consciousness) != 0)
                probe.Consciousness = piece.Consciousness.Evaluate(normalizedTime)[0];
            if ((needs & FullIntervalEventProbeNeeds.Pressure) != 0)
                probe.Pressure = (needs & FullIntervalEventProbeNeeds.Trial) != 0
                    ? probe.Trial.Pressure
                    : ctx.upstream.PressureAt(time);
            if ((needs & FullIntervalEventProbeNeeds.Compression) != 0)
                probe.Compression = (needs & FullIntervalEventProbeNeeds.Trial) != 0
                    ? probe.Trial.Compression
                    : ctx.upstream.CompressionAt(time);
            if ((needs & FullIntervalEventProbeNeeds.Respiratory) != 0)
                probe.Respiratory = (needs & FullIntervalEventProbeNeeds.Trial) != 0
                    ? probe.Trial.Respiratory
                    : ctx.upstream.RespiratoryAt(time);
            if ((needs & FullIntervalEventProbeNeeds.TunnelVision) != 0)
            {
                probe.TunnelVision = piece.Visuals[0].Evaluate(normalizedTime);
                probe.TunnelVisionTarget = piece.ConstantVisualTarget(0) ??
                    piece.VisualTargetAt!(0, normalizedTime);
            }
            if ((needs & FullIntervalEventProbeNeeds.Redout) != 0)
            {
                probe.Redout = piece.Visuals[1].Evaluate(normalizedTime);
                probe.RedoutTarget = piece.ConstantVisualTarget(1) ??
                    piece.VisualTargetAt!(1, normalizedTime);
            }
            if ((needs & FullIntervalEventProbeNeeds.Grayscale) != 0)
            {
                probe.Grayscale = piece.Visuals[2].Evaluate(normalizedTime);
                probe.GrayscaleTarget = piece.ConstantVisualTarget(2) ??
                    piece.VisualTargetAt!(2, normalizedTime);
            }
            if ((needs & FullIntervalEventProbeNeeds.NaturalOxygenDerivative) != 0)
                probe.NaturalOxygenDerivative =
                    NaturalOxygenDerivative(probe, coordinate);
            return probe;
        }

        for (var probeIndex = 0; probeIndex < ctx.eventProbes.Length; probeIndex++)
            ctx.eventProbes[probeIndex] = SampleAt(ctx.eventProbes[probeIndex].Time,
                FullIntervalEventProbeNeeds.Trial |
                FullIntervalEventProbeNeeds.Oxygen |
                FullIntervalEventProbeNeeds.Consciousness |
                FullIntervalEventProbeNeeds.Pressure |
                FullIntervalEventProbeNeeds.Compression |
                FullIntervalEventProbeNeeds.Respiratory |
                FullIntervalEventProbeNeeds.TunnelVision |
                FullIntervalEventProbeNeeds.Redout |
                FullIntervalEventProbeNeeds.Grayscale, -1);

        var perfusionSoftMinTime = ScanHeadCrossing(softMin * restHead, out _);
        var perfusionRangeTime = ScanHeadCrossing(restHead, out _,
            toleranceSeconds: 1e-12);
        var perfusionCapTime = ScanHeadCrossing(
            (0.25 * (1.0 - softMin) + softMin) * restHead, out _);
        var criticalPerfusionTime = ScanHeadCrossing(
            (ctx.settings.ConsciousnessCriticalPerfusionNorm * (1.0 - softMin) +
             softMin) * restHead, out _);
        var redoutOnsetTime = ScanHeadCrossing(
            restHead * (1.0 + ctx.settings.VisualRedoutOnsetHeadBloodOverfill), out _,
            toleranceSeconds: 1e-12);
        var redoutFullTime = ScanHeadCrossing(
            restHead * (1.0 + ctx.settings.VisualRedoutFullHeadBloodOverfill), out _,
            toleranceSeconds: 1e-12);
        var visualPerfusionTime = ScanHeadCrossing(0.45 * restHead, out _);
        var hypoperfusionThreshold = Clamp(
            ctx.settings.BrainO2HypoperfusionThreshold, 0.01, 1.0);
        var hypoperfusionTime = ScanHeadDerivedCrossing(
            head => ShapedPerfusion(head, ctx.settings) - hypoperfusionThreshold, out _,
            toleranceSeconds: 1e-12);
        var deliveryFloorTime = ScanHeadDerivedCrossing(
            head => DeliveryCore(ShapedPerfusion(head, ctx.settings), ctx.settings), out _);
        var deliveryCeilingTime = ScanHeadDerivedCrossing(
            head => DeliveryCore(ShapedPerfusion(head, ctx.settings), ctx.settings) - 1.0,
            out _);

        var activeNeeds = FullIntervalEventProbeNeeds.None;
        var activeCoordinate = -1;
        Func<FullIntervalEventProbe, double> activeMargin = null!;
        [MethodImpl(AggressiveOptimization)]
        double ActiveEventMarginAt(double time) =>
            activeMargin(SampleAt(time, activeNeeds, activeCoordinate));
        Func<double, double> activeEventMarginAt = ActiveEventMarginAt;

        [MethodImpl(AggressiveOptimization)]
        FullIntervalEventProbe PrepareProbe(FullIntervalEventProbe probe,
            FullIntervalEventProbeNeeds needs, int coordinate)
        {
            if ((needs & FullIntervalEventProbeNeeds.NaturalOxygenDerivative) != 0)
                probe.NaturalOxygenDerivative =
                    NaturalOxygenDerivative(probe, coordinate);
            return probe;
        }

        [MethodImpl(AggressiveOptimization)]
        double ScanSurface(Func<FullIntervalEventProbe, double> margin,
            FullIntervalEventProbeNeeds needs, out int direction, int coordinate = -1)
        {
            activeMargin = margin;
            activeNeeds = needs;
            activeCoordinate = coordinate;
            var values = new CrossingSamples(
                margin(PrepareProbe(ctx.eventProbes[0], needs, coordinate)),
                margin(PrepareProbe(ctx.eventProbes[1], needs, coordinate)),
                margin(PrepareProbe(ctx.eventProbes[2], needs, coordinate)),
                margin(PrepareProbe(ctx.eventProbes[3], needs, coordinate)),
                margin(PrepareProbe(ctx.eventProbes[4], needs, coordinate)),
                margin(PrepareProbe(ctx.eventProbes[5], needs, coordinate)),
                margin(PrepareProbe(ctx.eventProbes[6], needs, coordinate)));
            return ScanCrossing(activeEventMarginAt, ctx.cursor, end, values, out direction);
        }

        [MethodImpl(AggressiveOptimization)]
        double OxygenBoundEntryMargin(double oxygenValue, double level) =>
            oxygenValue - level;
        [MethodImpl(AggressiveOptimization)]
        double RawConsciousnessReserveMargin(double head, double headO2) =>
            ConsciousnessRawReserve(O2Normalized(headO2, ctx.settings),
                PerfusionNormalized(head, ctx.settings), ctx.settings);
        [MethodImpl(AggressiveOptimization)]
        double RawConsciousnessReserveAt(double time, double headO2) =>
            RawConsciousnessReserveMargin(ctx.upstream.HeadAt(time), headO2);

        [MethodImpl(AggressiveOptimization)]
        double BrainMargin(FullIntervalEventProbe probe)
        {
            var trial = probe.Trial;
            return BrainO2Target(EffectiveDelivery(
                ShapedPerfusion(trial.Head, ctx.settings), trial.HeartRate,
                trial.Arterial, ctx.settings), ctx.settings) - probe.HeadOxygen;
        }

        Offer(ScanSurface(BrainMargin,
            FullIntervalEventProbeNeeds.Trial | FullIntervalEventProbeNeeds.Oxygen,
            out _),
            "BrainO2ModeTransition");

        for (var coord = 0; coord < 3; coord++)
        {
            var index = coord;
            var lowBound = index == 0 ? ctx.settings.BrainO2Floor : 0.0;
            if (!coordPins[index])
            {
                var low = ScanSurface(
                    probe => OxygenBoundEntryMargin(probe.OxygenAt(index), lowBound),
                    FullIntervalEventProbeNeeds.Oxygen, out var lowDirection);
                if (lowDirection < 0)
                    Offer(low,
                        $"OxygenBoundEntry{OxygenCoordinateNames[index]}", index,
                        lowBound, lowDirection);
                var high = ScanSurface(
                    probe => OxygenBoundEntryMargin(probe.OxygenAt(index), 1.0),
                    FullIntervalEventProbeNeeds.Oxygen, out var highDirection);
                if (highDirection > 0)
                    Offer(high,
                        $"OxygenBoundEntry{OxygenCoordinateNames[index]}", index, 1.0,
                        highDirection);
            }
            else
            {
                var atLower = ctx.eventProbes[0].OxygenAt(index) <= lowBound;
                [MethodImpl(AggressiveOptimization)]
                double NaturalDerivative(FullIntervalEventProbe probe)
                {
                    return atLower
                        ? probe.NaturalOxygenDerivative
                        : -probe.NaturalOxygenDerivative;
                }

                var naturalNeeds = FullIntervalEventProbeNeeds.Trial |
                    FullIntervalEventProbeNeeds.Oxygen |
                    FullIntervalEventProbeNeeds.NaturalOxygenDerivative;
                var release = ScanSurface(NaturalDerivative, naturalNeeds,
                    out var releaseDirection, index);
                if (releaseDirection > 0)
                {
                    ctx.lastO2RootEvidence =
                        $"OxygenBoundRelease{OxygenCoordinateNames[index]} " +
                        $"coordinate={index} level={(atLower ? lowBound : 1.0):R} " +
                        $"direction={releaseDirection} time={release:R} " +
                        $"naturalResidual={NaturalDerivative(
                            SampleAt(release, naturalNeeds, index)):R}";
                    Offer(release,
                        $"OxygenBoundRelease{OxygenCoordinateNames[index]}", index,
                        atLower ? lowBound : 1.0, releaseDirection);
                }
            }
        }

        if (!ctx.state.IsDead)
        {
            var zeroBranch = ScanSurface(
                probe => RawConsciousnessReserveMargin(probe.Head, probe.HeadOxygen),
                FullIntervalEventProbeNeeds.Head |
                FullIntervalEventProbeNeeds.Oxygen, out var zeroBranchDirection);
            if (zeroBranchDirection > 0)
                Offer(zeroBranch, "ConsciousnessTargetZeroBranch",
                    direction: zeroBranchDirection);

            [MethodImpl(AggressiveOptimization)]
            double ConsciousnessMargin(FullIntervalEventProbe probe)
            {
                var target = ConsciousnessTargetValue(
                    O2Normalized(probe.HeadOxygen, ctx.settings),
                    PerfusionNormalized(probe.Head, ctx.settings), probe.Pressure,
                    ctx.settings);
                return target - probe.Consciousness;
            }

            var modeNeeds = FullIntervalEventProbeNeeds.Head |
                FullIntervalEventProbeNeeds.Pressure |
                FullIntervalEventProbeNeeds.Oxygen |
                FullIntervalEventProbeNeeds.Consciousness;
            var modeTransition = ScanSurface(ConsciousnessMargin, modeNeeds,
                out var modeDirection);
            Offer(modeTransition, "ConsciousnessModeTransition", direction: modeDirection);

            var threshold = ctx.state.IsUnconscious
                ? ctx.settings.ConsciousnessRecoveryThreshold
                : ctx.settings.ConsciousnessLossThreshold;
            var hysteresis = ScanSurface(
                probe => probe.Consciousness - threshold,
                FullIntervalEventProbeNeeds.Consciousness,
                out var hysteresisDirection);
            var validCrossing = ctx.state.IsUnconscious
                ? hysteresisDirection > 0
                : hysteresisDirection < 0;
            if (validCrossing)
                Offer(hysteresis,
                    ctx.state.IsUnconscious
                        ? "ConsciousnessRecovered"
                        : "ConsciousnessLost");
        }

        for (var channel = 0; channel < 3; channel++)
        {
            var index = channel;
            var needs = index switch
            {
                0 => FullIntervalEventProbeNeeds.TunnelVision,
                1 => FullIntervalEventProbeNeeds.Redout,
                _ => FullIntervalEventProbeNeeds.Grayscale
            };
            Offer(ScanSurface(
                probe => probe.VisualTarget(index) - probe.VisualValue(index),
                needs, out _), VisualTransitionNames[index]);
        }

        [MethodImpl(AggressiveOptimization)]
        double VisualReserve(FullIntervalEventProbe probe)
        {
            var perf = Clamp(probe.Head / restHead, 0.0, 1.0);
            var o2n = O2Normalized(probe.HeadOxygen, ctx.settings);
            return 0.7 * Clamp((perf - 0.45) / 0.55, 0.0, 1.0) +
                   0.3 * Clamp((o2n - 0.15) / 0.85, 0.0, 1.0);
        }

        Offer(perfusionSoftMinTime, "PerfusionSoftMinBound");
        Offer(perfusionRangeTime,
            "PerfusionRangeBound");
        Offer(perfusionCapTime,
            "PerfusionCapBranch");
        Offer(criticalPerfusionTime, "CriticalPerfusionBranch");
        Offer(ScanSurface(
            probe => probe.HeadOxygen - ctx.settings.BrainO2Blackout,
            FullIntervalEventProbeNeeds.Oxygen, out _),
            "O2NormFloor");
        Offer(ScanSurface(probe => probe.HeadOxygen - ctx.settings.BrainO2Full,
            FullIntervalEventProbeNeeds.Oxygen, out _),
            "O2NormCeiling");
        Offer(ScanSurface(probe => probe.HeadOxygen - (ctx.settings.BrainO2Blackout +
            ctx.settings.ConsciousnessCriticalO2Norm *
            (ctx.settings.BrainO2Full - ctx.settings.BrainO2Blackout)),
            FullIntervalEventProbeNeeds.Oxygen, out _),
            "CriticalO2Branch");
        Offer(ScanSurface(
            probe => probe.Pressure -
                     ctx.settings.CerebralPressureImpairmentDeadband,
            FullIntervalEventProbeNeeds.Pressure, out _),
            "PressureDeadbandBranch");
        Offer(ScanSurface(probe => probe.Pressure -
            (ctx.settings.CerebralPressureImpairmentDeadband +
             ctx.settings.ConsciousnessCriticalPressureNorm *
             (1.0 - ctx.settings.CerebralPressureImpairmentDeadband)),
            FullIntervalEventProbeNeeds.Pressure, out _),
            "CriticalPressureBranch");
        Offer(redoutOnsetTime, "RedoutOnset");
        Offer(redoutFullTime, "RedoutFull");
        Offer(visualPerfusionTime, "VisualPerfBranch");
        Offer(ScanSurface(probe => probe.HeadOxygen - (ctx.settings.BrainO2Blackout +
            0.15 * (ctx.settings.BrainO2Full - ctx.settings.BrainO2Blackout)),
            FullIntervalEventProbeNeeds.Oxygen, out _),
            "VisualO2Branch");
        Offer(ScanSurface(probe => 0.9 - probe.Compression *
            ctx.settings.GyLungCompressionSeverity -
            GxLungImpairment(ctx.upstream.GxMagnitude, ctx.settings) -
            0.5 * probe.Respiratory,
            FullIntervalEventProbeNeeds.Compression |
            FullIntervalEventProbeNeeds.Respiratory, out _),
            "LungEffectivenessFloor");
        var visualReserveNeeds = FullIntervalEventProbeNeeds.Head |
            FullIntervalEventProbeNeeds.Oxygen;
        Offer(ScanSurface(probe => VisualReserve(probe) - 0.82,
            visualReserveNeeds, out _),
            "VisualDeficitRamp");
        Offer(ScanSurface(VisualReserve, visualReserveNeeds, out _),
            "VisualDeficitCeiling");
        Offer(hypoperfusionTime, "HypoperfusionThreshold");
        Offer(deliveryFloorTime, "DeliveryFloor");
        Offer(deliveryCeilingTime, "DeliveryCeiling");

        [MethodImpl(AggressiveOptimization)]
        bool IsOxygenBoundEntry(
            (double Time, string Kind, int Coordinate, double Level, int Direction) item) =>
            item.Coordinate >= 0 &&
            item.Kind.StartsWith("OxygenBoundEntry", StringComparison.Ordinal);

        [MethodImpl(AggressiveOptimization)]
        bool IsConsciousnessTargetZeroBranch(
            (double Time, string Kind, int Coordinate, double Level, int Direction) item) =>
            item.Kind == "ConsciousnessTargetZeroBranch";

        [MethodImpl(AggressiveOptimization)]
        bool IsRootEvent(
            (double Time, string Kind, int Coordinate, double Level, int Direction) item) =>
            IsOxygenBoundEntry(item) || IsConsciousnessTargetZeroBranch(item);

        [MethodImpl(AggressiveOptimization)]
        static bool DirectedBracket(double lower, double upper, int direction) =>
            direction < 0
                ? lower >= 0.0 && upper <= 0.0 && (lower > 0.0 || upper < 0.0)
                : lower <= 0.0 && upper >= 0.0 && (lower < 0.0 || upper > 0.0);

        [MethodImpl(AggressiveOptimization)]
        double RootEventResidual(double time, double oxygenValue,
            (double Time, string Kind, int Coordinate, double Level, int Direction) item) =>
            IsOxygenBoundEntry(item)
                ? OxygenBoundEntryMargin(oxygenValue, item.Level)
                : RawConsciousnessReserveAt(time, oxygenValue);

        [MethodImpl(AggressiveOptimization)]
        double WholeEventResidual(double time,
            (double Time, string Kind, int Coordinate, double Level, int Direction) item)
        {
            var oxygen = piece.Oxygen.Evaluate((time - ctx.cursor) / span);
            var coordinate = IsOxygenBoundEntry(item) ? item.Coordinate : 0;
            return RootEventResidual(time, oxygen[coordinate], item);
        }

        [MethodImpl(AggressiveOptimization)]
        bool TryEventPrefixEndpoint(double time,
            (double Time, string Kind, int Coordinate, double Level, int Direction) item,
            out DownstreamSolution? prefix, out double residual, out string failure)
        {
            prefix = null;
            residual = double.NaN;
            failure = "";
            if (time == ctx.cursor)
            {
                var initialOxygen = IsOxygenBoundEntry(item)
                    ? OxygenCoordinateValue(in ctx.state, item.Coordinate)
                    : ctx.state.BloodO2Head;
                residual = RootEventResidual(ctx.cursor, initialOxygen, item);
                if (IsFiniteNumber(residual)) return true;
                failure = $"nonfinite initial root residual {residual:R}";
                return false;
            }

            var prefixSpan = time - ctx.cursor;
            if (!(prefixSpan > 0.0) || !IsFiniteNumber(prefixSpan))
            {
                failure = $"invalid O2 prefix span {prefixSpan:R} at {time:R}";
                return false;
            }
            prefix = SolveDownstream(ctx.state, ctx.upstream, ctx.cursor, prefixSpan, hold,
                ctx.settings, oxygenOnly: true);
            if (!prefix.Valid)
            {
                failure = $"invalid re-solved O2 prefix at {time:R}";
                return false;
            }
            var endpoint = prefix.Oxygen.Evaluate(1.0);
            var endpointValue = IsOxygenBoundEntry(item)
                ? endpoint[item.Coordinate]
                : endpoint[0];
            residual = RootEventResidual(time, endpointValue, item);
            if (IsFiniteNumber(endpointValue) && IsFiniteNumber(residual)) return true;
            failure = $"nonfinite re-solved root endpoint at {time:R}: " +
                $"residual {residual:R}";
            return false;
        }

        [MethodImpl(AggressiveOptimization)]
        bool TryRefineRootEvent(
            (double Time, string Kind, int Coordinate, double Level, int Direction) item,
            out double refinedTime, out bool emitEvent, out string failure)
        {
            refinedTime = double.NaN;
            emitEvent = false;
            failure = "";
            var sampleLo = ctx.cursor;
            var sampleValue = WholeEventResidual(sampleLo, item);
            var bracketLo = double.NaN;
            var bracketHi = double.NaN;
            for (var sample = 0; sample <= ScanProbes.Length; sample++)
            {
                var sampleHi = sample == ScanProbes.Length
                    ? end
                    : ctx.cursor + ScanProbes[sample] * span;
                var valueHi = WholeEventResidual(sampleHi, item);
                if (item.Time >= sampleLo - 1e-12 &&
                    item.Time <= sampleHi + 1e-12 &&
                    DirectedBracket(sampleValue, valueHi, item.Direction))
                {
                    bracketLo = sampleLo;
                    bracketHi = sampleHi;
                    break;
                }
                sampleLo = sampleHi;
                sampleValue = valueHi;
            }

            if (double.IsNaN(bracketLo))
            {
                failure = $"root event {item.Kind} coordinate {item.Coordinate} " +
                    $"level {item.Level:R} direction {item.Direction} has no " +
                    $"original bracket around {item.Time:R}";
                return false;
            }

            // item.Time already localizes the piece-curve root inside
            // the scan bracket; the re-solved prefix root has been
            // observed within well under 1e-6 s of it. Probe a narrow
            // bracket around item.Time first and refine there when the
            // re-solved seed residuals straddle; otherwise fall back
            // to the full scan bracket. The refinement and
            // acceptability logic below is unchanged either way.
            var seedLo = Math.Max(item.Time - BoundEventSeedPadSeconds,
                bracketLo);
            var seedHi = Math.Min(item.Time + BoundEventSeedPadSeconds,
                bracketHi);
            double lo;
            double hi;
            double fLo;
            double fHi;
            if (seedHi - seedLo > PressureBoundRootToleranceSeconds &&
                TryEventPrefixEndpoint(seedLo, item, out _,
                    out var seedLoResidual, out _) &&
                TryEventPrefixEndpoint(seedHi, item, out _,
                    out var seedHiResidual, out _) &&
                DirectedBracket(seedLoResidual, seedHiResidual,
                    item.Direction))
            {
                lo = seedLo;
                hi = seedHi;
                fLo = seedLoResidual;
                fHi = seedHiResidual;
            }
            else
            {
                var lowerValid = TryEventPrefixEndpoint(bracketLo, item,
                    out _, out var resolvedLo, out var lowerFailure);
                var upperValid = TryEventPrefixEndpoint(bracketHi, item,
                    out _, out var resolvedHi, out var upperFailure);
                if (!lowerValid || !upperValid)
                {
                    failure = $"root event {item.Kind} coordinate {item.Coordinate} " +
                        $"level {item.Level:R} direction {item.Direction} invalid " +
                        $"re-solved bracket [{bracketLo:R},{bracketHi:R}]: " +
                        $"{lowerFailure}{upperFailure}";
                    return false;
                }
                if (!DirectedBracket(resolvedLo, resolvedHi, item.Direction))
                {
                    failure = $"root event {item.Kind} coordinate {item.Coordinate} " +
                        $"level {item.Level:R} direction {item.Direction} re-solved " +
                        $"prefix residuals [{resolvedLo:R},{resolvedHi:R}] do not " +
                        $"bracket original interval [{bracketLo:R},{bracketHi:R}]";
                    return false;
                }

                lo = bracketLo;
                hi = bracketHi;
                fLo = resolvedLo;
                fHi = resolvedHi;
            }

            while (hi - lo > PressureBoundRootToleranceSeconds)
            {
                var mid = 0.5 * (lo + hi);
                if (mid == lo || mid == hi) break;
                if (!TryEventPrefixEndpoint(mid, item,
                        out _, out var fMid, out var midFailure))
                {
                    failure = $"root event {item.Kind} coordinate {item.Coordinate} " +
                        $"level {item.Level:R} direction {item.Direction} invalid " +
                        $"re-solved prefix at {mid:R}: {midFailure}";
                    return false;
                }
                if (fMid == 0.0)
                {
                    lo = mid;
                    hi = mid;
                    fLo = 0.0;
                    fHi = 0.0;
                    break;
                }
                if ((item.Direction < 0 && fMid > 0.0) ||
                    (item.Direction > 0 && fMid < 0.0))
                {
                    lo = mid;
                    fLo = fMid;
                }
                else
                {
                    hi = mid;
                    fHi = fMid;
                }
            }

            refinedTime = hi;
            if (!IsFiniteNumber(refinedTime) ||
                refinedTime < bracketLo || refinedTime > bracketHi)
            {
                failure = $"root event {item.Kind} coordinate {item.Coordinate} " +
                    $"level {item.Level:R} direction {item.Direction} failed at " +
                    $"refined time {refinedTime:R} outside original bracket " +
                    $"[{bracketLo:R},{bracketHi:R}]";
                return false;
            }
            if (!TryEventPrefixEndpoint(refinedTime, item,
                    out var finalPrefix, out var endpointResidual,
                    out var finalFailure))
            {
                failure = $"root event {item.Kind} coordinate {item.Coordinate} " +
                    $"level {item.Level:R} direction {item.Direction} invalid at " +
                    $"refined time {refinedTime:R}: {finalFailure}";
                return false;
            }

            if (finalPrefix == null)
            {
                failure = $"root event {item.Kind} coordinate {item.Coordinate} " +
                    $"has no positive re-solved prefix at {refinedTime:R}";
                return false;
            }
            if (IsConsciousnessTargetZeroBranch(item))
            {
                if (item.Direction != 1 || endpointResidual < 0.0)
                {
                    failure = $"ConsciousnessTargetZeroBranch raw reserve " +
                        $"{endpointResidual:R} is not on its positive side at " +
                        $"{refinedTime:R} within [{bracketLo:R},{bracketHi:R}]";
                    return false;
                }
                var trial = ctx.upstream.EvaluateHeadPressure(refinedTime);
                var headO2 = finalPrefix.Oxygen.EvaluateFirst(1.0);
                var target = ConsciousnessTargetValue(
                    O2Normalized(headO2, ctx.settings),
                    PerfusionNormalized(trial.Head, ctx.settings), trial.Pressure,
                    ctx.settings);
                if (!IsFiniteNumber(target) || target < 0.0 || target > 1.0)
                {
                    failure = $"ConsciousnessTargetZeroBranch final target " +
                        $"{target:R} invalid at {refinedTime:R}";
                    return false;
                }
                emitEvent = target > 0.0;
                return true;
            }

            var derivative = finalPrefix.Oxygen.Derivative(1.0)[
                item.Coordinate];
            var allowedCorrection =
                Math.Abs(derivative) * PressureBoundRootToleranceSeconds + 1e-12;
            ctx.lastO2RootEvidence =
                $"{item.Kind} coordinate={item.Coordinate} level={item.Level:R} " +
                $"direction={item.Direction} rootTime={refinedTime:R} " +
                $"originalBracket=[{bracketLo:R},{bracketHi:R}] " +
                $"endpointResidual={endpointResidual:R} derivative={derivative:R} " +
                $"allowed={allowedCorrection:R}";
            if (!IsFiniteNumber(derivative) || !IsFiniteNumber(allowedCorrection) ||
                item.Direction * derivative <= 0.0 ||
                Math.Abs(endpointResidual) > allowedCorrection)
            {
                failure = $"O2 bound {item.Kind} coordinate {item.Coordinate} " +
                    $"level {item.Level:R} direction {item.Direction} residual " +
                    $"{endpointResidual:R}, derivative {derivative:R}, allowed " +
                    $"{allowedCorrection:R} at {refinedTime:R}; bracket " +
                    $"[{bracketLo:R},{bracketHi:R}] endpoint residuals " +
                    $"[{fLo:R},{fHi:R}]";
                return false;
            }
            emitEvent = true;
            return true;
        }

        var candidatesValid = true;
        for (var candidateIndex = 0; candidateIndex < ctx.candidates.Count;)
        {
            var candidate = ctx.candidates[candidateIndex];
            if (!IsRootEvent(candidate))
            {
                candidateIndex++;
                continue;
            }
            var refined = TryRefineRootEvent(candidate, out var refinedTime,
                out var emitEvent, out var refinementFailure);
            if (!refined)
            {
                ctx.violations.Add(refinementFailure);
                candidatesValid = false;
                break;
            }
            if (!emitEvent)
            {
                ctx.candidates.RemoveAt(candidateIndex);
                continue;
            }
            ctx.candidates[candidateIndex] = (refinedTime, candidate.Kind,
                candidate.Coordinate, candidate.Level, candidate.Direction);
            candidateIndex++;
        }
        if (!candidatesValid) return false;

        var currentTrial = ctx.upstream.EvaluateHeadPressure(ctx.cursor);
        var currentTarget = ConsciousnessTargetValue(
            O2Normalized(ctx.state.BloodO2Head, ctx.settings),
            PerfusionNormalized(currentTrial.Head, ctx.settings),
            currentTrial.Pressure, ctx.settings);
        if (currentTarget == 0.0 && ctx.state.ConsciousnessLevel > 0.0)
        {
            var targetZeroBranches = ctx.candidates.Where(
                IsConsciousnessTargetZeroBranch).ToArray();
            if (targetZeroBranches.Length == 0)
            {
                ctx.candidates.RemoveAll(candidate =>
                    candidate.Kind == "ConsciousnessModeTransition");
            }
            else
            {
                var firstTargetZeroBranch = targetZeroBranches.Min(
                    candidate => candidate.Time);
                ctx.candidates.RemoveAll(candidate =>
                    candidate.Kind == "ConsciousnessModeTransition" &&
                    candidate.Time <= firstTargetZeroBranch);
            }
        }

        if (ctx.candidates.Count == 0)
        {
            if (!ctx.state.IsDead && hold.ConsciousnessLosing && currentTarget == 0.0)
            {
                if (TryCreateHomogeneousZeroTargetLoss(ctx.state, ctx.upstream, ctx.cursor, span,
                        piece, false, ctx.settings, out var homogeneousConsciousness,
                        out _) && homogeneousConsciousness != null)
                {
                    piece.Consciousness = homogeneousConsciousness;
                }
            }
            loc = ChaseLoC(ctx.state, piece.Consciousness, ctx.cursor, span, ctx.settings);
            var recorded = ctx.RecordPiece(piece, loc, end, null, out var acceptedNoEventEnd);
            if (!recorded) return false;
            ctx.state = acceptedNoEventEnd;
            ctx.cursor = end;
            return true;
        }

        var bestTime = ctx.candidates[0].Time;
        foreach (var candidate in ctx.candidates)
            if (candidate.Time < bestTime) bestTime = candidate.Time;
        ctx.due.Clear();
        foreach (var candidate in ctx.candidates)
        {
            var dueTolerance = IsOxygenBoundEntry(candidate)
                ? PressureBoundRootToleranceSeconds
                : FullIntervalEventScanTolerance;
            if (IsConsciousnessTargetZeroBranch(candidate) &&
                candidate.Time > bestTime)
                continue;
            if (candidate.Time - bestTime > dueTolerance) continue;
            if (ctx.due.Any(item => item.Kind == candidate.Kind &&
                                (!IsOxygenBoundEntry(candidate) ||
                                 (item.Coordinate == candidate.Coordinate &&
                                  item.Level == candidate.Level &&
                                  item.Direction == candidate.Direction))))
                continue;
            ctx.due.Add(candidate);
        }
        ctx.due.Sort((a, b) =>
        {
            var rankComparison = EventRank(a.Kind).CompareTo(EventRank(b.Kind));
            return rankComparison != 0
                ? rankComparison
                : string.CompareOrdinal(a.Kind, b.Kind);
        });

        var prefixSpan = bestTime - ctx.cursor;
        var prefixPiece = SolveDownstream(ctx.state, ctx.upstream, ctx.cursor, prefixSpan,
            hold, ctx.settings, allowHomogeneousZeroTargetLoss: true,
            zeroTargetBranchAtEnd: ctx.due.Any(IsConsciousnessTargetZeroBranch));
        if (!prefixPiece.Valid)
        {
            ctx.violations.Add($"downstream prefix invalid at offset {ctx.cursor:R}: " +
                prefixPiece.Failure);
            return false;
        }
        var oxygenBoundEntries = ctx.due.Where(IsOxygenBoundEntry).ToArray();
        var oxygenEntryValid = true;
        foreach (var oxygenEntry in oxygenBoundEntries)
        {
            var endpoint = prefixPiece.Oxygen.Evaluate(1.0);
            var residual = endpoint[oxygenEntry.Coordinate] - oxygenEntry.Level;
            var derivative = prefixPiece.Oxygen.Derivative(1.0)[
                oxygenEntry.Coordinate];
            var allowedCorrection =
                Math.Abs(derivative) * PressureBoundRootToleranceSeconds + 1e-12;
            ctx.lastO2RootEvidence =
                $"accepted {oxygenEntry.Kind} coordinate={oxygenEntry.Coordinate} " +
                $"level={oxygenEntry.Level:R} direction={oxygenEntry.Direction} " +
                $"time={bestTime:R} prefixResidual={residual:R} " +
                $"derivative={derivative:R} allowed={allowedCorrection:R}";
            if (IsFiniteNumber(endpoint[oxygenEntry.Coordinate]) &&
                IsFiniteNumber(residual) && IsFiniteNumber(derivative) &&
                IsFiniteNumber(allowedCorrection) &&
                oxygenEntry.Direction * derivative > 0.0 &&
                Math.Abs(residual) <= allowedCorrection)
                continue;
            ctx.violations.Add($"O2 bound {oxygenEntry.Kind} coordinate " +
                $"{oxygenEntry.Coordinate} level {oxygenEntry.Level:R} direction " +
                $"{oxygenEntry.Direction} prefix residual {residual:R}, derivative " +
                $"{derivative:R}, allowed {allowedCorrection:R} at {bestTime:R}");
            oxygenEntryValid = false;
            break;
        }
        if (!oxygenEntryValid) return false;
        var prefixLoc = ChaseLoC(ctx.state, prefixPiece.Consciousness, ctx.cursor,
            prefixSpan, ctx.settings);
        var prefixRecorded = ctx.RecordPiece(prefixPiece, prefixLoc, bestTime,
            oxygenBoundEntries, out var acceptedEventEnd,
            locContactAtEnd: ctx.due.Any(item => item.Kind == "LoCContactEntry"));
        if (!prefixRecorded)
            return false;
        ctx.state = acceptedEventEnd;
        ctx.cursor = bestTime;
        foreach (var candidate in ctx.due)
        {
            var kind = candidate.Kind;
            if (ctx.state.IsDead &&
                (kind is "NeckDeathExit" or "SuddenTrigger" or "SuddenRecovery" or
                    "ConsciousnessLost" or "ConsciousnessRecovered"))
                continue;
            if (IsOxygenBoundEntry(candidate))
                ctx.events.Add(new IntegrationEvent(kind, bestTime, ctx.state));
            else
                ctx.ApplyAndRecordEvent(kind, bestTime);
        }
        return true;
    }
}
