using System;
using Improbable;
using Improbable.Worker;
using Improbable.Collections;
using OpenStreetMap;
using Mapandcars;
using System.Linq;

namespace Managed
{
    internal class InitialiseWorld
    {
        public static MapReader ReadMapFile(string filename) {
            MapReader mapReader = new MapReader();
            try
            {
                string mapFilePath = System.AppDomain.CurrentDomain.BaseDirectory + filename;//"warwick_uni_map.osm";//
                Startup.ClassConnection.SendLogMessage(LogLevel.Info, Startup.LoggerName, "map file path: " + mapFilePath);

                mapReader.Read(mapFilePath);
                Startup.ClassConnection.SendLogMessage(LogLevel.Info, Startup.LoggerName, "Node list length: " + mapReader.nodes.Count);
            }
            catch (System.IO.FileNotFoundException)
            {
                Startup.ClassConnection.SendLogMessage(LogLevel.Info, Startup.LoggerName, "Can't read map file");
            }
            return mapReader;
        }


        public static List<ulong> createOsmNodes(MapReader mapReader, Dispatcher dispatcher, Connection connection) {
            List<ulong> roadNodeIds = new List<ulong>();
            foreach (OsmWay way in mapReader.ways.Values.ToList()) {
                if (way.IsRoad) {
                    foreach (ulong nodeId in way.NodeIDs) {
                        OsmNode thisNode;
                        if (mapReader.nodes.TryGetValue(nodeId, out thisNode))
                        {
                            if (thisNode.Id != nodeId) {
                                Startup.ClassConnection.SendLogMessage(LogLevel.Info, Startup.LoggerName, "Node ids don't match!");
                            }
                            if (!roadNodeIds.Contains(nodeId)) {
                                roadNodeIds.Add(nodeId);
                                RequestId<CreateEntityRequest> roadNodeRequestId = CreationRequests.CreateOsmNodeEntity(dispatcher, connection, "Road Node", thisNode.coords);
                                //requestIdToNodeIdDict.Add(roadNodeRequestId, nodeId);
                            }
                        } else {
                            Startup.ClassConnection.SendLogMessage(LogLevel.Info, Startup.LoggerName, "Couldn't find road node by id");
                        }
                    }
                }
                if (way.IsBuilding) {

                }
            }
            return roadNodeIds;
        }

        public static void createBusStops(MapReader mapReader, Dispatcher dispatcher, Connection connection)
        {
            foreach (ulong busStopId in mapReader.busStops)
            {
                OsmNode thisNode;
                if (mapReader.nodes.TryGetValue(busStopId, out thisNode))
                {
                    RequestId<CreateEntityRequest> busStopRequestId = CreationRequests.CreateBusStopEntity(dispatcher, connection, "Bus stop", thisNode.coords, thisNode.actoCode);
                    //requestIdToBusStopIdDict.Add(busStopRequestId, busStopId);
                }
                else
                    Startup.ClassConnection.SendLogMessage(LogLevel.Info, Startup.LoggerName, "Couldn't find bus stop node by id");
            }
        }
    }
}