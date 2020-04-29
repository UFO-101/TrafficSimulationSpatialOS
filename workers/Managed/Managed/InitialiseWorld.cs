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
                Startup.StaticConnection.SendLogMessage(LogLevel.Info, Startup.StaticLogName, "map file path: " + mapFilePath);

                mapReader.Read(mapFilePath);
                Startup.StaticConnection.SendLogMessage(LogLevel.Info, Startup.StaticLogName, "Node list length: " + mapReader.nodes.Count);
            }
            catch (System.IO.FileNotFoundException)
            {
                Startup.StaticConnection.SendLogMessage(LogLevel.Info, Startup.StaticLogName, "Can't read map file");
            }
            return mapReader;
        }


        public static void createOsmNodes(MapReader mapReader, Dispatcher dispatcher, Connection connection) {
            List<ulong> createdRoadNodeIds = new List<ulong>();
            foreach (OsmWay way in mapReader.ways.Values.ToList()) {
                if (way.IsRoad) {
                    foreach (ulong nodeId in way.NodeIDs) {
                        OsmNode thisNode;
                        if (mapReader.nodes.TryGetValue(nodeId, out thisNode))
                        {
                            if (thisNode.Id != nodeId) {
                                Startup.StaticConnection.SendLogMessage(LogLevel.Info, Startup.StaticLogName, "Node ids don't match!");
                            }
                            if (!createdRoadNodeIds.Contains(nodeId)) {
                                createdRoadNodeIds.Add(nodeId);
                                RequestId<CreateEntityRequest> roadNodeRequestId = CreationRequests.CreateOsmNodeEntity(dispatcher, connection, "Road Node", thisNode.coords);
                                //requestIdToNodeIdDict.Add(roadNodeRequestId, nodeId);
                            }
                        } else {
                            Startup.StaticConnection.SendLogMessage(LogLevel.Info, Startup.StaticLogName, "Couldn't find road node by id");
                        }
                    }
                }
                if (way.IsBuilding) {

                }
            }
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
                    Startup.StaticConnection.SendLogMessage(LogLevel.Info, Startup.StaticLogName, "Couldn't find bus stop node by id");
            }
        }
    }
}