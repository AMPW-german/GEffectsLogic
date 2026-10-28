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
using System.Reflection;
using System.Text;
using GEffectsLogic;
using Xunit.Abstractions;

namespace GEffectLogicTests;

[Trait("Category", "DtStability")]
public class PhysiologicalIntegrationTests
{
    private const double StateSpreadTolerance = 0.001;
    private const double ReferenceSpreadTolerance = 1e-6;
    private const double BloodSumTolerance = 1e-12;
    private const double JacobianPerturbation = 1e-7;
    private const double JacobianTolerance = 1e-6;
    private const double ReferenceDt = 0.02;

    private static readonly double[] CandidateDt = [0.01, 0.1, 0.25, 0.5, 1.0];
    private static readonly double[] UnitScalarInitialState = [1.0];
    private static readonly double[] AffineDenseQueryPoints =
    [
        0.0, NumericalMath.RadauC[0], NumericalMath.RadauC[1],
        NumericalMath.RadauC[2], 0.3, 1.0
    ];
    private static readonly string[] SimultaneousVisualTransitionOrder =
        ["VisualGrayscaleTransition", "VisualTunnelTransition"];

    private static readonly string[] ChannelNames =
        ["BloodHead", "BloodCore", "BloodLower", "HeadOverfill", "HeartRateMultiplier",
         "CardioFatigue", "CerebralPressureImpairment"];

    private readonly ITestOutputHelper _output;

    public PhysiologicalIntegrationTests(ITestOutputHelper output)
    {
        _output = output;
    }

    // Drive a single model-owned interval through the public Update path:
    // load an arbitrary precondition, update, and inspect the stored result.
    private static PhysiologicalModel.IntegrationResult AdvanceInterval(
        in PhysiologicalModel.IntegrationState state, double dt, double gx,
        double gy, double gz, LogicSettings settings)
    {
        var model = new GEffectsLogicInstance(settings: settings).PhysModel;
        model.LoadIntervalState(in state);
        try
        {
            model.Update(dt, gx, gy, gz);
        }
        catch (InvalidOperationException)
        {
            // Rejected intervals remain inspectable via LastIntervalResult.
        }
        return model.LastIntervalResult!;
    }

    public static IEnumerable<object[]> GateVectors => new object[][] { [-2.0], [-3.0], [-5.0] };

    public static IEnumerable<object[]> JacobianStates => new object[][]
    {
        [0.0, 0.0, -3.0, 0.24, 0.44, 0.7, 0.05, 0.3, 0.0],
        [0.0, 0.0, 5.0, 0.10, 0.55, 1.9, 0.2, 0.1, 0.0],
        [0.0, 0.0, 8.0, 0.05, 0.6, 1.2, 0.0, 0.05, 0.4]
    };

    [Theory]
    [MemberData(nameof(GateVectors))]
    public void CirculationEndpointsAgreeAcrossSupportedDt(double gz) =>
        AssertCirculationEndpointsAgreeAcrossSupportedDt(gz);

    // Issue #22 strict twins for the formerly deferred fast-transient loads;
    // they keep the same assertions executable in the Experimental step.
    [Theory]
    [Trait("Category", "Experimental")]
    [InlineData(-3.0)]
    [InlineData(-5.0)]
    public void DeferredCirculationEndpointsAccuracy(double gz) =>
        AssertCirculationEndpointsAgreeAcrossSupportedDt(gz);

    private void AssertCirculationEndpointsAgreeAcrossSupportedDt(double gz)
    {
        var failures = new List<string>();
        var rows = new StringBuilder();
        var endpoints = new double[CandidateDt.Length][];
        var pathAccepted = new bool[CandidateDt.Length];

        for (var candidateIndex = 0; candidateIndex < CandidateDt.Length; candidateIndex++)
            endpoints[candidateIndex] = RunPath(gz, CandidateDt[candidateIndex], rows, failures,
                out pathAccepted[candidateIndex]);

        var referenceEndpoint = RunPath(gz, ReferenceDt, rows, failures, out _);
        for (var channel = 0; channel < ChannelNames.Length; channel++)
        {
            var difference = Math.Abs(endpoints[0][channel] - referenceEndpoint[channel]);
            if (difference > ReferenceSpreadTolerance)
                failures.Add($"gz {gz:R} {ChannelNames[channel]}: dt 0.01 vs 0.02 reference difference " +
                             $"{difference:R} exceeds {ReferenceSpreadTolerance:R}; 0.01 is not a converged reference.");
        }

        for (var channel = 0; channel < ChannelNames.Length; channel++)
        {
            var values = endpoints.Select(endpoint => endpoint[channel]).ToArray();
            var minimum = values.Min();
            var maximum = values.Max();
            if (maximum - minimum > StateSpreadTolerance)
            {
                var minIndex = Array.IndexOf(values, minimum);
                var maxIndex = Array.IndexOf(values, maximum);
                var failure = $"gz {gz:R} {ChannelNames[channel]} at 1s: min {minimum:R} (dt {CandidateDt[minIndex]:R}), " +
                              $"max {maximum:R} (dt {CandidateDt[maxIndex]:R}), spread {maximum - minimum:R} exceeds {StateSpreadTolerance:R}; " +
                              $"minPathAccepted={pathAccepted[minIndex]}, maxPathAccepted={pathAccepted[maxIndex]}.";
                failures.Add(failure);
            }
        }

        for (var candidateIndex = 0; candidateIndex < CandidateDt.Length; candidateIndex++)
            CheckEndpointInvariants(endpoints[candidateIndex], gz, CandidateDt[candidateIndex], failures);

        foreach (var row in rows.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries))
            _output.WriteLine(row);

        Assert.True(failures.Count == 0,
            $"{failures.Count} circulation-gate breach(es) at gz {gz:R}:{Environment.NewLine}" +
            string.Join(Environment.NewLine, failures));
    }

    [Theory]
    [MemberData(nameof(JacobianStates))]
    public void CirculationJacobianMatchesFiniteDifferences(
        double gx, double gy, double gz, double head, double lower, double rate,
        double cardio, double pressure, double respiratory)
    {
        var state = new PhysiologicalModel.IntegrationState(head, lower, rate, cardio, pressure, respiratory);
        var rates = new double[PhysiologicalModel.CirculationDimensions];
        var jacobian = NumericalMath.CreateMatrix(PhysiologicalModel.CirculationDimensions);
        PhysiologicalModel.EvaluateCirculation(in state, gx, gy, gz, LogicSettings.Default, rates, jacobian);

        var failures = new List<string>();
        for (var variable = 0; variable < PhysiologicalModel.CirculationDimensions; variable++)
        {
            var plusRates = PerturbedRates(state, gx, gy, gz, variable, JacobianPerturbation);
            var minusRates = PerturbedRates(state, gx, gy, gz, variable, -JacobianPerturbation);
            for (var row = 0; row < PhysiologicalModel.CirculationDimensions; row++)
            {
                var finiteDifference = (plusRates[row] - minusRates[row]) / (2.0 * JacobianPerturbation);
                var difference = Math.Abs(finiteDifference - jacobian[row][variable]);
                if (difference > JacobianTolerance)
                    failures.Add($"state({head:R},{lower:R},{rate:R},{cardio:R},{pressure:R}) gz {gz:R}: " +
                                 $"J[{row},{variable}] analytic {jacobian[row][variable]:R} vs finite " +
                                 $"difference {finiteDifference:R}, diff {difference:R}.");
            }
        }

        Assert.True(failures.Count == 0,
            $"Jacobian mismatches:{Environment.NewLine}{string.Join(Environment.NewLine, failures)}");
    }

    private static double[] RunPath(double gz, double dt, StringBuilder rows, List<string> failures,
        out bool pathAccepted)
    {
        var model = new GEffectsLogicInstance(new DtStabilityLogger()).PhysModel;
        return AdvancePath(model, model.CaptureIntervalState(), LogicSettings.Default, gz, dt,
            rows, failures, out pathAccepted, out _);
    }

    private static double[] AdvancePath(PhysiologicalModel model,
        PhysiologicalModel.IntegrationState state, LogicSettings settings, double gz, double dt,
        StringBuilder rows, List<string> failures, out bool pathAccepted,
        out PhysiologicalModel.IntegrationState finalState,
        double gx = 0.0, double gy = 0.0)
    {
        var calls = (int)Math.Round(1.0 / dt);
        pathAccepted = true;
        for (var call = 1; call <= calls; call++)
        {
            var result = model.AdvanceCirculationInterval(in state, dt, gx, gy, gz, settings);
            var stageMinVolume = result.Stages.Length == 0
                ? double.NaN
                : result.Stages.Min(stage =>
                    Math.Min(stage.BloodHead, Math.Min(stage.BloodCore, stage.BloodLower)));
            rows.AppendLine(CultureInfo.InvariantCulture,
                $"gz={gz:R} dt={dt:R} call={call}/{calls} t={(call * dt).ToString("R", CultureInfo.InvariantCulture)} " +
                $"converged={result.Converged} iterations={result.Iterations} " +
                $"maxResidual={result.MaxScaledResidual.ToString("R", CultureInfo.InvariantCulture)} " +
                $"stageMinVolume={stageMinVolume.ToString("R", CultureInfo.InvariantCulture)} " +
                $"modeCrossings={result.ModeCrossings.Length} stateViolations={result.StateViolations.Length} " +
                $"entries=[{string.Join(",", result.PressureBoundEntries.Select(e => e.ToString("R", CultureInfo.InvariantCulture)))}] " +
                $"releases=[{string.Join(",", result.PressureBoundReleases.Select(e => e.ToString("R", CultureInfo.InvariantCulture)))}] " +
                $"coreEntries=[{string.Join(",", result.CoreBoundEntries.Select(e => e.ToString("R", CultureInfo.InvariantCulture)))}] " +
                $"coreReleases=[{string.Join(",", result.CoreBoundReleases.Select(e => e.ToString("R", CultureInfo.InvariantCulture)))}] " +
                $"segments={result.Segments.Length} " +
                $"head={result.Final.BloodHead:R} core={result.Final.BloodCore:R} lower={result.Final.BloodLower:R} " +
                $"hr={result.Final.HeartRateMultiplier:R} cardio={result.Final.CardioFatigue:R} " +
                $"pressure={result.Final.CerebralPressureImpairment:R} " +
                $"stagePressures=[{string.Join(",", result.Stages.Select(stage => stage.CerebralPressureImpairment.ToString("R", CultureInfo.InvariantCulture)))}]");

            if (!result.Converged)
                failures.Add($"gz {gz:R} dt {dt:R} call {call}/{calls} at {call * dt:R}s: Newton did not " +
                             $"converge; scaled residual {result.MaxScaledResidual:R} after {result.Iterations} iterations.");
            foreach (var crossing in result.ModeCrossings)
                failures.Add($"gz {gz:R} dt {dt:R} call {call}/{calls}: unsupported mode crossing: {crossing}.");
            foreach (var violation in result.StateViolations)
                failures.Add($"gz {gz:R} dt {dt:R} call {call}/{calls}: invalid stage: {violation}.");
            pathAccepted &= result.Converged && result.ModeCrossings.Length == 0 &&
                            result.StateViolations.Length == 0;
            state = result.Final;
        }

        finalState = state;
        var endpoint = EndpointChannels(state);
        rows.AppendLine(CultureInfo.InvariantCulture,
            $"endpoint gz={gz:R} dt={dt:R} pathAccepted={pathAccepted}: " +
                        $"head={endpoint[0]:R} core={endpoint[1]:R} " +
                        $"lower={endpoint[2]:R} overfill={endpoint[3]:R} hr={endpoint[4]:R} " +
                        $"cardio={endpoint[5]:R} pressure={endpoint[6]:R}");
        return endpoint;
    }

    private static double[] EndpointChannels(PhysiologicalModel.IntegrationState state) =>
    [
        state.BloodHead,
        state.BloodCore,
        state.BloodLower,
        (state.BloodHead - LogicSettings.Default.RestingBloodHead) / LogicSettings.Default.RestingBloodHead,
        state.HeartRateMultiplier,
        state.CardioFatigue,
        state.CerebralPressureImpairment
    ];

    private static void CheckEndpointInvariants(double[] endpoint, double gz, double dt, List<string> failures)
    {
        for (var channel = 0; channel < ChannelNames.Length; channel++)
            if (!double.IsFinite(endpoint[channel]))
                failures.Add($"gz {gz:R} dt {dt:R} endpoint {ChannelNames[channel]}: non-finite {endpoint[channel]:R}.");

        var head = endpoint[0];
        var core = endpoint[1];
        var lower = endpoint[2];
        var cardio = endpoint[5];
        var pressure = endpoint[6];
        if (head < 0.0 || core < 0.0 || lower < 0.0)
            failures.Add($"gz {gz:R} dt {dt:R} endpoint: negative volume head {head:R}, core {core:R}, lower {lower:R}.");
        if (head < LogicSettings.Default.MinHeadBloodFraction)
            failures.Add($"gz {gz:R} dt {dt:R} endpoint: BloodHead {head:R} below head floor.");
        var bloodSumError = Math.Abs(head + core + lower - 1.0);
        if (bloodSumError > BloodSumTolerance)
            failures.Add($"gz {gz:R} dt {dt:R} endpoint: blood sum error {bloodSumError:R}.");
        if (cardio is < 0.0 or > 1.0)
            failures.Add($"gz {gz:R} dt {dt:R} endpoint: CardioFatigue {cardio:R} outside [0,1].");
        if (pressure is < 0.0 or > 1.0)
            failures.Add($"gz {gz:R} dt {dt:R} endpoint: CerebralPressureImpairment {pressure:R} outside [0,1].");
    }

    private static double[] PerturbedRates(
        PhysiologicalModel.IntegrationState state, double gx, double gy, double gz,
        int variable, double perturbation)
    {
        var values = new[]
        {
            state.BloodHead, state.BloodLower, state.HeartRateMultiplier,
            state.CardioFatigue, state.CerebralPressureImpairment
        };
        values[variable] += perturbation;
        var perturbed = state with
        {
            BloodHead = values[0],
            BloodLower = values[1],
            HeartRateMultiplier = values[2],
            CardioFatigue = values[3],
            CerebralPressureImpairment = values[4]
        };
        var rates = new double[PhysiologicalModel.CirculationDimensions];
        PhysiologicalModel.EvaluateCirculation(in perturbed, gx, gy, gz, LogicSettings.Default, rates, null);
        return rates;
    }

    [Theory]
    [MemberData(nameof(JacobianStates))]
    public void MobilityLawRatesMatchIndependentForm(
        double gx, double gy, double gz, double head, double lower, double rate,
        double cardio, double pressure, double respiratory)
    {
        var settings = LogicSettings.Default;
        var gxTolerance = 1.0 + settings.GxToleranceImprovementFactor * Math.Abs(gx);
        var gyTolerance = Math.Clamp(1.0 - settings.GyToleranceReductionBase *
            Math.Pow(Math.Abs(gy), settings.GyToleranceNonlinearity), 0.1, 1.0);
        var combinedTolerance = gxTolerance * gyTolerance;
        var gzNetScaled = Math.Sign(gz) *
            Math.Pow(Math.Abs(gz) / combinedTolerance, settings.HydrostaticShiftExponent) - 1.0;
        var shift = settings.HydrostaticShiftRate * gzNetScaled;
        var fraction = Math.Clamp(settings.CoreLowerShiftFraction, 0.05, 0.95);
        var restHead = settings.RestingBloodHead;
        var restCore = settings.RestingBloodCore;
        var restLower = settings.RestingBloodLower;
        var core = 1.0 - head - lower;
        var overfill = Math.Max((head - restHead) / restHead, 0.0);
        var returnRate = settings.PassiveReturnRate * rate;
        var pressureReturn = settings.HeadPressureReturnRate *
                             (Math.Exp(settings.HeadPressureReturnExponent * overfill) - 1.0);
        var qHead = -shift + returnRate * (restHead - head) - pressureReturn;
        var qCore = shift + returnRate * (restCore - core);
        var qLower = shift * fraction + returnRate * (restLower - lower);
        var qTotal = qHead + qCore + qLower;
        var fHead = qHead - head * qTotal;
        var fLower = qLower - lower * qTotal;
        var fCore = qCore - core * qTotal;
        var expectedHead = restHead * fHead;
        var expectedLower = fLower + (1.0 - restHead) * fHead * lower / (1.0 - head);
        var expectedCore = fCore + (1.0 - restHead) * fHead * core / (1.0 - head);

        var state = new PhysiologicalModel.IntegrationState(head, lower, rate, cardio, pressure, respiratory);
        var rates = new double[PhysiologicalModel.CirculationDimensions];
        PhysiologicalModel.EvaluateCirculation(in state, gx, gy, gz, settings, rates, null);

        Assert.True(Math.Abs(rates[0] - expectedHead) <= 1e-12,
            $"rate[0] {rates[0]:R} vs expected {expectedHead:R}.");
        Assert.True(Math.Abs(rates[1] - expectedLower) <= 1e-12,
            $"rate[1] {rates[1]:R} vs expected {expectedLower:R}.");
        Assert.True(Math.Abs(-(rates[0] + rates[1]) - expectedCore) <= 1e-12,
            $"derived core {-(rates[0] + rates[1]):R} vs expected {expectedCore:R}.");
        Assert.True(Math.Abs(expectedHead + expectedLower + expectedCore) <= 1e-12,
            $"expected rate sum {expectedHead + expectedLower + expectedCore:R} not conserved.");
        Assert.Equal(Math.Sign(fHead), Math.Sign(rates[0]));
    }

    [Fact]
    public void MobilityLawNeutralStateHasZeroRates()
    {
        var settings = LogicSettings.Default;
        var state = new PhysiologicalModel.IntegrationState(settings.RestingBloodHead,
            settings.RestingBloodLower, 1.0, 0.0, 0.0, 0.0);
        var rates = new double[PhysiologicalModel.CirculationDimensions];
        PhysiologicalModel.EvaluateCirculation(in state, 0.0, 0.0, 1.0, settings, rates, null);
        foreach (var component in rates) Assert.True(Math.Abs(component) <= 1e-15,
            $"neutral rate {component:R} is not zero.");
    }

    public static IEnumerable<object[]> ConstantSourcePressureCases
    {
        get
        {
            foreach (var head in new[] { 0.2, 0.214, 0.23 })
                foreach (var pressure in new[] { 0.0, 0.8, 1.0 })
                    foreach (var gz in new[] { -5.0, 1.0 })
                        foreach (var tau in new[] { 25.0, 0.01, double.PositiveInfinity })
                            foreach (var dt in new[] { 0.01, 0.25, 1.0 })
                                yield return [head, pressure, gz, tau, dt];
        }
    }

    [Theory]
    [MemberData(nameof(ConstantSourcePressureCases))]
    public void PressureConvolutionMatchesConstantSourceAnalytic(
        double head, double initialPressure, double gz, double recoveryTau, double dt)
    {
        var settings = ConstantHeadSettings(recoveryTau);
        var state = new PhysiologicalModel.IntegrationState(head, 0.45, 1.0, 0.0, initialPressure, 0.0);
        var model = new GEffectsLogicInstance(new DtStabilityLogger()).PhysModel;
        var result = model.AdvanceCirculationInterval(in state, dt, 0.0, 0.0, gz, settings);

        var source = IndependentPressureSource(head, gz, settings);
        var decay = 1.0 / recoveryTau;
        var unclamped = decay == 0.0
            ? initialPressure + source * dt
            : source / decay + (initialPressure - source / decay) * Math.Exp(-decay * dt);
        var expected = Math.Min(1.0, unclamped);
        var actual = result.Final.CerebralPressureImpairment;
        _output.WriteLine(
            $"head={head:R} p0={initialPressure:R} gz={gz:R} tau={recoveryTau:R} dt={dt:R} " +
            $"source={source:R} expected={expected:R} actual={actual:R} converged={result.Converged} " +
            $"entries=[{string.Join(",", result.PressureBoundEntries.Select(e => e.ToString("R", CultureInfo.InvariantCulture)))}] " +
            $"releases=[{string.Join(",", result.PressureBoundReleases.Select(e => e.ToString("R", CultureInfo.InvariantCulture)))}]");
        Assert.True(result.Converged,
            $"pressure convolution case did not converge: residual {result.MaxScaledResidual:R}.");
        Assert.Empty(result.ModeCrossings);
        Assert.Empty(result.StateViolations);
        Assert.True(Math.Abs(actual - expected) <= 1e-12,
            $"pressure {actual:R} vs expected {expected:R} (unclamped {unclamped:R}).");
        foreach (var stage in result.Stages)
            Assert.InRange(stage.CerebralPressureImpairment, 0.0, 1.0);
        if (initialPressure == 0.0)
            foreach (var stage in result.Stages)
                Assert.True(stage.CerebralPressureImpairment >= 0.0,
                    $"stage pressure {stage.CerebralPressureImpairment:R} negative from zero start.");
    }

    [Fact]
    public void ConstantSourceUpperEntryLocalizesAtFirstCrossing()
    {
        var settings = ConstantHeadSettings(25.0);
        var state = new PhysiologicalModel.IntegrationState(0.23, 0.45, 1.0, 0.0, 0.95, 0.0);
        var model = new GEffectsLogicInstance(new DtStabilityLogger()).PhysModel;
        var result = model.AdvanceCirculationInterval(in state, 1.0, 0.0, 0.0, -5.0, settings);
        var source = IndependentPressureSource(0.23, -5.0, settings);
        var decay = 1.0 / 25.0;
        var expectedEntry = -Math.Log(1.0 - decay * (1.0 - 0.95) / (source - decay * 0.95)) / decay;
        _output.WriteLine(
            $"expectedEntry={expectedEntry:R} entries=[{string.Join(",", result.PressureBoundEntries.Select(e => e.ToString("R", CultureInfo.InvariantCulture)))}] " +
            $"releases=[{string.Join(",", result.PressureBoundReleases.Select(e => e.ToString("R", CultureInfo.InvariantCulture)))}] " +
            $"final={result.Final.CerebralPressureImpairment:R} converged={result.Converged}");
        Assert.True(result.Converged);
        Assert.Empty(result.ModeCrossings);
        Assert.Empty(result.StateViolations);
        Assert.Equal(1.0, result.Final.CerebralPressureImpairment);
        var entry = Assert.Single(result.PressureBoundEntries);
        Assert.True(Math.Abs(entry - expectedEntry) <= 1e-8,
            $"entry offset {entry:R} vs expected {expectedEntry:R}.");
        Assert.Empty(result.PressureBoundReleases);
        foreach (var stage in result.Stages)
            Assert.InRange(stage.CerebralPressureImpairment, 0.0, 1.0);
    }

    [Fact]
    public void BoundPressureReleasesAtZeroWhenSourceBelowDecay()
    {
        var settings = ConstantHeadSettings(25.0);
        var state = new PhysiologicalModel.IntegrationState(0.2, 0.45, 1.0, 0.0, 1.0, 0.0);
        var model = new GEffectsLogicInstance(new DtStabilityLogger()).PhysModel;
        var result = model.AdvanceCirculationInterval(in state, 1.0, 0.0, 0.0, 1.0, settings);
        _output.WriteLine(
            $"entries=[{string.Join(",", result.PressureBoundEntries.Select(e => e.ToString("R", CultureInfo.InvariantCulture)))}] " +
            $"releases=[{string.Join(",", result.PressureBoundReleases.Select(e => e.ToString("R", CultureInfo.InvariantCulture)))}] " +
            $"final={result.Final.CerebralPressureImpairment:R} expected={Math.Exp(-1.0 / 25.0):R} converged={result.Converged}");
        Assert.True(result.Converged);
        Assert.Empty(result.ModeCrossings);
        Assert.Empty(result.StateViolations);
        var release = Assert.Single(result.PressureBoundReleases);
        Assert.Equal(0.0, release);
        Assert.Empty(result.PressureBoundEntries);
        Assert.True(Math.Abs(result.Final.CerebralPressureImpairment - Math.Exp(-1.0 / 25.0)) <= 1e-12,
            $"final pressure {result.Final.CerebralPressureImpairment:R} vs exp(-1/25).");
    }

    public static IEnumerable<object[]> PressureCompositionCases => new object[][]
    {
        [0.2, 0.8, 1.0, 0.01, 0.25],
        [0.2, 0.8, 1.0, 25.0, 1.0],
        [0.2, 0.8, 1.0, double.PositiveInfinity, 0.5],
        [0.23, 0.0, -5.0, double.PositiveInfinity, 0.5],
        [0.23, 0.0, -5.0, 0.01, 0.25],
        [0.23, 0.5, -5.0, 25.0, 1.0],
        [0.2, 0.8, 1.0, 25.0, 0.01],
        [0.23, 0.0, -5.0, 25.0, 0.5]
    };

    [Theory]
    [MemberData(nameof(PressureCompositionCases))]
    public void PressureIntervalCompositionMatchesSingleStep(
        double head, double initialPressure, double gz, double recoveryTau, double dt)
    {
        var settings = ConstantHeadSettings(recoveryTau);
        var state = new PhysiologicalModel.IntegrationState(head, 0.45, 1.0, 0.0, initialPressure, 0.0);
        var model = new GEffectsLogicInstance(new DtStabilityLogger()).PhysModel;
        var single = model.AdvanceCirculationInterval(in state, dt, 0.0, 0.0, gz, settings);
        var first = model.AdvanceCirculationInterval(in state, dt / 2.0, 0.0, 0.0, gz, settings);
        var midState = first.Final;
        var chained = model.AdvanceCirculationInterval(in midState, dt / 2.0, 0.0, 0.0, gz, settings);
        _output.WriteLine(
            $"head={head:R} p0={initialPressure:R} gz={gz:R} tau={recoveryTau:R} dt={dt:R} " +
            $"single={single.Final.CerebralPressureImpairment:R} chained={chained.Final.CerebralPressureImpairment:R} " +
            $"entriesSingle=[{string.Join(",", single.PressureBoundEntries.Select(e => e.ToString("R", CultureInfo.InvariantCulture)))}] " +
            $"entriesChained=[{string.Join(",", first.PressureBoundEntries.Concat(chained.PressureBoundEntries).Select(e => e.ToString("R", CultureInfo.InvariantCulture)))}]");
        Assert.True(single.Converged && first.Converged && chained.Converged);
        Assert.Empty(single.StateViolations);
        Assert.Empty(first.StateViolations);
        Assert.Empty(chained.StateViolations);
        Assert.Empty(single.ModeCrossings);
        Assert.Empty(first.ModeCrossings);
        Assert.Empty(chained.ModeCrossings);
        Assert.True(Math.Abs(single.Final.CerebralPressureImpairment -
            chained.Final.CerebralPressureImpairment) <= 1e-12,
            $"single {single.Final.CerebralPressureImpairment:R} vs chained " +
            $"{chained.Final.CerebralPressureImpairment:R}.");
    }

    [Fact]
    public void PreconditionedNegativeLoadEndpointsAgreeAcrossDt() =>
        RunPreconditionedGate(-5.0);

    [Fact]
    public void PreconditionedRecoveryEndpointsAgreeAcrossDt() =>
        RunPreconditionedGate(1.0);

    // Issue #22 strict twin for the formerly deferred recovery accuracy check.
    [Fact]
    [Trait("Category", "Experimental")]
    public void DeferredPreconditionedRecoveryAccuracy() =>
        RunPreconditionedGate(1.0);

    private void RunPreconditionedGate(double nextGz)
    {
        var failures = new List<string>();
        var rows = new StringBuilder();
        var model = new GEffectsLogicInstance(new DtStabilityLogger()).PhysModel;
        var state = model.CaptureIntervalState();
        for (var call = 1; call <= 100; call++)
        {
            var result = model.AdvanceCirculationInterval(in state, 0.01, 0.0, 0.0, -5.0, LogicSettings.Default);
            if (!result.Converged)
                failures.Add($"precondition call {call}/100: Newton did not converge; residual " +
                             $"{result.MaxScaledResidual:R}.");
            foreach (var crossing in result.ModeCrossings)
                failures.Add($"precondition call {call}/100: unsupported mode crossing: {crossing}.");
            foreach (var violation in result.StateViolations)
                failures.Add($"precondition call {call}/100: invalid stage: {violation}.");
            state = result.Final;
        }

        var preconditioned = EndpointChannels(state);
        rows.AppendLine(CultureInfo.InvariantCulture,
            $"preconditioned endpoint after 1s at gz=-5 dt=0.01: head={preconditioned[0]:R} " +
            $"core={preconditioned[1]:R} lower={preconditioned[2]:R} overfill={preconditioned[3]:R} " +
            $"hr={preconditioned[4]:R} cardio={preconditioned[5]:R} pressure={preconditioned[6]:R}");

        var endpoints = new double[CandidateDt.Length][];
        var pathAccepted = new bool[CandidateDt.Length];
        for (var candidateIndex = 0; candidateIndex < CandidateDt.Length; candidateIndex++)
            endpoints[candidateIndex] = AdvancePath(model, state, LogicSettings.Default, nextGz,
                CandidateDt[candidateIndex], rows, failures, out pathAccepted[candidateIndex], out _);

        for (var channel = 0; channel < ChannelNames.Length; channel++)
        {
            var values = endpoints.Select(endpoint => endpoint[channel]).ToArray();
            var minimum = values.Min();
            var maximum = values.Max();
            if (maximum - minimum > StateSpreadTolerance)
            {
                var minIndex = Array.IndexOf(values, minimum);
                var maxIndex = Array.IndexOf(values, maximum);
                var failure = $"preconditioned gz {nextGz:R} {ChannelNames[channel]} at 1s: min {minimum:R} " +
                              $"(dt {CandidateDt[minIndex]:R}), max {maximum:R} (dt {CandidateDt[maxIndex]:R}), " +
                              $"spread {maximum - minimum:R} exceeds {StateSpreadTolerance:R}; " +
                              $"minPathAccepted={pathAccepted[minIndex]}, maxPathAccepted={pathAccepted[maxIndex]}.";
                failures.Add(failure);
            }
        }

        for (var candidateIndex = 0; candidateIndex < CandidateDt.Length; candidateIndex++)
            CheckEndpointInvariants(endpoints[candidateIndex], nextGz, CandidateDt[candidateIndex], failures);

        foreach (var row in rows.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries))
            _output.WriteLine(row);

        Assert.True(failures.Count == 0,
            $"{failures.Count} preconditioned-gate breach(es) at gz {nextGz:R}:{Environment.NewLine}" +
            string.Join(Environment.NewLine, failures));
    }

    [DenseDtFact]
    [Trait("Category", "DtDense")]
    public void DenseFirstSecondCirculationGate()
    {
        var failures = new List<string>();
        var rows = new StringBuilder();
        foreach (var gz in new[] { -2.0, -3.0, -5.0 })
        {
            var endpoints = new List<double[]>();
            var steps997 = new List<double>();
            var executed = 0;
            for (var ms = 10; ms <= 1000; ms++)
            {
                var dt = ms / 1000.0;
                var model = new GEffectsLogicInstance(new DtStabilityLogger()).PhysModel;
                var state = model.CaptureIntervalState();
                var t = 0.0;
                var pathAccepted = true;
                while (t < 1.0 - 1e-12)
                {
                    var step = Math.Min(dt, 1.0 - t);
                    if (ms == 997) steps997.Add(step);
                    var result = model.AdvanceCirculationInterval(in state, step, 0.0, 0.0, gz, LogicSettings.Default);
                    t += step;
                    if (!result.Converged)
                        failures.Add($"dense gz {gz:R} dt {dt:R} ending at {t:R}s: Newton did not " +
                                     $"converge; scaled residual {result.MaxScaledResidual:R} after " +
                                     $"{result.Iterations} iterations.");
                    foreach (var crossing in result.ModeCrossings)
                        failures.Add($"dense gz {gz:R} dt {dt:R} ending at {t:R}s: unsupported mode " +
                                     $"crossing: {crossing}.");
                    foreach (var violation in result.StateViolations)
                        failures.Add($"dense gz {gz:R} dt {dt:R} ending at {t:R}s: invalid stage: {violation}.");
                    pathAccepted &= result.Converged && result.ModeCrossings.Length == 0 &&
                                    result.StateViolations.Length == 0;
                    state = result.Final;
                }

                executed++;
                var endpoint = EndpointChannels(state);
                endpoints.Add(endpoint);
                CheckEndpointInvariants(endpoint, gz, dt, failures);
                rows.AppendLine(CultureInfo.InvariantCulture,
                    $"dense endpoint gz={gz:R} dt={dt:R} pathAccepted={pathAccepted}: " +
                    $"head={endpoint[0]:R} core={endpoint[1]:R} lower={endpoint[2]:R} " +
                    $"overfill={endpoint[3]:R} hr={endpoint[4]:R} cardio={endpoint[5]:R} " +
                    $"pressure={endpoint[6]:R}");
            }

            Assert.Equal(991, executed);
            if (gz == -5.0)
            {
                Assert.Equal(2, steps997.Count);
                Assert.True(Math.Abs(steps997[0] - 0.997) <= 1e-12,
                    $"997ms first step {steps997[0]:R} is not 0.997.");
                Assert.True(Math.Abs(steps997[1] - 0.003) <= 1e-12,
                    $"997ms alignment step {steps997[1]:R} is not 0.003.");
            }

            var reference = endpoints[10];
            for (var channel = 0; channel < ChannelNames.Length; channel++)
            {
                var difference = Math.Abs(endpoints[0][channel] - reference[channel]);
                if (difference > ReferenceSpreadTolerance)
                    failures.Add($"dense gz {gz:R} {ChannelNames[channel]}: dt 0.01 vs 0.02 reference " +
                                 $"difference {difference:R} exceeds {ReferenceSpreadTolerance:R}.");
            }

            for (var channel = 0; channel < ChannelNames.Length; channel++)
            {
                var minimum = endpoints.Min(endpoint => endpoint[channel]);
                var maximum = endpoints.Max(endpoint => endpoint[channel]);
                if (maximum - minimum > StateSpreadTolerance)
                    failures.Add($"dense gz {gz:R} {ChannelNames[channel]} at 1s: min {minimum:R}, " +
                                 $"max {maximum:R}, spread {maximum - minimum:R} exceeds " +
                                 $"{StateSpreadTolerance:R}.");
            }
        }

        foreach (var row in rows.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries))
            _output.WriteLine(row);

        Assert.True(failures.Count == 0,
            $"{failures.Count} dense-gate breach(es):{Environment.NewLine}" +
            string.Join(Environment.NewLine, failures));
    }

    public static IEnumerable<object[]> PlateauSourcePressureCases => new object[][] { [0.8], [1.0] };

    [Theory]
    [MemberData(nameof(PlateauSourcePressureCases))]
    public void PlateauSourcePressureEvolvesWithoutEvents(double initialPressure)
    {
        var settings = ConstantHeadSettings(25.0) with
        {
            CerebralPressureImpairmentMaxBuildRate = 0.0,
            CerebralPressureImpairmentNegativeGzRate = (1.0 / 25.0) / 1.5
        };
        var state = new PhysiologicalModel.IntegrationState(0.2, 0.45, 1.0, 0.0, initialPressure, 0.0);
        var model = new GEffectsLogicInstance(new DtStabilityLogger()).PhysModel;
        var result = model.AdvanceCirculationInterval(in state, 1.0, 0.0, 0.0, -5.0, settings);
        var expected = 1.0 + (initialPressure - 1.0) * Math.Exp(-1.0 / 25.0);
        _output.WriteLine(
            $"plateau p0={initialPressure:R} expected={expected:R} actual={result.Final.CerebralPressureImpairment:R} " +
            $"entries=[{string.Join(",", result.PressureBoundEntries.Select(e => e.ToString("R", CultureInfo.InvariantCulture)))}] " +
            $"releases=[{string.Join(",", result.PressureBoundReleases.Select(e => e.ToString("R", CultureInfo.InvariantCulture)))}]");
        Assert.True(result.Converged);
        Assert.Empty(result.ModeCrossings);
        Assert.Empty(result.StateViolations);
        Assert.Empty(result.PressureBoundEntries);
        Assert.Empty(result.PressureBoundReleases);
        Assert.True(Math.Abs(result.Final.CerebralPressureImpairment - expected) <= 1e-12,
            $"plateau final {result.Final.CerebralPressureImpairment:R} vs expected {expected:R}.");
        foreach (var stage in result.Stages)
            Assert.InRange(stage.CerebralPressureImpairment, 0.0, 1.0);
    }

    public static IEnumerable<object[]> CoincidentBoundStageCases => new object[][]
    {
        [NumericalMath.RadauC[0] - 2e-9],
        [NumericalMath.RadauC[1] - 2e-9],
        [1.0 - 2e-9]
    };

    [Theory]
    [MemberData(nameof(CoincidentBoundStageCases))]
    public void BoundEntryStageAtCrossingReportsExactlyOne(double crossing)
    {
        var settings = ConstantHeadSettings(25.0);
        var source = IndependentPressureSource(0.23, -5.0, settings);
        var decay = 1.0 / 25.0;
        var initialPressure = source / decay + (1.0 - source / decay) * Math.Exp(decay * crossing);
        var state = new PhysiologicalModel.IntegrationState(0.23, 0.45, 1.0, 0.0, initialPressure, 0.0);
        var model = new GEffectsLogicInstance(new DtStabilityLogger()).PhysModel;
        var result = model.AdvanceCirculationInterval(in state, 1.0, 0.0, 0.0, -5.0, settings);
        _output.WriteLine(
            $"coincident crossing={crossing:R} p0={initialPressure:R} " +
            $"entries=[{string.Join(",", result.PressureBoundEntries.Select(e => e.ToString("R", CultureInfo.InvariantCulture)))}] " +
            $"stages=[{string.Join(",", result.Stages.Select(stage => stage.CerebralPressureImpairment.ToString("R", CultureInfo.InvariantCulture)))}] " +
            $"final={result.Final.CerebralPressureImpairment:R}");
        Assert.True(result.Converged);
        Assert.Empty(result.ModeCrossings);
        Assert.Empty(result.StateViolations);
        var entry = Assert.Single(result.PressureBoundEntries);
        Assert.True(Math.Abs(entry - crossing) <= 1e-8,
            $"entry {entry:R} vs designed crossing {crossing:R}.");
        Assert.Empty(result.PressureBoundReleases);
        Assert.Equal(1.0, result.Final.CerebralPressureImpairment);
        foreach (var stage in result.Stages)
        {
            Assert.InRange(stage.CerebralPressureImpairment, 0.0, 1.0);
            Assert.True(stage.CerebralPressureImpairment <= 1.0,
                $"stage pressure {stage.CerebralPressureImpairment:R} above bound.");
        }
    }

    [Fact]
    public void CubicInterpolationReproducesPolynomial()
    {
        var nodes = new[] { 0.0, 0.2, 0.6, 1.0 };
        double Polynomial(double t) => 2.0 - 3.0 * t + 0.5 * t * t - 0.25 * t * t * t;
        var values = nodes.Select(Polynomial).ToArray();
        var coefficients = NumericalMath.CubicCoefficients(nodes, values);
        var expected = new[] { 2.0, -3.0, 0.5, -0.25 };
        for (var term = 0; term < 4; term++)
            Assert.True(Math.Abs(coefficients[term] - expected[term]) <= 1e-12,
                $"coefficient {term}: {coefficients[term]:R} vs {expected[term]:R}.");
        Assert.True(Math.Abs(NumericalMath.EvaluateCubic(coefficients, 0.35) - Polynomial(0.35)) <= 1e-12,
            $"interpolated value at 0.35 differs from polynomial.");
    }

    [Fact]
    public void AffineMomentsSupportZeroExtentAndRejectNonfiniteInputs()
    {
        var scalarMoments = new[] { 1.0, 1.0, 1.0 };
        Assert.True(NumericalMath.ScalarMoments(-2.0, 0.0, scalarMoments));
        Assert.All(scalarMoments, moment => Assert.Equal(0.0, moment));
        Assert.False(NumericalMath.ScalarMoments(double.NaN, 0.0, scalarMoments));

        var matrix = new[] { new[] { 1.0, 2.0 }, new[] { 3.0, 4.0 } };
        var moments = NumericalMath.MomentMatrices(matrix, 0.0);
        Assert.NotNull(moments);
        foreach (var moment in moments)
            foreach (var row in moment)
                Assert.All(row, value => Assert.Equal(0.0, value));

        var nonfiniteMatrix = new[] { new[] { 1.0, double.NaN }, new[] { 0.0, 1.0 } };
        Assert.Null(NumericalMath.MomentMatrices(nonfiniteMatrix, 0.0));
        Assert.False(NumericalMath.TryMomentMatrices(matrix, double.NaN, 0.1, out _));
        Assert.False(NumericalMath.TryMomentMatrices(matrix, 1.0,
            double.PositiveInfinity, out _));
    }

    [Fact]
    public void AffineMatrixMomentCoreMatchesDiagonalAndZeroMatrixValues()
    {
        var diagonalMatrices = new[]
        {
            new[]
            {
                new[] { -1e-12, 0.0 },
                new[] { 0.0, -1000.0 }
            },
            new[]
            {
                new[] { -1e-12, 0.0, 0.0 },
                new[] { 0.0, -25.0, 0.0 },
                new[] { 0.0, 0.0, -1000.0 }
            }
        };
        var extents = new[] { 0.0, 0.1, 1.0 };

        foreach (var matrix in diagonalMatrices)
            foreach (var extent in extents)
            {
                const double scale = 0.7;
                Assert.True(NumericalMath.TryMomentMatrices(matrix, scale, extent,
                    out var moments));
                for (var row = 0; row < matrix.Length; row++)
                    for (var column = 0; column < matrix.Length; column++)
                    {
                        if (row == column)
                        {
                            var expected = new double[3];
                            Assert.True(NumericalMath.ScalarMoments(
                                matrix[row][row] * scale, extent, expected));
                            for (var moment = 0; moment < 3; moment++)
                                Assert.True(Math.Abs(moments[moment, row, column] -
                                                     expected[moment]) <=
                                            2e-14 * Math.Max(1.0, Math.Abs(expected[moment])),
                                    $"J{moment}[{row},{column}]={moments[moment, row, column]:R}; " +
                                    $"expected {expected[moment]:R} at extent={extent:R}.");
                        }
                        else
                        {
                            for (var moment = 0; moment < 3; moment++)
                                Assert.Equal(0.0, moments[moment, row, column]);
                        }
                    }
            }

        foreach (var size in new[] { 2, 3 })
            foreach (var extent in extents)
            {
                var zero = NumericalMath.CreateMatrix(size);
                Assert.True(NumericalMath.TryMomentMatrices(zero, 0.7, extent,
                    out var moments));
                for (var moment = 0; moment < 3; moment++)
                    for (var row = 0; row < size; row++)
                        for (var column = 0; column < size; column++)
                        {
                            var expected = row == column
                                ? Math.Pow(extent, moment + 1) / (moment + 1)
                                : 0.0;
                            Assert.Equal(expected, moments[moment, row, column]);
                        }
            }

        var wrapperInput = diagonalMatrices[1];
        Assert.True(NumericalMath.TryMomentMatrices(wrapperInput, 1.0, 0.1,
            out var core));
        var wrapper = NumericalMath.MomentMatrices(wrapperInput, 0.1);
        Assert.NotNull(wrapper);
        for (var moment = 0; moment < 3; moment++)
            for (var row = 0; row < wrapperInput.Length; row++)
                for (var column = 0; column < wrapperInput.Length; column++)
                    Assert.Equal(core[moment, row, column], wrapper[moment][row][column]);

        var oversized = NumericalMath.CreateMatrix(4);
        Assert.False(NumericalMath.TryMomentMatrices(oversized, 1.0, 0.1, out _));

        var coupled = new[]
        {
            new[] { -0.2, 0.08, 0.01 },
            new[] { 0.03, -0.24, 0.02 },
            new[] { 0.02, 0.04, -0.18 }
        };
        void AssertMomentParity(NumericalMath.MatrixMoments uncached,
            NumericalMath.MatrixMoments cached, double extent)
        {
            for (var moment = 0; moment < 3; moment++)
                for (var row = 0; row < 3; row++)
                    for (var column = 0; column < 3; column++)
                    {
                        var expected = uncached[moment, row, column];
                        var actual = cached[moment, row, column];
                        var tolerance = 2e-13 * Math.Max(1.0, Math.Abs(expected));
                        Assert.True(Math.Abs(actual - expected) <= tolerance,
                            $"cached J{moment}[{row},{column}]={actual:R}; " +
                            $"uncached {expected:R} at extent={extent:R}.");
                    }
        }

        var cacheExtents = new[] { 0.0, 0.001, 0.05, 0.5, 1.0 };
        foreach (var scale in new[] { 0.7, 2.0, 8.0, 27.0 })
        {
            var powerCache = NumericalMath.CreateMomentPowerCache(coupled, scale);
            Assert.NotNull(powerCache);
            if (powerCache == null) return;

            foreach (var extent in cacheExtents)
            {
                Assert.True(NumericalMath.TryMomentMatrices(coupled, scale, extent,
                    out var uncached));
                Assert.True(NumericalMath.TryMomentMatrices(powerCache, extent,
                    out var cached));
                AssertMomentParity(uncached, cached, extent);
            }

            if (scale == 27.0)
            {
                Assert.True(NumericalMath.TryMomentMatrices(coupled, scale, 2.0,
                    out var uncached));
                Assert.True(NumericalMath.TryMomentMatrices(powerCache, 2.0,
                    out var cached));
                AssertMomentParity(uncached, cached, 2.0);
            }
        }

        Assert.Null(NumericalMath.CreateMomentPowerCache(coupled, 32.0));
        Assert.True(NumericalMath.TryMomentMatrices(coupled, 32.0, 0.5, out _));
    }

    [Fact]
    public void AffineMatrixMomentsMatchAnalyticSymmetricCoupling()
    {
        var matrix = new[]
        {
            new[] { -5.2, 1.3 },
            new[] { 1.3, -5.2 }
        };
        const double scale = 0.7;
        var lambdaPlus = scale * (-5.2 + 1.3);
        var lambdaMinus = scale * (-5.2 - 1.3);

        foreach (var extent in new[] { 0.01, 0.25, 1.0 })
        {
            Assert.True(NumericalMath.TryMomentMatrices(matrix, scale, extent,
                out var moments));
            var plus = new double[3];
            var minus = new double[3];
            Assert.True(NumericalMath.ScalarMoments(lambdaPlus, extent, plus));
            Assert.True(NumericalMath.ScalarMoments(lambdaMinus, extent, minus));

            for (var moment = 0; moment < 3; moment++)
            {
                var diagonal = 0.5 * (plus[moment] + minus[moment]);
                var coupling = 0.5 * (plus[moment] - minus[moment]);
                Assert.True(Math.Abs(moments[moment, 0, 0] - diagonal) <= 2e-13,
                    $"J{moment}[0,0]={moments[moment, 0, 0]:R}; " +
                    $"expected {diagonal:R} at extent={extent:R}.");
                Assert.True(Math.Abs(moments[moment, 1, 1] - diagonal) <= 2e-13,
                    $"J{moment}[1,1]={moments[moment, 1, 1]:R}; " +
                    $"expected {diagonal:R} at extent={extent:R}.");
                Assert.True(Math.Abs(moments[moment, 0, 1] - coupling) <= 2e-13,
                    $"J{moment}[0,1]={moments[moment, 0, 1]:R}; " +
                    $"expected {coupling:R} at extent={extent:R}.");
                Assert.True(Math.Abs(moments[moment, 1, 0] - coupling) <= 2e-13,
                    $"J{moment}[1,0]={moments[moment, 1, 0]:R}; " +
                    $"expected {coupling:R} at extent={extent:R}.");
            }
        }
    }

    [Fact]
    public void AffineMatrixMomentsMatchAnalyticNilpotentCoupling()
    {
        var baseMatrix = new[]
        {
            new[] { 0.0, 2.0, -0.5 },
            new[] { 0.0, 0.0, 3.0 },
            new[] { 0.0, 0.0, 0.0 }
        };
        const double scale = 0.4;

        foreach (var extent in new[] { 0.001, 0.1, 1.0 })
        {
            Assert.True(NumericalMath.TryMomentMatrices(baseMatrix, scale, extent,
                out var moments));
            for (var moment = 0; moment < 3; moment++)
                for (var row = 0; row < 3; row++)
                    for (var column = 0; column < 3; column++)
                    {
                        var a = scale * baseMatrix[row][column];
                        var aSquared = 0.0;
                        for (var inner = 0; inner < 3; inner++)
                            aSquared += scale * baseMatrix[row][inner] *
                                        scale * baseMatrix[inner][column];
                        var expected =
                            (row == column ? Math.Pow(extent, moment + 1) / (moment + 1) : 0.0) +
                            a * Math.Pow(extent, moment + 2) /
                            ((moment + 1) * (moment + 2)) +
                            aSquared * Math.Pow(extent, moment + 3) /
                            ((moment + 1) * (moment + 2) * (moment + 3));
                        Assert.True(Math.Abs(moments[moment, row, column] - expected) <= 2e-14,
                            $"J{moment}[{row},{column}]={moments[moment, row, column]:R}; " +
                            $"expected {expected:R} at extent={extent:R}.");
                    }
        }
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(1e-12)]
    [InlineData(0.7)]
    [InlineData(2.0)]
    [InlineData(10.0)]
    public void ExponentialConvolutionMatchesConstantTargetAndComposition(double decayRate)
    {
        const double initial = 0.17;
        const double target = 0.83;
        const double duration = 0.7;
        double Source(double _) => decayRate * target;

        var expected = target + (initial - target) * Math.Exp(-decayRate * duration);
        var whole = NumericalMath.ExponentialConvolution(
            initial, 0.0, duration, decayRate, Source);
        var first = NumericalMath.ExponentialConvolution(
            initial, 0.0, 0.3, decayRate, Source);
        var composed = NumericalMath.ExponentialConvolution(
            first, 0.3, duration, decayRate, Source);

        Assert.True(Math.Abs(whole - expected) <= 2e-15,
            $"whole convolution {whole:R} vs closed form {expected:R}.");
        Assert.True(Math.Abs(composed - expected) <= 2e-15,
            $"composed convolution {composed:R} vs closed form {expected:R}.");
    }

    [Fact]
    public void ScalarLagSolutionPreservesUnitPlateauAndPositiveClosedForm()
    {
        const double duration = 1.0;
        const double tau = 12.0;
        var unitPlateau = new PhysiologicalModel.ScalarLagSolution(
            1.0, duration, tau, _ => 1.0);
        var rising = new PhysiologicalModel.ScalarLagSolution(
            0.0, duration, tau, _ => 0.7);

        Assert.Equal(1.0, unitPlateau.Evaluate(0.0));
        foreach (var c in NumericalMath.RadauC)
        {
            Assert.Equal(1.0, unitPlateau.Evaluate(c));
            var value = rising.Evaluate(c);
            var expected = 0.7 * (1.0 - Math.Exp(-duration * c / tau));
            Assert.InRange(value, 0.0, 0.7);
            Assert.True(Math.Abs(value - expected) <= 2e-15,
                $"scalar lag {value:R} vs closed form {expected:R} at c={c:R}.");
        }
    }

    [Fact]
    public void ScalarLagSolutionHomogeneousZeroTargetPreservesTinyPositiveLoss()
    {
        const double initial = 1e-89;
        const double duration = 0.02;
        static double VariableRate(double c) => 0.25 + 1.5 * c;

        var variable = new PhysiologicalModel.ScalarLagSolution(
            initial, duration, _ => 0.0, VariableRate);
        var constantRate = new PhysiologicalModel.ScalarLagSolution(
            initial, duration, _ => 0.0, _ => 0.4);
        var points = new List<double> { 0.0, 0.001, 0.1, 0.33, 0.5, 0.9, 1.0 };
        points.AddRange(NumericalMath.RadauC);
        points.AddRange(NumericalMath.GaussLegendre16Nodes);

        foreach (var c in points.Distinct())
        {
            var value = variable.Evaluate(c);
            var expectedVariableRatio = Math.Exp(
                -duration * (0.25 * c + 0.75 * c * c));
            Assert.True(double.IsFinite(value) && value > 0.0,
                $"homogeneous value {value:R} was not positive at c={c:R}.");
            Assert.True(Math.Abs(value / initial - expectedVariableRatio) <= 2e-15,
                $"variable-rate ratio {value / initial:R} vs " +
                $"{expectedVariableRatio:R} at c={c:R}.");
            Assert.Equal(-VariableRate(c) * value, variable.Derivative(c));

            var constantValue = constantRate.Evaluate(c);
            var expectedConstantRatio = Math.Exp(-0.4 * duration * c);
            Assert.True(double.IsFinite(constantValue) && constantValue > 0.0,
                $"constant-rate value {constantValue:R} was not positive at c={c:R}.");
            Assert.True(Math.Abs(constantValue / initial - expectedConstantRatio) <=
                        2e-15,
                $"constant-rate ratio {constantValue / initial:R} vs " +
                $"{expectedConstantRatio:R} at c={c:R}.");
        }
    }

    [Fact]
    public void ScalarLagSolutionConstantZeroAndOneTargetsMatchClosedForm()
    {
        const double duration = 1.0;
        var points = new[] { 0.0, NumericalMath.RadauC[0],
            NumericalMath.RadauC[1], 1.0 };
        foreach (var initial in new[] { 0.7, 0.3 })
            foreach (var target in new[] { 0.0, 1.0 })
                foreach (var tau in new[] { 0.5, 2.0 })
                {
                    var scalar = new PhysiologicalModel.ScalarLagSolution(
                        initial, duration, tau, target);
                    foreach (var c in points)
                    {
                        var value = scalar.Evaluate(c);
                        var expected = target + (initial - target) *
                            Math.Exp(-duration * c / tau);
                        Assert.True(Math.Abs(value - expected) <= 1e-12,
                            $"constant-target value {value:R} vs {expected:R} at " +
                            $"initial={initial:R}, target={target:R}, tau={tau:R}, c={c:R}.");
                        Assert.True(Math.Abs(scalar.Derivative(c) -
                                            (target - value) / tau) <= 1e-12,
                            $"constant-target derivative mismatch at c={c:R}.");
                    }

                    var instantaneous = new PhysiologicalModel.ScalarLagSolution(
                        initial, duration, 0.0, target);
                    foreach (var c in points)
                    {
                        Assert.Equal(target, instantaneous.Evaluate(c));
                        Assert.Equal(0.0, instantaneous.Derivative(c));
                    }
                }
    }

    [Theory]
    [InlineData(0.01)]
    [InlineData(0.25)]
    [InlineData(1.0)]
    public void AffineCollocationPreservesUnitConsciousnessWithVaryingLossRate(double duration)
    {
        PhysiologicalModel.AffineSystemSpec SystemAt(double c)
        {
            var rate = 0.25 + 1.75 * c;
            var spec = new PhysiologicalModel.AffineSystemSpec
            {
                M = NumericalMath.CreateMatrix(1),
                B = new double[1],
                Instant = new bool[1],
                InstantTargets = new Func<double, double>?[1]
            };
            spec.M[0][0] = -rate;
            spec.B[0] = rate;
            return spec;
        }

        var firstRate = SystemAt(NumericalMath.RadauC[0]).M[0][0];
        var lastRate = SystemAt(NumericalMath.RadauC[2]).M[0][0];
        Assert.NotEqual(firstRate, lastRate);

        var solution = PhysiologicalModel.SolveAffineCollocation(
            UnitScalarInitialState, duration, SystemAt(0.0), SystemAt);
        Assert.True(solution.Valid);
        Assert.Null(solution.MomentPowers);
        Assert.Equal(1.0, solution.Y0[0]);
        foreach (var stage in solution.Stages) Assert.Equal(1.0, stage[0]);
        Assert.Equal(1.0, solution.Evaluate(0.0)[0]);
        foreach (var node in NumericalMath.RadauC)
            Assert.Equal(1.0, solution.Evaluate(node)[0]);
    }

    [Fact]
    public void AffineCollocationCoupledNilpotentMatchesAnalyticDenseValues()
    {
        const double duration = 0.4;

        PhysiologicalModel.AffineSystemSpec SystemAt(double c)
        {
            var spec = new PhysiologicalModel.AffineSystemSpec
            {
                M = NumericalMath.CreateMatrix(3),
                B = [0.0, 0.0, c],
                Instant = new bool[3],
                InstantTargets = new Func<double, double>?[3]
            };
            spec.M[0][1] = 2.0;
            spec.M[1][2] = 3.0;
            return spec;
        }

        var solution = PhysiologicalModel.SolveAffineCollocation(
            [0.0, 0.0, 0.0], duration, SystemAt(0.0), SystemAt);
        Assert.True(solution.Valid);

        foreach (var c in AffineDenseQueryPoints)
        {
            var time = duration * c;
            var expected = new[]
            {
                time * time * time * time / (4.0 * duration),
                time * time * time / (2.0 * duration),
                time * time / (2.0 * duration)
            };
            var actual = solution.Evaluate(c);
            for (var component = 0; component < 3; component++)
                Assert.True(Math.Abs(actual[component] - expected[component]) <= 1e-12,
                    $"component {component} at c={c:R}: {actual[component]:R} vs " +
                    $"analytic {expected[component]:R}.");
        }
    }

    [Fact]
    public void AffineCollocationDenseResidualBasisMatchesStageExpansion()
    {
        const double duration = 0.3;
        var baseMatrix = new[]
        {
            new[] { -5.2, 1.3, 0.1 },
            new[] { 0.4, -4.1, 0.8 },
            new[] { 0.2, 0.6, -3.3 }
        };
        var matrixDelta = new[]
        {
            new[] { 0.0, 1.0, 0.0 },
            new[] { 0.0, 0.0, 1.0 },
            new[] { 1.0, 0.0, 0.0 }
        };
        var baseSource = new[] { 0.3, -0.2, 0.1 };
        var initial = new[] { 0.15, 0.5, 0.22 };
        Assert.NotEqual(baseMatrix[0][2], baseMatrix[1][0]);

        PhysiologicalModel.AffineSystemSpec SystemAt(double c, bool instant = false)
        {
            var matrix = NumericalMath.CreateMatrix(3);
            for (var row = 0; row < 3; row++)
                for (var column = 0; column < 3; column++)
                    matrix[row][column] =
                        baseMatrix[row][column] + c * matrixDelta[row][column];

            var instantFlags = new bool[3];
            var instantTargets = new Func<double, double>?[3];
            if (instant)
            {
                instantFlags[1] = true;
                instantTargets[1] = cValue => 0.2 + 0.1 * cValue;
            }
            return new PhysiologicalModel.AffineSystemSpec
            {
                M = matrix,
                B =
                [
                    baseSource[0] + 0.4 * c,
                    baseSource[1] - 0.25 * c,
                    baseSource[2] + 0.15 * c * c
                ],
                Instant = instantFlags,
                InstantTargets = instantTargets
            };
        }

        var solution = PhysiologicalModel.SolveAffineCollocation(initial, duration,
            SystemAt(0.0), c => SystemAt(c));
        Assert.True(solution.Valid);
        Assert.NotNull(solution.MomentPowers);
        Assert.NotNull(solution.DenseMomentCoefficients);
        Assert.Equal(1.0, solution.DenseMomentExtent);
        Assert.True(solution.DenseMomentNorm * 0.5 <= 1.0);
        Assert.True(solution.DenseMomentNorm * 0.6 > 1.0);
        Assert.Contains(solution.Residuals.SelectMany(stage => stage),
            value => value != 0.0);

        var basis = NumericalMath.LagrangeBasis();
        var queryPoints = new[] { 0.0, 1e-6, 0.125, 0.25, 0.5, 0.6, 1.0 };
        foreach (var c in queryPoints)
        {
            Assert.True(NumericalMath.TryMomentMatrices(solution.M0, duration, c,
                out var moments));
            var actual = solution.Evaluate(c);
            for (var row = 0; row < 3; row++)
            {
                var expected = solution.Y0[row];
                for (var k = 0; k < 3; k++)
                    expected += duration * moments[0, row, k] * solution.BaseSource[k];
                for (var stage = 0; stage < 3; stage++)
                    for (var k = 0; k < 3; k++)
                    {
                        var weight = 0.0;
                        for (var moment = 0; moment < 3; moment++)
                            weight += basis[stage][moment] * moments[moment, row, k];
                        expected += duration * weight * solution.Residuals[stage][k];
                    }
                Assert.True(Math.Abs(actual[row] - expected) <= 1e-12,
                    $"component {row} at c={c:R}: {actual[row]:R} vs " +
                    $"stage-basis reference {expected:R}.");
            }
        }

        var cacheableSolution = PhysiologicalModel.SolveAffineCollocation(initial, 0.05,
            SystemAt(0.0), c => SystemAt(c));
        Assert.True(cacheableSolution.Valid);
        Assert.NotNull(cacheableSolution.MomentPowers);
        Assert.All(cacheableSolution.Evaluate(0.4),
            value => Assert.True(double.IsFinite(value)));

        var instantSolution = PhysiologicalModel.SolveAffineCollocation(initial, duration,
            SystemAt(0.0, true), c => SystemAt(c, true));
        Assert.True(instantSolution.Valid);
        Assert.Contains(instantSolution.Residuals.SelectMany(stage => stage),
            value => value != 0.0);
        foreach (var c in queryPoints)
            Assert.Equal(0.2 + 0.1 * c, instantSolution.Evaluate(c)[1]);
    }

    [Fact]
    public void AffineCollocationExtendedDenseExtentMatchesExactEvaluation()
    {
        var queryPoints = new[] { 0.25, 0.5, 0.75, 1.0 };

        PhysiologicalModel.AffineSystemSpec ResidualSystemAt(double c)
        {
            var baseMatrix = new[]
            {
                new[] { -5.2, 1.3, 0.1 },
                new[] { 0.4, -4.1, 0.8 },
                new[] { 0.2, 0.6, -3.3 }
            };
            var matrixDelta = new[]
            {
                new[] { 0.0, 1.0, 0.0 },
                new[] { 0.0, 0.0, 1.0 },
                new[] { 1.0, 0.0, 0.0 }
            };
            var matrix = NumericalMath.CreateMatrix(3);
            for (var row = 0; row < 3; row++)
                for (var column = 0; column < 3; column++)
                    matrix[row][column] =
                        baseMatrix[row][column] + c * matrixDelta[row][column];
            return new PhysiologicalModel.AffineSystemSpec
            {
                M = matrix,
                B =
                [
                    0.3 + 0.4 * c,
                    -0.2 - 0.25 * c,
                    0.1 + 0.15 * c * c
                ],
                Instant = new bool[3],
                InstantTargets = new Func<double, double>?[3]
            };
        }

        PhysiologicalModel.AffineSystemSpec DiagonalSystemAt(double c) =>
            new()
            {
                M =
                [
                    [-2.0, 0.0, 0.0],
                    [0.0, -4.0, 0.0],
                    [0.0, 0.0, -8.0]
                ],
                B = [1.0, -0.5, 0.25],
                Instant = new bool[3],
                InstantTargets = new Func<double, double>?[3]
            };

        PhysiologicalModel.AffineSystemSpec NilpotentSystemAt(double c)
        {
            var spec = new PhysiologicalModel.AffineSystemSpec
            {
                M = NumericalMath.CreateMatrix(3),
                B = [0.0, 0.0, c],
                Instant = new bool[3],
                InstantTargets = new Func<double, double>?[3]
            };
            spec.M[0][1] = 2.0;
            spec.M[1][2] = 3.0;
            return spec;
        }

        void Verify(double[] initial, double duration,
            Func<double, PhysiologicalModel.AffineSystemSpec> systemAt,
            string name)
        {
            var dense = PhysiologicalModel.SolveAffineCollocation(
                    initial, duration, systemAt(0.0), systemAt);
            var reference = PhysiologicalModel.SolveAffineCollocation(
                initial, duration, systemAt(0.0), systemAt,
                prepareDenseTaylor: false);
            Assert.True(dense.Valid, $"{name}: dense solve invalid.");
            Assert.True(reference.Valid,
                $"{name}: reference solve invalid.");
            Assert.NotNull(dense.MomentPowers);
            Assert.NotNull(dense.DenseMomentCoefficients);
            Assert.Equal(1.0, dense.DenseMomentExtent);
            Assert.True(dense.DenseMomentDegree <= 64);
            Assert.Null(reference.DenseMomentCoefficients);
            foreach (var c in queryPoints)
            {
                var denseValue = dense.Evaluate(c);
                var referenceValue = reference.Evaluate(c);
                for (var row = 0; row < 3; row++)
                    Assert.True(
                        Math.Abs(denseValue[row] - referenceValue[row])
                            <= 2e-12,
                        $"{name} component {row} at c={c:R}: " +
                        $"{denseValue[row]:R} vs " +
                        $"{referenceValue[row]:R}.");
            }
        }

        Verify([0.15, 0.5, 0.22], 0.3, ResidualSystemAt, "residual");
        Verify([0.5, 0.3, 0.1], 1.0, DiagonalSystemAt, "diagonal");
        Verify([0.0, 0.0, 0.0], 0.4, NilpotentSystemAt, "nilpotent");
    }

    [Fact]
    public void SolveAffineCollocationSkippedDenseTaylorMatchesPreparedEndpoint()
    {
        const double duration = 0.3;
        // Small norm so the certified Taylor extent covers the c = 1 endpoint
        // and the prepared solution actually evaluates through it there.
        var baseMatrix = new[]
        {
            new[] { -0.52, 0.13, 0.01 },
            new[] { 0.04, -0.41, 0.08 },
            new[] { 0.02, 0.06, -0.33 }
        };
        var matrixDelta = new[]
        {
            new[] { 0.0, 0.1, 0.0 },
            new[] { 0.0, 0.0, 0.1 },
            new[] { 0.1, 0.0, 0.0 }
        };
        var baseSource = new[] { 0.3, -0.2, 0.1 };
        var initial = new[] { 0.15, 0.5, 0.22 };

        PhysiologicalModel.AffineSystemSpec SystemAt(double c, bool instant)
        {
            var matrix = NumericalMath.CreateMatrix(3);
            for (var row = 0; row < 3; row++)
                for (var column = 0; column < 3; column++)
                    matrix[row][column] =
                        baseMatrix[row][column] + c * matrixDelta[row][column];
            var instantFlags = new bool[3];
            var instantTargets = new Func<double, double>?[3];
            if (instant)
            {
                instantFlags[1] = true;
                instantTargets[1] = cValue => 0.2 + 0.1 * cValue;
            }
            return new PhysiologicalModel.AffineSystemSpec
            {
                M = matrix,
                B =
                [
                    baseSource[0] + 0.4 * c,
                    baseSource[1] - 0.25 * c,
                    baseSource[2] + 0.15 * c * c
                ],
                Instant = instantFlags,
                InstantTargets = instantTargets
            };
        }

        foreach (var instant in new[] { false, true })
        {
            var prepared = PhysiologicalModel.SolveAffineCollocation(
                initial, duration, SystemAt(0.0, instant),
                c => SystemAt(c, instant));
            var skipped = PhysiologicalModel.SolveAffineCollocation(
                initial, duration, SystemAt(0.0, instant),
                c => SystemAt(c, instant), prepareDenseTaylor: false);
            Assert.True(prepared.Valid);
            Assert.True(skipped.Valid);
            Assert.NotNull(prepared.MomentPowers);
            Assert.NotNull(skipped.MomentPowers);
            Assert.NotNull(prepared.DenseMomentCoefficients);
            Assert.Null(skipped.DenseMomentCoefficients);

            var preparedEndpoint = prepared.Evaluate(1.0);
            var skippedEndpoint = skipped.Evaluate(1.0);
            var preparedDerivative = prepared.Derivative(1.0);
            var skippedDerivative = skipped.Derivative(1.0);
            for (var row = 0; row < 3; row++)
            {
                var endpointDelta =
                    Math.Abs(preparedEndpoint[row] - skippedEndpoint[row]);
                Assert.True(endpointDelta <= 1e-12,
                    $"instant={instant} endpoint[{row}]: " +
                    $"{preparedEndpoint[row]:R} vs {skippedEndpoint[row]:R} " +
                    $"delta {endpointDelta:R}.");
                var derivativeDelta = Math.Abs(preparedDerivative[row] -
                                               skippedDerivative[row]);
                Assert.True(derivativeDelta <= 1e-12,
                    $"instant={instant} derivative[{row}]: " +
                    $"{preparedDerivative[row]:R} vs " +
                    $"{skippedDerivative[row]:R} delta {derivativeDelta:R}.");
            }
        }
    }

    [Fact]
    public void QuadraticRootsInIntervalHandlesEdgeCases()
    {
        var roots = new double[2];
        Assert.Equal(0, NumericalMath.QuadraticRootsInInterval(0.0, 0.0, 0.0, -1.0, 1.0, roots));
        Assert.Equal(1, NumericalMath.QuadraticRootsInInterval(1.0, 0.0, 0.0, -1.0, 1.0, roots));
        Assert.Equal(0.0, roots[0], 12);
        Assert.Equal(0, NumericalMath.QuadraticRootsInInterval(1.0, 0.0, 0.0, 0.0, 1.0, roots));
        Assert.Equal(1, NumericalMath.QuadraticRootsInInterval(0.0, 2.0, -1.0, 0.0, 1.0, roots));
        Assert.Equal(0.5, roots[0], 12);
        Assert.Equal(1, NumericalMath.QuadraticRootsInInterval(1.0, -1.0, 0.25, 0.0, 1.0, roots));
        Assert.Equal(0.5, roots[0], 12);
        Assert.Equal(2, NumericalMath.QuadraticRootsInInterval(1.0, -0.9, 0.14, 0.0, 1.0, roots));
        Assert.Equal(0.2, roots[0], 12);
        Assert.Equal(0.7, roots[1], 12);
        Assert.Equal(0, NumericalMath.QuadraticRootsInInterval(1.0, -0.9, 0.14, 0.8, 1.0, roots));
        Assert.Equal(0, NumericalMath.QuadraticRootsInInterval(1.0, 0.0, 1.0, -1.0, 1.0, roots));
    }

    [Fact]
    public void BrentDekkerSignLocalizesRootsAcrossShapes()
    {
        const double tolerance = PhysiologicalModel.PressureBoundRootToleranceSeconds;

        var root = NumericalMath.BrentDekkerSign(x => x - 0.3, 0.0, 1.0, -0.3,
            0.7, tolerance);
        Assert.True(Math.Abs(root - 0.3) <= tolerance,
            $"linear root {root:R} vs 0.3.");
        root = NumericalMath.BrentDekkerSign(x => x * x * x - x - 2.0, 1.0, 2.0,
            -2.0, 4.0, tolerance);
        var expected = 1.5213797068045676;
        Assert.True(Math.Abs(root - expected) <= tolerance,
            $"nonlinear root {root:R} vs {expected:R}.");
        root = NumericalMath.BrentDekkerSign(
            x =>
            {
                var d = x - 0.4;
                return d * d * d;
            }, 0.0, 1.0, -0.064, 0.216, tolerance);
        Assert.True(Math.Abs(root - 0.4) <= tolerance,
            $"flat cubic root {root:R} vs 0.4.");
        root = NumericalMath.BrentDekkerSign(x => x - 1e-9, 0.0, 1.0, -1e-9,
            1.0 - 1e-9, tolerance);
        Assert.True(Math.Abs(root - 1e-9) <= tolerance,
            $"near-start root {root:R} vs 1e-9.");
        root = NumericalMath.BrentDekkerSign(x => x - (1.0 - 1e-9), 0.0, 1.0,
            -(1.0 - 1e-9), 1e-9, tolerance);
        Assert.True(Math.Abs(root - (1.0 - 1e-9)) <= tolerance,
            $"near-end root {root:R} vs {1.0 - 1e-9:R}.");
        Assert.Equal(0.4, NumericalMath.BrentDekkerSign(x => x - 0.4, 0.4, 1.0,
            0.0, 0.6, tolerance));
        Assert.Equal(0.5, NumericalMath.BrentDekkerSign(x => x - 0.5, 0.3, 0.7,
            -0.2, 0.2, tolerance));

        var evaluations = 0;
        root = NumericalMath.BrentDekkerSign(
            x =>
            {
                evaluations++;
                return x * x - 2.0;
            }, 0.0, 2.0, -2.0, 2.0, tolerance);
        Assert.True(Math.Abs(root - Math.Sqrt(2.0)) <= tolerance,
            $"sqrt(2) root {root:R}.");
        Assert.True(evaluations <= 20,
            $"refinement used {evaluations} evaluations; bisection alone needs " +
            "about 28 for this bracket and tolerance.");
    }

    [Fact]
    public void BrentDekkerSignRejectsInvalidInputs()
    {
        const double tolerance = PhysiologicalModel.PressureBoundRootToleranceSeconds;

        Assert.True(double.IsNaN(NumericalMath.BrentDekkerSign(x => x - 0.5,
            0.0, 1.0, 0.5, 0.7, tolerance)),
            "same-sign endpoints must not be treated as a bracket.");
        Assert.True(double.IsNaN(NumericalMath.BrentDekkerSign(x => x - 0.5,
            0.0, 1.0, double.NaN, 0.5, tolerance)),
            "nonfinite endpoint value must reject.");
        Assert.True(double.IsNaN(NumericalMath.BrentDekkerSign(
            x => x == 0.5 ? double.NaN : x - 0.5, 0.0, 1.0, -0.5, 0.5,
            tolerance)), "nonfinite mid-bracket evaluation must reject.");
        Assert.True(double.IsNaN(NumericalMath.BrentDekkerSign(x => x - 0.5,
            1.0, 0.0, 0.5, -0.5, tolerance)),
            "unordered bounds must reject.");
        Assert.True(double.IsNaN(NumericalMath.BrentDekkerSign(x => x - 0.5,
            0.5, 0.5, 0.0, 0.0, tolerance)),
            "degenerate bounds must reject.");
        Assert.True(double.IsNaN(NumericalMath.BrentDekkerSign(x => x - 0.5,
            double.NaN, 1.0, double.NaN, 0.5, tolerance)),
            "nonfinite bounds must reject.");
        Assert.True(double.IsNaN(NumericalMath.BrentDekkerSign(x => x - 0.5,
            0.0, 1.0, -0.5, 0.5, 0.0)),
            "zero tolerance must reject.");
        Assert.True(double.IsNaN(NumericalMath.BrentDekkerSign(x => x - 0.5,
            0.0, 1.0, -0.5, 0.5, -1e-8)),
            "negative tolerance must reject.");
        Assert.True(double.IsNaN(NumericalMath.BrentDekkerSign(x => x - 0.5,
            0.0, 1.0, -0.5, 0.5, double.PositiveInfinity)),
            "nonfinite tolerance must reject.");
        // A tolerance below the representable bracket resolution stalls the
        // sign bracket; exhaustion must report NaN, not a midpoint.
        Assert.True(double.IsNaN(NumericalMath.BrentDekkerSign(
            x => x * x - 2.0, 0.0, 2.0, -2.0, 2.0, 1e-20)),
            "unreachable tolerance must reject on exhaustion.");
    }

    [Fact]
    public void TrySolveLinearInPlaceMatchesPreservingWrapper()
    {
        double[][] matrix =
        {
            new[] { 0.0, 2.0, 1.0 },
            new[] { 1.0, -2.0, -3.0 },
            new[] { 2.0, 3.0, 1.0 }
        };
        double[][] originalMatrix =
        {
            new[] { 0.0, 2.0, 1.0 },
            new[] { 1.0, -2.0, -3.0 },
            new[] { 2.0, 3.0, 1.0 }
        };
        var rhs = new[] { 3.0, 0.0, 7.0 };
        var originalRhs = new[] { 3.0, 0.0, 7.0 };
        var expected = new[] { 1.0, 2.0, -1.0 };

        var wrapperSolution = new double[3];
        Assert.True(NumericalMath.TrySolveLinear(matrix, rhs, wrapperSolution));
        for (var row = 0; row < 3; row++)
        {
            for (var column = 0; column < 3; column++)
                Assert.Equal(originalMatrix[row][column], matrix[row][column]);
            Assert.Equal(originalRhs[row], rhs[row]);
            Assert.True(Math.Abs(wrapperSolution[row] - expected[row]) <= 1e-12,
                $"wrapper x[{row}] {wrapperSolution[row]:R} vs {expected[row]:R}.");
        }

        var inPlaceSolution = new double[3];
        Assert.True(NumericalMath.TrySolveLinearInPlace(matrix, rhs, inPlaceSolution));
        for (var row = 0; row < 3; row++)
            Assert.True(Math.Abs(inPlaceSolution[row] - expected[row]) <= 1e-12,
                $"in-place x[{row}] {inPlaceSolution[row]:R} vs {expected[row]:R}.");

        double[][] singular = { new[] { 1.0, 2.0 }, new[] { 2.0, 4.0 } };
        var singularRhs = new[] { 3.0, 6.0 };
        Assert.False(NumericalMath.TrySolveLinear(singular, singularRhs, new double[2]));
        Assert.False(NumericalMath.TrySolveLinearInPlace(singular, singularRhs, new double[2]));
    }

    [Fact]
    public void DenseHeadDeclineReleasesBoundAtSourceCrossing()
    {
        var settings = LogicSettings.Default;
        var decay = 1.0 / settings.CerebralPressureImpairmentRecoveryTau;
        var thresholdHead = HeadForPressureSource(decay, -5.0, settings);
        var headCoefficients = new[] { 0.23, -0.06, 0.0, 0.0 };
        var expectedRelease = (0.23 - thresholdHead) / 0.06;
        var diagnostics = new List<string>();
        var stageOffsets = new[] { NumericalMath.RadauC[0], NumericalMath.RadauC[1], 1.0 };
        var stagePressures = PhysiologicalModel.ReconstructIntervalPressure(1.0, decay,
            headCoefficients, stageOffsets, 1.0, -5.0, settings, diagnostics,
            out var entries, out var releases);
        var referenceFinal = IndependentFreePressure(1.0, expectedRelease, 1.0, decay,
            s => IndependentPressureSource(0.23 - 0.06 * s, -5.0, settings));
        _output.WriteLine(
            $"expectedRelease={expectedRelease:R} releases=[{string.Join(",", releases.Select(e => e.ToString("R", CultureInfo.InvariantCulture)))}] " +
            $"entries=[{string.Join(",", entries.Select(e => e.ToString("R", CultureInfo.InvariantCulture)))}] " +
            $"stages=[{string.Join(",", stagePressures.Select(p => p.ToString("R", CultureInfo.InvariantCulture)))}] " +
            $"referenceFinal={referenceFinal:R} diagnostics={diagnostics.Count}");
        var release = Assert.Single(releases);
        Assert.True(Math.Abs(release - expectedRelease) <= 1e-8,
            $"release {release:R} vs expected {expectedRelease:R}.");
        Assert.Empty(entries);
        Assert.Empty(diagnostics);
        Assert.Equal(1.0, stagePressures[0]);
        Assert.True(Math.Abs(stagePressures[2] - referenceFinal) <= 1e-6,
            $"final pressure {stagePressures[2]:R} vs independent reference {referenceFinal:R}.");
        foreach (var stage in stagePressures) Assert.InRange(stage, 0.0, 1.0);
    }
    [Fact]
    public void IntervalUpstreamZeroPressureSourceDecaysExactly()
    {
        var settings = LogicSettings.Default;
        var model = new GEffectsLogicInstance().PhysModel;
        var upstreamType = typeof(PhysiologicalModel).GetNestedType(
            "IntervalUpstream", BindingFlags.NonPublic)!;
        var ctor = upstreamType.GetConstructors(
            BindingFlags.NonPublic | BindingFlags.Instance).Single();
        var pressureAt = upstreamType.GetMethod("PressureAt",
            BindingFlags.NonPublic | BindingFlags.Instance)!;
        var tau = settings.CerebralPressureImpairmentRecoveryTau;
        var decay = 1.0 / Math.Max(tau, 1e-9);

        object CreateUpstream(double impairment, double[] head, double dt,
            double gz)
        {
            var initial = model.CaptureIntervalState() with
            {
                CerebralPressureImpairment = impairment
            };
            var segment = new PhysiologicalModel.IntegrationSegment(
                0.0, dt, initial, initial, [],
                PhysiologicalModel.BloodBounds.None, head, head, head, head);
            var circulation = new PhysiologicalModel.IntegrationResult(
                initial, initial, [], 0, 0.0, true, [], [],
                [], [], [], [], [], [], [], [], [], [segment], []);
            return ctor.Invoke([circulation, initial, 0.0, 0.0, gz, settings]);
        }

        double PressureAt(object upstream, double t) =>
            (double)pressureAt.Invoke(upstream, [t])!;

        var rest = settings.RestingBloodHead;
        var underfilled = new[] { 0.5 * rest, 0.0, 0.0, 0.0 };
        var constant = CreateUpstream(0.3, underfilled, 0.225, 2.0);
        foreach (var t in new[] { 0.0, 0.05, 0.225 })
        {
            var expected = 0.3 * Math.Exp(-t / tau);
            Assert.True(Math.Abs(PressureAt(constant, t) - expected) <= 1e-15,
                $"constant underfilled at {t:R}: {PressureAt(constant, t):R} " +
                $"vs {expected:R}.");
        }

        var dip = new[] { 0.7 * rest, 1.6 * rest, -4.0 * rest, 0.0 };
        var dipped = CreateUpstream(0.3, dip, 0.225, 2.0);
        var negative = CreateUpstream(0.3, underfilled, 0.225, -4.0);
        foreach (var t in new[] { 0.05, 0.225 })
        {
            var dipReference = IndependentFreePressure(0.3, 0.0, t, decay,
                s => IndependentPressureSource(
                    0.7 * rest + 1.6 * rest * s - 4.0 * rest * s * s, 2.0,
                    settings));
            Assert.True(Math.Abs(PressureAt(dipped, t) - dipReference) <= 1e-12,
                $"interior overfill at {t:R}: {PressureAt(dipped, t):R} vs " +
                $"{dipReference:R}.");
            var negativeReference = IndependentFreePressure(0.3, 0.0, t, decay,
                s => IndependentPressureSource(0.5 * rest, -4.0, settings));
            Assert.True(
                Math.Abs(PressureAt(negative, t) - negativeReference) <= 1e-12,
                $"negative source at {t:R}: {PressureAt(negative, t):R} vs " +
                $"{negativeReference:R}.");
        }
    }

    [Fact]
    public void LeanUpstreamEvaluateMatchesCirculationState()
    {
        var settings = LogicSettings.Default;
        var model = new GEffectsLogicInstance().PhysModel;
        var upstreamType = typeof(PhysiologicalModel).GetNestedType(
            "IntervalUpstream", BindingFlags.NonPublic)!;
        var ctor = upstreamType.GetConstructors(
            BindingFlags.NonPublic | BindingFlags.Instance).Single();
        var evaluate = upstreamType.GetMethod("Evaluate",
            BindingFlags.NonPublic | BindingFlags.Instance)!;
        var circulationAt = upstreamType.GetMethod("CirculationStateAt",
            BindingFlags.NonPublic | BindingFlags.Instance)!;

        var rested = model.CaptureIntervalState();
        var preconditioned = rested with
        {
            StrainingLevel = 0.6,
            StrainingFatigue = 0.4,
            RespiratoryFatigue = 0.7,
            GSuitFatigue = 0.2,
            CerebralPressureImpairment = 0.3
        };
        var instantTaus = settings with
        {
            StrainingTau = 1e-10,
            RespiratoryFatigueRecoveryTau = 1e-10
        };
        var cases = new (PhysiologicalModel.IntegrationState State, double Dt,
            double Gx, double Gy, double Gz, LogicSettings Settings)[]
        {
            (rested, 0.225, 0.0, 0.0, 2.5, settings),
            (preconditioned, 0.3, 0.0, 0.0, 2.5, settings),
            (preconditioned, 0.25, 10.0, 3.0, -4.0, settings),
            (preconditioned, 0.225, 10.0, 3.0, -4.0, instantTaus),
            (rested, 0.225, 10.0, 3.0, -4.0, instantTaus)
        };
        foreach (var (state, dt, gx, gy, gz, s) in cases)
        {
            var head = new[] { 0.8 * s.RestingBloodHead, 0.1, 0.0, 0.0 };
            var lower = new[] { 0.3, -0.05, 0.0, 0.0 };
            var rate = new[] { 1.1, 0.2, 0.0, 0.0 };
            var segment = new PhysiologicalModel.IntegrationSegment(
                0.0, dt, state, state, [],
                PhysiologicalModel.BloodBounds.None, head, lower, rate, rate);
            var circulation = new PhysiologicalModel.IntegrationResult(
                state, state, [], 0, 0.0, true, [], [],
                [], [], [], [], [], [], [], [], [], [segment], []);
            var upstream = ctor.Invoke([circulation, state, gx, gy, gz, s]);
            foreach (var t in new[] { 0.0, 0.05, 0.225 })
            {
                var trial = evaluate.Invoke(upstream, [t])!;
                var reference = circulationAt.Invoke(upstream, [t])!;
                Assert.Equal(Get(reference, "BloodHead"), Get(trial, "Head"));
                Assert.Equal(Get(reference, "BloodLower"), Get(trial, "Lower"));
                Assert.Equal(Get(reference, "HeartRateMultiplier"),
                    Get(trial, "HeartRate"));
                Assert.Equal(Get(reference, "StrainingLevel"),
                    Get(trial, "Straining"));
                Assert.Equal(Get(reference, "RespiratoryFatigue"),
                    Get(trial, "Respiratory"));
            }
        }

        static double Get(object instance, string name)
        {
            var type = instance.GetType();
            var field = type.GetField(name, BindingFlags.Public |
                BindingFlags.NonPublic | BindingFlags.Instance);
            if (field != null) return (double)field.GetValue(instance)!;
            return (double)type.GetProperty(name, BindingFlags.Public |
                BindingFlags.NonPublic | BindingFlags.Instance)!
                .GetValue(instance)!;
        }
    }

    [Fact]
    public void AffineSolutionEvaluateFirstMatchesFullEvaluation()
    {
        var y0 = new[] { 0.4, 0.6, 0.2 };
        PhysiologicalModel.AffineSystemSpec SystemAt(double c)
        {
            var spec = new PhysiologicalModel.AffineSystemSpec
            {
                M = NumericalMath.CreateMatrix(3),
                B = new double[3],
                Instant = new bool[3],
                InstantTargets = new Func<double, double>?[3]
            };
            spec.M[0][0] = -1.5;
            spec.M[0][1] = 0.4;
            spec.M[1][0] = -0.3;
            spec.M[1][1] = -2.0;
            spec.M[1][2] = 0.5;
            spec.M[2][2] = -1.0;
            spec.B[0] = 0.8;
            spec.B[1] = -0.2;
            spec.B[2] = 0.3;
            return spec;
        }

        var first = PhysiologicalModel.SolveAffineCollocation(y0, 1.0,
            SystemAt(0.0), SystemAt);
        var second = PhysiologicalModel.SolveAffineCollocation(y0, 1.0,
            SystemAt(0.0), SystemAt);
        Assert.NotNull(first.DenseMomentCoefficients);
        foreach (var c in new[] { 0.0, 0.25, 0.5, 1.0, 0.25, 0.5 })
        {
            var single = first.EvaluateFirst(c);
            Assert.Equal(first.EvaluateFirst(c), first.EvaluateFirst(c));
            var fullAfter = first.Evaluate(c);
            var singleAgain = first.EvaluateFirst(c);
            var reference = second.Evaluate(c);
            var referenceSingle = second.EvaluateFirst(c);
            Assert.Equal(3, fullAfter.Length);
            Assert.Equal(reference, fullAfter);
            Assert.Equal(reference[0], single);
            Assert.Equal(reference[0], singleAgain);
            Assert.Equal(reference[0], referenceSingle);
            Assert.Equal(first.Derivative(c), second.Derivative(c));
        }

        PhysiologicalModel.AffineSystemSpec InstantSystemAt(double c)
        {
            var spec = SystemAt(c);
            spec.Instant[0] = true;
            spec.InstantTargets[0] = _ => 0.42;
            return spec;
        }
        var instant = PhysiologicalModel.SolveAffineCollocation(y0, 1.0,
            InstantSystemAt(0.0), InstantSystemAt);
        var fallback = PhysiologicalModel.SolveAffineCollocation(y0, 1.0,
            SystemAt(0.0), SystemAt, prepareDenseTaylor: false);
        Assert.Null(fallback.DenseMomentCoefficients);
        foreach (var c in new[] { 0.0, 0.25, 0.5, 1.0 })
        {
            Assert.Equal(0.42, instant.Evaluate(c)[0]);
            Assert.Equal(instant.Evaluate(c)[0], instant.EvaluateFirst(c));
            Assert.Equal(fallback.Evaluate(c)[0], fallback.EvaluateFirst(c));
        }
    }

    [Fact]
    public void ZeroEliminationSkipPreservesExactSolveResults()
    {
        var size = 12;
        var sparse = Enumerable.Range(0, size)
            .Select(r => Enumerable.Range(0, size)
                .Select(c => r == c ? 1.0 : 0.0).ToArray()).ToArray();
        for (var r = 4; r < size; r += 3) sparse[r][0] = -0.0;
        sparse[1][2] = 0.5;
        sparse[2][1] = -0.75;
        sparse[3][3] = -2.0;
        var cases = new (double[][] Matrix, double[] Rhs, bool ExpectSolved)[]
        {
            (new[]
            {
                new[] { 4.0, 1.0, -1.0 },
                new[] { 1.0, 3.0, 1.0 },
                new[] { -1.0, 1.0, 5.0 }
            }, new[] { 7.0, -4.0, 15.0 }, true),
            (new[]
            {
                new[] { 1.0, 0.0, 0.0 },
                new[] { 0.0, 1.0, 0.0 },
                new[] { 0.0, 0.0, 1.0 }
            }, new[] { 2.5, -0.0, 4.0 }, true),
            (new[]
            {
                new[] { 1.0, 2.0, 0.0 },
                new[] { 2.0, 4.0, -0.0 },
                new[] { 0.0, -0.0, 3.0 }
            }, new[] { 3.0, 6.0, -1.0 }, false),
            (sparse,
                Enumerable.Range(0, size).Select(i => 0.25 * i - 1.0).ToArray(),
                true)
        };
        foreach (var (matrix, rhs, expectSolved) in cases)
        {
            var x = new double[rhs.Length];
            var solved = NumericalMath.TrySolveLinearInPlace(
                matrix.Select(row => row.ToArray()).ToArray(), rhs.ToArray(), x);
            Assert.Equal(expectSolved, solved);
            if (!solved) continue;
            var isIdentity = true;
            for (var r = 0; r < matrix.Length && isIdentity; r++)
                for (var c = 0; c < matrix[r].Length && isIdentity; c++)
                    if (matrix[r][c] != (r == c ? 1.0 : 0.0)) isIdentity = false;
            for (var row = 0; row < matrix.Length; row++)
            {
                var residual = matrix[row]
                    .Select((v, c) => v * x[c]).Sum() - rhs[row];
                Assert.True(Math.Abs(residual) <= 1e-13,
                    $"residual {residual:R} at row {row}.");
            }
            if (isIdentity) Assert.Equal(rhs, x);
        }
    }

    [Fact]
    public void DenseHeadDipReleasesAndReEntersBound()
    {
        var settings = LogicSettings.Default;
        var decay = 1.0 / settings.CerebralPressureImpairmentRecoveryTau;
        var thresholdHead = HeadForPressureSource(decay, -5.0, settings);
        var discriminant = 0.0064 - 0.32 * (0.23 - thresholdHead);
        var downwardRoot = (0.08 - Math.Sqrt(discriminant)) / 0.16;
        var upwardRoot = (0.08 + Math.Sqrt(discriminant)) / 0.16;
        var headCoefficients = new[] { 0.23, -0.08, 0.08, 0.0 };
        double SourceAt(double s) => IndependentPressureSource(0.23 - 0.08 * s + 0.08 * s * s,
            -5.0, settings);
        double FreeAt(double t) => IndependentFreePressure(1.0, downwardRoot, t, decay, SourceAt);
        var freeAtEnd = FreeAt(1.0);
        Assert.True(freeAtEnd > 1.0,
            $"prescribed dip does not re-enter: unbounded p(1) {freeAtEnd:R} <= 1.");
        var lo = upwardRoot;
        var hi = 1.0;
        while (hi - lo > 1e-8)
        {
            var mid = 0.5 * (lo + hi);
            if (FreeAt(mid) > 1.0) hi = mid; else lo = mid;
        }

        var referenceEntry = lo;
        var diagnostics = new List<string>();
        var stageOffsets = new[] { NumericalMath.RadauC[0], NumericalMath.RadauC[1], 1.0 };
        var stagePressures = PhysiologicalModel.ReconstructIntervalPressure(1.0, decay,
            headCoefficients, stageOffsets, 1.0, -5.0, settings, diagnostics,
            out var entries, out var releases);
        _output.WriteLine(
            $"tDown={downwardRoot:R} tUp={upwardRoot:R} referenceEntry={referenceEntry:R} " +
            $"releases=[{string.Join(",", releases.Select(e => e.ToString("R", CultureInfo.InvariantCulture)))}] " +
            $"entries=[{string.Join(",", entries.Select(e => e.ToString("R", CultureInfo.InvariantCulture)))}] " +
            $"stages=[{string.Join(",", stagePressures.Select(p => p.ToString("R", CultureInfo.InvariantCulture)))}] " +
            $"diagnostics={diagnostics.Count}");
        var release = Assert.Single(releases);
        Assert.True(Math.Abs(release - downwardRoot) <= 1e-8,
            $"release {release:R} vs expected {downwardRoot:R}.");
        var entry = Assert.Single(entries);
        Assert.True(Math.Abs(entry - referenceEntry) <= 1e-6,
            $"re-entry {entry:R} vs independent reference {referenceEntry:R}.");
        Assert.Empty(diagnostics);
        Assert.Equal(1.0, stagePressures[2]);
        foreach (var stage in stagePressures) Assert.InRange(stage, 0.0, 1.0);
    }

    [Theory]
    [MemberData(nameof(GateVectors))]
    public void ExtendedHistoryCirculationGate(double gz) =>
        AssertExtendedHistoryCirculationGate(gz);

    // Issue #22 strict twins for the formerly deferred fast-transient loads.
    [Theory]
    [Trait("Category", "Experimental")]
    [InlineData(-3.0)]
    [InlineData(-5.0)]
    public void DeferredExtendedHistoryAccuracy(double gz) =>
        AssertExtendedHistoryCirculationGate(gz);

    private void AssertExtendedHistoryCirculationGate(double gz)
    {
        const int seconds = 30;
        var failures = new List<string>();
        var rows = new StringBuilder();
        var checkpoints = new double[CandidateDt.Length][][];
        var pathAlive = new bool[CandidateDt.Length];
        var firstCoreEntry = new double[CandidateDt.Length];

        for (var candidateIndex = 0; candidateIndex < CandidateDt.Length; candidateIndex++)
        {
            var dt = CandidateDt[candidateIndex];
            var model = new GEffectsLogicInstance(new DtStabilityLogger()).PhysModel;
            var state = model.CaptureIntervalState();
            checkpoints[candidateIndex] = new double[seconds][];
            firstCoreEntry[candidateIndex] = double.NaN;
            pathAlive[candidateIndex] = true;
            var callsPerSecond = (int)Math.Round(1.0 / dt);
            var reachedSecond = 0;
            for (var second = 1; second <= seconds && pathAlive[candidateIndex]; second++)
            {
                for (var call = 1; call <= callsPerSecond; call++)
                {
                    var result = model.AdvanceCirculationInterval(in state, dt, 0.0, 0.0, gz,
                        LogicSettings.Default);
                    var t = (second - 1 + call / (double)callsPerSecond);
                    if (double.IsNaN(firstCoreEntry[candidateIndex]) &&
                        result.CoreBoundEntries.Length > 0)
                        firstCoreEntry[candidateIndex] = t - dt + result.CoreBoundEntries[0];
                    var stageMinVolume = result.Stages.Length == 0
                        ? double.NaN
                        : result.Stages.Min(stage =>
                            Math.Min(stage.BloodHead,
                                Math.Min(stage.BloodCore, stage.BloodLower)));
                    rows.AppendLine(CultureInfo.InvariantCulture,
                        $"extended gz={gz:R} dt={dt:R} second={second} call={call}/{callsPerSecond} t={t:R} " +
                        $"converged={result.Converged} iterations={result.Iterations} " +
                        $"maxResidual={result.MaxScaledResidual:R} stageMinVolume={stageMinVolume:R} " +
                        $"modeCrossings={result.ModeCrossings.Length} stateViolations={result.StateViolations.Length} " +
                        $"segments={result.Segments.Length} " +
                        $"entries=[{string.Join(",", result.PressureBoundEntries.Select(e => e.ToString("R", CultureInfo.InvariantCulture)))}] " +
                        $"releases=[{string.Join(",", result.PressureBoundReleases.Select(e => e.ToString("R", CultureInfo.InvariantCulture)))}] " +
                        $"coreEntries=[{string.Join(",", result.CoreBoundEntries.Select(e => e.ToString("R", CultureInfo.InvariantCulture)))}] " +
                        $"coreReleases=[{string.Join(",", result.CoreBoundReleases.Select(e => e.ToString("R", CultureInfo.InvariantCulture)))}] " +
                        $"head={result.Final.BloodHead:R} core={result.Final.BloodCore:R} lower={result.Final.BloodLower:R} " +
                        $"hr={result.Final.HeartRateMultiplier:R} cardio={result.Final.CardioFatigue:R} " +
                        $"pressure={result.Final.CerebralPressureImpairment:R} " +
                        $"stagePressures=[{string.Join(",", result.Stages.Select(stage => stage.CerebralPressureImpairment.ToString("R", CultureInfo.InvariantCulture)))}]");

                    var fatal = false;
                    if (!result.Converged)
                    {
                        failures.Add($"extended gz {gz:R} dt {dt:R} at {t:R}s: Newton did not converge; " +
                                     $"scaled residual {result.MaxScaledResidual:R} after {result.Iterations} iterations.");
                        fatal = true;
                    }

                    foreach (var crossing in result.ModeCrossings)
                    {
                        failures.Add($"extended gz {gz:R} dt {dt:R} at {t:R}s: unsupported mode crossing: {crossing}.");
                        fatal = true;
                    }

                    foreach (var violation in result.StateViolations)
                    {
                        failures.Add($"extended gz {gz:R} dt {dt:R} at {t:R}s: invalid stage: {violation}.");
                        fatal = true;
                    }

                    if (fatal)
                    {
                        pathAlive[candidateIndex] = false;
                        rows.AppendLine(CultureInfo.InvariantCulture,
                            $"first-failure gz={gz:R} dt={dt:R} t={t:R} state head={state.BloodHead:R} " +
                            $"lower={state.BloodLower:R} hr={state.HeartRateMultiplier:R} " +
                            $"cardio={state.CardioFatigue:R} pressure={state.CerebralPressureImpairment:R}");
                        for (var stage = 0; stage < result.Stages.Length; stage++)
                            rows.AppendLine(CultureInfo.InvariantCulture,
                                $"first-failure stage {stage + 1}: head={result.Stages[stage].BloodHead:R} " +
                                $"core={result.Stages[stage].BloodCore:R} lower={result.Stages[stage].BloodLower:R} " +
                                $"hr={result.Stages[stage].HeartRateMultiplier:R} cardio={result.Stages[stage].CardioFatigue:R} " +
                                $"pressure={result.Stages[stage].CerebralPressureImpairment:R}");
                        break;
                    }

                    state = result.Final;
                }

                if (pathAlive[candidateIndex])
                {
                    reachedSecond = second;
                    checkpoints[candidateIndex][second - 1] = EndpointChannels(state);
                    CheckEndpointInvariants(checkpoints[candidateIndex][second - 1], gz, dt, failures);
                }
            }

            rows.AppendLine(CultureInfo.InvariantCulture,
                $"extended path gz={gz:R} dt={dt:R} alive={pathAlive[candidateIndex]} " +
                $"reachedSecond={reachedSecond} " +
                $"firstCoreEntry={firstCoreEntry[candidateIndex]:R}");
        }

        if (gz == -5.0)
        {
            var entryTimes = firstCoreEntry.Where(t => !double.IsNaN(t)).ToArray();
            if (entryTimes.Length != CandidateDt.Length)
            {
                failures.Add($"extended gz {gz:R}: {entryTimes.Length}/{CandidateDt.Length} " +
                             "paths recorded a core-bound entry.");
            }
            else
            {
                var entrySpread = entryTimes.Max() - entryTimes.Min();
                if (entrySpread > 0.05)
                    failures.Add($"extended gz {gz:R}: core-bound entry spread {entrySpread:R} " +
                                 $"exceeds 0.05s ({string.Join(", ", entryTimes.Select(t => t.ToString("R", CultureInfo.InvariantCulture)))}).");
            }
        }
        else if (firstCoreEntry.Any(t => !double.IsNaN(t)))
        {
            failures.Add($"extended gz {gz:R}: unexpected core-bound entry at " +
                         string.Join(", ", firstCoreEntry.Where(t => !double.IsNaN(t))
                             .Select(t => t.ToString("R", CultureInfo.InvariantCulture))));
        }

        for (var second = 0; second < seconds; second++)
        {
            var reached = new List<double[]>();
            for (var candidateIndex = 0; candidateIndex < CandidateDt.Length; candidateIndex++)
                if (checkpoints[candidateIndex][second] is { } endpoint)
                    reached.Add(endpoint);

            if (reached.Count < CandidateDt.Length)
            {
                failures.Add($"extended gz {gz:R} second {second + 1}: only " +
                             $"{reached.Count}/{CandidateDt.Length} paths reached the checkpoint validly.");
                continue;
            }

            for (var channel = 0; channel < ChannelNames.Length; channel++)
            {
                var minimum = reached.Min(endpoint => endpoint[channel]);
                var maximum = reached.Max(endpoint => endpoint[channel]);
                if (maximum - minimum > StateSpreadTolerance)
                {
                    var failure = $"extended gz {gz:R} {ChannelNames[channel]} at {second + 1}s: " +
                                  $"min {minimum:R}, max {maximum:R}, spread {maximum - minimum:R} " +
                                  $"exceeds {StateSpreadTolerance:R}.";
                    failures.Add(failure);
                }
            }
        }

        foreach (var row in rows.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries))
            _output.WriteLine(row);

        Assert.True(failures.Count == 0,
            $"{failures.Count} extended-gate breach(es) at gz {gz:R}:{Environment.NewLine}" +
            string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public void ConstrainedCirculationMatchesIndependentHeldForm()
    {
        var settings = LogicSettings.Default;
        var head = 0.211714;
        var state = new PhysiologicalModel.IntegrationState(head, 1.0 - head, 0.833, 0.0,
            0.78, 0.0);
        var held = new double[PhysiologicalModel.CirculationDimensions];
        PhysiologicalModel.EvaluateCoreConstrainedCirculation(in state, 0.0, 0.0, -5.0,
            settings, held, null);
        var raw = new double[PhysiologicalModel.CirculationDimensions];
        PhysiologicalModel.EvaluateCirculation(in state, 0.0, 0.0, -5.0, settings, raw, null);

        var rawCoreRate = -(raw[0] + raw[1]);
        var headFree = head - settings.MinHeadBloodFraction;
        var share = headFree / (headFree + state.BloodLower);
        var expectedHead = raw[0] + rawCoreRate * share;
        Assert.True(Math.Abs(held[0] - expectedHead) <= 1e-12,
            $"held head rate {held[0]:R} vs expected {expectedHead:R}.");
        Assert.True(Math.Abs(held[1] + expectedHead) <= 1e-12,
            $"held lower rate {held[1]:R} vs expected {-expectedHead:R}.");
        Assert.Equal(0.0, held[0] + held[1]);
        Assert.Equal(raw[2], held[2]);
        Assert.Equal(raw[3], held[3]);
        Assert.Equal(raw[4], held[4]);
    }

    [Fact]
    public void ConstrainedJacobianMatchesFiniteDifferences()
    {
        var settings = LogicSettings.Default;
        var head = 0.211714;
        var state = new PhysiologicalModel.IntegrationState(head, 1.0 - head, 0.833, 0.0,
            0.78, 0.0);
        var rates = new double[PhysiologicalModel.CirculationDimensions];
        var jacobian = NumericalMath.CreateMatrix(PhysiologicalModel.CirculationDimensions);
        PhysiologicalModel.EvaluateCoreConstrainedCirculation(in state, 0.0, 0.0, -5.0,
            settings, rates, jacobian);

        var failures = new List<string>();
        for (var variable = 0; variable < PhysiologicalModel.CirculationDimensions; variable++)
        {
            var plus = PerturbedHeldRates(state, variable, JacobianPerturbation, settings);
            var minus = PerturbedHeldRates(state, variable, -JacobianPerturbation, settings);
            for (var row = 0; row < PhysiologicalModel.CirculationDimensions; row++)
            {
                var finiteDifference = (plus[row] - minus[row]) / (2.0 * JacobianPerturbation);
                var difference = Math.Abs(finiteDifference - jacobian[row][variable]);
                if (difference > JacobianTolerance)
                    failures.Add($"held J[{row},{variable}] analytic {jacobian[row][variable]:R} vs " +
                                 $"finite difference {finiteDifference:R}, diff {difference:R}.");
            }
        }

        Assert.True(failures.Count == 0,
            $"held Jacobian mismatches:{Environment.NewLine}{string.Join(Environment.NewLine, failures)}");
    }

    private static double[] PerturbedHeldRates(
        PhysiologicalModel.IntegrationState state, int variable, double perturbation,
        LogicSettings settings)
    {
        var values = new[]
        {
            state.BloodHead, state.BloodLower, state.HeartRateMultiplier,
            state.CardioFatigue, state.CerebralPressureImpairment
        };
        values[variable] += perturbation;
        var perturbed = state with
        {
            BloodHead = values[0],
            BloodLower = values[1],
            HeartRateMultiplier = values[2],
            CardioFatigue = values[3],
            CerebralPressureImpairment = values[4]
        };
        var rates = new double[PhysiologicalModel.CirculationDimensions];
        PhysiologicalModel.EvaluateCoreConstrainedCirculation(in perturbed, 0.0, 0.0, -5.0,
            settings, rates, null);
        return rates;
    }

    [Fact]
    public void HeldIntervalKeepsCoreExactlyAtZero()
    {
        var head = 0.21;
        var state = new PhysiologicalModel.IntegrationState(head, 1.0 - head, 0.83, 0.0,
            0.78, 0.0);
        var model = new GEffectsLogicInstance(new DtStabilityLogger()).PhysModel;
        var result = model.AdvanceCirculationInterval(in state, 1.0, 0.0, 0.0, -5.0, LogicSettings.Default);
        _output.WriteLine(
            $"held converged={result.Converged} iterations={result.Iterations} " +
            $"residual={result.MaxScaledResidual:R} segments={result.Segments.Length} " +
            $"coreEntries=[{string.Join(",", result.CoreBoundEntries.Select(e => e.ToString("R", CultureInfo.InvariantCulture)))}] " +
            $"coreReleases=[{string.Join(",", result.CoreBoundReleases.Select(e => e.ToString("R", CultureInfo.InvariantCulture)))}] " +
            $"final head={result.Final.BloodHead:R} core={result.Final.BloodCore:R} " +
            $"lower={result.Final.BloodLower:R} hr={result.Final.HeartRateMultiplier:R} " +
            $"pressure={result.Final.CerebralPressureImpairment:R}");
        Assert.True(result.Converged);
        Assert.Empty(result.ModeCrossings);
        Assert.Empty(result.StateViolations);
        Assert.Empty(result.CoreBoundEntries);
        Assert.Empty(result.CoreBoundReleases);
        Assert.All(result.Segments, segment => Assert.True(segment.CoreBound));
        AssertAcceptedCoverage(result, 1.0);
        foreach (var stage in result.Stages)
        {
            Assert.Equal(0.0, stage.BloodCore);
            Assert.True(stage.BloodHead >= LogicSettings.Default.MinHeadBloodFraction,
                $"held stage head {stage.BloodHead:R} below floor.");
            Assert.True(Math.Abs(stage.BloodHead + stage.BloodCore + stage.BloodLower - 1.0) <=
                        BloodSumTolerance, "held stage blood not conserved.");
        }

        Assert.Equal(0.0, result.Final.BloodCore);
        Assert.True(Math.Abs(result.Final.BloodHead + result.Final.BloodCore +
                             result.Final.BloodLower - 1.0) <= BloodSumTolerance);
    }

    [Fact]
    public void NegativeFiveCoreBoundEntryCorrectionSurvivesAdjacentTenMillisecondCalls()
    {
        var head = 0.21146608737125144;
        var reportedLower = 0.7885339126287486;
        var lower = BitConverter.Int64BitsToDouble(
            BitConverter.DoubleToInt64Bits(reportedLower) - 2L);
        var state = new PhysiologicalModel.IntegrationState(head, lower,
            0.8329045360148475, 0.0, 0.0, 0.0);
        Assert.True(state.BloodCore is > 0.0 and <= 1e-12,
            $"pre-entry core {state.BloodCore:R} is not a positive roundoff-scale reserve.");

        var model = new GEffectsLogicInstance(new DtStabilityLogger()).PhysModel;
        var first = model.AdvanceCirculationInterval(in state, 0.01, 0.0, 0.0, -5.0,
            LogicSettings.Default);
        Assert.True(first.Converged,
            $"first -5Gz bound interval rejected: " +
            $"crossings [{string.Join("; ", first.ModeCrossings)}] " +
            $"violations [{string.Join("; ", first.StateViolations)}].");
        Assert.Empty(first.StateViolations);
        Assert.Empty(first.ModeCrossings);
        Assert.Single(first.CoreBoundEntries);
        Assert.Empty(first.CoreBoundReleases);
        Assert.Single(first.Events, item => item.Kind == "CoreEntry");
        AssertAcceptedCoverage(first, 0.01);
        AssertPhysicalStages(first);
        Assert.Contains(first.Segments, segment => segment.CoreBound);
        Assert.Equal(0.0, first.Final.BloodCore);

        var firstFinal = first.Final;
        var second = model.AdvanceCirculationInterval(in firstFinal, 0.01, 0.0, 0.0, -5.0,
            LogicSettings.Default);
        Assert.True(second.Converged,
            $"adjacent -5Gz bound interval rejected: " +
            $"crossings [{string.Join("; ", second.ModeCrossings)}] " +
            $"violations [{string.Join("; ", second.StateViolations)}].");
        Assert.Empty(second.StateViolations);
        Assert.Empty(second.ModeCrossings);
        Assert.Empty(second.CoreBoundEntries);
        Assert.Empty(second.CoreBoundReleases);
        AssertAcceptedCoverage(second, 0.01);
        AssertPhysicalStages(second);
        Assert.All(second.Segments, segment => Assert.True(segment.CoreBound));
        Assert.Equal(0.0, second.Final.BloodCore);
    }

    [Fact]
    public void NegativeFiveCoreBoundStateSurvivesAdjacentTenMillisecondCalls()
    {
        var head = 0.21146608737125144;
        var reportedLower = 0.7885339126287486;
        var lower = BitConverter.Int64BitsToDouble(
            BitConverter.DoubleToInt64Bits(reportedLower) - 1L);
        var state = new PhysiologicalModel.IntegrationState(head, lower,
            0.8329045360148475, 0.0, 0.0, 0.0);
        Assert.Equal(0.0, state.BloodCore);

        var model = new GEffectsLogicInstance(new DtStabilityLogger()).PhysModel;
        var first = model.AdvanceCirculationInterval(in state, 0.01, 0.0, 0.0, -5.0,
            LogicSettings.Default);
        Assert.True(first.Converged,
            $"first held-core update rejected: " +
            $"crossings [{string.Join("; ", first.ModeCrossings)}] " +
            $"violations [{string.Join("; ", first.StateViolations)}].");
        Assert.Empty(first.StateViolations);
        AssertAcceptedCoverage(first, 0.01);
        AssertPhysicalStages(first);
        Assert.All(first.Segments, segment => Assert.True(segment.CoreBound));
        Assert.Empty(first.CoreBoundEntries);
        Assert.Empty(first.CoreBoundReleases);
        Assert.Equal(0.0, first.Final.BloodCore);

        var firstFinal = first.Final;
        var second = model.AdvanceCirculationInterval(in firstFinal, 0.01, 0.0, 0.0, -5.0,
            LogicSettings.Default);
        Assert.True(second.Converged,
            $"adjacent held-core update rejected: " +
            $"crossings [{string.Join("; ", second.ModeCrossings)}] " +
            $"violations [{string.Join("; ", second.StateViolations)}].");
        Assert.Empty(second.StateViolations);
        AssertAcceptedCoverage(second, 0.01);
        AssertPhysicalStages(second);
        Assert.All(second.Segments, segment => Assert.True(segment.CoreBound));
        Assert.Empty(second.CoreBoundEntries);
        Assert.Empty(second.CoreBoundReleases);
        Assert.Equal(0.0, second.Final.BloodCore);
    }

    [Fact]
    public void CoreBoundReleasesAtZeroUnderUnload()
    {
        var head = 0.21;
        var state = new PhysiologicalModel.IntegrationState(head, 1.0 - head, 0.83, 0.0,
            0.78, 0.0);
        var model = new GEffectsLogicInstance(new DtStabilityLogger()).PhysModel;
        var result = model.AdvanceCirculationInterval(in state, 1.0, 0.0, 0.0, 1.0, LogicSettings.Default);
        _output.WriteLine(
            $"unload converged={result.Converged} segments={result.Segments.Length} " +
            $"coreReleases=[{string.Join(",", result.CoreBoundReleases.Select(e => e.ToString("R", CultureInfo.InvariantCulture)))}] " +
            $"final core={result.Final.BloodCore:R} head={result.Final.BloodHead:R} " +
            $"lower={result.Final.BloodLower:R}");
        Assert.True(result.Converged);
        Assert.Empty(result.StateViolations);
        AssertAcceptedCoverage(result, 1.0);
        var release = Assert.Single(result.CoreBoundReleases);
        Assert.Equal(0.0, release);
        Assert.True(result.Final.BloodCore > 0.0,
            $"final core {result.Final.BloodCore:R} did not leave the boundary.");
        foreach (var stage in result.Stages)
            Assert.True(stage.BloodCore >= 0.0 && stage.BloodHead >= 0.0 &&
                        stage.BloodLower >= 0.0, "unload stage has negative volume.");
    }

    [Fact]
    public void HeldSegmentReleasesWhenDriveTurnsInward()
    {
        var settings = LogicSettings.Default;
        var gzNetScaled = Math.Sign(-5.0) *
            Math.Pow(5.0, settings.HydrostaticShiftExponent) - 1.0;
        var criticalRate = -settings.HydrostaticShiftRate * gzNetScaled /
                           (settings.PassiveReturnRate * settings.RestingBloodCore);
        var head = 0.21;
        var state = new PhysiologicalModel.IntegrationState(head, 1.0 - head,
            criticalRate - 1.5e-4, 0.6, 0.2, 0.0);
        var model = new GEffectsLogicInstance(new DtStabilityLogger()).PhysModel;
        var result = model.AdvanceCirculationInterval(in state, 1.0, 0.0, 0.0, -5.0, settings);

        var reference = double.NaN;
        var fineModel = new GEffectsLogicInstance(new DtStabilityLogger()).PhysModel;
        var fineState = state;
        var elapsed = 0.0;
        for (var call = 0; call < 100; call++)
        {
            var fine = fineModel.AdvanceCirculationInterval(in fineState, 0.01, 0.0, 0.0, -5.0, settings);
            Assert.True(fine.Converged);
            Assert.Empty(fine.StateViolations);
            Assert.Empty(fine.ModeCrossings);
            if (double.IsNaN(reference) && fine.CoreBoundReleases.Length > 0)
                reference = elapsed + fine.CoreBoundReleases[0];
            fineState = fine.Final;
            elapsed += 0.01;
        }

        _output.WriteLine(
            $"criticalRate={criticalRate:R} converged={result.Converged} " +
            $"segments={result.Segments.Length} " +
            $"coreReleases=[{string.Join(",", result.CoreBoundReleases.Select(e => e.ToString("R", CultureInfo.InvariantCulture)))}] " +
            $"reference={reference:R} final core={result.Final.BloodCore:R} " +
            $"hr={result.Final.HeartRateMultiplier:R}");
        Assert.True(result.Converged);
        Assert.Empty(result.StateViolations);
        Assert.Empty(result.ModeCrossings);
        AssertAcceptedCoverage(result, 1.0);
        var release = Assert.Single(result.CoreBoundReleases);
        Assert.True(release is > 0.0 and < 1.0,
            $"release {release:R} not inside the interval.");
        Assert.Contains(result.Segments, segment => !segment.CoreBound);
        Assert.False(double.IsNaN(reference), "fine .01 path produced no release.");
        Assert.True(Math.Abs(release - reference) <= 0.05,
            $"release {release:R} vs fine reference {reference:R}.");
    }

    [Fact]
    public void FreeIntervalLocalizesCoreEntry()
    {
        var settings = LogicSettings.Default;
        var head = 0.211714;
        var state = new PhysiologicalModel.IntegrationState(head, 1.0 - head - 0.001, 0.833,
            0.0, 0.78, 0.0);
        var model = new GEffectsLogicInstance(new DtStabilityLogger()).PhysModel;
        var result = model.AdvanceCirculationInterval(in state, 1.0, 0.0, 0.0, -5.0, settings);

        var reference = double.NaN;
        var fineModel = new GEffectsLogicInstance(new DtStabilityLogger()).PhysModel;
        var fineState = state;
        var elapsed = 0.0;
        var fineCoreEntries = 0;
        var coreHeld = false;
        for (var call = 0; call < 100; call++)
        {
            var fine = fineModel.AdvanceCirculationInterval(in fineState, 0.01, 0.0, 0.0, -5.0, settings);
            Assert.True(fine.Converged);
            Assert.Empty(fine.StateViolations);
            Assert.Empty(fine.ModeCrossings);
            AssertAcceptedCoverage(fine, 0.01);
            AssertPhysicalStages(fine);
            if (coreHeld)
            {
                Assert.All(fine.Segments, segment => Assert.True(segment.CoreBound));
                Assert.Empty(fine.CoreBoundEntries);
                Assert.Empty(fine.CoreBoundReleases);
            }
            fineCoreEntries += fine.CoreBoundEntries.Length;
            if (double.IsNaN(reference) && fine.CoreBoundEntries.Length > 0)
            {
                reference = elapsed + fine.CoreBoundEntries[0];
                Assert.Equal(0.0, fine.Final.BloodCore);
                coreHeld = true;
            }
            fineState = fine.Final;
            elapsed += 0.01;
        }

        Assert.Equal(1, fineCoreEntries);
        Assert.Equal(0.0, fineState.BloodCore);
        _output.WriteLine(
            $"entry converged={result.Converged} segments={result.Segments.Length} " +
            $"coreEntries=[{string.Join(",", result.CoreBoundEntries.Select(e => e.ToString("R", CultureInfo.InvariantCulture)))}] " +
            $"reference={reference:R} final core={result.Final.BloodCore:R} " +
            $"head={result.Final.BloodHead:R} lower={result.Final.BloodLower:R} " +
            $"pressure={result.Final.CerebralPressureImpairment:R}");
        Assert.True(result.Converged);
        Assert.Empty(result.StateViolations);
        Assert.Empty(result.ModeCrossings);
        AssertAcceptedCoverage(result, 1.0);
        var entry = Assert.Single(result.CoreBoundEntries);
        Assert.True(entry is > 0.0 and < 1.0,
            $"core entry {entry:R} not inside the interval.");
        var entrySegment = Assert.Single(result.Segments,
            segment => Math.Abs(segment.StartOffset + segment.Duration - entry) <= 1e-12);
        var coefficientOffset = entrySegment.StartOffset + entrySegment.Duration -
                                entrySegment.CoefficientStart;
        var rawHead = NumericalMath.EvaluateCubic(
            entrySegment.HeadCoefficients, coefficientOffset);
        var rawLower = NumericalMath.EvaluateCubic(
            entrySegment.LowerCoefficients, coefficientOffset);
        var rawCore = 1.0 - rawHead - rawLower;
        var headSlope = entrySegment.HeadCoefficients[1] +
                        2.0 * entrySegment.HeadCoefficients[2] * coefficientOffset +
                        3.0 * entrySegment.HeadCoefficients[3] *
                        coefficientOffset * coefficientOffset;
        var lowerSlope = entrySegment.LowerCoefficients[1] +
                         2.0 * entrySegment.LowerCoefficients[2] * coefficientOffset +
                         3.0 * entrySegment.LowerCoefficients[3] *
                         coefficientOffset * coefficientOffset;
        var allowedCorrection =
            Math.Abs(-headSlope - lowerSlope) *
            PhysiologicalModel.PressureBoundRootToleranceSeconds + 1e-12;
        var maximumCorrection = Math.Max(
            Math.Abs(entrySegment.Final.BloodHead - rawHead),
            Math.Max(Math.Abs(entrySegment.Final.BloodLower - rawLower),
                Math.Abs(entrySegment.Final.BloodCore - rawCore)));
        Assert.True(maximumCorrection <= allowedCorrection,
            $"entry correction {maximumCorrection:R} vs allowed {allowedCorrection:R}; " +
            $"raw core {rawCore:R}, slope {-headSlope - lowerSlope:R}.");
        Assert.Equal(0.0, entrySegment.Final.BloodCore);
        Assert.Single(result.Events, item => item.Kind == "CoreEntry");
        AssertPhysicalStages(result);
        Assert.Equal(0.0, result.Final.BloodCore);
        Assert.False(double.IsNaN(reference), "fine .01 path produced no entry.");
        Assert.True(Math.Abs(entry - reference) <= 0.05,
            $"entry {entry:R} vs fine reference {reference:R}.");
        foreach (var stage in result.Stages)
        {
            Assert.True(Math.Abs(stage.BloodHead + stage.BloodCore + stage.BloodLower - 1.0) <=
                        BloodSumTolerance, "entry stage blood not conserved.");
            Assert.InRange(stage.CerebralPressureImpairment, 0.0, 1.0);
        }
    }

    [Fact]
    public void NegativeInitialCoreIsRejected()
    {
        var state = new PhysiologicalModel.IntegrationState(0.21, 0.8, 0.83, 0.0, 0.78, 0.0);
        var model = new GEffectsLogicInstance(new DtStabilityLogger()).PhysModel;
        var result = model.AdvanceCirculationInterval(in state, 1.0, 0.0, 0.0, -5.0,
            LogicSettings.Default);
        _output.WriteLine(
            $"negative-core converged={result.Converged} " +
            $"violations=[{string.Join("; ", result.StateViolations)}] " +
            $"segments={result.Segments.Length} stages={result.Stages.Length}");
        Assert.False(result.Converged);
        Assert.NotEmpty(result.StateViolations);
        Assert.Empty(result.Segments);
        Assert.Empty(result.Stages);
        Assert.Empty(result.CoreBoundEntries);
        Assert.Empty(result.CoreBoundReleases);
    }

    private static void AssertPhysicalStages(PhysiologicalModel.IntegrationResult result)
    {
        Assert.All(result.Segments, segment => Assert.True(segment.Duration > 0.0,
            $"zero-duration segment at offset {segment.StartOffset:R}."));
        foreach (var stage in result.Stages.Concat(new[] { result.Final }))
        {
            Assert.True(stage.BloodHead >= 0.0 && stage.BloodCore >= 0.0 &&
                        stage.BloodLower >= 0.0,
                $"negative blood volume h={stage.BloodHead:R} c={stage.BloodCore:R} " +
                $"l={stage.BloodLower:R}.");
            Assert.True(Math.Abs(stage.BloodHead + stage.BloodCore + stage.BloodLower -
                                 1.0) <= BloodSumTolerance, "blood not conserved.");
            Assert.True(stage.CerebralPressureImpairment is >= 0.0 and <= 1.0,
                $"pressure {stage.CerebralPressureImpairment:R} outside [0,1].");
            Assert.True(stage.RespiratoryFatigue is >= 0.0 and <= 1.0,
                $"respiratory fatigue {stage.RespiratoryFatigue:R} outside [0,1].");
            Assert.True(stage.StrainingLevel is >= 0.0 and <= 1.0,
                $"straining level {stage.StrainingLevel:R} outside [0,1].");
            Assert.True(stage.StrainingFatigue is >= 0.0 and <= 1.0,
                $"straining fatigue {stage.StrainingFatigue:R} outside [0,1].");
            Assert.True(stage.GSuitFatigue is >= 0.0 and <= 1.0,
                $"g-suit fatigue {stage.GSuitFatigue:R} outside [0,1].");
        }
    }

    private static void AssertAcceptedCoverage(PhysiologicalModel.IntegrationResult result,
        double dt)
    {
        Assert.True(result.Segments.Length != 0,
            $"no accepted segments; converged={result.Converged} " +
            $"residual={result.MaxScaledResidual:R} " +
            $"crossings=[{string.Join("; ", result.ModeCrossings)}] " +
            $"violations=[{string.Join("; ", result.StateViolations)}]");
        var position = 0.0;
        foreach (var segment in result.Segments)
        {
            Assert.True(Math.Abs(segment.StartOffset - position) <= 1e-12,
                $"segment starts at {segment.StartOffset:R}, expected contiguous {position:R}.");
            position += segment.Duration;
        }

        Assert.True(Math.Abs(position - dt) <= 1e-12,
            $"accepted duration {position:R} does not cover caller dt {dt:R}; " +
            $"converged={result.Converged} residual={result.MaxScaledResidual:R} " +
            $"crossings=[{string.Join("; ", result.ModeCrossings)}] " +
            $"violations=[{string.Join("; ", result.StateViolations)}] " +
            $"transitions=[{string.Join("; ", result.ModeTransitions)}]");
        foreach (var offset in result.PressureBoundEntries.Concat(result.PressureBoundReleases)
                     .Concat(result.CoreBoundEntries).Concat(result.CoreBoundReleases)
                     .Concat(result.HeadBoundEntries).Concat(result.HeadBoundReleases)
                     .Concat(result.LowerBoundEntries).Concat(result.LowerBoundReleases)
                     .Concat(result.ModeTransitions))
            Assert.True(offset >= 0.0 && offset <= dt,
                $"event offset {offset:R} outside [0,{dt:R}].");
    }

    public static IEnumerable<object[]> BroadSentinelVectors => new object[][]
    {
        [0.0, 0.0, -1.0, false],
        [0.0, 0.0, -4.0, false],
        [0.0, 0.0, -6.0, false],
        [0.0, 0.0, -8.0, false],
        [2.0, 1.0, -5.0, false],
        [0.0, 0.0, -3.0, true]
    };

    [Theory]
    [MemberData(nameof(BroadSentinelVectors))]
    public void BroadFirstSecondCirculationGate(double gx, double gy, double gz,
        bool customResp) =>
        AssertBroadFirstSecondCirculationGate(gx, gy, gz, customResp);

    private void AssertBroadFirstSecondCirculationGate(double gx, double gy, double gz,
        bool customResp)
    {
        var settings = customResp
            ? LogicSettings.Default with { RespiratoryFatigueHrFloor = 0.8 }
            : LogicSettings.Default;
        var state = customResp
            ? new PhysiologicalModel.IntegrationState(0.21, 0.45, 0.85, 0.1, 0.3, 0.6)
            : new GEffectsLogicInstance(new DtStabilityLogger()).PhysModel.CaptureIntervalState();
        var failures = new List<string>();
        var rows = new StringBuilder();
        var endpoints = new double[CandidateDt.Length][];
        var pathAccepted = new bool[CandidateDt.Length];

        for (var candidateIndex = 0; candidateIndex < CandidateDt.Length; candidateIndex++)
            endpoints[candidateIndex] = AdvancePath(
                new GEffectsLogicInstance(new DtStabilityLogger()).PhysModel, state, settings,
                gz, CandidateDt[candidateIndex], rows, failures,
                out pathAccepted[candidateIndex], out _, gx, gy);

        var referenceEndpoint = AdvancePath(
            new GEffectsLogicInstance(new DtStabilityLogger()).PhysModel, state, settings, gz,
            ReferenceDt, rows, failures, out _, out _, gx, gy);
        for (var channel = 0; channel < ChannelNames.Length; channel++)
        {
            var difference = Math.Abs(endpoints[0][channel] - referenceEndpoint[channel]);
            if (difference > ReferenceSpreadTolerance)
                failures.Add($"gx {gx:R} gy {gy:R} gz {gz:R} resp {customResp} " +
                             $"{ChannelNames[channel]}: dt 0.01 vs 0.02 reference difference " +
                             $"{difference:R} exceeds {ReferenceSpreadTolerance:R}; " +
                             "0.01 is not a converged reference.");
        }

        for (var channel = 0; channel < ChannelNames.Length; channel++)
        {
            var values = endpoints.Select(endpoint => endpoint[channel]).ToArray();
            var minimum = values.Min();
            var maximum = values.Max();
            if (maximum - minimum > StateSpreadTolerance)
            {
                var minIndex = Array.IndexOf(values, minimum);
                var maxIndex = Array.IndexOf(values, maximum);
                var failure = $"gx {gx:R} gy {gy:R} gz {gz:R} resp {customResp} " +
                              $"{ChannelNames[channel]} at 1s: min {minimum:R} " +
                              $"(dt {CandidateDt[minIndex]:R}), max {maximum:R} " +
                              $"(dt {CandidateDt[maxIndex]:R}), spread {maximum - minimum:R} " +
                              $"exceeds {StateSpreadTolerance:R}; " +
                              $"minPathAccepted={pathAccepted[minIndex]}, " +
                              $"maxPathAccepted={pathAccepted[maxIndex]}.";
                failures.Add(failure);
            }
        }

        for (var candidateIndex = 0; candidateIndex < CandidateDt.Length; candidateIndex++)
            CheckEndpointInvariants(endpoints[candidateIndex], gz, CandidateDt[candidateIndex],
                failures);

        foreach (var row in rows.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries))
            _output.WriteLine(row);

        Assert.True(failures.Count == 0,
            $"{failures.Count} broad-gate breach(es) at gx {gx:R} gy {gy:R} gz {gz:R} " +
            $"resp {customResp}:{Environment.NewLine}" +
            string.Join(Environment.NewLine, failures));
    }

    // Issue #22 strict twins for the formerly deferred fast-transient loads;
    // they keep the same assertions executable in the Experimental step.
    [Theory]
    [Trait("Category", "Experimental")]
    [InlineData(0.0, 0.0, -6.0, false)]
    [InlineData(2.0, 1.0, -5.0, false)]
    [InlineData(0.0, 0.0, -8.0, false)]
    [InlineData(0.0, 0.0, -3.0, false)]
    [InlineData(0.0, 0.0, -4.0, false)]
    [InlineData(0.0, 0.0, -5.0, false)]
    [InlineData(0.0, 0.0, -3.0, true)]
    public void DeferredBroadFirstSecondAccuracy(double gx, double gy, double gz,
        bool customResp) =>
        AssertBroadFirstSecondCirculationGate(gx, gy, gz, customResp);

    [Theory]
    [InlineData(1.5e-4)]
    // Releases at ~0.025s and ~0.042s under -5 Gz sit inside the first ~7% of a
    // one-second span, which the collocation-node release scan cannot see.
    [InlineData(2e-4)]
    [InlineData(3e-4)]
    [InlineData(1e-6)]
    [InlineData(1e-12)]
    public void HeldReleaseLocalizationAcrossCallerDt(double rateOffset)
    {
        var settings = LogicSettings.Default;
        var gzNetScaled = Math.Sign(-5.0) *
            Math.Pow(5.0, settings.HydrostaticShiftExponent) - 1.0;
        var criticalRate = -settings.HydrostaticShiftRate * gzNetScaled /
                           (settings.PassiveReturnRate * settings.RestingBloodCore);
        var head = 0.21;
        var initial = new PhysiologicalModel.IntegrationState(head, 1.0 - head,
            criticalRate - rateOffset, 0.6, 0.2, 0.0);

        var fineModel = new GEffectsLogicInstance(new DtStabilityLogger()).PhysModel;
        var fineState = initial;
        var fineRelease = double.NaN;
        var elapsed = 0.0;
        for (var call = 0; call < 100; call++)
        {
            var fine = fineModel.AdvanceCirculationInterval(in fineState, 0.01, 0.0, 0.0, -5.0,
                settings);
            Assert.True(fine.Converged);
            Assert.Empty(fine.StateViolations);
            Assert.Empty(fine.ModeCrossings);
            AssertAcceptedCoverage(fine, 0.01);
            AssertPhysicalStages(fine);
            if (double.IsNaN(fineRelease) && fine.CoreBoundReleases.Length > 0)
                fineRelease = elapsed + fine.CoreBoundReleases[0];
            fineState = fine.Final;
            elapsed += 0.01;
        }

        Assert.False(double.IsNaN(fineRelease), "fine .01 path produced no release.");
        Assert.True(fineRelease is > 0.0 and < 1.0,
            $"fine release {fineRelease:R} outside (0,1).");

        var endpoints = new List<double[]>();
        foreach (var dt in new[] { 1.0, 0.01, 0.1, 0.25, 0.5 })
        {
            var model = new GEffectsLogicInstance(new DtStabilityLogger()).PhysModel;
            var state = initial;
            var t = 0.0;
            var pathRelease = double.NaN;
            var releaseCount = 0;
            while (t < 1.0 - 1e-12)
            {
                var step = Math.Min(dt, 1.0 - t);
                var result = model.AdvanceCirculationInterval(in state, step, 0.0, 0.0, -5.0,
                    settings);
                Assert.True(result.Converged,
                    $"dt {dt:R} at t {t + step:R}: not converged, residual " +
                    $"{result.MaxScaledResidual:R}.");
                Assert.Empty(result.StateViolations);
                Assert.Empty(result.ModeCrossings);
                AssertAcceptedCoverage(result, step);
                AssertPhysicalStages(result);
                if (t == 0.0)
                    Assert.True(result.Segments[0].CoreBound,
                        $"dt {dt:R} first segment after negative-drive start is not " +
                        "core-bound.");
                releaseCount += result.CoreBoundReleases.Length;
                if (double.IsNaN(pathRelease) && result.CoreBoundReleases.Length > 0)
                    pathRelease = t + result.CoreBoundReleases[0];
                state = result.Final;
                t += step;
            }

            Assert.Equal(1, releaseCount);
            Assert.True(pathRelease > 0.0,
                $"dt {dt:R} release {pathRelease:R} not strictly positive.");
            Assert.True(Math.Abs(pathRelease - fineRelease) <= 0.05,
                $"dt {dt:R} release {pathRelease:R} vs fine {fineRelease:R}.");
            var endpoint = EndpointChannels(state);
            Assert.True(endpoint[6] is >= 0.0 and <= 1.0,
                $"dt {dt:R} final pressure {endpoint[6]:R} outside [0,1].");
            endpoints.Add(endpoint);
            _output.WriteLine(
                $"offset={rateOffset:R} dt={dt:R} release={pathRelease:R} " +
                $"head={endpoint[0]:R} core={endpoint[1]:R} lower={endpoint[2]:R} " +
                $"overfill={endpoint[3]:R} hr={endpoint[4]:R} cardio={endpoint[5]:R} " +
                $"pressure={endpoint[6]:R}");
        }

        for (var channel = 0; channel < ChannelNames.Length; channel++)
        {
            var values = endpoints.Select(endpoint => endpoint[channel]).ToArray();
            Assert.True(values.Max() - values.Min() <= StateSpreadTolerance,
                $"offset {rateOffset:R} {ChannelNames[channel]} endpoint spread " +
                $"{values.Max() - values.Min():R} exceeds {StateSpreadTolerance:R}.");
        }

        var duration = Math.Min(1.0, fineRelease + 0.002);
        var nearEndModel = new GEffectsLogicInstance(new DtStabilityLogger()).PhysModel;
        var nearEnd = nearEndModel.AdvanceCirculationInterval(in initial, duration, 0.0, 0.0, -5.0,
            settings);
        Assert.True(nearEnd.Converged);
        Assert.Empty(nearEnd.StateViolations);
        Assert.Empty(nearEnd.ModeCrossings);
        AssertAcceptedCoverage(nearEnd, duration);
        AssertPhysicalStages(nearEnd);
        Assert.True(nearEnd.Segments[0].CoreBound,
            "near-end first segment after negative-drive start is not core-bound.");
        var nearEndRelease = Assert.Single(nearEnd.CoreBoundReleases);
        Assert.True(nearEndRelease > 0.0 && nearEndRelease <= duration,
            $"near-end release {nearEndRelease:R} outside duration {duration:R}.");

        var clipModel = new GEffectsLogicInstance(new DtStabilityLogger()).PhysModel;
        var clipState = initial;
        var clipTime = 0.0;
        while (clipTime < duration - 1e-12)
        {
            var step = Math.Min(0.01, duration - clipTime);
            var clip = clipModel.AdvanceCirculationInterval(in clipState, step, 0.0, 0.0, -5.0,
                settings);
            Assert.True(clip.Converged);
            Assert.Empty(clip.StateViolations);
            Assert.Empty(clip.ModeCrossings);
            AssertAcceptedCoverage(clip, step);
            AssertPhysicalStages(clip);
            clipState = clip.Final;
            clipTime += step;
        }

        var nearEndChannels = EndpointChannels(nearEnd.Final);
        var clipChannels = EndpointChannels(clipState);
        for (var channel = 0; channel < ChannelNames.Length; channel++)
            Assert.True(
                Math.Abs(nearEndChannels[channel] - clipChannels[channel]) <=
                StateSpreadTolerance,
                $"offset {rateOffset:R} near-end {ChannelNames[channel]} " +
                $"{nearEndChannels[channel]:R} vs clipped reference " +
                $"{clipChannels[channel]:R}.");
    }

    [ReleasePrototypeFact]
    [Trait("Category", "Performance")]
    public void PrototypeCirculationTimingScreen()
    {
        const int samples = 128;
        const int warmups = 256;
        const long noGcBytes = 256L * 1024 * 1024;
        var settings = LogicSettings.Default;
        var criticalRate = settings.HydrostaticShiftRate *
            (Math.Pow(5.0, settings.HydrostaticShiftExponent) + 1.0) /
            (settings.PassiveReturnRate * settings.RestingBloodCore);
        var states = new[]
        {
            new PhysiologicalModel.IntegrationState(0.2, 0.45, 1.0, 0.0, 0.0, 0.0),
            new PhysiologicalModel.IntegrationState(0.21, 0.79, 0.83, 0.0, 0.78, 0.0),
            new PhysiologicalModel.IntegrationState(0.211714, 1.0 - 0.211714 - 0.001,
                0.833, 0.0, 0.78, 0.0),
            new PhysiologicalModel.IntegrationState(0.21, 0.79, criticalRate - 0.001,
                0.6, 0.2, 0.0)
        };
        var names = new[] { "rested", "core-held", "core-entry", "core-release" };
        var model = new GEffectsLogicInstance(new DtStabilityLogger()).PhysModel;
        var maximumAcrossScenarios = 0.0;
        for (var scenario = 0; scenario < states.Length; scenario++)
        {
            var state = states[scenario];
            for (var warmup = 0; warmup < warmups; warmup++)
            {
                var warm = model.AdvanceCirculationInterval(in state, 1.0, 0.0, 0.0, -5.0, settings);
                Assert.True(warm.Converged);
                Assert.Empty(warm.StateViolations);
                Assert.Empty(warm.ModeCrossings);
            }

            var ticks = new long[samples];
            var results = new PhysiologicalModel.IntegrationResult[samples];
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            Assert.True(GC.TryStartNoGCRegion(noGcBytes),
                "Unable to reserve the no-GC region for the prototype timing screen.");
            try
            {
                for (var sample = 0; sample < samples; sample++)
                {
                    var started = System.Diagnostics.Stopwatch.GetTimestamp();
                    var result = model.AdvanceCirculationInterval(in state, 1.0, 0.0, 0.0, -5.0, settings);
                    ticks[sample] = System.Diagnostics.Stopwatch.GetTimestamp() - started;
                    results[sample] = result;
                }
            }
            finally
            {
                GC.EndNoGCRegion();
            }

            foreach (var result in results)
            {
                Assert.True(result.Converged);
                Assert.Empty(result.StateViolations);
                Assert.Empty(result.ModeCrossings);
            }

            var milliseconds = ticks.Select(value =>
                value * 1000.0 / System.Diagnostics.Stopwatch.Frequency).ToArray();
            var mean = milliseconds.Average();
            var maximum = milliseconds.Max();
            maximumAcrossScenarios = Math.Max(maximumAcrossScenarios, maximum);
            _output.WriteLine($"prototype circulation-only scenario={names[scenario]} " +
                $"samples={samples} meanMs={mean:R} maxMs={maximum:R}");
            _output.WriteLine("rawTicks=" + string.Join(",", ticks.Select(value =>
                value.ToString(CultureInfo.InvariantCulture))));
            _output.WriteLine("stopwatchFrequency=" +
                System.Diagnostics.Stopwatch.Frequency.ToString(CultureInfo.InvariantCulture));
        }

        Assert.True(maximumAcrossScenarios <= 0.5,
            $"Circulation-only maximum {maximumAcrossScenarios:R}ms exceeds complete-update every-frame 0.5ms budget.");
    }

    internal sealed class ReleasePrototypeFactAttribute : FactAttribute
    {
        public ReleasePrototypeFactAttribute()
        {
#if DEBUG
            Skip = "Prototype execution timing requires a Release build.";
#endif
        }
    }

    [DenseDtFact]
    [Trait("Category", "DtDense")]
    public void DenseCoreBoundaryGate()
    {
        var failures = new List<string>();
        var rows = new StringBuilder();
        var model = new GEffectsLogicInstance(new DtStabilityLogger()).PhysModel;
        var preconditioned = model.CaptureIntervalState();
        var preconditionSeconds = 0.0;
        for (var call = 1; call <= 1000 && preconditioned.BloodCore > 0.05; call++)
        {
            var result = model.AdvanceCirculationInterval(in preconditioned, 0.01, 0.0, 0.0, -5.0,
                LogicSettings.Default);
            if (!result.Converged)
                failures.Add($"boundary precondition call {call}/1000: nonconverged, residual " +
                             $"{result.MaxScaledResidual:R}.");
            foreach (var crossing in result.ModeCrossings)
                failures.Add($"boundary precondition call {call}/1000: crossing {crossing}.");
            foreach (var violation in result.StateViolations)
                failures.Add($"boundary precondition call {call}/1000: violation {violation}.");
            preconditioned = result.Final;
            preconditionSeconds += 0.01;
        }

        rows.AppendLine(CultureInfo.InvariantCulture,
            $"boundary preconditioned state at {preconditionSeconds:R}s gz=-5: " +
            $"head={preconditioned.BloodHead:R} " +
            $"core={preconditioned.BloodCore:R} lower={preconditioned.BloodLower:R} " +
            $"hr={preconditioned.HeartRateMultiplier:R} " +
            $"pressure={preconditioned.CerebralPressureImpairment:R}");

        var endpoints = new List<double[]>();
        var entryTimes = new List<double>();
        var steps997 = new List<double>();
        var executed = 0;
        for (var ms = 10; ms <= 1000; ms++)
        {
            var dt = ms / 1000.0;
            var state = preconditioned;
            var t = 0.0;
            var pathEntries = 0;
            var entryTime = double.NaN;
            var pathAccepted = true;
            while (t < 1.0 - 1e-12)
            {
                var step = Math.Min(dt, 1.0 - t);
                if (ms == 997) steps997.Add(step);
                var result = model.AdvanceCirculationInterval(in state, step, 0.0, 0.0, -5.0,
                    LogicSettings.Default);
                pathEntries += result.CoreBoundEntries.Length;
                if (double.IsNaN(entryTime) && result.CoreBoundEntries.Length > 0)
                    entryTime = t + result.CoreBoundEntries[0];
                t += step;
                if (!result.Converged)
                    failures.Add($"boundary dense dt {dt:R} ending {t:R}s: nonconverged, " +
                                 $"residual {result.MaxScaledResidual:R}.");
                foreach (var crossing in result.ModeCrossings)
                    failures.Add($"boundary dense dt {dt:R} ending {t:R}s: crossing {crossing}.");
                foreach (var violation in result.StateViolations)
                    failures.Add($"boundary dense dt {dt:R} ending {t:R}s: violation {violation}.");
                pathAccepted &= result.Converged && result.ModeCrossings.Length == 0 &&
                                result.StateViolations.Length == 0;
                state = result.Final;
            }

            executed++;
            if (pathEntries != 1)
                failures.Add($"boundary dense dt {dt:R}: {pathEntries} core entries, expected 1.");
            entryTimes.Add(entryTime);
            var endpoint = EndpointChannels(state);
            endpoints.Add(endpoint);
            CheckEndpointInvariants(endpoint, -5.0, dt, failures);
            rows.AppendLine(CultureInfo.InvariantCulture,
                $"boundary dense dt={dt:R} pathAccepted={pathAccepted} entry={entryTime:R}: " +
                $"head={endpoint[0]:R} core={endpoint[1]:R} lower={endpoint[2]:R} " +
                $"overfill={endpoint[3]:R} hr={endpoint[4]:R} cardio={endpoint[5]:R} " +
                $"pressure={endpoint[6]:R}");
        }

        Assert.Equal(991, executed);
        Assert.Equal(2, steps997.Count);
        Assert.True(Math.Abs(steps997[0] - 0.997) <= 1e-12,
            $"997ms first step {steps997[0]:R} is not 0.997.");
        Assert.True(Math.Abs(steps997[1] - 0.003) <= 1e-12,
            $"997ms alignment step {steps997[1]:R} is not 0.003.");

        var entrySpread = entryTimes.Max() - entryTimes.Min();
        if (entrySpread > 0.05)
            failures.Add($"boundary dense core entry spread {entrySpread:R} exceeds 0.05s " +
                         $"(min {entryTimes.Min():R}, max {entryTimes.Max():R}).");

        var reference = endpoints[10];
        for (var channel = 0; channel < ChannelNames.Length; channel++)
        {
            var difference = Math.Abs(endpoints[0][channel] - reference[channel]);
            if (difference > ReferenceSpreadTolerance)
                failures.Add($"boundary dense {ChannelNames[channel]}: 0.01 vs 0.02 difference " +
                             $"{difference:R} exceeds {ReferenceSpreadTolerance:R}.");
        }

        for (var channel = 0; channel < ChannelNames.Length; channel++)
        {
            var minimum = endpoints.Min(endpoint => endpoint[channel]);
            var maximum = endpoints.Max(endpoint => endpoint[channel]);
            if (maximum - minimum > StateSpreadTolerance)
                failures.Add($"boundary dense {ChannelNames[channel]}: min {minimum:R}, " +
                             $"max {maximum:R}, spread {maximum - minimum:R} exceeds " +
                             $"{StateSpreadTolerance:R}.");
        }

        foreach (var row in rows.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries))
            _output.WriteLine(row);

        Assert.True(failures.Count == 0,
            $"{failures.Count} boundary-dense-gate breach(es):{Environment.NewLine}" +
            string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public void IndependentStateAtZeroPreservesState()
    {
        var initial = new PhysiologicalModel.IntegrationState(0.22, 0.45, 1.4, 0.3, 0.6, 0.2)
        {
            StrainingLevel = 0.5,
            StrainingFatigue = 0.1,
            GSuitFatigue = 0.4
        };
        var evolved = PhysiologicalModel.IndependentCirculationStateAt(initial, 0.0, 4.0, 5.0,
            LogicSettings.Default);
        Assert.Equal(initial, evolved);
    }

    [Fact]
    public void IndependentStrainMatchesClosedForm()
    {
        var settings = LogicSettings.Default;
        var initial = new PhysiologicalModel.IntegrationState(0.2, 0.5, 1.2, 0.1, 0.4, 0.3)
        {
            StrainingLevel = 0.3
        };
        var target = Math.Clamp((5.0 - settings.StrainingStartGz) /
            (settings.StrainingFullGz - settings.StrainingStartGz), 0.0, 1.0);
        foreach (var t in new[] { 0.1, 0.37, 1.0, 2.5 })
        {
            var evolved = PhysiologicalModel.IndependentCirculationStateAt(initial, t, 0.0,
                5.0, settings);
            var expected = target + (0.3 - target) * Math.Exp(-t / settings.StrainingTau);
            Assert.True(Math.Abs(evolved.StrainingLevel - expected) <= 1e-15,
                $"strain {evolved.StrainingLevel:R} vs expected {expected:R} at t={t:R}.");
            Assert.Equal(initial.BloodHead, evolved.BloodHead);
            Assert.Equal(initial.BloodLower, evolved.BloodLower);
            Assert.Equal(initial.HeartRateMultiplier, evolved.HeartRateMultiplier);
            Assert.Equal(initial.CardioFatigue, evolved.CardioFatigue);
            Assert.Equal(initial.CerebralPressureImpairment,
                evolved.CerebralPressureImpairment);
        }
    }

    [Fact]
    public void IndependentFatigueBuildMatchesClosedFormIntegrals()
    {
        var settings = LogicSettings.Default;
        var initial = new PhysiologicalModel.IntegrationState(0.2, 0.5, 1.0, 0.0, 0.0, 0.0)
        {
            StrainingLevel = 0.02
        };
        const double t = 0.7;
        var tau = settings.StrainingTau;
        const double d = 0.02 - 1.0;
        var i1 = t + d * tau * (1.0 - Math.Exp(-t / tau));
        var i2 = t + 2.0 * d * tau * (1.0 - Math.Exp(-t / tau)) +
                 d * d * tau * 0.5 * (1.0 - Math.Exp(-2.0 * t / tau));
        var evolved = PhysiologicalModel.IndependentCirculationStateAt(initial, t, 0.0, 5.0,
            settings);
        Assert.True(Math.Abs(evolved.StrainingFatigue -
                             settings.StrainingFatigueBuildRate * i2) <= 1e-12,
            $"straining fatigue {evolved.StrainingFatigue:R} vs expected " +
            $"{settings.StrainingFatigueBuildRate * i2:R} (I2 {i2:R}).");
        Assert.True(Math.Abs(evolved.GSuitFatigue -
                             settings.GSuitFatigueBuildRate * i1) <= 1e-12,
            $"g-suit fatigue {evolved.GSuitFatigue:R} vs expected " +
            $"{settings.GSuitFatigueBuildRate * i1:R} (I1 {i1:R}).");
    }

    [Fact]
    public void IndependentFatigueRestsWhileStrainBelowThreshold()
    {
        var settings = LogicSettings.Default;
        var initial = new PhysiologicalModel.IntegrationState(0.2, 0.5, 1.0, 0.0, 0.0, 0.0)
        {
            StrainingLevel = 0.0,
            StrainingFatigue = 0.6,
            GSuitFatigue = 0.4
        };
        const double t = 1.7;
        var evolved = PhysiologicalModel.IndependentCirculationStateAt(initial, t, 0.0, 1.0,
            settings);
        Assert.Equal(0.0, evolved.StrainingLevel);
        Assert.Equal(0.6 * Math.Exp(-t / settings.StrainingFatigueRecoveryTau),
            evolved.StrainingFatigue, 12);
        Assert.Equal(0.4 * Math.Exp(-t / settings.GSuitFatigueRecoveryTau),
            evolved.GSuitFatigue, 12);
    }

    [Fact]
    public void IndependentFatigueDescendsAcrossThreshold()
    {
        var settings = LogicSettings.Default;
        var initial = new PhysiologicalModel.IntegrationState(0.2, 0.5, 1.0, 0.0, 0.0, 0.0)
        {
            StrainingLevel = 0.5
        };
        var crossing = settings.StrainingTau * Math.Log(0.5 / 0.01);
        const double t = 5.0;
        var evolved = PhysiologicalModel.IndependentCirculationStateAt(initial, t, 0.0, 1.0,
            settings);
        var i1 = 0.5 * settings.StrainingTau * (1.0 - Math.Exp(-crossing / settings.StrainingTau));
        var i2 = 0.5 * 0.5 * settings.StrainingTau * 0.5 *
                 (1.0 - Math.Exp(-2.0 * crossing / settings.StrainingTau));
        var expectedStrainFatigue = settings.StrainingFatigueBuildRate * i2 *
            Math.Exp(-(t - crossing) / settings.StrainingFatigueRecoveryTau);
        var expectedSuitFatigue = settings.GSuitFatigueBuildRate * i1 *
            Math.Exp(-(t - crossing) / settings.GSuitFatigueRecoveryTau);
        Assert.True(Math.Abs(evolved.StrainingFatigue - expectedStrainFatigue) <= 1e-12,
            $"straining fatigue {evolved.StrainingFatigue:R} vs analytic split " +
            $"{expectedStrainFatigue:R} (crossing {crossing:R}).");
        Assert.True(Math.Abs(evolved.GSuitFatigue - expectedSuitFatigue) <= 1e-12,
            $"g-suit fatigue {evolved.GSuitFatigue:R} vs analytic split {expectedSuitFatigue:R}.");
    }

    [Fact]
    public void IndependentFatigueAscendsAcrossThreshold()
    {
        var settings = LogicSettings.Default;
        var initial = new PhysiologicalModel.IntegrationState(0.2, 0.5, 1.0, 0.0, 0.0, 0.0)
        {
            StrainingLevel = 0.0,
            StrainingFatigue = 0.2,
            GSuitFatigue = 0.1
        };
        var crossing = settings.StrainingTau * Math.Log(1.0 / 0.99);
        const double t = 0.3;
        var evolved = PhysiologicalModel.IndependentCirculationStateAt(initial, t, 0.0, 5.0,
            settings);
        var h = t - crossing;
        var d = -0.99;
        var i1 = h + d * settings.StrainingTau * (1.0 - Math.Exp(-h / settings.StrainingTau));
        var i2 = h + 2.0 * d * settings.StrainingTau *
                 (1.0 - Math.Exp(-h / settings.StrainingTau)) +
                 d * d * settings.StrainingTau * 0.5 *
                 (1.0 - Math.Exp(-2.0 * h / settings.StrainingTau));
        var expectedStrainFatigue =
            0.2 * Math.Exp(-crossing / settings.StrainingFatigueRecoveryTau) +
            settings.StrainingFatigueBuildRate * i2;
        var expectedSuitFatigue =
            0.1 * Math.Exp(-crossing / settings.GSuitFatigueRecoveryTau) +
            settings.GSuitFatigueBuildRate * i1;
        Assert.True(Math.Abs(evolved.StrainingFatigue - expectedStrainFatigue) <= 1e-12,
            $"straining fatigue {evolved.StrainingFatigue:R} vs analytic split " +
            $"{expectedStrainFatigue:R} (crossing {crossing:R}).");
        Assert.True(Math.Abs(evolved.GSuitFatigue - expectedSuitFatigue) <= 1e-12,
            $"g-suit fatigue {evolved.GSuitFatigue:R} vs analytic split {expectedSuitFatigue:R}.");
    }

    [Fact]
    public void IndependentRespiratoryConstantSource()
    {
        var settings = LogicSettings.Default;
        var initial = new PhysiologicalModel.IntegrationState(0.2, 0.5, 1.0, 0.0, 0.0, 0.0);
        var build = settings.RespiratoryFatigueBuildRate *
            (4.0 - settings.GxRespiratoryFatigueThreshold) * 0.5 *
            (1.0 + (settings.GxRespiratoryFatigueAccelerationFactor - 1.0) * 4.0);
        var equilibrium = build * settings.RespiratoryFatigueRecoveryTau;
        const double t = 5.0;
        var expected = Math.Min(1.0,
            equilibrium * (1.0 - Math.Exp(-t / settings.RespiratoryFatigueRecoveryTau)));
        var evolved = PhysiologicalModel.IndependentCirculationStateAt(initial, t, 4.0, 1.0,
            settings);
        Assert.True(Math.Abs(evolved.RespiratoryFatigue - expected) <= 1e-12,
            $"respiratory fatigue {evolved.RespiratoryFatigue:R} vs expected {expected:R}.");
    }

    [Fact]
    public void IndependentRespiratoryRests()
    {
        var settings = LogicSettings.Default;
        var initial = new PhysiologicalModel.IntegrationState(0.2, 0.5, 1.0, 0.0, 0.0, 0.6);
        const double t = 3.0;
        var evolved = PhysiologicalModel.IndependentCirculationStateAt(initial, t, 0.0, 1.0,
            settings);
        Assert.Equal(0.6 * Math.Exp(-t / settings.RespiratoryFatigueRecoveryTau),
            evolved.RespiratoryFatigue, 12);
    }

    [Fact]
    public void IndependentRespiratoryCeiling()
    {
        var settings = LogicSettings.Default;
        var initial = new PhysiologicalModel.IntegrationState(0.2, 0.5, 1.0, 0.0, 0.0, 0.5);
        var evolved = PhysiologicalModel.IndependentCirculationStateAt(initial, 30.0, 4.0, 1.0,
            settings);
        Assert.Equal(1.0, evolved.RespiratoryFatigue);
    }

    [Fact]
    public void IndependentInstantaneousStrainingTau()
    {
        var settings = LogicSettings.Default with { StrainingTau = 1e-12 };
        var initial = new PhysiologicalModel.IntegrationState(0.2, 0.5, 1.0, 0.0, 0.0, 0.0)
        {
            StrainingLevel = 0.4
        };
        var evolved = PhysiologicalModel.IndependentCirculationStateAt(initial, 2.0, 0.0, 5.0,
            settings);
        Assert.Equal(1.0, evolved.StrainingLevel);
        Assert.Equal(settings.StrainingFatigueBuildRate * 2.0, evolved.StrainingFatigue, 12);
        Assert.Equal(settings.GSuitFatigueBuildRate * 2.0, evolved.GSuitFatigue, 12);
    }

    [Theory]
    [InlineData(0.01)]
    [InlineData(0.1)]
    [InlineData(0.25)]
    [InlineData(0.5)]
    [InlineData(1.0)]
    public void IndependentTrajectorySemigroupMatchesSingleInterval(double step)
    {
        var settings = LogicSettings.Default;
        var initial = new PhysiologicalModel.IntegrationState(0.2, 0.5, 1.0, 0.0, 0.0, 0.4)
        {
            StrainingLevel = 0.4,
            StrainingFatigue = 0.3,
            GSuitFatigue = 0.2
        };
        var single = PhysiologicalModel.IndependentCirculationStateAt(initial, 1.0, 4.0, 5.0,
            settings);
        var state = initial;
        var steps = (int)Math.Round(1.0 / step);
        for (var i = 0; i < steps; i++)
            state = PhysiologicalModel.IndependentCirculationStateAt(state, step, 4.0, 5.0,
                settings);
        Assert.True(Math.Abs(state.StrainingLevel - single.StrainingLevel) <= 1e-12,
            $"strain {state.StrainingLevel:R} vs {single.StrainingLevel:R} at step {step:R}.");
        Assert.True(Math.Abs(state.StrainingFatigue - single.StrainingFatigue) <= 1e-12,
            $"straining fatigue {state.StrainingFatigue:R} vs {single.StrainingFatigue:R}.");
        Assert.True(Math.Abs(state.GSuitFatigue - single.GSuitFatigue) <= 1e-12,
            $"g-suit fatigue {state.GSuitFatigue:R} vs {single.GSuitFatigue:R}.");
        Assert.True(Math.Abs(state.RespiratoryFatigue - single.RespiratoryFatigue) <= 1e-12,
            $"respiratory fatigue {state.RespiratoryFatigue:R} vs {single.RespiratoryFatigue:R}.");
    }

    [Fact]
    public void IndependentLeavesInputUnchanged()
    {
        var initial = new PhysiologicalModel.IntegrationState(0.2, 0.5, 1.0, 0.0, 0.0, 0.3)
        {
            StrainingLevel = 0.4,
            StrainingFatigue = 0.2,
            GSuitFatigue = 0.1
        };
        var before = initial;
        PhysiologicalModel.IndependentCirculationStateAt(initial, 1.0, 4.0, 5.0,
            LogicSettings.Default);
        Assert.Equal(before, initial);
    }

    [Fact]
    public void IndependentConstantStrainBuildReachesCeiling()
    {
        var initial = new PhysiologicalModel.IntegrationState(0.2, 0.5, 1.0, 0.0, 0.0, 0.0)
        {
            StrainingLevel = 1.0,
            StrainingFatigue = 0.95
        };
        var evolved = PhysiologicalModel.IndependentCirculationStateAt(initial, 30.0, 0.0, 5.0,
            LogicSettings.Default);
        Assert.Equal(1.0, evolved.StrainingLevel);
        Assert.Equal(1.0, evolved.StrainingFatigue);
    }

    [Fact]
    public void IndependentFatigueExactThresholdStartBuilds()
    {
        var settings = LogicSettings.Default;
        var initial = new PhysiologicalModel.IntegrationState(0.2, 0.5, 1.0, 0.0, 0.0, 0.1)
        {
            StrainingLevel = 0.01,
            StrainingFatigue = 0.2,
            GSuitFatigue = 0.1
        };
        const double t = 1.0;
        const double d = 0.01 - 1.0;
        var tau = settings.StrainingTau;
        var i1 = t + d * tau * (1.0 - Math.Exp(-t / tau));
        var i2 = t + 2.0 * d * tau * (1.0 - Math.Exp(-t / tau)) +
                 d * d * tau * 0.5 * (1.0 - Math.Exp(-2.0 * t / tau));
        var evolved = PhysiologicalModel.IndependentCirculationStateAt(initial, t, 0.0, 5.0,
            settings);
        Assert.True(Math.Abs(evolved.StrainingFatigue -
            (0.2 + settings.StrainingFatigueBuildRate * i2)) <= 1e-12,
            $"straining fatigue {evolved.StrainingFatigue:R} vs closed form " +
            $"{0.2 + settings.StrainingFatigueBuildRate * i2:R}.");
        Assert.True(Math.Abs(evolved.GSuitFatigue -
            (0.1 + settings.GSuitFatigueBuildRate * i1)) <= 1e-12,
            $"g-suit fatigue {evolved.GSuitFatigue:R} vs closed form " +
            $"{0.1 + settings.GSuitFatigueBuildRate * i1:R}.");
        AssertIndependentSemigroup(initial, 0.0, 5.0, t, settings);
    }

    [Fact]
    public void IndependentFatigueExactThresholdStartRests()
    {
        var settings = LogicSettings.Default;
        var initial = new PhysiologicalModel.IntegrationState(0.2, 0.5, 1.0, 0.0, 0.0, 0.1)
        {
            StrainingLevel = 0.01,
            StrainingFatigue = 0.5,
            GSuitFatigue = 0.4
        };
        const double t = 5.0;
        var evolved = PhysiologicalModel.IndependentCirculationStateAt(initial, t, 0.0, 1.0,
            settings);
        Assert.Equal(0.5 * Math.Exp(-t / settings.StrainingFatigueRecoveryTau),
            evolved.StrainingFatigue, 12);
        Assert.Equal(0.4 * Math.Exp(-t / settings.GSuitFatigueRecoveryTau),
            evolved.GSuitFatigue, 12);
        AssertIndependentSemigroup(initial, 0.0, 1.0, t, settings);
    }

    [Fact]
    public void IndependentFatigueAscendsToLowTarget()
    {
        var settings = LogicSettings.Default;
        var gz = settings.StrainingStartGz +
            0.02 * (settings.StrainingFullGz - settings.StrainingStartGz);
        var initial = new PhysiologicalModel.IntegrationState(0.2, 0.5, 1.0, 0.0, 0.0, 0.0)
        {
            StrainingLevel = 0.0,
            StrainingFatigue = 0.2,
            GSuitFatigue = 0.1
        };
        var tau = settings.StrainingTau;
        var crossing = tau * Math.Log(2.0);
        const double t = 1.0;
        var h = t - crossing;
        const double d = 0.01 - 0.02;
        var i1 = 0.02 * h + d * tau * (1.0 - Math.Exp(-h / tau));
        var i2 = 0.02 * 0.02 * h + 2.0 * 0.02 * d * tau * (1.0 - Math.Exp(-h / tau)) +
                 d * d * tau * 0.5 * (1.0 - Math.Exp(-2.0 * h / tau));
        var evolved = PhysiologicalModel.IndependentCirculationStateAt(initial, t, 0.0, gz,
            settings);
        var expectedStrainFatigue =
            0.2 * Math.Exp(-crossing / settings.StrainingFatigueRecoveryTau) +
            settings.StrainingFatigueBuildRate * i2;
        var expectedSuitFatigue =
            0.1 * Math.Exp(-crossing / settings.GSuitFatigueRecoveryTau) +
            settings.GSuitFatigueBuildRate * i1;
        Assert.True(Math.Abs(evolved.StrainingFatigue - expectedStrainFatigue) <= 1e-12,
            $"straining fatigue {evolved.StrainingFatigue:R} vs analytic split " +
            $"{expectedStrainFatigue:R} (crossing {crossing:R}).");
        Assert.True(Math.Abs(evolved.GSuitFatigue - expectedSuitFatigue) <= 1e-12,
            $"g-suit fatigue {evolved.GSuitFatigue:R} vs analytic split {expectedSuitFatigue:R}.");
        AssertIndependentSemigroup(initial, 0.0, gz, t, settings);
    }

    [Fact]
    public void IndependentFatigueDescendsFromLowStart()
    {
        var settings = LogicSettings.Default;
        var initial = new PhysiologicalModel.IntegrationState(0.2, 0.5, 1.0, 0.0, 0.0, 0.0)
        {
            StrainingLevel = 0.03,
            StrainingFatigue = 0.2,
            GSuitFatigue = 0.1
        };
        var tau = settings.StrainingTau;
        var crossing = tau * Math.Log(3.0);
        const double t = 5.0;
        var i1 = 0.03 * tau * (1.0 - Math.Exp(-crossing / tau));
        var i2 = 0.03 * 0.03 * tau * 0.5 * (1.0 - Math.Exp(-2.0 * crossing / tau));
        var evolved = PhysiologicalModel.IndependentCirculationStateAt(initial, t, 0.0, 1.0,
            settings);
        var expectedStrainFatigue = (0.2 + settings.StrainingFatigueBuildRate * i2) *
            Math.Exp(-(t - crossing) / settings.StrainingFatigueRecoveryTau);
        var expectedSuitFatigue = (0.1 + settings.GSuitFatigueBuildRate * i1) *
            Math.Exp(-(t - crossing) / settings.GSuitFatigueRecoveryTau);
        Assert.True(Math.Abs(evolved.StrainingFatigue - expectedStrainFatigue) <= 1e-12,
            $"straining fatigue {evolved.StrainingFatigue:R} vs analytic split " +
            $"{expectedStrainFatigue:R} (crossing {crossing:R}).");
        Assert.True(Math.Abs(evolved.GSuitFatigue - expectedSuitFatigue) <= 1e-12,
            $"g-suit fatigue {evolved.GSuitFatigue:R} vs analytic split {expectedSuitFatigue:R}.");
        AssertIndependentSemigroup(initial, 0.0, 1.0, t, settings);
    }

    [Fact]
    public void IndependentFatigueCeilingThenRestsAcrossCrossing()
    {
        var settings = LogicSettings.Default;
        var initial = new PhysiologicalModel.IntegrationState(0.2, 0.5, 1.0, 0.0, 0.0, 0.0)
        {
            StrainingLevel = 0.5,
            StrainingFatigue = 0.999,
            GSuitFatigue = 0.999
        };
        var tau = settings.StrainingTau;
        var crossing = tau * Math.Log(0.5 / 0.01);
        const double t = 5.0;
        var i1 = 0.5 * tau * (1.0 - Math.Exp(-crossing / tau));
        var i2 = 0.5 * 0.5 * tau * 0.5 * (1.0 - Math.Exp(-2.0 * crossing / tau));
        var strainAtCrossing = Math.Min(0.999 + settings.StrainingFatigueBuildRate * i2, 1.0);
        var suitAtCrossing = Math.Min(0.999 + settings.GSuitFatigueBuildRate * i1, 1.0);
        var evolved = PhysiologicalModel.IndependentCirculationStateAt(initial, t, 0.0, 1.0,
            settings);
        Assert.Equal(1.0, strainAtCrossing);
        Assert.Equal(1.0, suitAtCrossing);
        Assert.True(Math.Abs(evolved.StrainingFatigue -
            strainAtCrossing * Math.Exp(-(t - crossing) /
                settings.StrainingFatigueRecoveryTau)) <= 1e-12,
            $"straining fatigue {evolved.StrainingFatigue:R} vs ceiling-then-rest split.");
        Assert.True(Math.Abs(evolved.GSuitFatigue -
            suitAtCrossing * Math.Exp(-(t - crossing) /
                settings.GSuitFatigueRecoveryTau)) <= 1e-12,
            $"g-suit fatigue {evolved.GSuitFatigue:R} vs ceiling-then-rest split.");
        AssertIndependentSemigroup(initial, 0.0, 1.0, t, settings);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(1e-12)]
    public void IndependentRespiratoryInstantaneousTau(double tau)
    {
        var settings = LogicSettings.Default with { RespiratoryFatigueRecoveryTau = tau };
        var initial = new PhysiologicalModel.IntegrationState(0.2, 0.5, 1.0, 0.0, 0.0, 0.6);
        var build = settings.RespiratoryFatigueBuildRate *
            (4.0 - settings.GxRespiratoryFatigueThreshold) * 0.5 *
            (1.0 + (settings.GxRespiratoryFatigueAccelerationFactor - 1.0) * 4.0);
        var equilibrium = Math.Clamp(build * tau, 0.0, 1.0);
        var evolved = PhysiologicalModel.IndependentCirculationStateAt(initial, 1.0, 4.0, 1.0,
            settings);
        Assert.True(Math.Abs(evolved.RespiratoryFatigue - equilibrium) <= 1e-12,
            $"respiratory fatigue {evolved.RespiratoryFatigue:R} vs instantaneous " +
            $"equilibrium {equilibrium:R} at tau {tau:R}.");
        var half = PhysiologicalModel.IndependentCirculationStateAt(initial, 0.5, 4.0, 1.0,
            settings);
        var twoHalf = PhysiologicalModel.IndependentCirculationStateAt(half, 0.5, 4.0, 1.0,
            settings);
        Assert.True(Math.Abs(twoHalf.RespiratoryFatigue - evolved.RespiratoryFatigue) <= 1e-12,
            $"semigroup {twoHalf.RespiratoryFatigue:R} vs single " +
            $"{evolved.RespiratoryFatigue:R}.");
    }

    private static void AssertIndependentSemigroup(
        PhysiologicalModel.IntegrationState initial, double gx, double gz, double seconds,
        LogicSettings settings)
    {
        var single = PhysiologicalModel.IndependentCirculationStateAt(initial, seconds, gx, gz,
            settings);
        foreach (var step in new[] { 0.01, 0.1, 0.25, 0.5 })
        {
            var state = initial;
            var offset = 0.0;
            while (offset < seconds - 1e-12)
            {
                var h = Math.Min(step, seconds - offset);
                state = PhysiologicalModel.IndependentCirculationStateAt(state, h, gx, gz,
                    settings);
                offset += h;
            }

            Assert.True(Math.Abs(state.StrainingLevel - single.StrainingLevel) <= 1e-12,
                $"strain {state.StrainingLevel:R} vs {single.StrainingLevel:R} at step {step:R}.");
            Assert.True(Math.Abs(state.StrainingFatigue - single.StrainingFatigue) <= 1e-12,
                $"straining fatigue {state.StrainingFatigue:R} vs " +
                $"{single.StrainingFatigue:R} at step {step:R}.");
            Assert.True(Math.Abs(state.GSuitFatigue - single.GSuitFatigue) <= 1e-12,
                $"g-suit fatigue {state.GSuitFatigue:R} vs {single.GSuitFatigue:R} " +
                $"at step {step:R}.");
            Assert.True(Math.Abs(state.RespiratoryFatigue - single.RespiratoryFatigue) <= 1e-12,
                $"respiratory fatigue {state.RespiratoryFatigue:R} vs " +
                $"{single.RespiratoryFatigue:R} at step {step:R}.");
        }
    }

    [Theory]
    [InlineData(0.0, 0.0, 5.0)]
    [InlineData(2.0, 1.0, 5.0)]
    public void CirculationJacobianMatchesFiniteDifferencesWithStrain(
        double gx, double gy, double gz)
    {
        var state = new PhysiologicalModel.IntegrationState(0.12, 0.52, 1.7, 0.1, 0.0, 0.0)
        {
            StrainingLevel = 0.8,
            StrainingFatigue = 0.2,
            GSuitFatigue = 0.1
        };
        var rates = new double[PhysiologicalModel.CirculationDimensions];
        var jacobian = NumericalMath.CreateMatrix(PhysiologicalModel.CirculationDimensions);
        PhysiologicalModel.EvaluateCirculation(in state, gx, gy, gz, LogicSettings.Default,
            rates, jacobian);

        var failures = new List<string>();
        for (var variable = 0; variable < PhysiologicalModel.CirculationDimensions; variable++)
        {
            var plusRates = PerturbedRates(state, gx, gy, gz, variable, JacobianPerturbation);
            var minusRates = PerturbedRates(state, gx, gy, gz, variable, -JacobianPerturbation);
            for (var row = 0; row < PhysiologicalModel.CirculationDimensions; row++)
            {
                var finiteDifference = (plusRates[row] - minusRates[row]) /
                                       (2.0 * JacobianPerturbation);
                var difference = Math.Abs(finiteDifference - jacobian[row][variable]);
                if (difference > JacobianTolerance)
                    failures.Add($"strained J[{row},{variable}] analytic " +
                                 $"{jacobian[row][variable]:R} vs finite difference " +
                                 $"{finiteDifference:R}, diff {difference:R}.");
            }
        }

        Assert.True(failures.Count == 0,
            $"strained Jacobian mismatches:{Environment.NewLine}{string.Join(Environment.NewLine, failures)}");
    }

    [Fact]
    public void HeadBoundJacobianMatchesFiniteDifferences()
    {
        var settings = LogicSettings.Default;
        var state = new PhysiologicalModel.IntegrationState(
            settings.MinHeadBloodFraction, 0.6, 1.7, 0.1, 0.0, 0.0)
        {
            StrainingLevel = 0.8,
            StrainingFatigue = 0.2,
            GSuitFatigue = 0.1
        };
        var rates = new double[PhysiologicalModel.CirculationDimensions];
        var jacobian = NumericalMath.CreateMatrix(PhysiologicalModel.CirculationDimensions);
        PhysiologicalModel.EvaluateBoundConstrainedCirculation(in state, 0.0, 0.0, 5.0,
            settings, rates, jacobian, true, PhysiologicalModel.BloodBounds.Head);

        var failures = new List<string>();
        for (var variable = 0; variable < PhysiologicalModel.CirculationDimensions; variable++)
        {
            var plus = PerturbedBoundRates(state, variable, JacobianPerturbation, settings,
                0.0, 0.0, 5.0, PhysiologicalModel.BloodBounds.Head);
            var minus = PerturbedBoundRates(state, variable, -JacobianPerturbation, settings,
                0.0, 0.0, 5.0, PhysiologicalModel.BloodBounds.Head);
            for (var row = 0; row < PhysiologicalModel.CirculationDimensions; row++)
            {
                var finiteDifference = (plus[row] - minus[row]) / (2.0 * JacobianPerturbation);
                var difference = Math.Abs(finiteDifference - jacobian[row][variable]);
                if (difference > JacobianTolerance)
                    failures.Add($"head-bound J[{row},{variable}] analytic " +
                                 $"{jacobian[row][variable]:R} vs finite difference " +
                                 $"{finiteDifference:R}, diff {difference:R}.");
            }
        }

        Assert.True(failures.Count == 0,
            $"head-bound Jacobian mismatches:{Environment.NewLine}{string.Join(Environment.NewLine, failures)}");
    }

    [Fact]
    public void LowerBoundJacobianMatchesFiniteDifferences()
    {
        var settings = LogicSettings.Default;
        var state = new PhysiologicalModel.IntegrationState(0.22, 0.0, 0.5, 0.1, 0.0, 0.0);
        var rates = new double[PhysiologicalModel.CirculationDimensions];
        var jacobian = NumericalMath.CreateMatrix(PhysiologicalModel.CirculationDimensions);
        PhysiologicalModel.EvaluateBoundConstrainedCirculation(in state, 0.0, 0.0, -5.0,
            settings, rates, jacobian, true, PhysiologicalModel.BloodBounds.Lower);

        var failures = new List<string>();
        for (var variable = 0; variable < PhysiologicalModel.CirculationDimensions; variable++)
        {
            var plus = PerturbedBoundRates(state, variable, JacobianPerturbation, settings,
                0.0, 0.0, -5.0, PhysiologicalModel.BloodBounds.Lower);
            var minus = PerturbedBoundRates(state, variable, -JacobianPerturbation, settings,
                0.0, 0.0, -5.0, PhysiologicalModel.BloodBounds.Lower);
            for (var row = 0; row < PhysiologicalModel.CirculationDimensions; row++)
            {
                var finiteDifference = (plus[row] - minus[row]) / (2.0 * JacobianPerturbation);
                var difference = Math.Abs(finiteDifference - jacobian[row][variable]);
                if (difference > JacobianTolerance)
                    failures.Add($"lower-bound J[{row},{variable}] analytic " +
                                 $"{jacobian[row][variable]:R} vs finite difference " +
                                 $"{finiteDifference:R}, diff {difference:R}.");
            }
        }

        Assert.True(failures.Count == 0,
            $"lower-bound Jacobian mismatches:{Environment.NewLine}{string.Join(Environment.NewLine, failures)}");
    }

    private static double[] PerturbedBoundRates(
        PhysiologicalModel.IntegrationState state, int variable, double perturbation,
        LogicSettings settings, double gx, double gy, double gz,
        PhysiologicalModel.BloodBounds bounds)
    {
        var values = new[]
        {
            state.BloodHead, state.BloodLower, state.HeartRateMultiplier,
            state.CardioFatigue, state.CerebralPressureImpairment
        };
        values[variable] += perturbation;
        var perturbed = state with
        {
            BloodHead = values[0],
            BloodLower = values[1],
            HeartRateMultiplier = values[2],
            CardioFatigue = values[3],
            CerebralPressureImpairment = values[4]
        };
        var rates = new double[PhysiologicalModel.CirculationDimensions];
        PhysiologicalModel.EvaluateBoundConstrainedCirculation(in perturbed, gx, gy, gz,
            settings, rates, null, true, bounds);
        return rates;
    }

    private static readonly string[] ExtendedChannelNames =
        ["BloodHead", "BloodCore", "BloodLower", "HeadOverfill", "HeartRateMultiplier",
         "CardioFatigue", "CerebralPressureImpairment", "RespiratoryFatigue",
         "StrainingLevel", "StrainingFatigue", "GSuitFatigue"];

    private static double[] ExtendedChannels(PhysiologicalModel.IntegrationState state) =>
    [
        state.BloodHead,
        state.BloodCore,
        state.BloodLower,
        Math.Max((state.BloodHead - LogicSettings.Default.RestingBloodHead) /
            LogicSettings.Default.RestingBloodHead, 0.0),
        state.HeartRateMultiplier,
        state.CardioFatigue,
        state.CerebralPressureImpairment,
        state.RespiratoryFatigue,
        state.StrainingLevel,
        state.StrainingFatigue,
        state.GSuitFatigue
    ];

    [Theory]
    [InlineData(0.0, 0.0, 5.0)]
    [InlineData(2.0, 1.0, 5.0)]
    [InlineData(0.0, 4.0, 1.0)]
    [InlineData(0.0, 0.0, 1.0)]
    public void PositiveLoadThirtySecondCirculationGate(double gx, double gy, double gz)
    {
        const int seconds = 30;
        var settings = LogicSettings.Default;
        var model = new GEffectsLogicInstance(new DtStabilityLogger()).PhysModel;
        var initial = model.CaptureIntervalState();
        var failures = new List<string>();
        var rows = new StringBuilder();

        var reference = AdvancePositivePath(model, initial, settings, gx, gy, gz,
            ReferenceDt, seconds, rows, failures, out _, out _);
        var endpoints = new double[CandidateDt.Length][][];
        var eventLog = new string[CandidateDt.Length];
        for (var candidateIndex = 0; candidateIndex < CandidateDt.Length; candidateIndex++)
        {
            var pathAccepted = false;
            endpoints[candidateIndex] = AdvancePositivePath(model, initial, settings, gx, gy,
                gz, CandidateDt[candidateIndex], seconds, rows, failures, out pathAccepted,
                out var events);
            eventLog[candidateIndex] = events;
            if (!pathAccepted)
                failures.Add($"dt {CandidateDt[candidateIndex]:R}: path rejected.");
        }

        foreach (var entry in eventLog.Select((log, index) => (log, index)))
            _output.WriteLine($"dt {CandidateDt[entry.index]:R} events: {entry.log}");

        for (var second = 0; second < seconds; second++)
        {
            var reached = true;
            for (var candidateIndex = 0; candidateIndex < CandidateDt.Length; candidateIndex++)
                reached &= endpoints[candidateIndex][second] != null;
            if (!reached)
            {
                failures.Add($"gx {gx:R} gy {gy:R} gz {gz:R} second {second + 1}: " +
                             "not all candidate paths reached the checkpoint.");
                continue;
            }

            for (var channel = 0; channel < ExtendedChannelNames.Length; channel++)
            {
                var minimum = double.MaxValue;
                var maximum = double.MinValue;
                for (var candidateIndex = 0; candidateIndex < CandidateDt.Length; candidateIndex++)
                {
                    var value = endpoints[candidateIndex][second]![channel];
                    minimum = Math.Min(minimum, value);
                    maximum = Math.Max(maximum, value);
                }

                if (maximum - minimum > StateSpreadTolerance)
                    failures.Add($"gx {gx:R} gy {gy:R} gz {gz:R} " +
                                 $"{ExtendedChannelNames[channel]} at {second + 1}s: min " +
                                 $"{minimum:R}, max {maximum:R}, spread {maximum - minimum:R} " +
                                 $"exceeds {StateSpreadTolerance:R}.");

                var referenceDifference = Math.Abs(endpoints[0][second]![channel] -
                    reference[second][channel]);
                if (referenceDifference > ReferenceSpreadTolerance)
                    failures.Add($"gx {gx:R} gy {gy:R} gz {gz:R} " +
                                 $"{ExtendedChannelNames[channel]} at {second + 1}s: dt 0.01 " +
                                 $"vs 0.02 difference {referenceDifference:R} exceeds " +
                                 $"{ReferenceSpreadTolerance:R}.");
            }
        }

        foreach (var row in rows.ToString().Split(Environment.NewLine,
                     StringSplitOptions.RemoveEmptyEntries))
            _output.WriteLine(row);

        Assert.True(failures.Count == 0,
            $"{failures.Count} positive-load breach(es) at ({gx:R},{gy:R},{gz:R}):" +
            $"{Environment.NewLine}{string.Join(Environment.NewLine, failures)}");
    }

    private static double[][] AdvancePositivePath(
        PhysiologicalModel model, PhysiologicalModel.IntegrationState initial,
        LogicSettings settings, double gx, double gy, double gz, double dt, int seconds,
        StringBuilder rows, List<string> failures, out bool pathAccepted,
        out string eventSummary)
    {
        var callsPerSecond = (int)Math.Round(1.0 / dt);
        var checkpoints = new double[seconds][];
        var entries = 0;
        var releases = 0;
        var transitions = 0;
        var segments = 0;
        pathAccepted = true;
        var state = initial;
        for (var second = 0; second < seconds; second++)
        {
            for (var call = 0; call < callsPerSecond; call++)
            {
                var result = model.AdvanceCirculationInterval(in state, dt, gx, gy, gz, settings);
                if (!result.Converged)
                    failures.Add($"({gx:R},{gy:R},{gz:R}) dt {dt:R} second {second + 1} call " +
                                 $"{call + 1}: not converged, residual " +
                                 $"{result.MaxScaledResidual:R}.");
                foreach (var crossing in result.ModeCrossings)
                    failures.Add($"({gx:R},{gy:R},{gz:R}) dt {dt:R}: unresolved mode " +
                                 $"crossing {crossing}.");
                foreach (var violation in result.StateViolations)
                    failures.Add($"({gx:R},{gy:R},{gz:R}) dt {dt:R}: state violation " +
                                 $"{violation}.");
                pathAccepted &= result.Converged && result.ModeCrossings.Length == 0 &&
                                result.StateViolations.Length == 0;
                AssertAcceptedCoverage(result, dt);
                AssertPhysicalStages(result);
                Assert.Equal(state, result.Initial);
                entries += result.CoreBoundEntries.Length + result.HeadBoundEntries.Length +
                           result.LowerBoundEntries.Length;
                releases += result.CoreBoundReleases.Length +
                            result.HeadBoundReleases.Length +
                            result.LowerBoundReleases.Length;
                transitions += result.ModeTransitions.Length;
                segments += result.Segments.Length;
                state = result.Final;
            }

            checkpoints[second] = ExtendedChannels(state);
            rows.AppendLine(CultureInfo.InvariantCulture,
                $"({gx:R},{gy:R},{gz:R}) dt={dt:R} second={second + 1} " +
                $"head={state.BloodHead:R} lower={state.BloodLower:R} " +
                $"hr={state.HeartRateMultiplier:R} cardio={state.CardioFatigue:R} " +
                $"strain={state.StrainingLevel:R} strainFat={state.StrainingFatigue:R} " +
                $"suitFat={state.GSuitFatigue:R} resp={state.RespiratoryFatigue:R}");
        }

        eventSummary = $"entries={entries} releases={releases} transitions={transitions} " +
                       $"segments={segments}";
        return checkpoints;
    }

    [Fact]
    public void HeadFloorEntryAndReleaseLocalize()
    {
        var settings = LogicSettings.Default;
        var entryTimes = new List<double>();
        foreach (var dt in CandidateDt)
        {
            var model = new GEffectsLogicInstance(new DtStabilityLogger()).PhysModel;
            var state = new PhysiologicalModel.IntegrationState(
                settings.MinHeadBloodFraction + 0.0001, 0.6, 1.7, 0.1, 0.0, 0.0);
            var offset = 0.0;
            var firstEntry = double.NaN;
            double? loadRelease = null;
            var sawBound = false;
            while (offset < 1.0 - 1e-12)
            {
                var step = Math.Min(dt, 1.0 - offset);
                var result = model.AdvanceCirculationInterval(in state, step, 0.0, 0.0, 5.0, settings);
                Assert.True(result.Converged,
                    $"dt {dt:R} load call at {offset:R}s did not converge: " +
                    $"residual {result.MaxScaledResidual:R} " +
                    $"crossings [{string.Join("; ", result.ModeCrossings)}] " +
                    $"violations [{string.Join("; ", result.StateViolations)}].");
                Assert.Empty(result.ModeCrossings);
                Assert.Empty(result.StateViolations);
                AssertAcceptedCoverage(result, step);
                AssertPhysicalStages(result);
                Assert.Equal(state, result.Initial);
                if (double.IsNaN(firstEntry) && result.HeadBoundEntries.Length != 0)
                    firstEntry = offset + result.HeadBoundEntries[0];
                if (!loadRelease.HasValue && result.HeadBoundReleases.Length != 0)
                    loadRelease = offset + result.HeadBoundReleases[0];
                sawBound |= result.Segments.Any(segment => segment.HeadBound);
                state = result.Final;
                offset += step;
            }

            Assert.False(double.IsNaN(firstEntry),
                $"dt {dt:R}: no head-floor entry within the 1s load.");
            Assert.True(sawBound,
                $"dt {dt:R}: entry {firstEntry:R} but no head-bound segment accepted.");
            entryTimes.Add(firstEntry);

            var unloadOffset = 0.0;
            double? unloadRelease = null;
            while (unloadOffset < 1.0 - 1e-12)
            {
                var step = Math.Min(dt, 1.0 - unloadOffset);
                var result = model.AdvanceCirculationInterval(in state, step, 0.0, 0.0, 1.0, settings);
                Assert.True(result.Converged,
                    $"dt {dt:R} unload call at {unloadOffset:R}s did not converge.");
                Assert.Empty(result.StateViolations);
                AssertAcceptedCoverage(result, step);
                AssertPhysicalStages(result);
                Assert.Equal(state, result.Initial);
                if (!unloadRelease.HasValue && result.HeadBoundReleases.Length != 0)
                    unloadRelease = unloadOffset + result.HeadBoundReleases[0];
                state = result.Final;
                unloadOffset += step;
            }

            var actualRelease = unloadRelease ?? loadRelease;
            Assert.True(actualRelease.HasValue,
                $"dt {dt:R}: no head-floor release in load or unload interval; " +
                $"final head {state.BloodHead:R}.");
            Assert.True(state.BloodHead > settings.MinHeadBloodFraction,
                $"dt {dt:R}: head {state.BloodHead:R} did not depart the floor.");
            _output.WriteLine($"dt {dt:R}: head entry {firstEntry:R}, release " +
                              $"{actualRelease:R}, final head {state.BloodHead:R}.");
        }

        var spread = entryTimes.Max() - entryTimes.Min();
        Assert.True(spread <= 0.05,
            $"head-floor entry spread {spread:R} exceeds 0.05s " +
            $"({string.Join(", ", entryTimes.Select(t => t.ToString("R", CultureInfo.InvariantCulture)))}).");
    }

    [Fact]
    public void HeadBoundEntryNearIntervalStartAndEnd()
    {
        var settings = LogicSettings.Default;
        var model = new GEffectsLogicInstance(new DtStabilityLogger()).PhysModel;
        var initial = new PhysiologicalModel.IntegrationState(
            settings.MinHeadBloodFraction + 0.0001, 0.6, 1.7, 0.1, 0.0, 0.0);
        var probe = model.AdvanceCirculationInterval(in initial, 0.5, 0.0, 0.0, 5.0, settings);
        Assert.True(probe.Converged);
        var entry = Assert.Single(probe.HeadBoundEntries);
        Assert.True(entry is > 0.0 and < 0.5);

        var exactEnd = model.AdvanceCirculationInterval(in initial, entry, 0.0, 0.0, 5.0, settings);
        Assert.True(exactEnd.Converged,
            $"exact-end entry call: crossings " +
            $"[{string.Join("; ", exactEnd.ModeCrossings)}] violations " +
            $"[{string.Join("; ", exactEnd.StateViolations)}].");
        AssertAcceptedCoverage(exactEnd, entry);
        AssertPhysicalStages(exactEnd);
        Assert.Equal(initial, exactEnd.Initial);
        Assert.True(exactEnd.Final.BloodHead <= settings.MinHeadBloodFraction + 1e-10,
            $"head {exactEnd.Final.BloodHead:R} not at floor after endpoint entry.");

        var nearStart = new PhysiologicalModel.IntegrationState(
            settings.MinHeadBloodFraction + 1e-7, 0.6, 1.7, 0.1, 0.0, 0.0);
        var startResult = model.AdvanceCirculationInterval(in nearStart, 0.25, 0.0, 0.0, 5.0, settings);
        Assert.True(startResult.Converged);
        Assert.Empty(startResult.StateViolations);
        AssertAcceptedCoverage(startResult, 0.25);
        AssertPhysicalStages(startResult);
        var startEntry = Assert.Single(startResult.HeadBoundEntries);
        Assert.True(startEntry is > 0.0 and < 1e-3,
            $"near-start entry {startEntry:R} not within the first millisecond.");
        Assert.Equal(nearStart, startResult.Initial);
    }

    [Fact]
    public void HeadFloorEqualityStartHoldsBound()
    {
        var settings = LogicSettings.Default;
        var model = new GEffectsLogicInstance(new DtStabilityLogger()).PhysModel;
        var state = new PhysiologicalModel.IntegrationState(
            settings.MinHeadBloodFraction, 0.6, 1.7, 0.1, 0.0, 0.0);
        var result = model.AdvanceCirculationInterval(in state, 0.25, 0.0, 0.0, 5.0, settings);
        Assert.True(result.Converged);
        Assert.Empty(result.StateViolations);
        Assert.Empty(result.HeadBoundEntries);
        AssertAcceptedCoverage(result, 0.25);
        Assert.All(result.Segments, segment => Assert.True(segment.HeadBound));
        Assert.All(result.Stages, stage =>
            Assert.Equal(settings.MinHeadBloodFraction, stage.BloodHead));
    }

    [Fact]
    public void HeadFloorEntryCoexistsWithCoreRelease()
    {
        var settings = LogicSettings.Default;
        var model = new GEffectsLogicInstance(new DtStabilityLogger()).PhysModel;
        var head = settings.MinHeadBloodFraction + 0.0001;
        var state = new PhysiologicalModel.IntegrationState(head, 1.0 - head, 1.7, 0.1,
            0.0, 0.0);
        var result = model.AdvanceCirculationInterval(in state, 0.25, 0.0, 0.0, 5.0, settings);
        Assert.True(result.Converged);
        Assert.Empty(result.ModeCrossings);
        Assert.Empty(result.StateViolations);
        AssertAcceptedCoverage(result, 0.25);
        AssertPhysicalStages(result);
        Assert.NotEmpty(result.CoreBoundReleases);
        Assert.True(result.HeadBoundEntries.Length != 0,
            $"head entries [{string.Join(", ", result.HeadBoundEntries)}] " +
            $"final head {result.Final.BloodHead:R} " +
            $"converged={result.Converged} " +
            $"violations=[{string.Join("; ", result.StateViolations)}]");
    }

    [Fact]
    public void LowerFloorEntryAndReleaseLocalize()
    {
        var settings = LogicSettings.Default;
        var model = new GEffectsLogicInstance(new DtStabilityLogger()).PhysModel;
        var state = new PhysiologicalModel.IntegrationState(0.22, 0.0001, 0.5, 0.1, 0.0, 0.0);
        var loaded = model.AdvanceCirculationInterval(in state, 0.25, 0.0, 0.0, -6.0, settings);
        _output.WriteLine(
            $"lower-floor load converged={loaded.Converged} segments={loaded.Segments.Length} " +
            $"lowerEntries=[{string.Join(",", loaded.LowerBoundEntries.Select(e => e.ToString("R", CultureInfo.InvariantCulture)))}] " +
            $"lowerReleases=[{string.Join(",", loaded.LowerBoundReleases.Select(e => e.ToString("R", CultureInfo.InvariantCulture)))}] " +
            $"violations=[{string.Join(";", loaded.StateViolations)}]");
        Assert.True(loaded.Converged,
            $"lower-floor load did not converge: violations " +
            $"[{string.Join("; ", loaded.StateViolations)}] crossings " +
            $"[{string.Join("; ", loaded.ModeCrossings)}].");
        Assert.Empty(loaded.ModeCrossings);
        Assert.Empty(loaded.StateViolations);
        AssertAcceptedCoverage(loaded, 0.25);
        AssertPhysicalStages(loaded);
        var entry = Assert.Single(loaded.LowerBoundEntries);
        Assert.True(entry is > 0.0 and <= 0.25);
        Assert.Contains(loaded.Segments, segment => segment.LowerBound);

        var loadedFinal = loaded.Final;
        var released = model.AdvanceCirculationInterval(in loadedFinal, 0.25, 0.0, 0.0, 1.0, settings);
        Assert.True(released.Converged);
        Assert.Empty(released.StateViolations);
        AssertAcceptedCoverage(released, 0.25);
        AssertPhysicalStages(released);
        Assert.NotEmpty(released.LowerBoundReleases);
        Assert.True(released.Final.BloodLower > 0.0,
            $"released lower {released.Final.BloodLower:R} did not leave zero.");
    }

    [Theory]
    [InlineData(-1.0)]
    [InlineData(-5.0)]
    public void NegativeBloodCoordinatesAreRejected(double deficit)
    {
        var settings = LogicSettings.Default;
        var model = new GEffectsLogicInstance(new DtStabilityLogger()).PhysModel;
        var belowFloor = new PhysiologicalModel.IntegrationState(
            settings.MinHeadBloodFraction + deficit, 0.6, 1.0, 0.0, 0.0, 0.0);
        var headResult = model.AdvanceCirculationInterval(in belowFloor, 0.25, 0.0, 0.0, 1.0, settings);
        Assert.False(headResult.Converged);
        Assert.NotEmpty(headResult.StateViolations);

        var belowZero = new PhysiologicalModel.IntegrationState(0.3, deficit, 1.0, 0.0,
            0.0, 0.0);
        var lowerResult = model.AdvanceCirculationInterval(in belowZero, 0.25, 0.0, 0.0, 1.0, settings);
        Assert.False(lowerResult.Converged);
        Assert.NotEmpty(lowerResult.StateViolations);
    }

    [Theory]
    [InlineData(-5e-6)]
    [InlineData(1.000005)]
    public void OutOfBoundsInitialCardioIsRejected(double cardio)
    {
        var settings = LogicSettings.Default;
        var model = new GEffectsLogicInstance(new DtStabilityLogger()).PhysModel;
        var state = new PhysiologicalModel.IntegrationState(settings.RestingBloodHead,
            settings.RestingBloodLower, 1.0, cardio, 0.0, 0.0);
        var result = model.AdvanceCirculationInterval(in state, 0.25, 0.0, 0.0, 1.0, settings);
        Assert.False(result.Converged);
        Assert.NotEmpty(result.StateViolations);
        Assert.Empty(result.Segments);
        Assert.Empty(result.Stages);
    }

    [Fact]
    public void HeadwardBoundaryFollowsActualHeadDerivative()
    {
        var settings = LogicSettings.Default;
        var model = new GEffectsLogicInstance(new DtStabilityLogger()).PhysModel;
        var boundary = new PhysiologicalModel.IntegrationState(
            settings.RestingBloodHead, 0.7, 1.0, 0.0, 0.0, 0.0)
        {
            StrainingLevel = 1.0
        };
        var rates = new double[PhysiologicalModel.CirculationDimensions];
        var mode = PhysiologicalModel.EvaluateCirculation(in boundary, 0.0, 0.0, 1.0,
            settings, rates, null);
        Assert.True(rates[0] > 0.0,
            $"head rate {rates[0]:R} should be positive at the boundary state.");
        Assert.True((mode & PhysiologicalModel.CirculationMode.HeadOverfilled) != 0,
            $"mode {mode} should select the headward branch when head' > 0 at rest level.");

        var endpoints = new List<double[]>();
        foreach (var dt in CandidateDt)
        {
            var state = boundary;
            var offset = 0.0;
            while (offset < 1.0 - 1e-12)
            {
                var step = Math.Min(dt, 1.0 - offset);
                var result = model.AdvanceCirculationInterval(in state, step, 0.0, 0.0, 1.0, settings);
                Assert.True(result.Converged,
                    $"boundary dt {dt:R} at {offset:R}s: residual " +
                    $"{result.MaxScaledResidual:R} crossings " +
                    $"[{string.Join("; ", result.ModeCrossings)}] violations " +
                    $"[{string.Join("; ", result.StateViolations)}].");
                Assert.Empty(result.ModeCrossings);
                Assert.Empty(result.StateViolations);
                AssertAcceptedCoverage(result, step);
                AssertPhysicalStages(result);
                Assert.Equal(state, result.Initial);
                state = result.Final;
                offset += step;
            }

            endpoints.Add(ExtendedChannels(state));
        }

        for (var channel = 0; channel < ExtendedChannelNames.Length; channel++)
        {
            var minimum = endpoints.Min(e => e[channel]);
            var maximum = endpoints.Max(e => e[channel]);
            Assert.True(maximum - minimum <= StateSpreadTolerance,
                $"boundary {ExtendedChannelNames[channel]} at 1s: min {minimum:R}, " +
                $"max {maximum:R}, spread {maximum - minimum:R}.");
        }
    }

    [Fact]
    public void ModeTransitionAtIntervalEndpointIsDeterministic()
    {
        var settings = LogicSettings.Default;
        var model = new GEffectsLogicInstance(new DtStabilityLogger()).PhysModel;
        var initial = new PhysiologicalModel.IntegrationState(0.12, 0.52, 1.7, 0.1, 0.0, 0.0)
        {
            StrainingLevel = 0.8,
            StrainingFatigue = 0.2,
            GSuitFatigue = 0.1
        };
        var probe = model.AdvanceCirculationInterval(in initial, 1.0, 0.0, 0.0, 5.0, settings);
        Assert.True(probe.Converged,
            $"probe: crossings [{string.Join("; ", probe.ModeCrossings)}] " +
            $"violations [{string.Join("; ", probe.StateViolations)}].");
        Assert.NotEmpty(probe.ModeTransitions);
        var crossing = probe.ModeTransitions[0];
        Assert.True(crossing is > 0.0 and < 1.0);

        var exactEnd = model.AdvanceCirculationInterval(in initial, crossing, 0.0, 0.0, 5.0, settings);
        Assert.True(exactEnd.Converged,
            $"exact-end call: crossings [{string.Join("; ", exactEnd.ModeCrossings)}] " +
            $"violations [{string.Join("; ", exactEnd.StateViolations)}].");
        AssertAcceptedCoverage(exactEnd, crossing);
        AssertPhysicalStages(exactEnd);
        Assert.Equal(initial, exactEnd.Initial);
        var rerun = model.AdvanceCirculationInterval(in initial, crossing, 0.0, 0.0, 5.0, settings);
        Assert.Equal(exactEnd.Final, rerun.Final);

        var nearEnd = model.AdvanceCirculationInterval(in initial, crossing + 1e-6, 0.0, 0.0, 5.0,
            settings);
        Assert.True(nearEnd.Converged,
            $"near-end call: crossings [{string.Join("; ", nearEnd.ModeCrossings)}] " +
            $"violations [{string.Join("; ", nearEnd.StateViolations)}].");
        AssertAcceptedCoverage(nearEnd, crossing + 1e-6);
        AssertPhysicalStages(nearEnd);
        Assert.Equal(initial, nearEnd.Initial);
        Assert.NotEmpty(nearEnd.ModeTransitions);
    }

    [Fact]
    public void FullIntervalModeRestingHeadBoundaryLocalizesOnce()
    {
        var settings = LogicSettings.Default;
        var initial = new PhysiologicalModel.IntegrationState(
            0.19919102341711858, 0.4592791369019615, 1.2685094794503124,
            0.5847021136804541, 0.005062584299589358, 0.0)
        {
            StrainingLevel = 3.7790233266268377e-6,
            StrainingFatigue = 0.9488171597463394,
            GSuitFatigue = 0.6723834871741656,
            BloodO2Head = 0.9335027827825298,
            BloodO2Core = 0.9792215161767513,
            BloodO2Lower = 0.9755424209144228,
            ArterialOxygenation = 1.0,
            ConsciousnessLevel = 0.529748042300978,
            VisualGrayscaleLevel = 0.002632893494324454,
            VisualTunnelVisionLevel = 0.1838856592383788,
            VisualRedoutLevel = 0.0,
            VisualLoCLevel = 0.0
        };
        const double duration = 0.20256244765573198;
        const double gz = -1.6127926155320573;
        var model = new GEffectsLogicInstance(settings: settings).PhysModel;
        var result = AdvanceInterval(in initial, duration, 0.0, 0.0, gz,
            settings);

        Assert.True(result.Converged,
            $"frame561 interval rejected: crossings " +
            $"[{string.Join("; ", result.ModeCrossings)}] violations " +
            $"[{string.Join("; ", result.StateViolations)}].");
        AssertAcceptedCoverage(result, duration);
        AssertPhysicalStages(result);
        // Resolution-driven subdivision adds a fourth segment whose
        // BloodSegmentBoundary at ~0.0338s is an integration-internal split,
        // not a coincident-mode event; the physical triad still localizes once
        // at ~0.0362s.
        Assert.Equal(4, result.Segments.Length);
        var rootTolerance = PhysiologicalModel.PressureBoundRootToleranceSeconds;
        var modeTransition = Assert.Single(result.Events,
            item => item.Kind == "ModeTransition0");
        var perfusionRange = Assert.Single(result.Events,
            item => item.Kind == "PerfusionRangeBound");
        var bloodSegmentBoundaries = result.Events
            .Where(item => item.Kind == "BloodSegmentBoundary").ToArray();
        Assert.Equal(2, bloodSegmentBoundaries.Length);
        var bloodSegmentBoundary = Assert.Single(bloodSegmentBoundaries,
            item => Math.Abs(item.Offset - modeTransition.Offset) <= rootTolerance);
        Assert.True(Math.Abs(perfusionRange.Offset - modeTransition.Offset) <= rootTolerance,
            $"perfusion range offset {perfusionRange.Offset:R} vs mode transition " +
            $"{modeTransition.Offset:R} exceeds {rootTolerance:R}.");
        Assert.True(Math.Abs(bloodSegmentBoundary.Offset - modeTransition.Offset) <=
                    rootTolerance,
            $"blood segment offset {bloodSegmentBoundary.Offset:R} vs mode transition " +
            $"{modeTransition.Offset:R} exceeds {rootTolerance:R}.");

        var partitioned = RunFullPath(0.0, 0.0, gz,
            [0.5 * duration, 0.5 * duration], settings, initial);
        var headSpread = Math.Abs(result.Final.BloodHead - partitioned.BloodHead);
        Assert.True(headSpread <= StateSpreadTolerance,
            $"head spread {headSpread:R}: whole {result.Final.BloodHead:R}, " +
            $"partitioned {partitioned.BloodHead:R}.");
        var headO2Spread = Math.Abs(result.Final.BloodO2Head - partitioned.BloodO2Head);
        Assert.True(headO2Spread <= StateSpreadTolerance,
            $"head O2 spread {headO2Spread:R}: whole {result.Final.BloodO2Head:R}, " +
            $"partitioned {partitioned.BloodO2Head:R}.");
        var consciousnessSpread =
            Math.Abs(result.Final.ConsciousnessLevel - partitioned.ConsciousnessLevel);
        Assert.True(consciousnessSpread <= StateSpreadTolerance,
            $"consciousness spread {consciousnessSpread:R}: " +
            $"whole {result.Final.ConsciousnessLevel:R}, " +
            $"partitioned {partitioned.ConsciousnessLevel:R}.");
        var tunnelVisionSpread =
            Math.Abs(result.Final.VisualTunnelVisionLevel -
                     partitioned.VisualTunnelVisionLevel);
        Assert.True(tunnelVisionSpread <= StateSpreadTolerance,
            $"tunnel vision spread {tunnelVisionSpread:R}: " +
            $"whole {result.Final.VisualTunnelVisionLevel:R}, " +
            $"partitioned {partitioned.VisualTunnelVisionLevel:R}.");
    }

    [Fact]
    public void CardioThresholdCrossingLocalizesAndPartitionsAgree()
    {
        var settings = LogicSettings.Default;
        var model = new GEffectsLogicInstance(new DtStabilityLogger()).PhysModel;
        var initial = new PhysiologicalModel.IntegrationState(0.12, 0.52, 1.7, 0.0, 0.0, 0.0)
        {
            StrainingLevel = 0.8,
            StrainingFatigue = 0.2,
            GSuitFatigue = 0.1
        };

        var whole = model.AdvanceCirculationInterval(in initial, 1.0, 0.0, 0.0, 5.0, settings);
        Assert.True(whole.Converged,
            $"whole call: crossings [{string.Join("; ", whole.ModeCrossings)}] " +
            $"violations [{string.Join("; ", whole.StateViolations)}].");
        Assert.Empty(whole.ModeCrossings);
        Assert.Empty(whole.StateViolations);
        AssertAcceptedCoverage(whole, 1.0);
        AssertPhysicalStages(whole);
        Assert.All(whole.Stages, stage => Assert.True(stage.CardioFatigue >= 0.0,
            $"negative cardio stage {stage.CardioFatigue:R}."));
        Assert.NotEmpty(whole.ModeTransitions);
        Assert.True(whole.Segments.Length >= 2,
            "cardio threshold crossing accepted inside a whole-interval segment.");

        var state = initial;
        for (var step = 0; step < 100; step++)
        {
            var result = model.AdvanceCirculationInterval(in state, 0.01, 0.0, 0.0, 5.0, settings);
            Assert.True(result.Converged,
                $"partition step {step + 1}: crossings " +
                $"[{string.Join("; ", result.ModeCrossings)}] violations " +
                $"[{string.Join("; ", result.StateViolations)}].");
            AssertAcceptedCoverage(result, 0.01);
            AssertPhysicalStages(result);
            Assert.All(result.Stages, stage => Assert.True(stage.CardioFatigue >= 0.0,
                $"negative cardio stage {stage.CardioFatigue:R}."));
            state = result.Final;
        }

        var wholeChannels = ExtendedChannels(whole.Final);
        var partitionedChannels = ExtendedChannels(state);
        for (var channel = 0; channel < ExtendedChannelNames.Length; channel++)
            Assert.True(
                Math.Abs(wholeChannels[channel] - partitionedChannels[channel]) <=
                StateSpreadTolerance,
                $"{ExtendedChannelNames[channel]}: whole {wholeChannels[channel]:R} " +
                $"vs partitioned {partitionedChannels[channel]:R}.");
    }

    [Fact]
    public void CardioThresholdExactBoundarySelectsDerivativeSide()
    {
        var settings = LogicSettings.Default;
        var threshold = 1.0 + settings.CardioFatigueHrElevationThreshold;
        var rates = new double[PhysiologicalModel.CirculationDimensions];

        var rising = new PhysiologicalModel.IntegrationState(0.12, 0.52, threshold, 0.0,
            0.0, 0.0);
        var mode = PhysiologicalModel.EvaluateCirculation(in rising, 0.0, 0.0, 5.0,
            settings, rates, null);
        Assert.True(rates[2] > 0.0,
            $"hr rate {rates[2]:R} should be positive at the threshold state.");
        Assert.True((mode & PhysiologicalModel.CirculationMode.CardioBuild) != 0,
            $"mode {mode} should select cardio build at threshold with rising HR.");

        var falling = new PhysiologicalModel.IntegrationState(0.26, 0.30, threshold, 0.4,
            0.0, 0.0);
        mode = PhysiologicalModel.EvaluateCirculation(in falling, 0.0, 0.0, -4.0,
            settings, rates, null);
        Assert.True(rates[2] < 0.0,
            $"hr rate {rates[2]:R} should be negative at the overfilled threshold state.");
        Assert.True((mode & PhysiologicalModel.CirculationMode.CardioBuild) == 0,
            $"mode {mode} should select cardio recovery at threshold with falling HR.");
    }

    [Fact]
    public void CardioRecoveryAfterThresholdFollowsExactDecay()
    {
        var settings = LogicSettings.Default;
        var threshold = 1.0 + settings.CardioFatigueHrElevationThreshold;
        var model = new GEffectsLogicInstance(new DtStabilityLogger()).PhysModel;
        var initial = new PhysiologicalModel.IntegrationState(0.26, 0.30, 2.0, 0.4, 0.0, 0.0);
        var found = false;
        var atEvent = initial;
        var offset = 0.0;
        var state = initial;
        while (!found && offset < 10.0 - 1e-12)
        {
            var probe = model.AdvanceCirculationInterval(in state, 1.0, 0.0, 0.0, -4.0, settings);
            Assert.True(probe.Converged,
                $"probe at {offset:R}s: crossings " +
                $"[{string.Join("; ", probe.ModeCrossings)}] violations " +
                $"[{string.Join("; ", probe.StateViolations)}].");
            AssertAcceptedCoverage(probe, 1.0);
            AssertPhysicalStages(probe);
            foreach (var segment in probe.Segments)
            {
                if (Math.Abs(segment.Final.HeartRateMultiplier - threshold) > 1e-8)
                    continue;
                found = true;
                atEvent = segment.Final;
                break;
            }

            state = probe.Final;
            offset += 1.0;
        }

        Assert.True(found,
            "no segment ended at the cardio threshold within 10 seconds.");

        var tail = model.AdvanceCirculationInterval(in atEvent, 1.0, 0.0, 0.0, -4.0, settings);
        Assert.True(tail.Converged,
            $"tail: crossings [{string.Join("; ", tail.ModeCrossings)}] " +
            $"violations [{string.Join("; ", tail.StateViolations)}].");
        AssertAcceptedCoverage(tail, 1.0);
        AssertPhysicalStages(tail);
        Assert.All(tail.Stages, stage => Assert.True(stage.CardioFatigue >= 0.0,
            $"negative cardio stage {stage.CardioFatigue:R}."));
        var expected = atEvent.CardioFatigue *
                       Math.Exp(-1.0 / settings.CardioFatigueRecoveryTau);
        Assert.True(Math.Abs(tail.Final.CardioFatigue - expected) <= 1e-12,
            $"cardio {tail.Final.CardioFatigue:R} vs exact decay {expected:R} " +
            $"from {atEvent.CardioFatigue:R} over 1s.");
    }

    [Fact]
    public void FullIntervalMatchesWrapperUpdateAtSupportedDt()
    {
        var settings = LogicSettings.Default;
        foreach (var dt in new[] { 1.0, 0.997 })
        {
            var directModel = new GEffectsLogicInstance().PhysModel;
            var directInitial = directModel.CaptureIntervalState();
            var direct = AdvanceInterval(in directInitial, dt, 0.5, 0.3, 3.0,
                settings);
            Assert.True(direct.Converged,
                $"dt {dt:R}: full interval rejected: " +
                $"crossings [{string.Join("; ", direct.ModeCrossings)}] " +
                $"violations [{string.Join("; ", direct.StateViolations)}].");

            var instance = new GEffectsLogicInstance();
            instance.Update(dt, 0.5, 0.3, 3.0);
            var wrapped = instance.PhysModel.CaptureIntervalState();
            Assert.Equal(direct.Final.BloodHead, wrapped.BloodHead);
            Assert.Equal(direct.Final.BloodLower, wrapped.BloodLower);
            Assert.Equal(direct.Final.HeartRateMultiplier, wrapped.HeartRateMultiplier);
            Assert.Equal(direct.Final.BloodO2Head, wrapped.BloodO2Head);
            Assert.Equal(direct.Final.BloodO2Core, wrapped.BloodO2Core);
            Assert.Equal(direct.Final.BloodO2Lower, wrapped.BloodO2Lower);
            Assert.Equal(direct.Final.ConsciousnessLevel, wrapped.ConsciousnessLevel);
            Assert.Equal(direct.Final.VisualTunnelVisionLevel,
                wrapped.VisualTunnelVisionLevel);
            Assert.Equal(direct.Final.VisualRedoutLevel, wrapped.VisualRedoutLevel);
            Assert.Equal(direct.Final.VisualLoCLevel, wrapped.VisualLoCLevel);
            Assert.Equal(direct.Final.SuddenLoCAccumulator, wrapped.SuddenLoCAccumulator);
        }
    }

    [Fact]
    public void FullIntervalInstantaneousRedoutUsesRedoutTarget()
    {
        var model = new GEffectsLogicInstance().PhysModel;
        var state = model.CaptureIntervalState() with
        {
            BloodHead = 0.24,
            BloodLower = 0.41,
            VisualRedoutLevel = 0.0
        };
        var settings = LogicSettings.Default with { VisualRedoutInTau = 0.0 };
        var result = AdvanceInterval(in state, 0.01, 0.0, 0.0, 1.0, settings);

        Assert.True(result.Converged,
            $"instantaneous redout update rejected: " +
            $"crossings [{string.Join("; ", result.ModeCrossings)}] " +
            $"violations [{string.Join("; ", result.StateViolations)}].");
        Assert.True(result.Final.VisualRedoutLevel > 0.0,
            "instantaneous redout should follow the head-overfill target.");
    }

    [Fact]
    public void FullIntervalConstantRedoutTargetMatchesClosedFormAndComposition()
    {
        var settings = LogicSettings.Default;
        var state = new GEffectsLogicInstance().PhysModel.CaptureIntervalState() with
        {
            BloodHead = 0.30,
            BloodLower = 0.35,
            ConsciousnessLevel = 0.8,
            VisualRedoutLevel = 0.2
        };
        const double duration = 0.25;
        var targetHead = settings.RestingBloodHead *
                         (1.0 + settings.VisualRedoutFullHeadBloodOverfill);

        var wholeModel = new GEffectsLogicInstance().PhysModel;
        var whole = AdvanceInterval(in state, duration, 0.0, 0.0, 1.0,
            settings);
        Assert.True(whole.Converged,
            $"whole redout update rejected: " +
            $"crossings [{string.Join("; ", whole.ModeCrossings)}] " +
            $"violations [{string.Join("; ", whole.StateViolations)}].");
        AssertVisualRedoutBounds(whole);

        var splitModel = new GEffectsLogicInstance().PhysModel;
        var first = AdvanceInterval(in state, 0.1, 0.0, 0.0, 1.0,
            settings);
        Assert.True(first.Converged,
            $"first redout piece rejected: " +
            $"crossings [{string.Join("; ", first.ModeCrossings)}] " +
            $"violations [{string.Join("; ", first.StateViolations)}].");
        AssertVisualRedoutBounds(first);
        var firstFinal = first.Final;
        var second = AdvanceInterval(in firstFinal, 0.15, 0.0, 0.0, 1.0,
            settings);
        Assert.True(second.Converged,
            $"second redout piece rejected: " +
            $"crossings [{string.Join("; ", second.ModeCrossings)}] " +
            $"violations [{string.Join("; ", second.StateViolations)}].");
        AssertVisualRedoutBounds(second);

        bool Saturated(double head) => head >= targetHead;
        foreach (var segment in whole.Segments)
        {
            Assert.True(Saturated(segment.Initial.BloodHead));
            Assert.True(Saturated(segment.Final.BloodHead));
            Assert.All(segment.Stages, stage => Assert.True(Saturated(stage.BloodHead)));
        }
        foreach (var segment in first.Segments.Concat(second.Segments))
        {
            Assert.True(Saturated(segment.Initial.BloodHead));
            Assert.True(Saturated(segment.Final.BloodHead));
            Assert.All(segment.Stages, stage => Assert.True(Saturated(stage.BloodHead)));
        }

        var expected = 1.0 + (state.VisualRedoutLevel - 1.0) *
            Math.Exp(-duration / settings.VisualRedoutInTau);
        Assert.True(Math.Abs(whole.Final.VisualRedoutLevel - expected) <= 1e-12,
            $"whole redout {whole.Final.VisualRedoutLevel:R} vs closed form {expected:R}.");
        Assert.True(Math.Abs(second.Final.VisualRedoutLevel - expected) <= 1e-12,
            $"composed redout {second.Final.VisualRedoutLevel:R} vs closed form {expected:R}.");
    }

    [Fact]
    public void FullIntervalHeadRangeFindsAnalyticCubicExtremaAndCoefficientOrigin()
    {
        var coefficients = new[] { 0.19, 0.2, -0.2, 0.0 };
        var state = default(PhysiologicalModel.IntegrationState);
        var segment = new PhysiologicalModel.IntegrationSegment(
            0.0, 1.0, state, state, Array.Empty<PhysiologicalModel.IntegrationState>(),
            PhysiologicalModel.BloodBounds.None, coefficients, Array.Empty<double>(),
            Array.Empty<double>(), Array.Empty<double>())
        {
            CoefficientStart = 0.0
        };
        Assert.Equal(0.19, NumericalMath.EvaluateCubic(coefficients, 0.0), 12);
        Assert.Equal(0.19, NumericalMath.EvaluateCubic(coefficients, 1.0), 12);
        Assert.True(PhysiologicalModel.TryHeadRange([segment], 0.0, 1.0,
            out var minimum, out var maximum));
        Assert.True(Math.Abs(minimum - 0.19) <= 1e-12,
            $"minimum {minimum:R} vs analytic 0.19.");
        Assert.True(Math.Abs(maximum - 0.24) <= 1e-12,
            $"maximum {maximum:R} vs analytic 0.24.");

        var shiftedSegment = segment with
        {
            StartOffset = 1.25,
            Duration = 0.5,
            CoefficientStart = 1.0
        };
        Assert.True(PhysiologicalModel.TryHeadRange([shiftedSegment], 1.25, 1.75,
            out minimum, out maximum));
        Assert.True(Math.Abs(minimum - 0.2275) <= 1e-12,
            $"partial-range minimum {minimum:R} vs analytic 0.2275.");
        Assert.True(Math.Abs(maximum - 0.24) <= 1e-12,
            $"partial-range maximum {maximum:R} vs analytic 0.24.");
    }

    [Fact]
    public void FullIntervalRestingHeadPositiveGRedoutUsesExactOutTauDecay()
    {
        var settings = LogicSettings.Default;
        var initial = new GEffectsLogicInstance().PhysModel.CaptureIntervalState() with
        {
            VisualRedoutLevel = 0.6
        };
        const double duration = 0.25;
        var circulation = new GEffectsLogicInstance().PhysModel
            .AdvanceCirculationInterval(in initial, duration, 0.0, 0.0, 5.0, settings);
        Assert.True(circulation.Converged);
        Assert.True(PhysiologicalModel.TryHeadRange(circulation.Segments, 0.0,
            duration, out _, out var maximumHead));
        var redoutOnsetHead = settings.RestingBloodHead *
            (1.0 + settings.VisualRedoutOnsetHeadBloodOverfill);
        Assert.True(maximumHead < redoutOnsetHead,
            $"head maximum {maximumHead:R} did not " +
            $"certify zero redout target below {redoutOnsetHead:R}.");

        var model = new GEffectsLogicInstance().PhysModel;
        var result = AdvanceInterval(in initial, duration, 0.0, 0.0, 5.0,
            settings);
        Assert.True(result.Converged,
            $"positive-G visual update rejected: " +
            $"crossings [{string.Join("; ", result.ModeCrossings)}] " +
            $"violations [{string.Join("; ", result.StateViolations)}].");
        AssertAcceptedCoverage(result, duration);
        AssertPhysicalStages(result);
        foreach (var segment in result.Segments)
        {
            for (var stage = 0; stage < segment.Stages.Length; stage++)
            {
                var time = segment.StartOffset +
                    NumericalMath.RadauC[stage] * segment.Duration;
                var expected = initial.VisualRedoutLevel *
                    Math.Exp(-time / settings.VisualRedoutOutTau);
                Assert.True(Math.Abs(segment.Stages[stage].VisualRedoutLevel - expected) <=
                            1e-12,
                    $"redout stage at {time:R} was " +
                    $"{segment.Stages[stage].VisualRedoutLevel:R}, expected {expected:R}.");
            }

            var endTime = segment.StartOffset + segment.Duration;
            var expectedEnd = initial.VisualRedoutLevel *
                Math.Exp(-endTime / settings.VisualRedoutOutTau);
            Assert.True(Math.Abs(segment.Final.VisualRedoutLevel - expectedEnd) <= 1e-12,
                $"redout endpoint at {endTime:R} was " +
                $"{segment.Final.VisualRedoutLevel:R}, expected {expectedEnd:R}.");
        }
    }

    [Fact]
    public void FullIntervalOverfilledHeadVisualChannelsUseExactOutTauWhenRangeStaysHigh()
    {
        var settings = LogicSettings.Default;
        var initial = new GEffectsLogicInstance().PhysModel.CaptureIntervalState() with
        {
            BloodHead = 0.23,
            BloodLower = 0.45,
            VisualTunnelVisionLevel = 0.6,
            VisualGrayscaleLevel = 0.4
        };
        const double duration = 0.25;
        var circulation = new GEffectsLogicInstance().PhysModel
            .AdvanceCirculationInterval(in initial, duration, 0.0, 0.0, -5.0, settings);
        Assert.True(circulation.Converged);
        Assert.True(PhysiologicalModel.TryHeadRange(circulation.Segments, 0.0,
            duration, out var minimumHead, out _));

        var model = new GEffectsLogicInstance().PhysModel;
        var result = AdvanceInterval(in initial, duration, 0.0, 0.0, -5.0,
            settings);
        Assert.True(result.Converged,
            $"negative-G visual update rejected: " +
            $"crossings [{string.Join("; ", result.ModeCrossings)}] " +
            $"violations [{string.Join("; ", result.StateViolations)}].");
        AssertAcceptedCoverage(result, duration);
        AssertPhysicalStages(result);
        Assert.True(minimumHead > settings.RestingBloodHead,
            $"head minimum {minimumHead:R} did not stay above resting " +
            $"{settings.RestingBloodHead:R}.");

        foreach (var segment in result.Segments)
        {
            for (var stage = 0; stage < segment.Stages.Length; stage++)
            {
                var time = segment.StartOffset +
                    NumericalMath.RadauC[stage] * segment.Duration;
                var expectedTunnel = initial.VisualTunnelVisionLevel *
                    Math.Exp(-time / settings.VisualTunnelVisionOutTau);
                var expectedGrayscale = initial.VisualGrayscaleLevel *
                    Math.Exp(-time / settings.VisualGrayscaleOutTau);
                Assert.True(Math.Abs(segment.Stages[stage].VisualTunnelVisionLevel -
                                     expectedTunnel) <= 1e-12,
                    $"tunnel stage at {time:R} was " +
                    $"{segment.Stages[stage].VisualTunnelVisionLevel:R}, " +
                    $"expected {expectedTunnel:R}.");
                Assert.True(Math.Abs(segment.Stages[stage].VisualGrayscaleLevel -
                                     expectedGrayscale) <= 1e-12,
                    $"grayscale stage at {time:R} was " +
                    $"{segment.Stages[stage].VisualGrayscaleLevel:R}, " +
                    $"expected {expectedGrayscale:R}.");
            }

            var endTime = segment.StartOffset + segment.Duration;
            var expectedTunnelEnd = initial.VisualTunnelVisionLevel *
                Math.Exp(-endTime / settings.VisualTunnelVisionOutTau);
            var expectedGrayscaleEnd = initial.VisualGrayscaleLevel *
                Math.Exp(-endTime / settings.VisualGrayscaleOutTau);
            Assert.True(Math.Abs(segment.Final.VisualTunnelVisionLevel -
                                 expectedTunnelEnd) <= 1e-12,
                $"tunnel endpoint at {endTime:R} was " +
                $"{segment.Final.VisualTunnelVisionLevel:R}, " +
                $"expected {expectedTunnelEnd:R}.");
            Assert.True(Math.Abs(segment.Final.VisualGrayscaleLevel -
                                 expectedGrayscaleEnd) <= 1e-12,
                $"grayscale endpoint at {endTime:R} was " +
                $"{segment.Final.VisualGrayscaleLevel:R}, " +
                $"expected {expectedGrayscaleEnd:R}.");
        }
    }

    [Fact]
    public void FullIntervalTunnelVisionRespondsAfterOverfillTransitionsToHypoperfusion()
    {
        var settings = LogicSettings.Default;
        var initial = new GEffectsLogicInstance().PhysModel.CaptureIntervalState() with
        {
            BloodHead = 0.30,
            BloodLower = 0.35,
            VisualTunnelVisionLevel = 0.0
        };
        const int coarseSteps = 20;
        const int fineSteps = 100;
        var coarseState = RunFullPath(0.0, 0.0, 5.0,
            Enumerable.Repeat(0.1, coarseSteps).ToArray(), settings, initial);

        Assert.True(initial.BloodHead > settings.RestingBloodHead);
        Assert.True(coarseState.BloodHead < settings.RestingBloodHead,
            $"final head {coarseState.BloodHead:R} did not enter hypoperfusion.");
        Assert.True(coarseState.VisualTunnelVisionLevel > 0.0,
            "tunnel vision did not respond after the head crossed below resting.");

        var fineState = RunFullPath(0.0, 0.0, 5.0,
            Enumerable.Repeat(0.02, fineSteps).ToArray(), settings, initial);

        Assert.True(Math.Abs(coarseState.VisualTunnelVisionLevel -
                             fineState.VisualTunnelVisionLevel) <=
                    StateSpreadTolerance,
            $"coarse tunnel vision {coarseState.VisualTunnelVisionLevel:R} vs fine " +
            $"{fineState.VisualTunnelVisionLevel:R}.");
    }

    [Fact]
    public void FullIntervalRedoutAtNegativeFiveGzRemainsPositiveAndMatchesFineCadence()
    {
        var settings = LogicSettings.Default;
        var wholeModel = new GEffectsLogicInstance().PhysModel;
        var state = wholeModel.CaptureIntervalState();
        var whole = AdvanceInterval(in state, 0.25, 0.0, 0.0, -5.0,
            settings);
        Assert.True(whole.Converged,
            $"quarter-second redout update rejected: " +
            $"crossings [{string.Join("; ", whole.ModeCrossings)}] " +
            $"violations [{string.Join("; ", whole.StateViolations)}].");
        AssertVisualRedoutBounds(whole);
        Assert.True(whole.Final.VisualRedoutLevel > 0.0,
            "negative-five-Gz redout must have a positive endpoint.");

        var fineModel = new GEffectsLogicInstance().PhysModel;
        var fineState = fineModel.CaptureIntervalState();
        for (var step = 0; step < 25; step++)
        {
            var fine = AdvanceInterval(in fineState, 0.01, 0.0, 0.0, -5.0,
                settings);
            Assert.True(fine.Converged,
                $"10ms redout update {step + 1} rejected: " +
                $"crossings [{string.Join("; ", fine.ModeCrossings)}] " +
                $"violations [{string.Join("; ", fine.StateViolations)}].");
            AssertVisualRedoutBounds(fine);
            fineState = fine.Final;
        }

        Assert.True(fineState.VisualRedoutLevel > 0.0,
            "10ms negative-five-Gz redout must have a positive endpoint.");
        Assert.True(Math.Abs(whole.Final.VisualRedoutLevel - fineState.VisualRedoutLevel) <=
                    StateSpreadTolerance,
            $"redout spread {Math.Abs(whole.Final.VisualRedoutLevel - fineState.VisualRedoutLevel):R} " +
            $"at 0.25s: dt=0.25 {whole.Final.VisualRedoutLevel:R}, " +
            $"dt=0.01 {fineState.VisualRedoutLevel:R}.");
    }

    // Bound pressure episodes cannot be reached end-to-end through
    // Update under stable transients (entry/release marks collide
    // with the homogeneous zero-target consciousness corridor), so this
    // synthesizes circulation results carrying bound entry/release marks and
    // exercises the memoized IntervalUpstream.PressureAt path directly.
    // IntervalUpstream is private, so this one unavoidable reflection seam
    // constructs it; no further reflection abstraction is added.
    [Fact]
    public void IntervalUpstreamMemoizedPressurePreservesBoundEpisodes()
    {
        var settings = LogicSettings.Default;
        var model = new GEffectsLogicInstance().PhysModel;
        var upstreamType = typeof(PhysiologicalModel).GetNestedType(
            "IntervalUpstream", BindingFlags.NonPublic)!;
        var ctor = upstreamType.GetConstructors(
            BindingFlags.NonPublic | BindingFlags.Instance).Single();
        var pressureAt = upstreamType.GetMethod("PressureAt",
            BindingFlags.NonPublic | BindingFlags.Instance)!;
        var diagnostics = upstreamType.GetProperty("Diagnostics",
            BindingFlags.NonPublic | BindingFlags.Instance)!;
        var episodesProp = upstreamType.GetProperty("PressureEpisodes",
            BindingFlags.NonPublic | BindingFlags.Instance)!;

        const double dt = 0.225;
        var initial = model.CaptureIntervalState() with
        {
            CerebralPressureImpairment = 0.9
        };
        var headOverfilled = new[] { 0.3, 0.0, 0.0, 0.0 };
        var segment = new PhysiologicalModel.IntegrationSegment(
            0.0, dt, initial, initial, [], PhysiologicalModel.BloodBounds.None,
            headOverfilled, headOverfilled, headOverfilled, headOverfilled);

        object CreateUpstream(double impairment, double[] entries,
            double[] releases)
        {
            var start = initial with { CerebralPressureImpairment = impairment };
            var circulation = new PhysiologicalModel.IntegrationResult(
                start, start, [], 0, 0.0, true, [], [],
                entries, releases, [], [], [], [], [], [],
                [], [segment], []);
            return ctor.Invoke([circulation, start, 0.0, 0.0, -5.0, settings]);
        }

        double PressureAt(object upstream, double t) =>
            (double)pressureAt.Invoke(upstream, [t])!;

        var upstream = CreateUpstream(0.9, [0.03, 0.12], [0.07, 0.18]);
        var episodes = ((double Entry, double Release)[])episodesProp.GetValue(
            upstream)!;
        _output.WriteLine($"episodes: {string.Join(";",
            episodes.Select(e => $"{e.Entry:R}-{e.Release:R}"))}");
        // Bound episodes return exactly 1.0; free spans evolve through the
        // existing convolution.
        Assert.Equal(1.0, PressureAt(upstream, 0.05));
        Assert.Equal(1.0, PressureAt(upstream, 0.15));
        var freeEarly = PressureAt(upstream, 0.01);
        Assert.True(freeEarly is > 0.9 and <= 1.0,
            $"free pressure at 0.01 was {freeEarly:R}");
        // Repeated exact coordinates are served identically by the cache.
        Assert.Equal(PressureAt(upstream, 0.08765),
            PressureAt(upstream, 0.08765));
        Assert.Equal(PressureAt(upstream, 0.01),
            PressureAt(upstream, 0.01));
        // A different upstream instance does not share cached coordinates.
        var other = CreateUpstream(0.2, [0.03, 0.12], [0.07, 0.18]);
        Assert.NotEqual(PressureAt(upstream, 0.01), PressureAt(other, 0.01));
        Assert.Equal(1.0, PressureAt(other, 0.05));
        // Initial-bound path: impairment at the bound with an overfilled
        // initial head keeps the source above decay, so the first release
        // mark opens an episode at t=0.
        var boundedStart = initial with
        {
            BloodHead = 0.3,
            CerebralPressureImpairment = 1.0
        };
        var bounded = ctor.Invoke(
        [
            new PhysiologicalModel.IntegrationResult(
                boundedStart, boundedStart, [], 0, 0.0, true, [], [],
                [], [0.1], [], [], [], [], [], [],
                [], [segment], []),
            boundedStart, 0.0, 0.0, -5.0, settings
        ]);
        Assert.Equal(1.0, PressureAt(bounded, 0.05));
        Assert.Equal(1.0, PressureAt(bounded, 0.05));
        var afterRelease = PressureAt(bounded, 0.22);
        Assert.True(double.IsFinite(afterRelease),
            $"post-release pressure {afterRelease:R}");
        // Diagnostics produced by free-span evaluation only appear on
        // misses; bound hits never run the span evaluator.
        var upstreamDiagnostics =
            (HashSet<string>)diagnostics.GetValue(bounded)!;
        Assert.Empty(upstreamDiagnostics);

        // End-to-end sanity: a sustained -5Gz bound hold through
        // Update converges with pressure pinned at the bound.
        var holdStart = model.CaptureIntervalState() with
        {
            BloodHead = 0.30,
            BloodLower = 0.35,
            CerebralPressureImpairment = 1.0
        };
        var hold = AdvanceInterval(in holdStart, 0.25, 0.0, 0.0,
            -5.0, settings);
        Assert.True(hold.Converged,
            $"bound hold rejected: " +
            $"crossings [{string.Join("; ", hold.ModeCrossings)}] " +
            $"violations [{string.Join("; ", hold.StateViolations)}].");
        AssertAcceptedCoverage(hold, 0.25);
        AssertPhysicalStages(hold);
        Assert.Equal(1.0, hold.Final.CerebralPressureImpairment);
    }

    [Fact]
    public void FullIntervalUnequalVisualTausKeepChannelsIndependentAcrossPartition()
    {
        var settings = LogicSettings.Default with
        {
            VisualTunnelVisionInTau = 1.0,
            VisualGrayscaleInTau = 20.0,
            VisualTunnelVisionOutTau = 12.0,
            VisualGrayscaleOutTau = 1.0
        };
        var initial = new GEffectsLogicInstance(settings: settings).PhysModel
            .CaptureIntervalState() with
        {
            BloodHead = 0.30,
            BloodLower = 0.35,
            VisualTunnelVisionLevel = 0.5,
            VisualGrayscaleLevel = 0.5,
            VisualRedoutLevel = 0.8
        };

        var whole = RunFullPath(0.0, 0.0, 5.0, [0.25, 0.25, 0.25, 0.25],
            settings, initial);
        var fine = RunFullPath(0.0, 0.0, 5.0,
            Enumerable.Repeat(0.02, 50).ToArray(), settings, initial);

        Assert.True(whole.BloodHead < settings.RestingBloodHead,
            $"final head {whole.BloodHead:R} did not enter hypoperfusion.");
        // Unequal in-taus: the tunnel channel tracks the shared physiological
        // target far faster than grayscale.
        Assert.True(whole.VisualTunnelVisionLevel >
                    whole.VisualGrayscaleLevel + 0.05,
            $"tunnel {whole.VisualTunnelVisionLevel:R} did not outpace " +
            $"grayscale {whole.VisualGrayscaleLevel:R} under unequal taus.");
        Assert.True(Math.Abs(whole.VisualTunnelVisionLevel -
                             fine.VisualTunnelVisionLevel) <=
                    StateSpreadTolerance,
            $"tunnel spread {Math.Abs(whole.VisualTunnelVisionLevel - fine.VisualTunnelVisionLevel):R} " +
            $"over 1s: quarters {whole.VisualTunnelVisionLevel:R} vs " +
            $"fine {fine.VisualTunnelVisionLevel:R}.");
        Assert.True(Math.Abs(whole.VisualGrayscaleLevel -
                             fine.VisualGrayscaleLevel) <=
                    StateSpreadTolerance,
            $"grayscale spread {Math.Abs(whole.VisualGrayscaleLevel - fine.VisualGrayscaleLevel):R} " +
            $"over 1s: quarters {whole.VisualGrayscaleLevel:R} vs " +
            $"fine {fine.VisualGrayscaleLevel:R}.");
        // Redout follows its own target and out-tau independently of the
        // shared physiological target.
        Assert.True(whole.VisualRedoutLevel < 0.8,
            $"redout {whole.VisualRedoutLevel:R} did not decay after the " +
            "head left overfill.");
    }

    [Fact]
    public void FullIntervalHeadOxygenAtFloorRemainsBoundedUnderLoad()
    {
        var settings = LogicSettings.Default;
        var model = new GEffectsLogicInstance().PhysModel;
        var initial = model.CaptureIntervalState() with
        {
            BloodHead = settings.MinHeadBloodFraction,
            HeartRateMultiplier = 0.0,
            BloodO2Head = settings.BrainO2Floor,
            BloodO2Core = 0.0,
            BloodO2Lower = 0.0
        };

        foreach (var dt in new[] { 0.01, 0.25, 1.0 })
        {
            var result = AdvanceInterval(in initial, dt, 0.0, 0.0, 5.0, settings);
            Assert.True(result.Converged,
                $"head oxygen floor update rejected at dt {dt:R}: " +
                $"crossings [{string.Join("; ", result.ModeCrossings)}] " +
                $"violations [{string.Join("; ", result.StateViolations)}].");
            AssertAcceptedCoverage(result, dt);
            AssertPhysicalStages(result);
            Assert.Equal(settings.BrainO2Floor,
                result.Segments[0].Stages[0].BloodO2Head);
            Assert.All(result.Stages,
                stage => Assert.InRange(stage.BloodO2Head, settings.BrainO2Floor, 1.0));
            Assert.InRange(result.Final.BloodO2Head, settings.BrainO2Floor, 1.0);
        }
    }

    [Fact]
    public void FullIntervalHeadOxygenFloorEntryMatchesPartitionedPathAndEndpoint()
    {
        var settings = LogicSettings.Default;
        var model = new GEffectsLogicInstance().PhysModel;
        var initial = model.CaptureIntervalState() with
        {
            BloodHead = settings.MinHeadBloodFraction,
            HeartRateMultiplier = 0.0,
            BloodO2Head = settings.BrainO2Floor + 3.0040120864119715e-4,
            BloodO2Core = 0.0,
            BloodO2Lower = 0.0
        };
        void AssertHeadOxygenBound(PhysiologicalModel.IntegrationResult result)
        {
            Assert.All(result.Stages,
                stage => Assert.InRange(stage.BloodO2Head, settings.BrainO2Floor, 1.0));
            Assert.InRange(result.Final.BloodO2Head, settings.BrainO2Floor, 1.0);
        }
        const double duration = 0.25;

        var whole = AdvanceInterval(in initial, duration, 0.0, 0.0, 5.0,
            settings);
        Assert.True(whole.Converged,
            $"whole head-O2 entry rejected: " +
            $"crossings [{string.Join("; ", whole.ModeCrossings)}] " +
            $"violations [{string.Join("; ", whole.StateViolations)}].");
        AssertAcceptedCoverage(whole, duration);
        AssertPhysicalStages(whole);
        var wholeEntries = whole.Events.Where(
            item => item.Kind == "OxygenBoundEntryHead").ToArray();
        var wholeEntry = Assert.Single(wholeEntries);
        Assert.Equal(settings.BrainO2Floor, wholeEntry.State.BloodO2Head);
        AssertHeadOxygenBound(whole);

        var partitionedModel = new GEffectsLogicInstance().PhysModel;
        var partitionedState = initial;
        var partitionedEntryTimes = new List<double>();
        var elapsed = 0.0;
        for (var step = 0; step < 25; step++)
        {
            var part = AdvanceInterval(in partitionedState, 0.01,
                0.0, 0.0, 5.0, settings);
            Assert.True(part.Converged,
                $"10ms head-O2 entry path step {step + 1} rejected: " +
                $"crossings [{string.Join("; ", part.ModeCrossings)}] " +
                $"violations [{string.Join("; ", part.StateViolations)}].");
            AssertAcceptedCoverage(part, 0.01);
            AssertPhysicalStages(part);
            AssertHeadOxygenBound(part);
            foreach (var entry in part.Events.Where(
                         item => item.Kind == "OxygenBoundEntryHead"))
            {
                Assert.Equal(settings.BrainO2Floor, entry.State.BloodO2Head);
                partitionedEntryTimes.Add(elapsed + entry.Offset);
            }
            partitionedState = part.Final;
            elapsed += 0.01;
        }

        var partitionedEntryTime = Assert.Single(partitionedEntryTimes);
        Assert.True(Math.Abs(wholeEntry.Offset - partitionedEntryTime) <= 0.001,
            $"head-O2 entry spread {Math.Abs(wholeEntry.Offset - partitionedEntryTime):R}: " +
            $"whole={wholeEntry.Offset:R}, partitioned={partitionedEntryTime:R}.");
        Assert.True(Math.Abs(whole.Final.BloodO2Head -
                             partitionedState.BloodO2Head) <= 0.001,
            $"head-O2 endpoint spread {Math.Abs(whole.Final.BloodO2Head - partitionedState.BloodO2Head):R}: " +
            $"whole={whole.Final.BloodO2Head:R}, partitioned={partitionedState.BloodO2Head:R}.");

        var endpointModel = new GEffectsLogicInstance().PhysModel;
        var lo = 0.01;
        var lowCandidate = AdvanceInterval(in initial, lo, 0.0, 0.0,
            5.0, settings);
        Assert.True(lowCandidate.Converged);
        AssertAcceptedCoverage(lowCandidate, lo);
        AssertPhysicalStages(lowCandidate);
        AssertHeadOxygenBound(lowCandidate);
        var endpointEntry = Assert.Single(lowCandidate.Events,
            item => item.Kind == "OxygenBoundEntryHead");
        Assert.True(Math.Abs(endpointEntry.Offset - lo) <=
                    PhysiologicalModel.PressureBoundRootToleranceSeconds,
            $"head-O2 entry {endpointEntry.Offset:R} was not at caller endpoint {lo:R}.");
        Assert.Equal(settings.BrainO2Floor, endpointEntry.State.BloodO2Head);
        Assert.Equal(settings.BrainO2Floor, lowCandidate.Final.BloodO2Head);
    }

    [Fact]
    public void FullIntervalLocContactEntryProjectsAcceptedCeiling()
    {
        var settings = LogicSettings.Default;
        var initial = new PhysiologicalModel.IntegrationState(
            0.18526366340815686, 0.46225205869644553, 1.3660669610460265,
            0.5922563896029277, 0.0013332345817441914, 0.0)
        {
            StrainingLevel = 8.230030063828039e-5,
            StrainingFatigue = 0.9685068041168222,
            GSuitFatigue = 0.6793242407149919,
            BloodO2Head = 0.8827803303915214,
            BloodO2Core = 0.9788609336493237,
            BloodO2Lower = 0.9755043536999678,
            ArterialOxygenation = 1.0,
            ConsciousnessLevel = 0.396272332536663,
            LungCompressionLevel = 0.0,
            PainLevel = 0.0,
            GyNeckFatigue = 0.0,
            GyNeckFatigueDeathDwell = 0.0,
            SuddenLoCAccumulator = 0.0,
            VisualGrayscaleLevel = 0.01228695353421653,
            VisualTunnelVisionLevel = 0.2773005776505957,
            VisualRedoutLevel = 0.0,
            VisualLoCLevel = 0.05178623811146388
        };
        const double duration = 0.20789884425180144;
        const double gz = -0.8537793413370298;
        var model = new GEffectsLogicInstance(settings: settings).PhysModel;
        var result = AdvanceInterval(in initial, duration, 0.0, 0.0, gz,
            settings);
        Assert.True(result.Converged,
            $"LoC contact update rejected: crossings " +
            $"[{string.Join("; ", result.ModeCrossings)}] violations " +
            $"[{string.Join("; ", result.StateViolations)}].");
        AssertAcceptedCoverage(result, duration);
        AssertPhysicalStages(result);
        var entry = Assert.Single(result.Events,
            item => item.Kind == "LoCContactEntry");
        Assert.Equal(0.0, entry.State.VisualLoCLevel);
        var expectedContact = initial.VisualLoCLevel / settings.VisualLoCDecreaseRate;
        var contactTolerance =
            PhysiologicalModel.PressureBoundRootToleranceSeconds + 1e-12;
        Assert.True(Math.Abs(entry.Offset - expectedContact) <= contactTolerance,
            $"LoC contact {entry.Offset:R} vs analytic contact {expectedContact:R} " +
            $"exceeds {contactTolerance:R}.");

        var fallbackInitial = initial with
        {
            ConsciousnessLevel = settings.ConsciousnessRecoveryThreshold + 1e-7
        };
        var fallback = AdvanceInterval(in fallbackInitial, duration, 0.0, 0.0,
            gz, settings);
        Assert.True(fallback.Converged,
            $"LoC fallback update rejected: crossings " +
            $"[{string.Join("; ", fallback.ModeCrossings)}] violations " +
            $"[{string.Join("; ", fallback.StateViolations)}].");
        AssertAcceptedCoverage(fallback, duration);
        AssertPhysicalStages(fallback);
        Assert.Single(fallback.Events, item => item.Kind == "LoCContactEntry");

        var partitioned = RunFullPath(0.0, 0.0, gz,
            [0.5 * duration, 0.5 * duration], settings, initial);
        var visualLoCSpread =
            Math.Abs(result.Final.VisualLoCLevel - partitioned.VisualLoCLevel);
        Assert.True(visualLoCSpread <= StateSpreadTolerance,
            $"visual LoC spread {visualLoCSpread:R} across whole and half-step paths.");
    }

    private static double LoCCeiling(double consciousness, LogicSettings settings)
    {
        var range = Math.Max(
            settings.ConsciousnessRecoveryThreshold -
            settings.ConsciousnessLossThreshold, 1e-9);
        var fraction = Math.Clamp(
            (settings.ConsciousnessRecoveryThreshold - consciousness) / range,
            0.0, 1.0);
        return Math.Pow(fraction, settings.VisualLoCConsciousnessExponent);
    }

    [Fact]
    public void FullIntervalLocContactEntryLocalizesMovingCeiling()
    {
        var settings = LogicSettings.Default;
        var model = new GEffectsLogicInstance(settings: settings).PhysModel;
        var initial = model.CaptureIntervalState() with
        {
            ConsciousnessLevel = 0.2,
            VisualLoCLevel = 0.0
        };
        var startCeiling = LoCCeiling(initial.ConsciousnessLevel, settings);
        Assert.True(startCeiling is > 0.0 and < 1.0,
            $"fixture ceiling {startCeiling:R} is not a moving nonzero ceiling.");

        const double duration = 0.5;
        var result = AdvanceInterval(in initial, duration, 0.0, 0.0, 1.0,
            settings);
        Assert.True(result.Converged,
            $"moving-ceiling LoC contact rejected: crossings " +
            $"[{string.Join("; ", result.ModeCrossings)}] violations " +
            $"[{string.Join("; ", result.StateViolations)}].");
        AssertAcceptedCoverage(result, duration);
        AssertPhysicalStages(result);
        var entry = Assert.Single(result.Events,
            item => item.Kind == "LoCContactEntry");
        Assert.True(entry.Offset is > 0.0 and <= duration,
            $"LoC contact entry {entry.Offset:R} outside (0,{duration:R}].");
        // A nonzero contact level proves this entry crossed a moving ceiling,
        // not the analytic ceiling-zero piece.
        Assert.True(entry.State.VisualLoCLevel > 0.0,
            "contact entry landed on a zero ceiling.");
        var entryCeiling = LoCCeiling(entry.State.ConsciousnessLevel, settings);
        Assert.True(Math.Abs(entry.State.VisualLoCLevel - entryCeiling) <= 1e-9,
            $"contact level {entry.State.VisualLoCLevel:R} vs ceiling " +
            $"{entryCeiling:R} at entry offset {entry.Offset:R}.");

        var partitioned = RunFullPath(0.0, 0.0, 1.0,
            [0.25, 0.25], settings, initial);
        var fine = RunFullPath(0.0, 0.0, 1.0,
            Enumerable.Repeat(0.02, 25).ToArray(), settings, initial);
        Assert.True(Math.Abs(result.Final.VisualLoCLevel -
                             partitioned.VisualLoCLevel) <= StateSpreadTolerance,
            $"visual LoC spread {Math.Abs(result.Final.VisualLoCLevel - partitioned.VisualLoCLevel):R} " +
            "across whole and half-step paths.");
        Assert.True(Math.Abs(result.Final.VisualLoCLevel -
                             fine.VisualLoCLevel) <= StateSpreadTolerance,
            $"visual LoC spread {Math.Abs(result.Final.VisualLoCLevel - fine.VisualLoCLevel):R} " +
            "across whole and fine paths.");
        Assert.True(Math.Abs(result.Final.ConsciousnessLevel -
                             fine.ConsciousnessLevel) <= StateSpreadTolerance,
            $"consciousness spread {Math.Abs(result.Final.ConsciousnessLevel - fine.ConsciousnessLevel):R} " +
            "across whole and fine paths.");
    }

    [Fact]
    public void FullIntervalLocContactCoincidentCeilingTracksWithoutEntry()
    {
        var settings = LogicSettings.Default;
        var model = new GEffectsLogicInstance(settings: settings).PhysModel;
        const double consciousness0 = 0.2;
        var initial = model.CaptureIntervalState() with
        {
            ConsciousnessLevel = consciousness0,
            VisualLoCLevel = LoCCeiling(consciousness0, settings)
        };

        const double duration = 0.5;
        var result = AdvanceInterval(in initial, duration, 0.0, 0.0, 1.0,
            settings);
        Assert.True(result.Converged,
            $"coincident LoC contact rejected: crossings " +
            $"[{string.Join("; ", result.ModeCrossings)}] violations " +
            $"[{string.Join("; ", result.StateViolations)}].");
        AssertAcceptedCoverage(result, duration);
        AssertPhysicalStages(result);
        Assert.DoesNotContain(result.Events,
            item => item.Kind == "LoCContactEntry");
        var finalCeiling = LoCCeiling(result.Final.ConsciousnessLevel, settings);
        Assert.True(Math.Abs(result.Final.VisualLoCLevel - finalCeiling) <= 1e-9,
            $"pinned visual LoC {result.Final.VisualLoCLevel:R} vs ceiling " +
            $"{finalCeiling:R}.");

        var partitioned = RunFullPath(0.0, 0.0, 1.0,
            [0.25, 0.25], settings, initial);
        Assert.True(Math.Abs(result.Final.VisualLoCLevel -
                             partitioned.VisualLoCLevel) <= StateSpreadTolerance,
            $"visual LoC spread {Math.Abs(result.Final.VisualLoCLevel - partitioned.VisualLoCLevel):R} " +
            "across whole and half-step paths.");
    }

    // LoC contact exits need a controlled consciousness trajectory across one
    // piece, which the full interval solver does not expose directly; the
    // single unavoidable reflection seam invokes ChaseLoC and reads the
    // private LoCTrajectory fields.
    private static readonly MethodInfo ChaseLoCMethod =
        typeof(PhysiologicalModel).GetMethod("ChaseLoC",
            BindingFlags.NonPublic | BindingFlags.Static)!;

    private static (bool Contact, double FirstEventTime, string FirstEventKind)
        RunChaseLoC(PhysiologicalModel.IntegrationState state,
            PhysiologicalModel.AffineSolution consciousness, double span,
            LogicSettings settings)
    {
        var trajectory = ChaseLoCMethod.Invoke(null,
            [state, consciousness, 0.0, span, settings])!;
        var trajectoryType = trajectory.GetType();
        const BindingFlags fields =
            BindingFlags.NonPublic | BindingFlags.Instance;
        return (
            (bool)trajectoryType.GetField("Contact", fields)!
                .GetValue(trajectory)!,
            (double)trajectoryType.GetField("FirstEventTime", fields)!
                .GetValue(trajectory)!,
            (string)trajectoryType.GetField("FirstEventKind", fields)!
                .GetValue(trajectory)!);
    }

    private static PhysiologicalModel.IntegrationState LocContactState(
        double consciousness, LogicSettings settings) =>
        new(0.4, 0.3, 1.0, 0.0, 0.0, 0.0)
        {
            ConsciousnessLevel = consciousness,
            VisualLoCLevel = LoCCeiling(consciousness, settings)
        };

    [Fact]
    public void ChaseLocContactExitLocalizesUpwardRateThreshold()
    {
        var settings = LogicSettings.Default;
        const double span = 2.0;
        const double consciousness0 = 0.3;
        var consciousness = new PhysiologicalModel.AffineSolution
        {
            Valid = true,
            Y0 = [consciousness0],
            Duration = span,
            ScalarOverride = new PhysiologicalModel.ScalarLagSolution(
                consciousness0, span, 1.0, 0.0)
        };
        var state = LocContactState(consciousness0, settings);

        var trajectory = RunChaseLoC(state, consciousness, span, settings);
        Assert.True(trajectory.Contact,
            "in-band initial ceiling derivative must keep contact.");
        Assert.Equal("LoCContactExit", trajectory.FirstEventKind);
        // Ceiling derivative rises through VisualLoCIncreaseRate when the
        // exponentially falling consciousness reaches 0.2:
        // 10 * fraction^2 * level = 0.5 at level 0.2 gives t = ln(1.5).
        var expected = Math.Log(1.5);
        Assert.True(
            Math.Abs(trajectory.FirstEventTime - expected) <= 1e-6,
            $"upward exit {trajectory.FirstEventTime:R} vs analytic " +
            $"{expected:R}.");
    }

    [Fact]
    public void ChaseLocContactExitLocalizesDownwardRateThreshold()
    {
        var settings = LogicSettings.Default;
        const double span = 1.0;
        // Accelerating recovery: consciousness rises cubically so the ceiling
        // falls through -VisualLoCDecreaseRate inside the probed region.
        Func<double, double> levelAt = c => 0.15 + 8.0 * c * c * c;
        var consciousness = new PhysiologicalModel.AffineSolution
        {
            Valid = true,
            Y0 = [levelAt(0.0)],
            Duration = span,
            InstantTargets = [levelAt]
        };
        var state = LocContactState(levelAt(0.0), settings);

        var trajectory = RunChaseLoC(state, consciousness, span, settings);
        Assert.True(trajectory.Contact,
            "zero initial ceiling derivative must keep contact.");
        Assert.Equal("LoCContactExit", trajectory.FirstEventKind);
        // Replicate the model's finite-difference derivative on the same
        // target and solve ceiling' == -VisualLoCDecreaseRate.
        var range = settings.ConsciousnessRecoveryThreshold -
                    settings.ConsciousnessLossThreshold;
        double CeilingDerivative(double t)
        {
            var c = t / span;
            var level = levelAt(c);
            var fraction = (settings.ConsciousnessRecoveryThreshold - level) /
                           range;
            if (fraction is <= 0.0 or >= 1.0) return 0.0;
            var delta = Math.Min(1e-6, Math.Max(1e-9, 0.5 * (1.0 - c)));
            var slope = (levelAt(c + delta) - level) / delta / span;
            return -settings.VisualLoCConsciousnessExponent *
                   Math.Pow(fraction,
                       settings.VisualLoCConsciousnessExponent - 1.0) / range *
                   slope;
        }
        var lo = 0.0;
        var hi = double.NaN;
        for (var step = 1; step <= 1000; step++)
        {
            var t = step * span / 1000.0;
            if (-settings.VisualLoCDecreaseRate - CeilingDerivative(t) > 0.0)
            {
                lo = (step - 1) * span / 1000.0;
                hi = t;
                break;
            }
            lo = t;
        }
        Assert.True(double.IsFinite(hi),
            "downward threshold crossing must exist inside the piece.");
        while (hi - lo > 1e-12)
        {
            var mid = 0.5 * (lo + hi);
            if (-settings.VisualLoCDecreaseRate - CeilingDerivative(mid) > 0.0)
                hi = mid;
            else lo = mid;
        }
        var expected = 0.5 * (lo + hi);
        Assert.True(expected is > 0.0 and < span,
            "downward threshold crossing must exist inside the piece.");
        Assert.True(
            Math.Abs(trajectory.FirstEventTime - expected) <= 1e-5,
            $"downward exit {trajectory.FirstEventTime:R} vs reference " +
            $"{expected:R}.");
    }

    [Fact]
    public void ChaseLocContactSkipsEventWhenConsciousnessGoesNonfinite()
    {
        var settings = LogicSettings.Default;
        const double span = 1.0;
        Func<double, double> levelAt = c => c < 0.5
            ? 0.15
            : double.NaN;
        var consciousness = new PhysiologicalModel.AffineSolution
        {
            Valid = true,
            Y0 = [levelAt(0.0)],
            Duration = span,
            InstantTargets = [levelAt]
        };
        var state = LocContactState(levelAt(0.0), settings);

        var trajectory = RunChaseLoC(state, consciousness, span, settings);
        Assert.Equal("", trajectory.FirstEventKind);
        Assert.True(double.IsPositiveInfinity(trajectory.FirstEventTime),
            "nonfinite derivative scans must not produce an exit event.");
    }

    [Fact]
    public void FullIntervalLocContactExitMatchesRecordedInterval()
    {
        var settings = LogicSettings.Default;
        // Frame-77 pre-frame state of the benchmark workload: a +6.28 Gz
        // interval whose ceiling outruns the upward tracking rate. The retuned
        // trajectory exits twice: a transient margin graze at ~0.057s where
        // contact is re-established, then the sustained crossing at ~0.062s.
        // Exit times re-baselined after defect-driven resolution splitting:
        // the previously recorded ~0.056/~0.062 values encoded the coarser
        // under-resolved trajectory.
        var initial = new PhysiologicalModel.IntegrationState(
            0.04305829564336118, 0.48055852104943236, 2.769829647589408,
            0.03751104080073985, 0.0, 0.0)
        {
            StrainingLevel = 0.9999999797791038,
            StrainingFatigue = 0.24446907777141275,
            GSuitFatigue = 0.0676815820537806,
            BloodO2Head = 0.39989121544316386,
            BloodO2Core = 0.9789875023740395,
            BloodO2Lower = 0.9747604022774918,
            ArterialOxygenation = 1.0,
            ConsciousnessLevel = 0.17653025502348318,
            VisualGrayscaleLevel = 0.4309121864786893,
            VisualTunnelVisionLevel = 0.775025322010744,
            VisualRedoutLevel = 0.0,
            VisualLoCLevel = 0.19333365253994816
        };
        const double duration = 0.2529090646846475;
        const double gz = 6.28023037531947;
        var model = new GEffectsLogicInstance(settings: settings).PhysModel;
        var result = AdvanceInterval(in initial, duration, 0.0, 0.0, gz,
            settings);
        Assert.True(result.Converged,
            $"recorded LoC exit update rejected: crossings " +
            $"[{string.Join("; ", result.ModeCrossings)}] violations " +
            $"[{string.Join("; ", result.StateViolations)}].");
        AssertAcceptedCoverage(result, duration);
        AssertPhysicalStages(result);
        var exits = result.Events
            .Where(item => item.Kind == "LoCContactExit")
            .Select(item => item.Offset)
            .ToArray();
        Assert.Equal(2, exits.Length);
        Assert.True(Math.Abs(exits[0] - 0.05736089757083686) <= 1e-6,
            $"first LoC contact exit {exits[0]:R} vs recorded 0.05736089757083686.");
        Assert.True(Math.Abs(exits[1] - 0.06159525897352559) <= 1e-6,
            $"second LoC contact exit {exits[1]:R} vs recorded 0.06159525897352559.");
    }

    [Fact]
    public void ScanCrossingSafeguardedRefinePreservesPlateauAndDirection()
    {
        var scanCrossing = typeof(PhysiologicalModel).GetMethods(
                BindingFlags.NonPublic | BindingFlags.Static)
            .Single(method => method.Name == "ScanCrossing" &&
                              method.GetParameters().Length == 7 &&
                              Nullable.GetUnderlyingType(
                                  method.GetParameters()[3].ParameterType) !=
                              null);
        const double tolerance =
            PhysiologicalModel.PressureBoundRootToleranceSeconds;

        (double Root, int Direction) Scan(Func<double, double> f,
            bool refine)
        {
            object?[] args =
                [f, 0.0, 1.0, null, 0, tolerance, refine];
            var root = (double)scanCrossing.Invoke(null, args)!;
            return (root, (int)args[4]!);
        }

        // Plateau: exact zeros between opposite signs; the refined and
        // bisection paths must report the same root bracket and direction.
        var positive = Scan(
            t => t < 0.4 ? 1.0 : t <= 0.6 ? 0.0 : -1.0, false);
        var positiveRefined = Scan(
            t => t < 0.4 ? 1.0 : t <= 0.6 ? 0.0 : -1.0, true);
        Assert.Equal(positive.Direction, positiveRefined.Direction);
        Assert.Equal(-1, positiveRefined.Direction);
        Assert.True(Math.Abs(positive.Root - positiveRefined.Root) <= 2e-8,
            $"plateau root {positiveRefined.Root:R} vs {positive.Root:R}.");

        var negative = Scan(
            t => t < 0.4 ? -1.0 : t <= 0.6 ? 0.0 : 1.0, false);
        var negativeRefined = Scan(
            t => t < 0.4 ? -1.0 : t <= 0.6 ? 0.0 : 1.0, true);
        Assert.Equal(negative.Direction, negativeRefined.Direction);
        Assert.Equal(1, negativeRefined.Direction);
        Assert.True(Math.Abs(negative.Root - negativeRefined.Root) <= 2e-8,
            $"reversed plateau root {negativeRefined.Root:R} vs " +
            $"{negative.Root:R}.");

        // Exact endpoint zeros return the endpoint unchanged in both modes.
        Assert.Equal(0.0, Scan(t => t, false).Root);
        Assert.Equal(0.0, Scan(t => t, true).Root);

        // Nonfinite samples invalidate the scan identically in both modes: the
        // sign change across the NaN region must not produce a crossing.
        double NaNScan(double t) => t < 0.3 ? 1.0 :
            t <= 0.7 ? double.NaN : -1.0;
        Assert.Equal(Scan(NaNScan, false), Scan(NaNScan, true));
        Assert.True(double.IsPositiveInfinity(Scan(NaNScan, true).Root));
    }

    [Fact]
    public void FullIntervalRecoverySwitchesWhenTargetFallsThroughConsciousness()
    {
        var settings = LogicSettings.Default;
        var model = new GEffectsLogicInstance().PhysModel;
        var initial = model.CaptureIntervalState() with { ConsciousnessLevel = 0.9999 };
        var result = AdvanceInterval(in initial, 1.0, 0.0, 0.0, 5.0, settings);

        Assert.True(result.Converged,
            $"falling-target recovery update rejected: " +
            $"crossings [{string.Join("; ", result.ModeCrossings)}] " +
            $"violations [{string.Join("; ", result.StateViolations)}].");
        var transitions = result.Events.Where(
            item => item.Kind == "ConsciousnessModeTransition").ToArray();
        Assert.NotEmpty(transitions);
        var transition = transitions[transitions.Length - 1];
        Assert.True(transition.Offset is > 0.0 and < 1.0,
            $"consciousness mode transition {transition.Offset:R} not inside interval.");
        var recoveringPiece = Assert.Single(result.Segments,
            segment => Math.Abs(segment.StartOffset + segment.Duration - transition.Offset) <=
                       1e-12);
        var losingPiece = Assert.Single(result.Segments,
            segment => Math.Abs(segment.StartOffset - transition.Offset) <= 1e-12);
        Assert.True(losingPiece.Final.ConsciousnessLevel <
                    losingPiece.Initial.ConsciousnessLevel,
            $"consciousness did not enter loss mode after transition at " +
            $"{transition.Offset:R}; preceding endpoint " +
            $"{recoveringPiece.Final.ConsciousnessLevel:R}, following endpoint " +
            $"{losingPiece.Final.ConsciousnessLevel:R}.");
    }

    [Fact]
    public void FullIntervalConsciousnessNearZeroRisingTargetProjectsOnceAtCursor()
    {
        var settings = LogicSettings.Default;
        var model = new GEffectsLogicInstance().PhysModel;
        var initial = model.CaptureIntervalState() with
        {
            BloodO2Head = settings.BrainO2Full - 1e-4,
            BloodO2Core = settings.CoreBloodO2Resting,
            BloodO2Lower = settings.LowerBloodO2Resting
        };
        var target = PhysiologicalModel.ConsciousnessTargetAndTau(
            PhysiologicalModel.O2Normalized(initial.BloodO2Head, settings),
            PhysiologicalModel.PerfusionNormalized(initial.BloodHead, settings),
            initial.CerebralPressureImpairment, settings).Target;
        initial = initial with { ConsciousnessLevel = target + 1e-13 };

        var result = AdvanceInterval(in initial, 0.01, 0.0, 0.0, 1.0, settings);
        Assert.True(result.Converged,
            $"near-zero rising-target update rejected: " +
            $"crossings [{string.Join("; ", result.ModeCrossings)}] " +
            $"violations [{string.Join("; ", result.StateViolations)}].");
        AssertAcceptedCoverage(result, 0.01);
        AssertPhysicalStages(result);
        var transition = Assert.Single(result.Events,
            item => item.Kind == "ConsciousnessModeTransition");
        Assert.Equal(0.0, transition.Offset);
        Assert.Equal(target, transition.State.ConsciousnessLevel);
        Assert.All(result.Stages,
            stage => Assert.InRange(stage.ConsciousnessLevel, 0.0, 1.0));
        Assert.InRange(result.Final.ConsciousnessLevel, 0.0, 1.0);
        Assert.DoesNotContain(result.Events, item =>
            item.Kind is "ConsciousnessLost" or "ConsciousnessRecovered");
    }

    [Fact]
    public void FullIntervalConsciousnessZeroTargetBranchPrecedesModeTransition()
    {
        var settings = LogicSettings.Default;
        var model = new GEffectsLogicInstance().PhysModel;
        var initial = model.CaptureIntervalState() with
        {
            BloodO2Head = 0.316,
            ConsciousnessLevel = 1e-6
        };
        var startO2 = PhysiologicalModel.O2Normalized(initial.BloodO2Head, settings);
        var startPerfusion = PhysiologicalModel.PerfusionNormalized(initial.BloodHead,
            settings);
        var rawReserve = PhysiologicalModel.ConsciousnessRawReserve(
            startO2, startPerfusion, settings);
        var target = PhysiologicalModel.ConsciousnessTargetAndTau(startO2,
            startPerfusion, initial.CerebralPressureImpairment, settings).Target;
        Assert.True(rawReserve < 0.0, $"fixture raw reserve {rawReserve:R} is not clamped.");
        Assert.Equal(0.0, target);

        const double duration = 0.1;
        var result = AdvanceInterval(in initial, duration, 0.0, 0.0, 1.0,
            settings);
        Assert.True(result.Converged,
            $"target-zero branch update rejected: " +
            $"crossings [{string.Join("; ", result.ModeCrossings)}] " +
            $"violations [{string.Join("; ", result.StateViolations)}].");
        AssertAcceptedCoverage(result, duration);
        AssertPhysicalStages(result);
        var branch = Assert.Single(result.Events,
            item => item.Kind == "ConsciousnessTargetZeroBranch");
        var branchO2 = PhysiologicalModel.O2Normalized(branch.State.BloodO2Head, settings);
        var branchPerfusion = PhysiologicalModel.PerfusionNormalized(
            branch.State.BloodHead, settings);
        var branchTarget = PhysiologicalModel.ConsciousnessTargetAndTau(branchO2,
            branchPerfusion, branch.State.CerebralPressureImpairment, settings).Target;
        Assert.True(branchTarget > 0.0,
            $"zero-branch event target {branchTarget:R} remained clamped.");
        var modeEvents = result.Events.Where(
            item => item.Kind == "ConsciousnessModeTransition").ToArray();
        Assert.NotEmpty(modeEvents);
        Assert.All(modeEvents,
            item => Assert.True(item.Offset > branch.Offset,
                $"mode transition {item.Offset:R} preceded target-zero branch " +
                $"{branch.Offset:R}."));
        Assert.All(result.Stages,
            stage => Assert.InRange(stage.ConsciousnessLevel, 0.0, 1.0));
        Assert.InRange(result.Final.ConsciousnessLevel, 0.0, 1.0);
    }

    [Fact]
    public void FullIntervalConsciousnessZeroTargetPlateauCrossesPositivelyNearEndpoint()
    {
        var settings = LogicSettings.Default;
        var initial = new GEffectsLogicInstance().PhysModel.CaptureIntervalState() with
        {
            BloodO2Head = 0.316,
            ConsciousnessLevel = 1e-6
        };
        var initialO2 = PhysiologicalModel.O2Normalized(initial.BloodO2Head, settings);
        var initialPerfusion = PhysiologicalModel.PerfusionNormalized(initial.BloodHead,
            settings);
        var initialRawReserve = PhysiologicalModel.ConsciousnessRawReserve(
            initialO2, initialPerfusion, settings);
        Assert.True(initialRawReserve < 0.0);
        Assert.Equal(0.0, PhysiologicalModel.ConsciousnessTargetAndTau(
            initialO2, initialPerfusion, initial.CerebralPressureImpairment, settings).Target);

        var probeModel = new GEffectsLogicInstance().PhysModel;
        var probe = AdvanceInterval(in initial, 0.1, 0.0, 0.0, 1.0,
            settings);
        Assert.True(probe.Converged,
            $"target-zero branch probe rejected: crossings " +
            $"[{string.Join("; ", probe.ModeCrossings)}] violations " +
            $"[{string.Join("; ", probe.StateViolations)}].");
        var probeBranch = Assert.Single(probe.Events,
            item => item.Kind == "ConsciousnessTargetZeroBranch");
        var duration = probeBranch.Offset + 1e-6;

        var nearEndModel = new GEffectsLogicInstance().PhysModel;
        var nearEnd = AdvanceInterval(in initial, duration, 0.0, 0.0, 1.0,
            settings);
        Assert.True(nearEnd.Converged,
            $"near-end target-zero branch rejected: crossings " +
            $"[{string.Join("; ", nearEnd.ModeCrossings)}] violations " +
            $"[{string.Join("; ", nearEnd.StateViolations)}].");
        var branch = Assert.Single(nearEnd.Events,
            item => item.Kind == "ConsciousnessTargetZeroBranch");
        Assert.True(duration - branch.Offset is >= 0.0 and <= 2e-6,
            $"positive raw-reserve crossing at {branch.Offset:R} was not near " +
            $"endpoint {duration:R}.");
        var firstConsciousnessEvent = nearEnd.Events
            .Where(item => item.Kind is "ConsciousnessTargetZeroBranch" or
                "ConsciousnessModeTransition")
            .OrderBy(item => item.Offset)
            .First();
        Assert.Equal("ConsciousnessTargetZeroBranch", firstConsciousnessEvent.Kind);
        var branchRawReserve = PhysiologicalModel.ConsciousnessRawReserve(
            PhysiologicalModel.O2Normalized(branch.State.BloodO2Head, settings),
            PhysiologicalModel.PerfusionNormalized(branch.State.BloodHead, settings),
            settings);
        Assert.True(branchRawReserve > 0.0,
            $"positive-direction crossing ended on nonpositive side: {branchRawReserve:R}.");
    }

    [Fact]
    public void FullIntervalIdenticalVisualTransitionsRemainSimultaneousAndOrdered()
    {
        var defaults = LogicSettings.Default;
        var settings = defaults with
        {
            VisualGrayscaleInTau = defaults.VisualTunnelVisionInTau,
            VisualGrayscaleOutTau = defaults.VisualTunnelVisionOutTau
        };
        var initial = new GEffectsLogicInstance().PhysModel.CaptureIntervalState() with
        {
            VisualTunnelVisionLevel = 1e-6,
            VisualGrayscaleLevel = 1e-6
        };
        var result = AdvanceInterval(in initial, 1.0,
            0.0, 0.0, 5.0, settings);

        Assert.True(result.Converged,
            $"coincident visual transition update rejected: crossings " +
            $"[{string.Join("; ", result.ModeCrossings)}] violations " +
            $"[{string.Join("; ", result.StateViolations)}].");
        var transitions = result.Events.Where(item =>
            item.Kind is "VisualGrayscaleTransition" or "VisualTunnelTransition").ToArray();
        Assert.True(transitions.Length >= 2 && transitions.Length % 2 == 0,
            $"visual transition events were not paired: " +
            $"[{string.Join(", ", transitions.Select(item => $"{item.Offset:R}:{item.Kind}"))}].");
        for (var index = 0; index < transitions.Length; index += 2)
        {
            Assert.Equal(SimultaneousVisualTransitionOrder,
                transitions.Skip(index).Take(2).Select(item => item.Kind));
            Assert.Equal(transitions[index].Offset, transitions[index + 1].Offset);
        }
    }

    [Fact]
    public void FullIntervalNoConsciousnessModeTransitionWhileTargetStaysZero()
    {
        var settings = LogicSettings.Default;
        var model = new GEffectsLogicInstance().PhysModel;
        var initial = model.CaptureIntervalState() with
        {
            BloodHead = settings.MinHeadBloodFraction,
            HeartRateMultiplier = 0.0,
            BloodO2Head = settings.BrainO2Blackout,
            BloodO2Core = settings.BrainO2Floor,
            BloodO2Lower = settings.BrainO2Floor,
            ConsciousnessLevel = 1e-6
        };
        var target = PhysiologicalModel.ConsciousnessTargetAndTau(
            PhysiologicalModel.O2Normalized(initial.BloodO2Head, settings),
            PhysiologicalModel.PerfusionNormalized(initial.BloodHead, settings),
            initial.CerebralPressureImpairment, settings).Target;
        Assert.Equal(0.0, target);

        var result = AdvanceInterval(in initial, 0.01, 0.0, 0.0, 5.0, settings);
        Assert.True(result.Converged,
            $"zero-target update rejected: " +
            $"crossings [{string.Join("; ", result.ModeCrossings)}] " +
            $"violations [{string.Join("; ", result.StateViolations)}].");
        AssertAcceptedCoverage(result, 0.01);
        AssertPhysicalStages(result);
        Assert.DoesNotContain(result.Events,
            item => item.Kind == "ConsciousnessTargetZeroBranch");
        Assert.DoesNotContain(result.Events,
            item => item.Kind == "ConsciousnessModeTransition");
        Assert.All(result.Stages,
            stage => Assert.True(stage.ConsciousnessLevel is > 0.0 and <= 1.0));
        Assert.InRange(result.Final.ConsciousnessLevel, 0.0, 1.0);
    }

    private static PhysiologicalModel.IntegrationState RunFullPath(double gx, double gy,
        double gz, double[] dts, LogicSettings settings,
        PhysiologicalModel.IntegrationState? initialState = null)
    {
        var model = new GEffectsLogicInstance(settings: settings).PhysModel;
        var state = initialState ?? model.CaptureIntervalState();
        foreach (var dt in dts)
        {
            var result = AdvanceInterval(in state, dt, gx, gy, gz, settings);
            Assert.True(result.Converged,
                $"gx {gx:R} gy {gy:R} gz {gz:R} dt {dt:R}: full interval rejected: " +
                $"crossings [{string.Join("; ", result.ModeCrossings)}] " +
                $"violations [{string.Join("; ", result.StateViolations)}].");
            state = result.Final;
            Assert.True(state.BloodO2Head is >= 0.0 and <= 1.0,
                $"head O2 {state.BloodO2Head:R} out of bounds after dt {dt:R}.");
            Assert.True(state.ConsciousnessLevel is >= 0.0 and <= 1.0,
                $"consciousness {state.ConsciousnessLevel:R} out of bounds after dt {dt:R}.");
        }

        return state;
    }

    private static void AssertVisualRedoutBounds(
        PhysiologicalModel.IntegrationResult result)
    {
        Assert.InRange(result.Final.VisualRedoutLevel, 0.0, 1.0);
        foreach (var segment in result.Segments)
        {
            Assert.InRange(segment.Initial.VisualRedoutLevel, 0.0, 1.0);
            Assert.InRange(segment.Final.VisualRedoutLevel, 0.0, 1.0);
            Assert.All(segment.Stages,
                stage => Assert.InRange(stage.VisualRedoutLevel, 0.0, 1.0));
        }
    }

    [Fact]
    public void FullIntervalPartitionConsistencyAtPositiveLoad()
    {
        var settings = LogicSettings.Default;
        var whole = RunFullPath(0.0, 0.0, 3.0, [1.0], settings);
        var quarters = RunFullPath(0.0, 0.0, 3.0, [0.25, 0.25, 0.25, 0.25], settings);
        var fine = RunFullPath(0.0, 0.0, 3.0,
            Enumerable.Repeat(0.01, 100).ToArray(), settings);
        var headO2Spread = Math.Max(Math.Abs(whole.BloodO2Head - fine.BloodO2Head),
            Math.Abs(quarters.BloodO2Head - fine.BloodO2Head));
        var consciousnessSpread = Math.Max(
            Math.Abs(whole.ConsciousnessLevel - fine.ConsciousnessLevel),
            Math.Abs(quarters.ConsciousnessLevel - fine.ConsciousnessLevel));
        Assert.True(headO2Spread <= StateSpreadTolerance,
            $"head O2 spread {headO2Spread:R} across cadences at gz 3 over 1s.");
        Assert.True(consciousnessSpread <= StateSpreadTolerance,
            $"consciousness spread {consciousnessSpread:R} across cadences at gz 3 over 1s.");
    }

    [Fact]
    public void FullIntervalSuddenTriggerInsideOneSecond()
    {
        var settings = LogicSettings.Default;
        var model = new GEffectsLogicInstance().PhysModel;
        var state = model.CaptureIntervalState() with { SuddenLoCAccumulator = 0.79 };
        var result = AdvanceInterval(in state, 1.0, 8.0, 8.0, 1.0, settings);
        Assert.True(result.Converged,
            $"sudden precondition rejected: " +
            $"crossings [{string.Join("; ", result.ModeCrossings)}] " +
            $"violations [{string.Join("; ", result.StateViolations)}].");
        Assert.True(result.Final.InSuddenLoC || result.Final.IsUnconscious ||
                    result.Final.IsDead ||
                    result.Events.Any(e => e.Kind == "SuddenTrigger"),
            "preconditioned accumulator 0.79 under gx 8 gy 8 must trigger within 1s.");

        var splitModel = new GEffectsLogicInstance().PhysModel;
        var half = AdvanceInterval(in state, 0.5, 8.0, 8.0, 1.0, settings);
        Assert.True(half.Converged, "first half of the partitioned sudden run rejected.");
        var wholeOffset = result.Events.First(e => e.Kind == "SuddenTrigger").Offset;
        var halfOffset = half.Events.First(e => e.Kind == "SuddenTrigger").Offset;
        Assert.True(Math.Abs(wholeOffset - halfOffset) <= 0.05,
            $"sudden trigger offset {wholeOffset:R} (1s) vs {halfOffset:R} (0.5s partition) " +
            "exceeds 0.05s cross-cadence agreement.");
    }

    [Fact]
    public void FullIntervalDeathIsPermanentAfterUnloading()
    {
        var settings = LogicSettings.Default;
        var model = new GEffectsLogicInstance().PhysModel;
        var state = model.CaptureIntervalState() with
        {
            GyNeckFatigue = settings.GyNeckFatigueDeathLevel,
            GyNeckFatigueDeathDwell = 0.9
        };
        var loaded = AdvanceInterval(in state, 1.0, 0.0, 10.0, 1.0, settings);
        Assert.True(loaded.Converged,
            $"death precondition rejected: " +
            $"crossings [{string.Join("; ", loaded.ModeCrossings)}] " +
            $"violations [{string.Join("; ", loaded.StateViolations)}].");
        var deathEvents = string.Join("; ", loaded.Events.Select(e =>
            $"{e.Offset:R}:{e.Kind}:dead={e.State.IsDead}:dwell={e.State.GyNeckFatigueDeathDwell:R}"));
        var deathSegments = string.Join("; ", loaded.Segments.Select(segment =>
            $"{segment.StartOffset:R}+{segment.Duration:R}:dead={segment.Final.IsDead}:" +
            $"neck={segment.Final.GyNeckFatigue:R}:dwell={segment.Final.GyNeckFatigueDeathDwell:R}"));
        Assert.True(loaded.Final.IsDead,
            $"partial dwell at the death level under gy 10 must complete within 1s; " +
            $"events [{deathEvents}], segments [{deathSegments}], " +
            $"final neck={loaded.Final.GyNeckFatigue:R} " +
            $"dwell={loaded.Final.GyNeckFatigueDeathDwell:R}.");
        Assert.Single(loaded.Events, e => e.Kind == "Death");

        var loadedFinal = loaded.Final;
        var unloaded = AdvanceInterval(in loadedFinal, 1.0, 0.0, 0.0, 1.0, settings);
        Assert.True(unloaded.Converged,
            $"dead unload rejected: " +
            $"crossings [{string.Join("; ", unloaded.ModeCrossings)}] " +
            $"violations [{string.Join("; ", unloaded.StateViolations)}].");
        Assert.True(unloaded.Final.IsDead, "death must be permanent after unloading.");
        Assert.Equal(0.0, unloaded.Final.ConsciousnessLevel);
        Assert.True(unloaded.Final.IsUnconscious);
        Assert.Equal(1.0, unloaded.Final.VisualLoCLevel);
        Assert.Equal(1.0, unloaded.Final.GyNeckFatigueDeathDwell);
    }

    [Fact]
    public void FullIntervalDeathCompletesAtExactEndpoint()
    {
        var settings = LogicSettings.Default;
        var model = new GEffectsLogicInstance().PhysModel;
        const double initialDwell = 0.8;
        var state = model.CaptureIntervalState() with
        {
            GyNeckFatigue = settings.GyNeckFatigueDeathLevel,
            GyNeckFatigueDeathDwell = initialDwell
        };
        var dt = (1.0 - initialDwell) * settings.GyNeckFatigueDeathDelay;
        var result = AdvanceInterval(in state, dt, 0.0, 10.0, 1.0, settings);

        Assert.True(result.Converged,
            $"endpoint death update rejected: " +
            $"crossings [{string.Join("; ", result.ModeCrossings)}] " +
            $"violations [{string.Join("; ", result.StateViolations)}].");
        Assert.True(result.Final.IsDead,
            $"endpoint death was not applied; events " +
            $"[{string.Join("; ", result.Events.Select(e => $"{e.Offset:R}:{e.Kind}"))}], " +
            $"final dwell={result.Final.GyNeckFatigueDeathDwell:R}.");
        var death = Assert.Single(result.Events, e => e.Kind == "Death");
        Assert.Equal(dt, death.Offset);
        Assert.True(result.Final.IsDead);
        Assert.Equal(1.0, result.Final.GyNeckFatigueDeathDwell);
        Assert.Equal(0.0, result.Final.ConsciousnessLevel);
        Assert.True(result.Final.IsUnconscious);
        Assert.Equal(1.0, result.Final.VisualLoCLevel);
    }

    [Fact]
    public void FullIntervalRejectsOutOfBoundsInitialState()
    {
        var model = new GEffectsLogicInstance().PhysModel;
        var state = model.CaptureIntervalState() with { BloodO2Head = 1.5 };
        var result = AdvanceInterval(in state, 0.25, 0.0, 0.0, 1.0,
            LogicSettings.Default);
        Assert.False(result.Converged,
            "out-of-bounds initial head O2 must not silently converge.");
        Assert.NotEmpty(result.StateViolations);
        Assert.Equal(0, result.Iterations);
        Assert.Empty(result.Segments);
        Assert.Empty(result.Events);
        Assert.Contains(result.StateViolations,
            violation => violation.Contains("initial state", StringComparison.Ordinal));
    }

    [Fact]
    public void FullIntervalRejectsInvalidInitialBloodMassBeforeCirculation()
    {
        var model = new GEffectsLogicInstance().PhysModel;
        var state = model.CaptureIntervalState() with
        {
            BloodHead = 0.6,
            BloodLower = 0.5
        };
        var result = AdvanceInterval(in state, 0.25, 0.0, 0.0, 1.0,
            LogicSettings.Default);

        Assert.False(result.Converged,
            "initial blood compartments with negative core volume must be rejected.");
        Assert.Equal(0, result.Iterations);
        Assert.Empty(result.Segments);
        Assert.Empty(result.Events);
        Assert.Contains(result.StateViolations,
            violation => violation.Contains("initial state", StringComparison.Ordinal));
        Assert.Contains(result.StateViolations,
            violation => violation.Contains("blood", StringComparison.Ordinal));
    }

    private static double IndependentFreePressure(double startPressure, double from, double to,
        double decay, Func<double, double> source)
    {
        if (to <= from) return startPressure;
        const int panels = 10000;
        var width = (to - from) / panels;
        var sum = Math.Exp(-decay * (to - from)) * source(from) + source(to);
        for (var index = 1; index < panels; index++)
        {
            var s = from + index * width;
            var weight = index % 2 is 0 ? 2.0 : 4.0;
            sum += weight * Math.Exp(-decay * (to - s)) * source(s);
        }

        return startPressure * Math.Exp(-decay * (to - from)) + sum * width / 3.0;
    }

    private static double HeadForPressureSource(double target, double gz, LogicSettings settings)
    {
        var negativeInput = settings.CerebralPressureImpairmentNegativeGzRate *
            Math.Min(Math.Max(0.0, -gz), settings.CerebralPressureImpairmentNegativeGzCap);
        var restSigma = 1.0 / (1.0 + Math.Exp(settings.CerebralPressureImpairmentExponent *
            settings.CerebralPressureImpairmentMidOverfill));
        var sigmaTarget = (target - negativeInput) /
            settings.CerebralPressureImpairmentMaxBuildRate + restSigma;
        var logit = Math.Log(sigmaTarget / (1.0 - sigmaTarget));
        var overfill = settings.CerebralPressureImpairmentMidOverfill +
            logit / settings.CerebralPressureImpairmentExponent;
        return settings.RestingBloodHead * (1.0 + overfill);
    }

    internal sealed class DenseDtFactAttribute : FactAttribute
    {
        public DenseDtFactAttribute()
        {
            if (Environment.GetEnvironmentVariable("GEFFECTS_DENSE_DT_TESTS") != "1")
                Skip = "Set GEFFECTS_DENSE_DT_TESTS=1 to run dense dt tests.";
        }
    }

    private static LogicSettings ConstantHeadSettings(double recoveryTau) =>
        LogicSettings.Default with
        {
            HydrostaticShiftRate = 0.0,
            PassiveReturnRate = 0.0,
            HeadPressureReturnRate = 0.0,
            CerebralPressureImpairmentRecoveryTau = recoveryTau
        };

    private static double IndependentPressureSource(double head, double gz, LogicSettings settings)
    {
        var overfill = Math.Max((head - settings.RestingBloodHead) / settings.RestingBloodHead, 0.0);
        var logistic = settings.CerebralPressureImpairmentMaxBuildRate /
            (1.0 + Math.Exp(-settings.CerebralPressureImpairmentExponent *
                (overfill - settings.CerebralPressureImpairmentMidOverfill)));
        var restLogistic = settings.CerebralPressureImpairmentMaxBuildRate /
            (1.0 + Math.Exp(settings.CerebralPressureImpairmentExponent *
                settings.CerebralPressureImpairmentMidOverfill));
        return Math.Max(logistic - restLogistic, 0.0) +
               settings.CerebralPressureImpairmentNegativeGzRate *
               Math.Min(Math.Max(0.0, -gz), settings.CerebralPressureImpairmentNegativeGzCap);
    }
}
