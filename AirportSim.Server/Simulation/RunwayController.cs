using AirportSim.Shared.Models;

namespace AirportSim.Server.Simulation
{
    public class RunwayController
    {
        public RunwayStatus Status { get; private set; } = RunwayStatus.Free;
        private string? _currentOccupantId;

        public bool IsFree => Status == RunwayStatus.Free;

        public bool TryOccupy(string flightId)
        {
            if (!IsFree) return false;
            
            Status = RunwayStatus.Occupied;
            _currentOccupantId = flightId;
            return true;
        }

        public void Release(string flightId)
        {
            if (_currentOccupantId == flightId)
            {
                Status = RunwayStatus.Free;
                _currentOccupantId = null;
            }
        }
    }
}