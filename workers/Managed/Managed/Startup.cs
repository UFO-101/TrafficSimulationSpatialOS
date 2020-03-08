using System;
using System.Reflection;
using System.Linq;
using System.Net;
using System.IO;

using Improbable;
using Improbable.Worker;
using Improbable.Collections;

using System.Diagnostics;
using Newtonsoft.Json;

using OpenStreetMap;
using Mapandcars;

namespace Managed
{
    internal class Startup
    {
        // Simulation parameters
        //private const double carSpeed = 1;
        private const double upateInterval = 0.2;
        private const int numberOfCars = 300;

        // SpatialOS necesities
        private const string WorkerType = "Managed";
        public const string LoggerName = "Managed Worker";
        private const int ErrorExitStatus = 1;
        private const uint GetOpListTimeoutInMilliseconds = 100;

        public static Connection ClassConnection;
        private static Dispatcher ClassDispatcher;
        private static bool ClassIsConnected;

        // My utility variables
        private static MapReader mapReader;
        private static List<EntityId> carEntityIds = new List<EntityId>();
        private static List<EntityId> busStopEntityIds = new List<EntityId>();
        private static List<Coordinates> carPositions = new List<Coordinates>();
        private static List<ulong> carRoadIds = new List<ulong>();
        private static List<ulong> carNodeIds = new List<ulong>(); // The current node that each car is aiming
        private static List<ulong> prevCarNodeIds = new List<ulong>(); // The node that the car is coming from

        //private static List<int> carNodeIndices = new List<int>();
        private static List<ulong> roadNodeIds = new List<ulong>();
        //private static Map<RequestId<CreateEntityRequest>, ulong> requestIdToNodeIdDict = new Map<RequestId<CreateEntityRequest>, ulong>();
        private static Map<RequestId<CreateEntityRequest>, string> requestIdToBusVehicleIdDict = new Map<RequestId<CreateEntityRequest>, string>();
        //private static Map<ulong, EntityId> nodeIdToEntityIdDict = new Map<ulong, EntityId>();
        private static Map<string, EntityId> busVehicleIdToEntityIdDict = new Map<string, EntityId>();
        private static System.Collections.Generic.HashSet<string> busVehicleIds = new System.Collections.Generic.HashSet<string>();
        //private static bool busStopsCreated = false;

        private static List<RequestId<CreateEntityRequest>> carCreationRequestIds = new List<RequestId<CreateEntityRequest>>();

        private static int Main(string[] args)
        {
            if (args.Length != 4) {
                PrintUsage();
                return ErrorExitStatus;
            }

            // Avoid missing component errors because no components are directly used in this project
            // and the GeneratedCode assembly is not loaded but it should be
            Assembly.Load("GeneratedCode");

            var connectionParameters = new ConnectionParameters
            {
                WorkerType = WorkerType,
                Network =
                {
                    ConnectionType = NetworkConnectionType.Tcp
                }
            };

            //ServicePointManager.ServerCertificateValidationCallback = MyRemoteCertificateValidationCallback;
            ServicePointManager.ServerCertificateValidationCallback += (o, cert, chain, errors) => true;

            using (var connection = ConnectWithReceptionist(args[1], Convert.ToUInt16(args[2]), args[3], connectionParameters))
            {
                var dispatcher = new Dispatcher();
                var isConnected = true;
                ClassConnection = connection;
                ClassDispatcher = dispatcher;
                ClassIsConnected = isConnected;

                mapReader = InitialiseWorld.ReadMapFile("sheen_map.osm");
                roadNodeIds = InitialiseWorld.createOsmNodes(mapReader, dispatcher, connection);
                InitialiseWorld.createBusStops(mapReader, dispatcher, connection);

                dispatcher.OnCreateEntityResponse(EntityCreateCallback);

                for (int i = 0; i < numberOfCars; i++)
                {
                    RequestId<CreateEntityRequest> creationRequestId = CreationRequests.CreateCarEntity(dispatcher, connection, false, "");
                    carCreationRequestIds.Add(creationRequestId);
                }

                UpdateBusesOnMap();
                ClassConnection.SendLogMessage(LogLevel.Info, LoggerName, "Buses on map count: " + busVehicleIds.Count);
                foreach (string busVehicleId in busVehicleIds)
                {
                    RequestId<CreateEntityRequest> creationRequestId = CreationRequests.CreateCarEntity(dispatcher, connection, true, busVehicleId);
                    carCreationRequestIds.Add(creationRequestId);
                    requestIdToBusVehicleIdDict.Add(creationRequestId, busVehicleId);
                }

                dispatcher.OnDisconnect(op =>
                {
                    Console.Error.WriteLine("[disconnect] " + op.Reason);
                    isConnected = false;
                    ClassIsConnected = isConnected;
                });

                Stopwatch busUpdateTimer = new Stopwatch();
                busUpdateTimer.Start();

                EntityId Id;
                Random random = new Random();
                bool busTimesInitialised = false;

                Stopwatch timer = new Stopwatch();
                timer.Start();

                bool updateBuses = true;

                while (isConnected)
                {
                    // The first two if statements rely on lazy evaluation to short-circuit (careful when changing order)
                    if (!busTimesInitialised && UpdateBusTimes())
                            busTimesInitialised = true;

                    if(updateBuses && busUpdateTimer.Elapsed.TotalSeconds >= 1800 && UpdateBusesOnMap())
                    {
                        UpdateBusTimes();
                        busUpdateTimer.Restart();
                    }

                    if(timer.Elapsed.TotalSeconds >= upateInterval)
                    {
                        for(int i = 0; i < carEntityIds.Count; i++)
                        {
                            Coordinates carPos = carPositions[i];
                            ulong startNode = carNodeIds[i];
                            List<Coordinates> pathCoords = new List<Coordinates>(new Coordinates[]{mapReader.nodes[startNode].coords});
                            List<ulong> pathNodes = new List<ulong>(new ulong[]{startNode});
                            double newPathSegmentLength = 0;
                            double pathLength = 0;
                            bool pastFirstNode = false;
                            ulong nextNodeId = startNode;
                            OsmNode nextNode = mapReader.nodes[nextNodeId];
                            bool reachedSpeedLimit = false;

                            do {
                                Id = carEntityIds[i];
                                OsmNode currentCarNode;
                                if(!mapReader.nodes.TryGetValue(pathNodes.Last(), out currentCarNode)) {
                                    ClassConnection.SendLogMessage(LogLevel.Info, LoggerName, "couldn't find current car node");
                                }

                                // We need to pass the current node we're aiming for before proceeding to the next
                                if(pastFirstNode){
                                    do{
                                        nextNodeId = currentCarNode.adjacentNodes[random.Next(currentCarNode.adjacentNodes.Count)];
                                        if(!mapReader.nodes.ContainsKey(nextNodeId)){
                                            ClassConnection.SendLogMessage(LogLevel.Info, LoggerName, "couldn't find adjacent node");
                                        }
                                        if(!roadNodeIds.Contains(nextNodeId)){
                                            ClassConnection.SendLogMessage(LogLevel.Info, LoggerName, "adjacent node is not in road nodes");
                                        }
                                    }
                                    while(nextNodeId == prevCarNodeIds[i] && currentCarNode.adjacentNodes.Count > 1);
                                    prevCarNodeIds[i] = pathNodes.Last();
                                    nextNode = mapReader.nodes[nextNodeId];
                                    pathCoords.Add(nextNode.coords);
                                    pathNodes.Add(nextNodeId);
                                }

                                newPathSegmentLength = Coords.Dist(carPos, pathCoords.Last());
                                OsmWay currentRoadWay = mapReader.ways[carRoadIds[i]];
                                double maxPathLength = MilesPerHoursTo10metersPerTimeInterval(currentRoadWay.SpeedLimit);
                                if(pathLength + newPathSegmentLength > maxPathLength) {
                                    double correctSegmentLength = maxPathLength - pathLength;
                                    Coordinates currentDirection = Coords.Subtract(pathCoords.Last(), carPos);
                                    carPos = Coords.Add(carPos, Coords.ScaleToLength(currentDirection, correctSegmentLength));
                                    pathLength += correctSegmentLength;
                                    reachedSpeedLimit = true;
                                } else {
                                    pathLength += newPathSegmentLength;
                                    carPos = nextNode.coords;
                                }
                                pastFirstNode = true;
                            }
                            while(!reachedSpeedLimit);

                            carNodeIds[i] = nextNodeId;
                            if(!nextNode.waysOn.Contains(carRoadIds[i])) {
                                carRoadIds[i] = nextNode.waysOn.First();
                            }
                            carPositions[i] = carPos;
                            var update = Improbable.Position.Update.FromInitialData(new PositionData(carPos));
                            ClassConnection.SendComponentUpdate(Improbable.Position.Metaclass, carEntityIds[i], update);
                        }
                        timer.Restart();
                        //firstIteration = false;
                    }

                    using (var opList = connection.GetOpList(GetOpListTimeoutInMilliseconds))
                    {
                        dispatcher.Process(opList);
                    }
                }
            }

            // This means we forcefully disconnected
            return ErrorExitStatus;
        }

        private static bool UpdateBusesOnMap()
        {            
            ClassConnection.SendLogMessage(LogLevel.Info, LoggerName, "Updating bus list");
            foreach (ulong busStopId in mapReader.busStops.GetRange(0, 3))
            {
                OsmNode busStopNode = mapReader.nodes[busStopId];
                string json = string.Empty;
                string actoCode = busStopNode.actoCode;
                string app_id = "d95991f2";
                string app_key = "db59c31bd87e57bbe3512137ca40a450";
                string url = @"https://api.tfl.gov.uk/StopPoint/"+actoCode+@"/Arrivals?app_id="+app_id+@"&app_key="+app_key;

                ClassConnection.SendLogMessage(LogLevel.Info, LoggerName, "Sending request to: " + url);

                HttpWebRequest request = WebRequest.CreateHttp(url);
                request.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
                using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
                using (Stream stream = response.GetResponseStream())
                using (StreamReader reader = new StreamReader(stream))
                {
                    json = reader.ReadToEnd();
                }

                dynamic arrivals = JsonConvert.DeserializeObject(json);
                foreach (var bus in arrivals)
                {
                    string vehicleId = bus.vehicleId;
                    busVehicleIds.Add(vehicleId);
                }
            }
            return true;
        }

        private static bool UpdateBusTimes()
        {
            if(busVehicleIdToEntityIdDict.Count != busVehicleIds.Count)
                return false;

            ClassConnection.SendLogMessage(LogLevel.Info, LoggerName, "Updating bus times");
            foreach(string busVehicleId in busVehicleIdToEntityIdDict.Keys.ToArray()){
                string json = string.Empty;
                string app_id = "d95991f2";
                string app_key = "db59c31bd87e57bbe3512137ca40a450";
                string url = @"https://api.tfl.gov.uk/Vehicle/" + busVehicleId + @"/Arrivals?app_id=" + app_id + @"&app_key=" + app_key;
                ClassConnection.SendLogMessage(LogLevel.Info, LoggerName, "Sending request to: " + url);

                HttpWebRequest request = WebRequest.CreateHttp(url);
                request.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
                using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
                using (Stream stream = response.GetResponseStream())
                using (StreamReader reader = new StreamReader(stream))
                {
                    json = reader.ReadToEnd();
                }

                dynamic arrivals = JsonConvert.DeserializeObject(json);
                List<string> next_stops = new List<string>();
                foreach(var arrival in arrivals)
                {
                    string info = "";
                    info += arrival.station_name;
                    info += " arrives at ";
                    info += arrival.expectedArrival;
                    info += ". ";
                    next_stops.Add(info);
                }

                EntityId entityId = new EntityId();
                if(busVehicleIdToEntityIdDict.TryGetValue(busVehicleId, out entityId)){
                    var update = Bus.Update.FromInitialData(new BusData(busVehicleId, next_stops, new List<uint>()));
                    ClassConnection.SendComponentUpdate(Bus.Metaclass, entityId, update);
                    ClassConnection.SendLogMessage(LogLevel.Info, LoggerName, "Update bus with entity Id: " + entityId.Id + " with text: " + next_stops[0]);
                } else {
                    ClassConnection.SendLogMessage(LogLevel.Info, LoggerName, "Couldn't find bus stop id in dictionary");
                }
            }
            return true;
        }

        private static void PrintUsage()
        {
            Console.WriteLine("Usage: mono Managed.exe receptionist <hostname> <port> <worker_id>");
            Console.WriteLine("Connects to SpatialOS");
            Console.WriteLine("    <hostname>      - hostname of the receptionist to connect to.");
            Console.WriteLine("    <port>          - port to use");
            Console.WriteLine("    <worker_id>     - name of the worker assigned by SpatialOS.");
        }

        private static Connection ConnectWithReceptionist(string hostname, ushort port, string workerId, ConnectionParameters connectionParameters)
        {
            Connection connection;
            // You might want to change this to true or expose it as a command-line option
            // if using `spatial cloud connect external` for debugging
            connectionParameters.Network.UseExternalIp = false;
            using (var future = Connection.ConnectAsync(hostname, port, workerId, connectionParameters))
            {
                connection = future.Get();
            }
            connection.SendLogMessage(LogLevel.Info, LoggerName, "Successfully connected using the Receptionist");
            return connection;
        }

        private static void EntityCreateCallback(CreateEntityResponseOp response) {
            if (carCreationRequestIds.Contains(response.RequestId)) {
                if(response.EntityId.HasValue) {
                    EntityId entityId = response.EntityId.Value;

                    carEntityIds.Add(entityId);

                    Random random = new Random();
                    ulong roadNodeId = roadNodeIds[random.Next(0, roadNodeIds.Count)];
                    
                    //carNodeIndices.Add(roadNodeId);
                    carNodeIds.Add(roadNodeId);
                    prevCarNodeIds.Add(roadNodeId);

                    OsmNode roadNode;
                    if(mapReader.nodes.TryGetValue(roadNodeId, out roadNode)){
                        carPositions.Add(roadNode.coords);
                        carRoadIds.Add(roadNode.waysOn.First());
                    } else {
                        ClassConnection.SendLogMessage(LogLevel.Info, LoggerName, "Couldn't find road node in dictionary.");
                    }
                }
                else {
                    ClassConnection.SendLogMessage(LogLevel.Info, LoggerName, "Car was not created");
                }
            }

            //if (requestIdToNodeIdDict.ContainsKey(response.RequestId)) {
            //    if(response.EntityId.HasValue) {
            //        EntityId entityId = response.EntityId.Value;
            //        ulong roadNodeId = requestIdToNodeIdDict[response.RequestId];
            //        nodeIdToEntityIdDict.Add(roadNodeId, entityId);
            //    }
            //    else {
            //        ClassConnection.SendLogMessage(LogLevel.Info, LoggerName, "Road node was not created");
            //    }
            //}
            
            if(requestIdToBusVehicleIdDict.ContainsKey(response.RequestId)) {
                if (response.EntityId.HasValue) {
                    EntityId entityId = response.EntityId.Value;
                    string busVehicleId = requestIdToBusVehicleIdDict[response.RequestId];
                    busVehicleIdToEntityIdDict.Add(busVehicleId, entityId);
                    //if(requestIdToBusStopIdDict.Count == busStopIdToEntityIdDict.Count){
                    //    busStopsCreated = true;
                    //}
                }
                else {
                    ClassConnection.SendLogMessage(LogLevel.Info, LoggerName, "Bus was not created");
                }
            }
        }

        public static double MilesPerHoursTo10metersPerTimeInterval(int mph){
            return ((mph * 1.60934) / (60*60)) * 100 * upateInterval; // But is this actually right?
        }
    }

}