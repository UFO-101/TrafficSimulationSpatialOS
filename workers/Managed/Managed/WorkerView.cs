using System;
using Improbable;
using Improbable.Collections;
using Improbable.Worker;
using Mapandcars;
using OpenStreetMap;
using System.Linq;

namespace Managed
{
    internal class WorkerView : Dispatcher
    {
        private Connection connection;
        public List<RequestId<CreateEntityRequest>> carCreationRequestIds = new List<RequestId<CreateEntityRequest>>();

        private System.Collections.Generic.HashSet<EntityId> entitiesInView = new System.Collections.Generic.HashSet<EntityId>();
        public System.Collections.Generic.HashSet<EntityId> positionsInAuthority { get; } = new System.Collections.Generic.HashSet<EntityId>();
        private System.Collections.Generic.HashSet<EntityId> carsInView = new System.Collections.Generic.HashSet<EntityId>();
        public System.Collections.Generic.HashSet<EntityId> carsInAuthority { get; } = new System.Collections.Generic.HashSet<EntityId>();
        private System.Collections.Generic.HashSet<EntityId> busesInView = new System.Collections.Generic.HashSet<EntityId>();
        public System.Collections.Generic.HashSet<EntityId> busesInAuthority { get; } = new System.Collections.Generic.HashSet<EntityId>();
        public System.Collections.Generic.HashSet<EntityId> busStopsInAuthority { get; } = new System.Collections.Generic.HashSet<EntityId>();

        public Map<EntityId, Coordinates> entityPositions { get; } = new Map<EntityId, Coordinates>();
        public Map<EntityId, ulong> carRoadIds { get; } = new Map<EntityId, ulong>();
        public Map<EntityId, ulong> carNodeIds { get; } = new Map<EntityId, ulong>(); // The current node that each car is aiming
        public Map<EntityId, ulong> prevCarNodeIds { get; } = new Map<EntityId, ulong>(); // The node that the car is coming from
        public Map<EntityId, string> busesVehicleIds { get; } = new Map<EntityId, string>();
        public Map<EntityId, List<string>> busesNextStops { get; } = new Map<EntityId, List<string>>();
        public Map<EntityId, Map<string, string>> busesArrivalEstimate { get; } = new Map<EntityId, Map<string, string>>();
        public Map<EntityId, string> busStopAtcoCodes { get; } = new Map<EntityId, string>();

        public WorkerView(Connection connection): base()
        {
            this.connection = connection;
            OnCreateEntityResponse(Startup.EntityCreateCallback);
            OnAddEntity(op => { entitiesInView.Add(op.EntityId); });
            OnRemoveEntity(op => { entitiesInView.Remove(op.EntityId); });
            OnAuthorityChange(Car.Metaclass, UpdateCarAuthorityList);
            OnAuthorityChange(Bus.Metaclass, UpdateBusAuthorityList);
            OnAuthorityChange(BusStop.Metaclass, UpdateBusStopAuthorityList);
            OnAuthorityChange(Position.Metaclass, UpdatePositionAuthorityList);
            OnComponentUpdate(Position.Metaclass, op => {
                UpdatePositionData(op.EntityId, op.Update.coords, "[Position component UPDATE]");
            });
            OnAddComponent(Position.Metaclass, op => {
                //UpdatePositionData(op.EntityId, op.Data.coords, "[Position component ADD]");
            });
            OnComponentUpdate(Car.Metaclass, op => {
                UpdateCarData(op.EntityId, op.Update.currentRoadId, op.Update.currentNodeId, op.Update.prevNodeId);
            });
            OnAddComponent(Car.Metaclass, op => {
                UpdateCarData(op.EntityId, op.Data.currentRoadId, op.Data.currentNodeId, op.Data.prevNodeId);
            });
            OnComponentUpdate(Bus.Metaclass, op => {
                UpdateBusData(op.EntityId, op.Update.vehicleId, op.Update.nextStops, op.Update.stopArrivalEstimates);
            });
            OnAddComponent(Bus.Metaclass, op => {
                UpdateBusData(op.EntityId, op.Data.vehicleId, op.Data.nextStops, op.Data.stopArrivalEstimates);
            });
            OnComponentUpdate(BusStop.Metaclass, op => {
                UpdateBusStopData(op.EntityId, op.Update.atcoCode);
            });
            OnAddComponent(BusStop.Metaclass, op => {
                UpdateBusStopData(op.EntityId, op.Data.atcoCode);
            });
        }

        private void UpdatePositionAuthorityList(AuthorityChangeOp op)
        {
            if (op.Authority == Authority.Authoritative)
                positionsInAuthority.Add(op.EntityId);
            else if (op.Authority == Authority.NotAuthoritative)
                positionsInAuthority.Remove(op.EntityId);
        }

        private void UpdateCarAuthorityList(AuthorityChangeOp op)
        {
            if(op.Authority == Authority.Authoritative)
                carsInAuthority.Add(op.EntityId);
            else if (op.Authority == Authority.NotAuthoritative)
                carsInAuthority.Remove(op.EntityId);
        }

        private void UpdateBusAuthorityList(AuthorityChangeOp op)
        {
            if (op.Authority == Authority.Authoritative)
            {
                busesInAuthority.Add(op.EntityId);
                connection.SendLogMessage(LogLevel.Info, "Worker View", "Bus with entityId " + op.EntityId + " now in authority - updaing bus times");
                Startup.UpdateBusTimes(connection, this, op.EntityId);
            }
            else if (op.Authority == Authority.NotAuthoritative)
            {
                Startup.busVehicleIds.Remove(busesVehicleIds[op.EntityId]);
                busesInAuthority.Remove(op.EntityId);
                connection.SendLogMessage(LogLevel.Info, "Worker View", "Bus with entityId " + op.EntityId + " is NOT in authority");
            }
        }

        private void UpdateBusStopAuthorityList(AuthorityChangeOp op)
        {
            if (op.Authority == Authority.Authoritative)
                busStopsInAuthority.Add(op.EntityId);
            else if (op.Authority == Authority.NotAuthoritative)
                busStopsInAuthority.Remove(op.EntityId);
        }

        private void UpdatePositionData(EntityId entityId, Option<Coordinates> coords, string message)
        {
            if (!entitiesInView.Contains(entityId))
                entitiesInView.Add(entityId);

            if (coords.HasValue)
            {
                if (coords.Value.y == -99.99)
                {
                    randomisePosition(entityId);
                }
                else
                    entityPositions[entityId] = coords.Value;
            }
            else
            {
                connection.SendLogMessage(LogLevel.Error, "Worker View", "Position Update Data coords doesn't have value!");
            }
        }

        private void UpdateCarData(EntityId entityId, Option<ulong> currentRoadId, Option<ulong> currentNodeId, Option<ulong> prevNodeId)
        {
            if (!carsInView.Contains(entityId))
                carsInView.Add(entityId);

            if (!carsInView.Contains(entityId) && !(currentRoadId.HasValue && currentRoadId.Value == 99999 && currentNodeId.HasValue
                && currentNodeId.Value == 99999 && prevNodeId.HasValue && prevNodeId.Value == 99999))
            {
                connection.SendLogMessage(LogLevel.Error, "Worker View", "Newly viewed car doesn't have hardcoded values");
            }

            // Ignores the initial data
            if (currentRoadId.HasValue && currentRoadId.Value == 99999 && currentNodeId.HasValue
                && currentNodeId.Value == 99999 && prevNodeId.HasValue && prevNodeId.Value == 99999)
            {
                return;
            }

            if (currentRoadId.HasValue)
                carRoadIds[entityId] = currentRoadId.Value;
            else
                connection.SendLogMessage(LogLevel.Error, "Worker View", "Car Update Data currentRoadId doesn't have value!");

            if (currentNodeId.HasValue)
                carNodeIds[entityId] = currentNodeId.Value;
            else
                connection.SendLogMessage(LogLevel.Error, "Worker View", "Car Update Data currentNodeId doesn't have value!");

            if (prevNodeId.HasValue)
                prevCarNodeIds[entityId] = prevNodeId.Value;
            else
                connection.SendLogMessage(LogLevel.Info, "Worker View", "Car Update Data prevNodeId doesn't have value (which may be okay)");
        }

        private void UpdateBusData(EntityId entityId, Option<string> busVehicleId, Option<List<string>> nextStops, Option<Map<string, string>> stopArrivalEstimates)
        {
            if (!carsInView.Contains(entityId))
                carsInView.Add(entityId);

            if (busVehicleId.HasValue)
                busesVehicleIds[entityId] = busVehicleId.Value;
            else
                connection.SendLogMessage(LogLevel.Error, "Worker View", "Bus Update Data busVehicleId doesn't have value!");

            if (nextStops.HasValue)
                busesNextStops[entityId] = nextStops.Value;
            else
                connection.SendLogMessage(LogLevel.Error, "Worker View", "Bus Update Data nextStops doesn't have value!");

            if (stopArrivalEstimates.HasValue)
                busesArrivalEstimate[entityId] = stopArrivalEstimates.Value;
            else
                connection.SendLogMessage(LogLevel.Info, "Worker View", "Bus Update Data arrivalEstimate doesn't have value (which may be okay)");
        }

        private void UpdateBusStopData(EntityId entityId, Option<string> atcoCode)
        {
            if (atcoCode.HasValue)
                busStopAtcoCodes[entityId] = atcoCode.Value;
            else
                connection.SendLogMessage(LogLevel.Error, "Worker View", "Bus Stop Update Data busVehicleId doesn't have value!");
        }

        public void randomisePosition(EntityId entityId)
        {
            // initialise the random starting positions of the cars - won't be sent to spatial os until MoveCars() reads the data
            Random random = new Random();
            ulong[] roadNodesArray = Startup.MapReader.roadNodes.ToArray();
            ulong roadNodeId = roadNodesArray[random.Next(0, roadNodesArray.Length)];
            OsmNode roadNode = Startup.MapReader.nodes[roadNodeId];
            Coordinates startCoords = roadNode.coords;
            ulong roadId = roadNode.waysOn.First();

            entityPositions[entityId] = startCoords;
            carNodeIds[entityId] = roadNodeId;
            carRoadIds[entityId] = roadId;
            prevCarNodeIds[entityId] = roadNodeId;

            var positionComponentUpdate = Position.Update.FromInitialData(new PositionData(startCoords));
            var carComponentUpdate = Car.Update.FromInitialData(new CarData(roadNodeId, roadNodeId, roadId));
            UpdateParameters updateParameters = new UpdateParameters();
            updateParameters.Loopback = ComponentUpdateLoopback.ShortCircuited;
            connection.SendComponentUpdate(Position.Metaclass, entityId, positionComponentUpdate.DeepCopy(), updateParameters);
            connection.SendComponentUpdate(Car.Metaclass, entityId, carComponentUpdate.DeepCopy(), updateParameters);
        }
    }
}