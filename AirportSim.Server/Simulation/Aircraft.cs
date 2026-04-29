using System;
using AirportSim.Shared.Models;

namespace AirportSim.Server.Simulation
{
    public class Aircraft
    {
        public AircraftState State { get; private set; }

        private double _phaseDurationMs;
        private double _timeInCurrentPhaseMs;
        private readonly Random _rand = new();

        // NEW: probability of a go-around per approach, modified by weather
        public double GoAroundChance { get; set; } = 0.08;

        // NEW: marks the aircraft as fully done and ready for removal
        public bool IsFinished { get; private set; }

        public Aircraft(FlightEvent flightEvent)
        {
            State = new AircraftState
            {
                FlightId     = flightEvent.FlightId,
                Type         = flightEvent.Type,
                FlightType   = flightEvent.FlightType,
                Phase        = flightEvent.FlightType == FlightType.Arrival
                                   ? AircraftPhase.Approaching
                                   : AircraftPhase.Parked,
                PhaseProgress = 0.0,
                Position     = new SimPoint(0, 0),
                Heading      = 0,
                Status       = AircraftStatus.Normal,
                GoAroundCount = 0
            };

            SetPhaseDuration();
            UpdatePositionAndHeading();
        }

        public void Tick(double simDeltaMs, RunwayController runway)
        {
            if (IsFinished) return;

            // ── Holding: wait for runway to clear ────────────────────────────
            if (State.Phase == AircraftPhase.Holding)
            {
                if (runway.TryOccupy(State.FlightId))
                    AdvancePhase();
                return;
            }

            // ── OnFinal: request runway at 90% progress ───────────────────────
            if (State.Phase == AircraftPhase.OnFinal && State.PhaseProgress > 0.9)
            {
                if (!runway.TryOccupy(State.FlightId))
                    return;
            }

            // ── GoAround: no runway interaction, just climb and recycle ───────
            if (State.Phase == AircraftPhase.GoAround)
            {
                _timeInCurrentPhaseMs += simDeltaMs;
                State.PhaseProgress = Math.Clamp(_timeInCurrentPhaseMs / _phaseDurationMs, 0.0, 1.0);
                UpdatePositionAndHeading();

                if (State.PhaseProgress >= 1.0)
                {
                    // Recycle back to Approaching for another attempt
                    State.Phase           = AircraftPhase.Approaching;
                    State.Status          = AircraftStatus.Normal;
                    _timeInCurrentPhaseMs = 0;
                    State.PhaseProgress   = 0;
                    SetPhaseDuration();
                }
                return;
            }

            // ── Normal tick ───────────────────────────────────────────────────
            _timeInCurrentPhaseMs += simDeltaMs;
            State.PhaseProgress = Math.Clamp(_timeInCurrentPhaseMs / _phaseDurationMs, 0.0, 1.0);

            UpdatePositionAndHeading();
            UpdateAltitudeAndSpeed();

            if (State.PhaseProgress >= 1.0)
                AdvancePhase(runway);
        }

        // NEW: called by SimulationEngine to flag this aircraft as an emergency
        public void DeclareEmergency()
        {
            State.Status = AircraftStatus.Emergency;
            GoAroundChance = 0.0;   // emergencies always land
        }

        // ── Phase transitions ─────────────────────────────────────────────────

        private void AdvancePhase(RunwayController? runway = null)
        {
            if (State.FlightType == FlightType.Arrival)
                AdvanceArrivalPhase(runway);
            else
                AdvanceDeparturePhase(runway);

            _timeInCurrentPhaseMs = 0;
            State.PhaseProgress   = 0;
            SetPhaseDuration();
        }

        private void AdvanceArrivalPhase(RunwayController? runway)
        {
            switch (State.Phase)
            {
                case AircraftPhase.Approaching:
                    State.Phase = AircraftPhase.OnFinal;
                    break;

                case AircraftPhase.OnFinal:
                    // NEW: roll the dice for a go-around
                    if (State.GoAroundCount < 2 && _rand.NextDouble() < GoAroundChance)
                    {
                        runway?.Release(State.FlightId);
                        State.Phase        = AircraftPhase.GoAround;
                        State.Status       = AircraftStatus.GoAround;
                        State.GoAroundCount++;
                    }
                    else
                    {
                        State.Phase = AircraftPhase.Landing;
                    }
                    break;

                case AircraftPhase.Landing:
                    State.Phase = AircraftPhase.Rollout;
                    break;

                case AircraftPhase.Rollout:
                    runway?.Release(State.FlightId);
                    State.Phase = AircraftPhase.Taxiing;
                    break;

                case AircraftPhase.Taxiing:
                    State.Phase = AircraftPhase.Parked;
                    break;

                case AircraftPhase.Parked:
                    IsFinished = true;
                    break;
            }
        }

        private void AdvanceDeparturePhase(RunwayController? runway)
        {
            switch (State.Phase)
            {
                case AircraftPhase.Parked:
                    State.Phase = AircraftPhase.Taxiing;
                    break;

                case AircraftPhase.Taxiing:
                    State.Phase = AircraftPhase.Holding;
                    break;

                case AircraftPhase.Holding:
                    State.Phase = AircraftPhase.Takeoff;
                    break;

                case AircraftPhase.Takeoff:
                    runway?.Release(State.FlightId);
                    State.Phase = AircraftPhase.Climbing;
                    break;

                case AircraftPhase.Climbing:
                    State.Phase = AircraftPhase.Departed;
                    IsFinished  = true;
                    break;
            }
        }

        // ── Duration table ────────────────────────────────────────────────────

        private void SetPhaseDuration()
        {
            double multiplier = State.Type switch
            {
                AircraftType.Small  => 1.0,
                AircraftType.Medium => 1.5,
                AircraftType.Large  => 2.0,
                _                   => 1.0
            };

            double baseMinutes = State.Phase switch
            {
                AircraftPhase.Approaching => 5.0,
                AircraftPhase.OnFinal     => 2.0,
                AircraftPhase.GoAround    => 3.0,   // NEW: climb-out + reposition
                AircraftPhase.Landing     => 0.5,
                AircraftPhase.Rollout     => 1.0,
                AircraftPhase.Taxiing     => 2.0,
                AircraftPhase.Parked      => 20.0,
                AircraftPhase.Holding     => 0.0,   // waits for runway — no timer
                AircraftPhase.Takeoff     => 0.5,
                AircraftPhase.Climbing    => 3.0,
                AircraftPhase.Departed    => 0.0,
                _                         => 1.0
            };

            _phaseDurationMs = baseMinutes * 60_000 * multiplier;
            if (_phaseDurationMs == 0) _phaseDurationMs = 1;
        }

        // ── Position + heading ────────────────────────────────────────────────

        private void UpdatePositionAndHeading()
        {
            double startX, startY, endX, endY;

            switch (State.Phase)
            {
                case AircraftPhase.Approaching:
                    (startX, startY, endX, endY) = (2200, 100, 1600, 300);
                    State.Heading = 180 + 45;   // coming in from right, descending
                    break;

                case AircraftPhase.OnFinal:
                    (startX, startY, endX, endY) = (1600, 300, 1400, 480);
                    State.Heading = 225;
                    break;

                // NEW: go-around climbs back out to the right
                case AircraftPhase.GoAround:
                    (startX, startY, endX, endY) = (1400, 480, 2200, 100);
                    State.Heading = 45;
                    break;

                case AircraftPhase.Landing:
                case AircraftPhase.Rollout:
                    (startX, startY, endX, endY) = (1400, 480, 600, 480);
                    State.Heading = 270;
                    break;

                case AircraftPhase.Taxiing when State.FlightType == FlightType.Arrival:
                    (startX, startY, endX, endY) = (600, 480, 300, 380);
                    State.Heading = 315;
                    break;

                case AircraftPhase.Taxiing:
                    (startX, startY, endX, endY) = (300, 380, 400, 480);
                    State.Heading = 135;
                    break;

                case AircraftPhase.Parked:
                    (startX, startY, endX, endY) = (300, 380, 300, 380);
                    State.Heading = 0;
                    break;

                case AircraftPhase.Holding:
                    (startX, startY, endX, endY) = (400, 480, 400, 480);
                    State.Heading = 90;
                    break;

                case AircraftPhase.Takeoff:
                    (startX, startY, endX, endY) = (400, 480, 1600, 480);
                    State.Heading = 90;
                    break;

                case AircraftPhase.Climbing:
                    (startX, startY, endX, endY) = (1600, 480, 2200, 100);
                    State.Heading = 45;
                    break;

                default:
                    (startX, startY, endX, endY) = (0, 0, 0, 0);
                    break;
            }

            State.Position = new SimPoint(
                startX + (endX - startX) * State.PhaseProgress,
                startY + (endY - startY) * State.PhaseProgress
            );
        }

        // NEW: derives altitude and speed from phase + progress
        private void UpdateAltitudeAndSpeed()
        {
            // Altitude: ground = 0 ft, max approach = ~3000 ft
            double groundY  = 480.0;
            double currentY = State.Position.Y;
            State.AltitudeFt = (int)Math.Max(0, (groundY - currentY) * 7.8);

            // Approximate speeds per phase
            State.SpeedKts = State.Phase switch
            {
                AircraftPhase.Approaching => 180,
                AircraftPhase.OnFinal     => 140,
                AircraftPhase.GoAround    => 160,
                AircraftPhase.Landing     => 120,
                AircraftPhase.Rollout     => 60,
                AircraftPhase.Taxiing     => 15,
                AircraftPhase.Parked      => 0,
                AircraftPhase.Holding     => 0,
                AircraftPhase.Takeoff     => 150,
                AircraftPhase.Climbing    => 200,
                _                         => 0
            };

            // Scale speed by type
            double factor = State.Type switch
            {
                AircraftType.Small  => 0.8,
                AircraftType.Medium => 1.0,
                AircraftType.Large  => 1.15,
                _                   => 1.0
            };

            State.SpeedKts = (int)(State.SpeedKts * factor);
        }
    }
}