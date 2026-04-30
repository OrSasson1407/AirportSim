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

        public double GoAroundChance { get; set; } = 0.08;
        public bool IsFinished { get; private set; }
        
        // Expose if the last go-around was forced by weather minimums
        public bool LastGoAroundWasWeatherForced { get; private set; }

        public Aircraft(FlightEvent flightEvent)
        {
            State = new AircraftState
            {
                FlightId      = flightEvent.FlightId,
                Type          = flightEvent.Type,
                FlightType    = flightEvent.FlightType,
                Phase         = flightEvent.FlightType == FlightType.Arrival
                                    ? AircraftPhase.Approaching
                                    : AircraftPhase.Parked,
                PhaseProgress = 0.0,
                Position      = new SimPoint(0, 0),
                Heading       = 0,
                Status        = AircraftStatus.Normal,
                GoAroundCount = 0,
                // Arrivals spawn with partial fuel (between 30% and 80%), Departures spawn full
                CurrentFuelPercent = flightEvent.FlightType == FlightType.Arrival 
                                        ? 30.0 + (_rand.NextDouble() * 50.0) 
                                        : 100.0 
            };

            SetPhaseDuration();
            UpdatePositionAndHeading();
        }

        // Passing WeatherCondition into Tick
        public void Tick(double simDeltaMs, RunwayController runway, WeatherCondition currentWeather)
        {
            if (IsFinished) return;

            // NEW: Always update fuel consumption unless parked
            UpdateFuel(simDeltaMs);

            // ── Holding: wait for the DEPARTURE runway to clear ───────────────
            if (State.Phase == AircraftPhase.Holding)
            {
                if (runway.TryOccupyDeparture(State.FlightId))
                    AdvancePhase(runway, currentWeather);
                return;
            }

            // ── OnFinal: request the ARRIVAL runway at 90% progress ───────────
            if (State.Phase == AircraftPhase.OnFinal && State.PhaseProgress > 0.9)
            {
                if (!runway.TryOccupyArrival(State.FlightId))
                    return;
            }

            // ── GoAround: no runway interaction, just climb and recycle ────────
            if (State.Phase == AircraftPhase.GoAround)
            {
                _timeInCurrentPhaseMs += simDeltaMs;
                State.PhaseProgress = Math.Clamp(
                    _timeInCurrentPhaseMs / _phaseDurationMs, 0.0, 1.0);
                UpdatePositionAndHeading();

                if (State.PhaseProgress >= 1.0)
                {
                    State.Phase           = AircraftPhase.Approaching;
                    // Only revert status to Normal if we aren't currently in a Fuel Emergency
                    if (State.Status != AircraftStatus.Emergency) 
                        State.Status = AircraftStatus.Normal;
                        
                    _timeInCurrentPhaseMs = 0;
                    State.PhaseProgress   = 0;
                    LastGoAroundWasWeatherForced = false; // Reset the flag
                    SetPhaseDuration();
                }
                return;
            }

            // ── Normal tick ───────────────────────────────────────────────────
            _timeInCurrentPhaseMs += simDeltaMs;
            State.PhaseProgress = Math.Clamp(
                _timeInCurrentPhaseMs / _phaseDurationMs, 0.0, 1.0);

            UpdatePositionAndHeading();
            UpdateAltitudeAndSpeed();

            if (State.PhaseProgress >= 1.0)
                AdvancePhase(runway, currentWeather);
        }

        public void DeclareEmergency(string reason = "General Emergency")
        {
            State.Status          = AircraftStatus.Emergency;
            State.EmergencyReason = reason;
            GoAroundChance        = 0.0; // An aircraft in an emergency MUST land
        }

        // ── Systems Updates (Fuel) ────────────────────────────────────────────
        
        private void UpdateFuel(double simDeltaMs)
        {
            // Negligible fuel burn while parked at the gate (APU / Ground Power)
            if (State.Phase == AircraftPhase.Parked) return;

            // Base rate: % lost per millisecond (adjusted for simulation speed)
            double baseBurnRatePerMs = 0.00002; 
            
            // Engines work harder during certain phases
            double phaseMultiplier = State.Phase switch
            {
                AircraftPhase.Takeoff   => 3.5,
                AircraftPhase.Climbing  => 2.8,
                AircraftPhase.GoAround  => 2.8,
                AircraftPhase.Holding   => 1.5,
                AircraftPhase.Taxiing   => 0.4,
                AircraftPhase.Rollout   => 0.8,
                _                       => 1.2
            };

            // Larger aircraft burn more fuel globally
            double typeMultiplier = State.Type switch
            {
                AircraftType.Small  => 0.6,
                AircraftType.Medium => 1.0,
                AircraftType.Large  => 1.8,
                _                   => 1.0
            };

            double burnAmount = baseBurnRatePerMs * phaseMultiplier * typeMultiplier * simDeltaMs;
            State.CurrentFuelPercent = Math.Max(0, State.CurrentFuelPercent - burnAmount);

            // Trigger an automatic emergency if fuel drops to a critical level
            if (State.CurrentFuelPercent <= 10.0 && State.Status != AircraftStatus.Emergency)
            {
                DeclareEmergency("Low Fuel (Bingo)");
            }
        }

        // ── Phase transitions ─────────────────────────────────────────────────

        private void AdvancePhase(RunwayController? runway, WeatherCondition weather)
        {
            if (State.FlightType == FlightType.Arrival)
                AdvanceArrivalPhase(runway, weather);
            else
                AdvanceDeparturePhase(runway);

            _timeInCurrentPhaseMs = 0;
            State.PhaseProgress   = 0;
            SetPhaseDuration();
        }

        private void AdvanceArrivalPhase(RunwayController? runway, WeatherCondition weather)
        {
            switch (State.Phase)
            {
                case AircraftPhase.Approaching:
                    State.Phase = AircraftPhase.OnFinal;
                    break;

                case AircraftPhase.OnFinal:
                    
                    // VISIBILITY CHECKS
                    bool isLowVisibility = (weather == WeatherCondition.Fog || weather == WeatherCondition.Storm);
                    bool isCat3Equipped = (State.Type == AircraftType.Large); // Only Large are CAT III
                    
                    bool forcedByWeather = isLowVisibility && !isCat3Equipped;
                    double actualGoAroundChance = forcedByWeather ? 1.0 : GoAroundChance;

                    if (State.GoAroundCount < 2 && _rand.NextDouble() < actualGoAroundChance)
                    {
                        runway?.ReleaseArrival(State.FlightId);
                        State.Phase         = AircraftPhase.GoAround;
                        // Don't overwrite an emergency status with a standard go-around status
                        if (State.Status != AircraftStatus.Emergency)
                            State.Status = AircraftStatus.GoAround;
                            
                        State.GoAroundCount++;
                        LastGoAroundWasWeatherForced = forcedByWeather;
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
                    runway?.ReleaseArrival(State.FlightId);
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
                    runway?.ReleaseDeparture(State.FlightId);
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
                AircraftPhase.GoAround    => 3.0,
                AircraftPhase.Landing     => 0.5,
                AircraftPhase.Rollout     => 1.0,
                AircraftPhase.Taxiing     => 2.0,
                AircraftPhase.Parked      => 20.0,
                AircraftPhase.Holding     => 0.0,
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
                // ── Arrivals use the bottom strip (Y ≈ 520) ──────────────────
                case AircraftPhase.Approaching:
                    (startX, startY, endX, endY) = (2200, 100, 1600, 300);
                    State.Heading = 180 + 45;
                    break;

                case AircraftPhase.OnFinal:
                    (startX, startY, endX, endY) = (1600, 300, 1400, 520);
                    State.Heading = 225;
                    break;

                case AircraftPhase.GoAround:
                    (startX, startY, endX, endY) = (1400, 520, 2200, 100);
                    State.Heading = 45;
                    break;

                case AircraftPhase.Landing:
                case AircraftPhase.Rollout:
                    (startX, startY, endX, endY) = (1400, 520, 600, 520);
                    State.Heading = 270;
                    break;

                case AircraftPhase.Taxiing when State.FlightType == FlightType.Arrival:
                    (startX, startY, endX, endY) = (600, 520, 300, 400);
                    State.Heading = 315;
                    break;

                // ── Departures use the top strip (Y ≈ 460) ───────────────────
                case AircraftPhase.Parked:
                    (startX, startY, endX, endY) = (300, 400, 300, 400);
                    State.Heading = 0;
                    break;

                case AircraftPhase.Taxiing:   // departure taxi to holding point
                    (startX, startY, endX, endY) = (300, 400, 420, 460);
                    State.Heading = 135;
                    break;

                case AircraftPhase.Holding:
                    (startX, startY, endX, endY) = (420, 460, 420, 460);
                    State.Heading = 90;
                    break;

                case AircraftPhase.Takeoff:
                    (startX, startY, endX, endY) = (420, 460, 1580, 460);
                    State.Heading = 90;
                    break;

                case AircraftPhase.Climbing:
                    (startX, startY, endX, endY) = (1580, 460, 2200, 80);
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

        // ── Altitude + speed ──────────────────────────────────────────────────

        private void UpdateAltitudeAndSpeed()
        {
            double groundY  = 520.0;
            double currentY = State.Position.Y;
            State.AltitudeFt = (int)Math.Max(0, (groundY - currentY) * 7.8);

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