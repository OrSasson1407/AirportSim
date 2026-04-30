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
                CurrentFuelPercent = flightEvent.FlightType == FlightType.Arrival 
                                        ? 30.0 + (_rand.NextDouble() * 50.0) 
                                        : 100.0 
            };

            SetPhaseDuration();
            UpdatePositionAndHeading();
        }

        // NEW: Added rvrMeters to the Tick signature
        public void Tick(double simDeltaMs, RunwayController runway, WeatherCondition currentWeather, int rvrMeters)
        {
            if (IsFinished) return;

            UpdateFuel(simDeltaMs);

            if (State.Phase == AircraftPhase.Diverted)
            {
                _timeInCurrentPhaseMs += simDeltaMs;
                State.PhaseProgress = Math.Clamp(
                    _timeInCurrentPhaseMs / _phaseDurationMs, 0.0, 1.0);
                UpdatePositionAndHeading();

                if (State.PhaseProgress >= 1.0)
                {
                    IsFinished = true;
                }
                return;
            }

            if (State.Phase == AircraftPhase.Holding)
            {
                if (runway.TryOccupyDeparture(State.FlightId))
                    AdvancePhase(runway, currentWeather, rvrMeters);
                return;
            }

            if (State.Phase == AircraftPhase.OnFinal && State.PhaseProgress > 0.9)
            {
                if (!runway.TryOccupyArrival(State.FlightId))
                    return;
            }

            if (State.Phase == AircraftPhase.GoAround)
            {
                _timeInCurrentPhaseMs += simDeltaMs;
                State.PhaseProgress = Math.Clamp(
                    _timeInCurrentPhaseMs / _phaseDurationMs, 0.0, 1.0);
                UpdatePositionAndHeading();

                if (State.PhaseProgress >= 1.0)
                {
                    State.Phase           = AircraftPhase.Approaching;
                    if (State.Status != AircraftStatus.Emergency && State.Status != AircraftStatus.Diverting) 
                        State.Status = AircraftStatus.Normal;
                        
                    _timeInCurrentPhaseMs = 0;
                    State.PhaseProgress   = 0;
                    LastGoAroundWasWeatherForced = false;
                    SetPhaseDuration();
                }
                return;
            }

            _timeInCurrentPhaseMs += simDeltaMs;
            State.PhaseProgress = Math.Clamp(
                _timeInCurrentPhaseMs / _phaseDurationMs, 0.0, 1.0);

            UpdatePositionAndHeading();
            UpdateAltitudeAndSpeed();

            if (State.PhaseProgress >= 1.0)
                AdvancePhase(runway, currentWeather, rvrMeters);
        }

        public void DeclareEmergency(string reason = "General Emergency")
        {
            State.Status          = AircraftStatus.Emergency;
            State.EmergencyReason = reason;
            GoAroundChance        = 0.0;
        }

        private void UpdateFuel(double simDeltaMs)
        {
            if (State.Phase == AircraftPhase.Parked) return;

            double baseBurnRatePerMs = 0.00002; 
            
            double phaseMultiplier = State.Phase switch
            {
                AircraftPhase.Takeoff   => 3.5,
                AircraftPhase.Climbing  => 2.8,
                AircraftPhase.GoAround  => 2.8,
                AircraftPhase.Holding   => 1.5,
                AircraftPhase.Pushback  => 0.2, 
                AircraftPhase.Taxiing   => 0.4,
                AircraftPhase.Rollout   => 0.8,
                AircraftPhase.Diverted  => 2.0, 
                _                       => 1.2
            };

            double typeMultiplier = State.Type switch
            {
                AircraftType.Small  => 0.6,
                AircraftType.Medium => 1.0,
                AircraftType.Large  => 1.8,
                _                   => 1.0
            };

            double burnAmount = baseBurnRatePerMs * phaseMultiplier * typeMultiplier * simDeltaMs;
            State.CurrentFuelPercent = Math.Max(0, State.CurrentFuelPercent - burnAmount);

            if (State.CurrentFuelPercent <= 10.0 && State.CurrentFuelPercent > 2.0 && State.Status != AircraftStatus.Emergency && State.Status != AircraftStatus.Diverting)
            {
                DeclareEmergency("Low Fuel (Bingo)");
            }
            else if ((State.CurrentFuelPercent <= 2.0 || State.GoAroundCount >= 3) && State.Status != AircraftStatus.Diverting && State.Phase != AircraftPhase.Diverted && State.FlightType == FlightType.Arrival)
            {
                if (State.Phase != AircraftPhase.Rollout && State.Phase != AircraftPhase.Taxiing && State.Phase != AircraftPhase.Landing)
                {
                    State.Status = AircraftStatus.Diverting;
                    State.Phase = AircraftPhase.Diverted;
                    State.EmergencyReason = State.GoAroundCount >= 3 ? "Excessive Go-Arounds" : "Critical Fuel Dry";
                    _timeInCurrentPhaseMs = 0;
                    State.PhaseProgress = 0;
                    SetPhaseDuration();
                }
            }
        }

        private void AdvancePhase(RunwayController? runway, WeatherCondition weather, int rvrMeters)
        {
            if (State.FlightType == FlightType.Arrival)
                AdvanceArrivalPhase(runway, weather, rvrMeters);
            else
                AdvanceDeparturePhase(runway);

            _timeInCurrentPhaseMs = 0;
            State.PhaseProgress   = 0;
            SetPhaseDuration();
        }

        private void AdvanceArrivalPhase(RunwayController? runway, WeatherCondition weather, int rvrMeters)
        {
            switch (State.Phase)
            {
                case AircraftPhase.Approaching:
                    State.Phase = AircraftPhase.OnFinal;
                    break;
                case AircraftPhase.OnFinal:
                    
                    // NEW: Real RVR Category Minimums!
                    int minimumRvr = State.Type switch
                    {
                        AircraftType.Small  => 550, // CAT I
                        AircraftType.Medium => 300, // CAT II
                        AircraftType.Large  => 200, // CAT IIIa
                        _                   => 550
                    };

                    bool forcedByWeather = rvrMeters < minimumRvr;
                    double actualGoAroundChance = forcedByWeather ? 1.0 : GoAroundChance;

                    if (State.GoAroundCount < 2 && _rand.NextDouble() < actualGoAroundChance)
                    {
                        runway?.ReleaseArrival(State.FlightId);
                        State.Phase         = AircraftPhase.GoAround;
                        if (State.Status != AircraftStatus.Emergency && State.Status != AircraftStatus.Diverting)
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
                    State.Phase = AircraftPhase.Pushback; 
                    break;
                case AircraftPhase.Pushback:
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
                AircraftPhase.Pushback    => 1.0, 
                AircraftPhase.Holding     => 0.0,
                AircraftPhase.Takeoff     => 0.5,
                AircraftPhase.Climbing    => 3.0,
                AircraftPhase.Departed    => 0.0,
                AircraftPhase.Diverted    => 4.0, 
                _                         => 1.0
            };

            _phaseDurationMs = baseMinutes * 60_000 * multiplier;
            if (_phaseDurationMs == 0) _phaseDurationMs = 1;
        }

        private void UpdatePositionAndHeading()
        {
            double startX, startY, endX, endY;
            
            double gateX = State.GateX > 0 ? State.GateX : 300;
            double gateY = State.GateY > 0 ? State.GateY : 400;

            switch (State.Phase)
            {
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
                    (startX, startY, endX, endY) = (600, 520, gateX, gateY);
                    State.Heading = 315;
                    break;

                case AircraftPhase.Parked:
                    (startX, startY, endX, endY) = (gateX, gateY, gateX, gateY);
                    State.Heading = 0; 
                    break;

                case AircraftPhase.Pushback:
                    (startX, startY, endX, endY) = (gateX, gateY, gateX, gateY + 30);
                    State.Heading = 0; 
                    break;

                case AircraftPhase.Taxiing:   
                    (startX, startY, endX, endY) = (gateX, gateY + 30, 420, 460);
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
                case AircraftPhase.Diverted:
                    startX = State.Position.X;
                    startY = State.Position.Y;
                    endX = -500; 
                    endY = -500;
                    State.Heading = 315; 
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
                AircraftPhase.Pushback    => 5,  
                AircraftPhase.Taxiing     => 15,
                AircraftPhase.Parked      => 0,
                AircraftPhase.Holding     => 0,
                AircraftPhase.Takeoff     => 150,
                AircraftPhase.Climbing    => 200,
                AircraftPhase.Diverted    => 220, 
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