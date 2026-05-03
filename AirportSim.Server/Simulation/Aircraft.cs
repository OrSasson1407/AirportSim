using System;
using System.Collections.Generic;
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

        private List<SimPoint> _currentRoute;
        private double _trailTimerMs = 0; 

        public Aircraft(FlightEvent flightEvent, RunwayId assignedRunway) // NEW
        {
            State = new AircraftState
            {
                FlightId      = flightEvent.FlightId,
                Type          = flightEvent.Type,
                FlightType    = flightEvent.FlightType,
                Origin        = flightEvent.Origin, 
                Destination   = flightEvent.Destination, 
                AssignedRunway = assignedRunway, // NEW
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
                                        : 100.0,
                
                DelayMinutes  = flightEvent.DelayMinutes,
                Turnaround    = flightEvent.FlightType == FlightType.Departure 
                                        ? TurnaroundPhase.Deplaning 
                                        : TurnaroundPhase.None
            };

            SetPhaseDuration();
            UpdatePositionAndHeading();
        }

        public void GrantClearance(string clearanceType)
        {
            switch (clearanceType.ToLower())
            {
                case "pushback": State.ClearedToPushback = true; break;
                case "taxi":     State.ClearedToTaxi = true; break;
                case "takeoff":  State.ClearedToTakeoff = true; break;
                case "land":     State.ClearedToLand = true; break;
            }
        }

        public void AssignSpeed(int speedKts) => State.AssignedSpeedKts = speedKts;
        public void AssignAltitude(int altitudeFt) => State.AssignedAltitudeFt = altitudeFt;

        public void Tick(double simDeltaMs, RunwayController runway, GateManager gates, WeatherCondition currentWeather, int rvrMeters, int assignedHoldAltitude = 0)
        {
            if (IsFinished) return;

            UpdateFuel(simDeltaMs);

            _trailTimerMs += simDeltaMs;
            if (_trailTimerMs >= 2000) 
            {
                if (State.Phase != AircraftPhase.Parked && State.Phase != AircraftPhase.Holding && State.Phase != AircraftPhase.Pushback)
                {
                    State.RecentTrail.Add(State.Position);
                    if (State.RecentTrail.Count > 15) State.RecentTrail.RemoveAt(0); 
                }
                _trailTimerMs = 0;
            }

            if (State.Phase == AircraftPhase.Parked)
            {
                if (State.FlightType == FlightType.Departure)
                {
                    TickTurnaround(simDeltaMs);
                    State.PhaseProgress = (State.Turnaround == TurnaroundPhase.Ready && State.ClearedToPushback) ? 1.0 : 0.0;
                }
                else
                {
                    _timeInCurrentPhaseMs += simDeltaMs;
                    State.PhaseProgress = Math.Clamp(_timeInCurrentPhaseMs / (10 * 60_000), 0.0, 1.0);
                    if (State.PhaseProgress >= 1.0) IsFinished = true;
                }

                UpdatePositionAndHeading();
                
                if (State.PhaseProgress >= 1.0 && State.FlightType == FlightType.Departure)
                    AdvancePhase(runway, gates, currentWeather, rvrMeters);
                
                return; 
            }

            if (State.Phase == AircraftPhase.Diverted)
            {
                _timeInCurrentPhaseMs += simDeltaMs;
                State.PhaseProgress = Math.Clamp(_timeInCurrentPhaseMs / _phaseDurationMs, 0.0, 1.0);
                UpdatePositionAndHeading();

                if (State.PhaseProgress >= 1.0) IsFinished = true;
                return;
            }

            if (State.Phase == AircraftPhase.Holding)
            {
                if (State.FlightType == FlightType.Departure)
                {
                    // NEW: Targeted TryOccupy
                    if (State.ClearedToTakeoff && runway.TryOccupy(State.AssignedRunway, State.FlightId, State.FlightType))
                        AdvancePhase(runway, gates, currentWeather, rvrMeters);
                    return;
                }
                else
                {
                    _timeInCurrentPhaseMs += simDeltaMs;
                    State.PhaseProgress = (_timeInCurrentPhaseMs % _phaseDurationMs) / _phaseDurationMs; 
                    UpdatePositionAndHeading();
                    
                    if (State.AltitudeFt < assignedHoldAltitude - 100) State.AltitudeFt += 20;
                    else if (State.AltitudeFt > assignedHoldAltitude + 100) State.AltitudeFt -= 20;
                    else State.AltitudeFt = assignedHoldAltitude;
                    
                    return; 
                }
            }

            if (State.Phase == AircraftPhase.OnFinal && State.PhaseProgress > 0.8 && !State.ClearedToLand)
            {
                runway?.Release(State.AssignedRunway, State.FlightId); // NEW
                State.Phase = AircraftPhase.GoAround;
                if (State.Status != AircraftStatus.Emergency && State.Status != AircraftStatus.Diverting) 
                    State.Status = AircraftStatus.GoAround;
                    
                State.GoAroundCount++;
                State.ClearedToLand = false; 
                LastGoAroundWasWeatherForced = false; 
                _timeInCurrentPhaseMs = 0;
                State.PhaseProgress = 0;
                SetPhaseDuration();
                
                State.AssignedSpeedKts = null;
                State.AssignedAltitudeFt = null;
                return;
            }

            if (State.Phase == AircraftPhase.OnFinal && State.PhaseProgress > 0.9)
            {
                if (!runway.TryOccupy(State.AssignedRunway, State.FlightId, State.FlightType)) return; // NEW
            }

            if (State.Phase == AircraftPhase.GoAround)
            {
                _timeInCurrentPhaseMs += simDeltaMs;
                State.PhaseProgress = Math.Clamp(_timeInCurrentPhaseMs / _phaseDurationMs, 0.0, 1.0);
                UpdatePositionAndHeading();

                if (State.PhaseProgress >= 1.0)
                {
                    State.Phase = AircraftPhase.Holding; 
                    if (State.Status != AircraftStatus.Emergency && State.Status != AircraftStatus.Diverting) 
                        State.Status = AircraftStatus.Normal;
                        
                    _timeInCurrentPhaseMs = 0;
                    State.PhaseProgress = 0;
                    State.ClearedToLand = false; 
                    LastGoAroundWasWeatherForced = false;
                    SetPhaseDuration();
                    
                    State.AssignedSpeedKts = null;
                    State.AssignedAltitudeFt = null;
                }
                return;
            }

            _timeInCurrentPhaseMs += simDeltaMs;
            State.PhaseProgress = Math.Clamp(_timeInCurrentPhaseMs / _phaseDurationMs, 0.0, 1.0);

            UpdatePositionAndHeading();
            UpdateAltitudeAndSpeed();

            bool canAdvance = true;
            if (State.Phase == AircraftPhase.Pushback && !State.ClearedToTaxi) canAdvance = false;
            if (State.Phase == AircraftPhase.Rollout && !State.ClearedToTaxi) canAdvance = false;

            if (State.PhaseProgress >= 1.0 && canAdvance)
                AdvancePhase(runway, gates, currentWeather, rvrMeters);
        }

        public void SendToHold()
        {
            State.Phase = AircraftPhase.Holding;
            _timeInCurrentPhaseMs = 0;
            State.PhaseProgress = 0;
            State.AltitudeFt = 15000; 
            SetPhaseDuration();
        }

        public void ClearFromHold()
        {
            State.Phase = AircraftPhase.Approaching;
            _timeInCurrentPhaseMs = 0;
            State.PhaseProgress = 0;
            SetPhaseDuration();
        }

        private void TickTurnaround(double simDeltaMs)
        {
            if (State.Turnaround == TurnaroundPhase.Ready) return;

            double baseTimeMins = State.Type switch {
                AircraftType.Small => 30.0, AircraftType.Medium => 45.0, AircraftType.Large => 90.0, _ => 45.0
            };

            double phaseMultiplier = State.Turnaround switch {
                TurnaroundPhase.Deplaning => 0.15, TurnaroundPhase.Cleaning => 0.20,
                TurnaroundPhase.Fueling => 0.25, TurnaroundPhase.Boarding => 0.40, _ => 1.0
            };

            double expectedDurationMs = baseTimeMins * phaseMultiplier * 60_000;

            if (_rand.NextDouble() < 0.00005) State.DelayMinutes += 5;

            expectedDurationMs += (State.DelayMinutes * 60_000 * phaseMultiplier);

            _timeInCurrentPhaseMs += simDeltaMs;
            State.TurnaroundProgress = Math.Clamp(_timeInCurrentPhaseMs / expectedDurationMs, 0.0, 1.0);

            if (State.TurnaroundProgress >= 1.0)
            {
                _timeInCurrentPhaseMs = 0;
                State.TurnaroundProgress = 0;
                State.Turnaround = State.Turnaround switch {
                    TurnaroundPhase.Deplaning => TurnaroundPhase.Cleaning,
                    TurnaroundPhase.Cleaning => TurnaroundPhase.Fueling,
                    TurnaroundPhase.Fueling => TurnaroundPhase.Boarding,
                    TurnaroundPhase.Boarding => TurnaroundPhase.Ready,
                    _ => TurnaroundPhase.Ready
                };
            }
        }

        public void DeclareEmergency(string reason = "General Emergency")
        {
            State.Status = AircraftStatus.Emergency;
            State.EmergencyReason = reason;
            GoAroundChance = 0.0;
        }

        private void UpdateFuel(double simDeltaMs)
        {
            if (State.Phase == AircraftPhase.Parked) return;

            double baseBurnRatePerMs = 0.00002; 
            double phaseMultiplier = State.Phase switch
            {
                AircraftPhase.Takeoff => 3.5, AircraftPhase.Climbing => 2.8, AircraftPhase.GoAround => 2.8,
                AircraftPhase.Holding => 1.5, AircraftPhase.Pushback => 0.2, AircraftPhase.Taxiing  => 0.4,
                AircraftPhase.Rollout => 0.8, AircraftPhase.Diverted => 2.0, _ => 1.2
            };

            double typeMultiplier = State.Type switch { AircraftType.Small => 0.6, AircraftType.Large => 1.8, _ => 1.0 };

            State.CurrentFuelPercent = Math.Max(0, State.CurrentFuelPercent - (baseBurnRatePerMs * phaseMultiplier * typeMultiplier * simDeltaMs));

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

        private void AdvancePhase(RunwayController? runway, GateManager gates, WeatherCondition weather, int rvrMeters)
        {
            if (State.FlightType == FlightType.Arrival)
                AdvanceArrivalPhase(runway, gates, weather, rvrMeters);
            else
                AdvanceDeparturePhase(runway, gates);

            State.AssignedSpeedKts = null;
            State.AssignedAltitudeFt = null;

            _timeInCurrentPhaseMs = 0;
            State.PhaseProgress = 0;
            SetPhaseDuration();
        }

        private void AdvanceArrivalPhase(RunwayController? runway, GateManager gates, WeatherCondition weather, int rvrMeters)
        {
            double gateX = State.GateX > 0 ? State.GateX : 300;
            double gateY = State.GateY > 0 ? State.GateY : 400;

            switch (State.Phase)
            {
                case AircraftPhase.Approaching: State.Phase = AircraftPhase.OnFinal; break;
                case AircraftPhase.OnFinal:
                    int minimumRvr = State.Type switch { AircraftType.Small => 550, AircraftType.Medium => 300, AircraftType.Large => 200, _ => 550 };
                    bool forcedByWeather = rvrMeters < minimumRvr;
                    double actualGoAroundChance = forcedByWeather ? 1.0 : GoAroundChance;

                    if (State.GoAroundCount < 2 && _rand.NextDouble() < actualGoAroundChance)
                    {
                        runway?.Release(State.AssignedRunway, State.FlightId); // NEW
                        State.Phase = AircraftPhase.GoAround;
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
                case AircraftPhase.Landing: State.Phase = AircraftPhase.Rollout; break;
                case AircraftPhase.Rollout:
                    runway?.Release(State.AssignedRunway, State.FlightId); // NEW
                    State.Phase = AircraftPhase.Taxiing;
                    
                    // NEW: Dynamic exit point based on runway
                    double runwayY = State.AssignedRunway == RunwayId.Runway28L ? 480 : 520;
                    _currentRoute = gates.GetArrivalRoute(new SimPoint(600, runwayY), new SimPoint(gateX, gateY));
                    break;
                case AircraftPhase.Taxiing:
                    State.Phase = AircraftPhase.Parked;
                    _currentRoute = null;
                    State.RecentTrail.Clear(); 
                    break;
            }
        }

        private void AdvanceDeparturePhase(RunwayController? runway, GateManager gates)
        {
            double gateX = State.GateX > 0 ? State.GateX : 300;
            double gateY = State.GateY > 0 ? State.GateY : 400;

            switch (State.Phase)
            {
                case AircraftPhase.Parked: State.Phase = AircraftPhase.Pushback; break;
                case AircraftPhase.Pushback:
                    State.Phase = AircraftPhase.Taxiing;
                    _currentRoute = gates.GetDepartureRoute(new SimPoint(gateX, gateY));
                    break;
                case AircraftPhase.Taxiing:
                    State.Phase = AircraftPhase.Holding;
                    _currentRoute = null;
                    break;
                case AircraftPhase.Holding: State.Phase = AircraftPhase.Takeoff; break;
                case AircraftPhase.Takeoff:
                    runway?.Release(State.AssignedRunway, State.FlightId); // NEW
                    State.Phase = AircraftPhase.Climbing;
                    break;
                case AircraftPhase.Climbing: State.Phase = AircraftPhase.Departed; IsFinished = true; break;
            }
        }

        private void SetPhaseDuration()
        {
            double multiplier = State.Type switch { AircraftType.Small => 1.0, AircraftType.Medium => 1.5, AircraftType.Large => 2.0, _ => 1.0 };
            double baseMinutes = State.Phase switch
            {
                AircraftPhase.Approaching => 5.0, AircraftPhase.OnFinal => 2.0, AircraftPhase.GoAround => 3.0,
                AircraftPhase.Landing => 0.5, AircraftPhase.Rollout => 1.0, AircraftPhase.Taxiing => 3.0,
                AircraftPhase.Pushback => 1.0, 
                AircraftPhase.Holding => State.FlightType == FlightType.Arrival ? 4.0 : 0.0, 
                AircraftPhase.Takeoff => 0.5, AircraftPhase.Climbing => 3.0, AircraftPhase.Diverted => 4.0, _ => 1.0
            };
            _phaseDurationMs = baseMinutes * 60_000 * multiplier;
            if (_phaseDurationMs == 0) _phaseDurationMs = 1;
        }

        private void UpdatePositionAndHeading()
        {
            double startX, startY, endX, endY;
            double gateX = State.GateX > 0 ? State.GateX : 300;
            double gateY = State.GateY > 0 ? State.GateY : 400;
            
            // NEW: Align visual path with the assigned runway
            double runwayY = State.AssignedRunway == RunwayId.Runway28L ? 480 : 520; 

            if (State.Phase == AircraftPhase.Taxiing && _currentRoute != null && _currentRoute.Count >= 2)
            {
                double totalDist = 0;
                var segDists = new List<double>();
                for (int i = 0; i < _currentRoute.Count - 1; i++)
                {
                    double d = Distance(_currentRoute[i], _currentRoute[i + 1]);
                    segDists.Add(d);
                    totalDist += d;
                }

                double targetDist = totalDist * State.PhaseProgress;
                double accum = 0;

                for (int i = 0; i < _currentRoute.Count - 1; i++)
                {
                    if (accum + segDists[i] >= targetDist || i == _currentRoute.Count - 2)
                    {
                        double segProgress = (targetDist - accum) / segDists[i];
                        segProgress = Math.Clamp(segProgress, 0.0, 1.0);

                        double curX = _currentRoute[i].X + (_currentRoute[i + 1].X - _currentRoute[i].X) * segProgress;
                        double curY = _currentRoute[i].Y + (_currentRoute[i + 1].Y - _currentRoute[i].Y) * segProgress;

                        State.Position = new SimPoint(curX, curY);
                        State.Heading = CalculateHeading(_currentRoute[i], _currentRoute[i + 1]);
                        return;
                    }
                    accum += segDists[i];
                }
            }

            switch (State.Phase)
            {
                case AircraftPhase.Approaching: (startX, startY, endX, endY) = (2200, runwayY - 200, 1600, runwayY - 180); State.Heading = 225; break;
                case AircraftPhase.OnFinal: (startX, startY, endX, endY) = (1600, runwayY - 180, 1400, runwayY); State.Heading = 225; break;
                case AircraftPhase.GoAround: (startX, startY, endX, endY) = (1400, runwayY, 2200, 100); State.Heading = 45; break;
                case AircraftPhase.Landing:
                case AircraftPhase.Rollout: (startX, startY, endX, endY) = (1400, runwayY, 600, runwayY); State.Heading = 270; break;
                case AircraftPhase.Parked: (startX, startY, endX, endY) = (gateX, gateY, gateX, gateY); State.Heading = 0; break;
                case AircraftPhase.Pushback: (startX, startY, endX, endY) = (gateX, gateY, gateX, gateY + 30); State.Heading = 0; break;
                
                case AircraftPhase.Holding:
                    if (State.FlightType == FlightType.Arrival)
                    {
                        double angle = State.PhaseProgress * Math.PI * 2;
                        double curX = 1800 + Math.Cos(angle) * 150;
                        double curY = 150 + Math.Sin(angle) * 60;
                        (startX, startY, endX, endY) = (curX, curY, curX, curY); 
                        
                        double tangent = Math.Atan2(Math.Cos(angle) * 60, -Math.Sin(angle) * 150);
                        State.Heading = (tangent * 180.0 / Math.PI + 90 + 360) % 360;
                    }
                    else
                    {
                        (startX, startY, endX, endY) = (State.Position.X, State.Position.Y, State.Position.X, State.Position.Y);
                        State.Heading = 90;
                    }
                    break;

                case AircraftPhase.Takeoff:
                    (startX, startY, endX, endY) = (State.Position.X, State.Position.Y, 1580, State.Position.Y);
                    State.Heading = 90;
                    break;
                
                case AircraftPhase.Climbing: (startX, startY, endX, endY) = (1580, runwayY, 2200, 80); State.Heading = 45; break;
                case AircraftPhase.Diverted: (startX, startY, endX, endY) = (State.Position.X, State.Position.Y, -500, -500); State.Heading = 315; break;
                default: (startX, startY, endX, endY) = (0, 0, 0, 0); break;
            }

            State.Position = new SimPoint(
                startX + (endX - startX) * State.PhaseProgress,
                startY + (endY - startY) * State.PhaseProgress
            );
        }

        private void UpdateAltitudeAndSpeed()
        {
            if (State.Phase == AircraftPhase.Holding && State.FlightType == FlightType.Arrival)
            {
                int holdSpeed = State.AssignedSpeedKts ?? (int)(210 * (State.Type switch { AircraftType.Small => 0.8, AircraftType.Large => 1.15, _ => 1.0 }));
                State.SpeedKts = holdSpeed;
                return;
            }

            double groundY  = 520.0;
            double currentY = State.Position.Y;
            int defaultAlt = (int)Math.Max(0, (groundY - currentY) * 7.8);
            
            if (State.AssignedAltitudeFt.HasValue && 
               (State.Phase == AircraftPhase.Approaching || State.Phase == AircraftPhase.Climbing || State.Phase == AircraftPhase.Diverted || State.Phase == AircraftPhase.GoAround))
            {
                State.AltitudeFt = State.AssignedAltitudeFt.Value;
            }
            else
            {
                State.AltitudeFt = defaultAlt;
            }

            int defaultSpeed = State.Phase switch
            {
                AircraftPhase.Approaching => 180, AircraftPhase.OnFinal => 140, AircraftPhase.GoAround => 160,
                AircraftPhase.Landing => 120, AircraftPhase.Rollout => 60, AircraftPhase.Pushback => 5,  
                AircraftPhase.Taxiing => 15, AircraftPhase.Parked => 0, 
                AircraftPhase.Takeoff => 150, AircraftPhase.Climbing => 200, AircraftPhase.Diverted => 220, _ => 0
            };

            double factor = State.Type switch { AircraftType.Small => 0.8, AircraftType.Large => 1.15, _ => 1.0 };
            
            if (State.AssignedSpeedKts.HasValue && 
               (State.Phase == AircraftPhase.Approaching || State.Phase == AircraftPhase.Climbing || State.Phase == AircraftPhase.Diverted || State.Phase == AircraftPhase.GoAround))
            {
                State.SpeedKts = State.AssignedSpeedKts.Value;
            }
            else
            {
                State.SpeedKts = (int)(defaultSpeed * factor);
            }
        }

        private static double Distance(SimPoint a, SimPoint b)
        {
            double dx = a.X - b.X;
            double dy = a.Y - b.Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        private static double CalculateHeading(SimPoint a, SimPoint b)
        {
            double radians = Math.Atan2(b.Y - a.Y, b.X - a.X);
            double degrees = radians * (180.0 / Math.PI);
            return (degrees + 90 + 360) % 360; 
        }
    }
}