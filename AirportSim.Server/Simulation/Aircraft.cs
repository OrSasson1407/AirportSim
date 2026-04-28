using System;
using AirportSim.Shared.Models;

namespace AirportSim.Server.Simulation
{
    public class Aircraft
    {
        public AircraftState State { get; private set; }
        private double _phaseDurationMs;
        private double _timeInCurrentPhaseMs;

        public Aircraft(FlightEvent flightEvent)
        {
            State = new AircraftState
            {
                FlightId = flightEvent.FlightId,
                Type = flightEvent.Type,
                FlightType = flightEvent.FlightType,
                Phase = flightEvent.FlightType == FlightType.Arrival ? AircraftPhase.Approaching : AircraftPhase.Parked,
                PhaseProgress = 0.0,
                Position = new SimPoint(0, 0), 
                Heading = 0
            };
            
            SetPhaseDuration();
            UpdatePositionAndHeading();
        }

        public void Tick(double simDeltaMs, RunwayController runway)
        {
            if (State.Phase == AircraftPhase.Holding)
            {
                if (runway.TryOccupy(State.FlightId))
                {
                    AdvancePhase();
                }
                return; 
            }
            
            if (State.Phase == AircraftPhase.OnFinal && State.PhaseProgress > 0.9)
            {
                 if (!runway.TryOccupy(State.FlightId))
                 {
                     return; 
                 }
            }

            _timeInCurrentPhaseMs += simDeltaMs;
            State.PhaseProgress = Math.Clamp(_timeInCurrentPhaseMs / _phaseDurationMs, 0.0, 1.0);

            UpdatePositionAndHeading();

            if (State.PhaseProgress >= 1.0)
            {
                AdvancePhase(runway);
            }
        }

        private void AdvancePhase(RunwayController? runway = null)
        {
            if (State.FlightType == FlightType.Arrival)
            {
                State.Phase = State.Phase switch
                {
                    AircraftPhase.Approaching => AircraftPhase.OnFinal,
                    AircraftPhase.OnFinal => AircraftPhase.Landing,
                    AircraftPhase.Landing => AircraftPhase.Rollout,
                    AircraftPhase.Rollout => AircraftPhase.Taxiing,
                    AircraftPhase.Taxiing => AircraftPhase.Parked,
                    _ => State.Phase
                };
                
                if (State.Phase == AircraftPhase.Taxiing)
                {
                    runway?.Release(State.FlightId);
                }
            }
            else 
            {
                State.Phase = State.Phase switch
                {
                    AircraftPhase.Parked => AircraftPhase.Taxiing,
                    AircraftPhase.Taxiing => AircraftPhase.Holding,
                    AircraftPhase.Holding => AircraftPhase.Takeoff,
                    AircraftPhase.Takeoff => AircraftPhase.Climbing,
                    AircraftPhase.Climbing => AircraftPhase.Departed,
                    _ => State.Phase
                };
                
                if (State.Phase == AircraftPhase.Climbing)
                {
                    runway?.Release(State.FlightId);
                }
            }

            _timeInCurrentPhaseMs = 0;
            State.PhaseProgress = 0;
            SetPhaseDuration();
        }

        private void SetPhaseDuration()
        {
            double multiplier = State.Type switch
            {
                AircraftType.Small => 1.0,
                AircraftType.Medium => 1.5,
                AircraftType.Large => 2.0,
                _ => 1.0
            };

            double baseMinutes = State.Phase switch
            {
                AircraftPhase.Approaching => 5.0,
                AircraftPhase.OnFinal => 2.0,
                AircraftPhase.Landing => 0.5,
                AircraftPhase.Rollout => 1.0,
                AircraftPhase.Taxiing => 2.0,
                AircraftPhase.Parked => 20.0,
                AircraftPhase.Holding => 0.0, 
                AircraftPhase.Takeoff => 0.5,
                AircraftPhase.Climbing => 3.0,
                AircraftPhase.Departed => 0.0, 
                _ => 1.0
            };

            _phaseDurationMs = baseMinutes * 60000 * multiplier;
            if (_phaseDurationMs == 0) _phaseDurationMs = 1; 
        }

        private void UpdatePositionAndHeading()
        {
            double startX = 0, startY = 0, endX = 0, endY = 0;
            
            switch (State.Phase)
            {
                case AircraftPhase.Approaching:
                    startX = 2200; startY = 100; endX = 1600; endY = 300;
                    State.Heading = 270; 
                    break;
                case AircraftPhase.OnFinal:
                    startX = 1600; startY = 300; endX = 1400; endY = 480; 
                    State.Heading = 270;
                    break;
                case AircraftPhase.Landing:
                case AircraftPhase.Rollout:
                    startX = 1400; startY = 480; endX = 600; endY = 480;
                    State.Heading = 270;
                    break;
                case AircraftPhase.Taxiing:
                    if (State.FlightType == FlightType.Arrival)
                    {
                        startX = 600; startY = 480; endX = 300; endY = 380; 
                        State.Heading = 315;
                    }
                    else
                    {
                        startX = 300; startY = 380; endX = 400; endY = 480; 
                        State.Heading = 135;
                    }
                    break;
                case AircraftPhase.Takeoff:
                    startX = 400; startY = 480; endX = 1600; endY = 480; 
                    State.Heading = 90; 
                    break;
                case AircraftPhase.Climbing:
                    startX = 1600; startY = 480; endX = 2200; endY = 100;
                    State.Heading = 90;
                    break;
                case AircraftPhase.Holding:
                    startX = 400; startY = 480; endX = 400; endY = 480;
                    State.Heading = 90;
                    break;
                case AircraftPhase.Parked:
                    startX = 300; startY = 380; endX = 300; endY = 380;
                    State.Heading = 0;
                    break;
            }

            State.Position = new SimPoint(
                startX + (endX - startX) * State.PhaseProgress,
                startY + (endY - startY) * State.PhaseProgress
            );
        }
    }
}