using System;
//using System.Collections.Generic;
using System.Linq;
using Improbable;
using System.Text;
using System.Threading.Tasks;
using Improbable.Worker;
using OpenStreetMap;
using Mapandcars;
using Improbable.Collections;

namespace Managed
{
    internal class Movement
    {
        public static double MilesPerHoursTo10metersPerTimeInterval(int mph)
        {
            return ((mph * 1.60934) / (60 * 60)) * 100 * Startup.upateInterval; 
        }

        public static void MoveCars(Connection connection, WorkerView dispatcher, MapReader mapReader)
        {
            Random random = new Random();
            foreach (EntityId entityId in dispatcher.carsInAuthority)
            {
                Coordinates carPos;
                if (!dispatcher.entityPositions.TryGetValue(entityId, out carPos))
                    connection.SendLogMessage(LogLevel.Error, connection.GetWorkerId(), "No car position for entityId: " + entityId.ToString());
                ulong currentNodeId;
                if (!dispatcher.carNodeIds.TryGetValue(entityId, out currentNodeId))
                {
                    connection.SendLogMessage(LogLevel.Error, connection.GetWorkerId(), "No car node for entityId: " + entityId.ToString() + " - randomising");
                    dispatcher.randomisePosition(entityId);
                    continue;
                }
                ulong prevNodeId;
                if (!dispatcher.prevCarNodeIds.TryGetValue(entityId, out prevNodeId))
                    connection.SendLogMessage(LogLevel.Error, connection.GetWorkerId(), "No prev car node for entityId: " + entityId.ToString());
                ulong currentRoadId;
                if (!dispatcher.carRoadIds.TryGetValue(entityId, out currentRoadId))
                    connection.SendLogMessage(LogLevel.Error, connection.GetWorkerId(), "No car road id for entityId: " + entityId.ToString());
                ulong newCurrentNodeId = currentNodeId;
                ulong newPrevNodeId = prevNodeId;
                ulong newCurrentRoadId = currentRoadId;

                if (currentRoadId == 0)
                    connection.SendLogMessage(LogLevel.Error, connection.GetWorkerId(), "currentNodeId: " + currentNodeId + " prevNodeId: " + prevNodeId + " currentRoadId: " + currentRoadId + " for entityId: " + entityId.ToString());

                List<Coordinates> pathCoords = new List<Coordinates>(new Coordinates[] { mapReader.nodes[currentNodeId].coords });
                List<ulong> pathNodes = new List<ulong>(new ulong[] { currentNodeId });
                double newPathSegmentLength = 0;
                double pathLength = 0;
                bool pastFirstNode = false;
                ulong nextNodeId = currentNodeId;
                OsmNode nextNode = mapReader.nodes[nextNodeId];
                double maxPathLength = 0;
                bool reachedSpeedLimit = false;
                bool bus = dispatcher.busesInAuthority.Contains(entityId);

                do
                {
                    OsmNode currentCarNode;
                    if (!mapReader.nodes.TryGetValue(pathNodes.Last(), out currentCarNode))
                    {
                        connection.SendLogMessage(LogLevel.Info, connection.GetWorkerId(), "couldn't find current car node");
                    }

                    // We need to pass the current node we're aiming for before proceeding to the next
                    if (pastFirstNode)
                    {
                        List<ulong> attemptedNodes = new List<ulong>();
                        do
                        {
                            int highestSpeedLimit = 0;
                            double smallestAngle = 180;
                            ulong nearestNodeId = 0;
                            DateTime predictedTime = new DateTime();
                            nextNodeId = currentCarNode.adjacentNodes[0];

                            Coordinates busStopToBusRelativeCoords = new Coordinates();
                            if (bus)
                            {
                                string nextStopAtcoCode = dispatcher.busesNextStops[entityId].First();
                                Map<string, string> arrivalEstimates = dispatcher.busesArrivalEstimate[entityId];
                                string nextStopExpectedTime;
                                if(!arrivalEstimates.TryGetValue(nextStopAtcoCode, out nextStopExpectedTime))
                                {
                                    connection.SendLogMessage(LogLevel.Error, connection.GetWorkerId(), "Bus entity " + entityId + " couldn't get expected time for stop " + nextStopAtcoCode);
                                    goto EndOfThisEntityMovement;
                                }
                                predictedTime = DateTime.Parse(nextStopExpectedTime, System.Globalization.CultureInfo.InvariantCulture);
                                int timeToBusStop = predictedTime.Subtract(DateTime.UtcNow).Seconds;
                                //connection.SendLogMessage(LogLevel.Error, connection.GetWorkerId(), "Bus entity " + entityId + " should get to stop " + nextStopAtcoCode + " in " + timeToBusStop + " minutes");
                                ulong busStopNodeId;
                                if (mapReader.busStops.TryGetValue(nextStopAtcoCode, out busStopNodeId))
                                {
                                    OsmNode busStopNode = mapReader.nodes[busStopNodeId];
                                    busStopToBusRelativeCoords = Coords.Subtract(busStopNode.coords, carPos);
                                    nearestNodeId = mapReader.nearestRoadNodesToBusStops[busStopNodeId];
                                    //connection.SendLogMessage(LogLevel.Info, connection.GetWorkerId(), "Bus entity " + entityId + " DID find stop" + nextStopAtcoCode);
                                }
                                else
                                {
                                    connection.SendLogMessage(LogLevel.Info, connection.GetWorkerId(), "Bus entity " + entityId + " could not find stop " + nextStopAtcoCode + " DELETING");
                                    connection.SendDeleteEntityRequest(entityId, new Option<uint>());

                                    string vehicleId;
                                    if (dispatcher.busesVehicleIds.TryGetValue(entityId, out vehicleId))
                                        Startup.busVehicleIds.Remove(vehicleId);
                                    
                                    goto EndOfThisEntityMovement;
                                }
                                if (timeToBusStop < 1 || Coords.Length(busStopToBusRelativeCoords) > 150)
                                {
                                    if (moveVehicleToBusStop(connection, dispatcher, mapReader, entityId, nextStopAtcoCode))
                                    {
                                        connection.SendLogMessage(LogLevel.Error, connection.GetWorkerId(), "Successfully teleported bus");
                                        goto EndOfThisEntityMovement;
                                    }
                                    else
                                    {
                                        connection.SendLogMessage(LogLevel.Error, connection.GetWorkerId(), "Couldn't teleport bus");
                                    }
                                    dispatcher.busesNextStops[entityId].RemoveAt(0);
                                }
                            }
                            foreach (ulong nodeId in currentCarNode.adjacentNodes)
                            {
                                OsmNode node = mapReader.nodes[nodeId];
                                if (bus)
                                {
                                    if(nodeId == nearestNodeId || currentCarNode.Id == nearestNodeId)
                                    {
                                        int timeToBusStop = predictedTime.Subtract(DateTime.UtcNow).Seconds;
                                        if (timeToBusStop > 30)
                                        {
                                            // time wasted
                                        }
                                        int timeSinceBusStop = DateTime.UtcNow.Subtract(predictedTime).Seconds;
                                        if (timeSinceBusStop < 30)
                                        {
                                            // wait at bus stop
                                            goto EndOfThisEntityMovement;
                                        }
                                        updateBusToNextStop(connection, dispatcher, entityId);
                                    }
                                    Coordinates nodeToBusRelativeCoodrs = Coords.Subtract(node.coords, carPos);
                                    double angleBetween = Coords.AngleBetween(busStopToBusRelativeCoords, nodeToBusRelativeCoodrs);
                                    if (angleBetween < smallestAngle && !attemptedNodes.Contains(nodeId))
                                    {
                                        smallestAngle = angleBetween;
                                        nextNodeId = nodeId;
                                    }
                                }
                                else
                                {
                                    foreach (ulong wayId in node.waysOn)
                                    {
                                        OsmWay way = mapReader.ways[wayId];
                                        int limit = way.SpeedLimit;
                                        if (limit > highestSpeedLimit && !attemptedNodes.Contains(nodeId) && random.NextDouble() < 0.7)
                                        {
                                            highestSpeedLimit = limit;
                                            nextNodeId = nodeId;
                                        }
                                    }
                                }

                            }
                            attemptedNodes.Add(nextNodeId);
                            if (!mapReader.nodes.ContainsKey(nextNodeId))
                            {
                                connection.SendLogMessage(LogLevel.Error, connection.GetWorkerId(), "couldn't find adjacent node");
                            }
                            if (!mapReader.roadNodes.Contains(nextNodeId))
                            {
                                connection.SendLogMessage(LogLevel.Error, connection.GetWorkerId(), "adjacent node is not in road nodes");
                            }
                        }
                        while (nextNodeId == prevNodeId && currentCarNode.adjacentNodes.Count > 1);
                        newPrevNodeId = pathNodes.Last();
                        nextNode = mapReader.nodes[nextNodeId];
                        pathCoords.Add(nextNode.coords);
                        pathNodes.Add(nextNodeId);
                    }

                    newPathSegmentLength = Coords.Dist(carPos, pathCoords.Last());
                    OsmWay currentRoadWay = mapReader.ways[currentRoadId];
                    maxPathLength = MilesPerHoursTo10metersPerTimeInterval(currentRoadWay.SpeedLimit);
                    if (pathLength + newPathSegmentLength > maxPathLength)
                    {
                        double correctSegmentLength = maxPathLength - pathLength;
                        Coordinates currentDirection = Coords.Subtract(pathCoords.Last(), carPos);
                        carPos = Coords.Add(carPos, Coords.ScaleToLength(currentDirection, correctSegmentLength));
                        pathLength += correctSegmentLength;
                        reachedSpeedLimit = true;
                    }
                    else
                    {
                        pathLength += newPathSegmentLength;
                        carPos = nextNode.coords;
                    }
                    pastFirstNode = true;
                }
                while (!reachedSpeedLimit);

                if (newPosIsACarCrash(connection, dispatcher, carPos, nextNodeId, newPrevNodeId, entityId, maxPathLength * 0.8))
                    continue;

                newCurrentNodeId = nextNodeId;
                if (!nextNode.waysOn.Contains(currentRoadId))
                {
                    newCurrentRoadId = nextNode.waysOn.First();
                }
                var positionComponentUpdate = Position.Update.FromInitialData(new PositionData(carPos));
                var carComponentUpdate = Car.Update.FromInitialData(new CarData(newCurrentNodeId, newPrevNodeId, newCurrentRoadId));
                connection.SendComponentUpdate(Position.Metaclass, entityId, positionComponentUpdate); // short-curcuits by default
                connection.SendComponentUpdate(Car.Metaclass, entityId, carComponentUpdate);

            EndOfThisEntityMovement:;
            }
        }

        private static bool newPosIsACarCrash(Connection connection, WorkerView dispatcher, Coordinates newCarPos, ulong newCarNode, ulong prevCarNode, EntityId entityId, double minDistance)
        {
            foreach (EntityId otherCarEntityId in dispatcher.carsInAuthority)
            {
                try
                {
                    if (dispatcher.carNodeIds[otherCarEntityId] == newCarNode && dispatcher.prevCarNodeIds[otherCarEntityId] == prevCarNode && otherCarEntityId != entityId)
                    {
                        if (Coords.Dist(newCarPos, dispatcher.entityPositions[otherCarEntityId]) < minDistance)
                        {
                            return true;
                        }
                    }
                }
                catch (System.Collections.Generic.KeyNotFoundException e)
                {
                    //connection.SendLogMessage(LogLevel.Error, connection.GetWorkerId(), "Exception: " + e.Message);
                    continue;
                }
            }
            return false;
        }

        private static bool moveVehicleToBusStop(Connection connection, WorkerView dispatcher, MapReader mapReader, EntityId entityId, string busStopAtcoCode)
        {
            ulong busStopNodeId;
            if (mapReader.busStops.TryGetValue(busStopAtcoCode, out busStopNodeId))
            {
                ulong nearestNodeId = mapReader.nearestRoadNodesToBusStops[busStopNodeId];
                OsmNode nearestNode = mapReader.nodes[nearestNodeId];

                var positionComponentUpdate = Position.Update.FromInitialData(new PositionData(nearestNode.coords));
                var carComponentUpdate = Car.Update.FromInitialData(new CarData(nearestNodeId, nearestNodeId, nearestNode.waysOn.First()));
                connection.SendComponentUpdate(Position.Metaclass, entityId, positionComponentUpdate); // short curcuits by default
                connection.SendComponentUpdate(Car.Metaclass, entityId, carComponentUpdate);
                //updateBusToNextStop(connection, dispatcher, entityId);
                return true;
            }
            return false;
        }

        private static void updateBusToNextStop(Connection connection, WorkerView dispatcher, EntityId entityId)
        {
            List<string> nextStops;
            if(dispatcher.busesNextStops.TryGetValue(entityId, out nextStops))
            {
                // This could be optimised - redundent data is being sent
                string vehicleId = dispatcher.busesVehicleIds[entityId];
                Map<string, string> arrivalTimes = dispatcher.busesArrivalEstimate[entityId];
                nextStops.RemoveAt(0);
                var busComponentUpdate = Bus.Update.FromInitialData(new BusData(vehicleId, nextStops, arrivalTimes));
                connection.SendComponentUpdate(Bus.Metaclass, entityId, busComponentUpdate);
            }
            else
            {
                connection.SendLogMessage(LogLevel.Error, connection.GetWorkerId(), "Couldn't get list of next stops for bus: " + entityId);
            }
        }
    }
}
