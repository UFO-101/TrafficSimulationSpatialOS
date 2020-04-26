using System;
using System.Reflection;
using System.Linq;
using System.Net;
using System.IO;

using Improbable;
using Improbable.Worker;
using Improbable.Collections;
using Improbable.Worker.Query;

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
        private const double upateInterval = 0.5;
        private const int numberOfCars = 5;

        // SpatialOS necesities
        private const string WorkerType = "Managed";
        public const string LoggerName = "Managed Worker (Startup)";
        private const int ErrorExitStatus = 1;
        private const uint GetOpListTimeoutInMilliseconds = 100;

        public static Connection ClassConnection;
        private static bool ClassIsConnected;

        // My utility variables
        public static MapReader MapReader;
        private static List<EntityId> busStopEntityIds = new List<EntityId>();

        private static Map<RequestId<CreateEntityRequest>, string> requestIdToBusVehicleIdDict = new Map<RequestId<CreateEntityRequest>, string>();
        private static Map<string, EntityId> busVehicleIdToEntityIdDict = new Map<string, EntityId>();
        private static System.Collections.Generic.HashSet<string> busVehicleIds = new System.Collections.Generic.HashSet<string>();

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

            ServicePointManager.ServerCertificateValidationCallback += (o, cert, chain, errors) => true;

            using (var connection = ConnectWithReceptionist(args[1], Convert.ToUInt16(args[2]), args[3], connectionParameters))
            {
                WorkerView dispatcher = new WorkerView();
                var isConnected = true;
                ClassConnection = connection;
                WorkerView.ClassConnection = connection;
                ClassIsConnected = isConnected;
                string workerId = connection.GetWorkerId();

                MapReader = InitialiseWorld.ReadMapFile("sheen_map.osm");

                if (workerId == "simulation0")
                {
                    InitialiseWorld.createOsmNodes(MapReader, dispatcher, connection);
                    InitialiseWorld.createBusStops(MapReader, dispatcher, connection);
                    CreateCars(dispatcher, connection);
                    //UpdateBusesList();
                    ClassConnection.SendLogMessage(LogLevel.Info, LoggerName, "Buses on map count: " + busVehicleIds.Count);
                    //CreateBusesInList(dispatcher, connection);
                }

                dispatcher.OnDisconnect(op =>
                {
                    Console.Error.WriteLine("[disconnect] " + op.Reason);
                    isConnected = false;
                    ClassIsConnected = isConnected;
                });

                Stopwatch busUpdateTimer = new Stopwatch();
                busUpdateTimer.Start();
                bool busTimesInitialised = false;
                Stopwatch timer = new Stopwatch();
                timer.Start();

                bool updateBuses = true;
                while (isConnected)
                {
                    // The first two if statements rely on lazy evaluation to short-circuit (careful when changing order)
                    if (!busTimesInitialised && UpdateBusTimes())
                    {
                        busTimesInitialised = true;
                    }

                    if(updateBuses && busUpdateTimer.Elapsed.TotalSeconds >= 1800 && UpdateBusesList())
                    {
                        //UpdateBusTimes();
                        busUpdateTimer.Restart();
                    }

                    if(timer.Elapsed.TotalSeconds >= upateInterval)
                    {
                        MoveCars(connection, dispatcher);
                        timer.Restart();
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

        private static bool UpdateBusesList()
        {            
            ClassConnection.SendLogMessage(LogLevel.Info, LoggerName, "Updating bus list");
            foreach (ulong busStopId in MapReader.busStops)
            {
                OsmNode busStopNode = MapReader.nodes[busStopId];
                string json = string.Empty;
                string actoCode = busStopNode.actoCode;
                string app_id = "d95991f2";
                string app_key = "db59c31bd87e57bbe3512137ca40a450";
                string url = @"https://api.tfl.gov.uk/StopPoint/"+actoCode+@"/Arrivals?app_id="+app_id+@"&app_key="+app_key;
                ClassConnection.SendLogMessage(LogLevel.Info, LoggerName, "sending request to: " + url);

                try
                {
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
                catch(WebException ex)
                {
                    ClassConnection.SendLogMessage(LogLevel.Info, LoggerName, "web exception - status " + ex.Status);
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
                ClassConnection.SendLogMessage(LogLevel.Info, LoggerName, "sending request to: " + url);

                try {
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
                    catch (WebException ex)
                {
                    ClassConnection.SendLogMessage(LogLevel.Info, LoggerName, "web exception - status " + ex.Status);
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

        private static void CreateCars(WorkerView dispatcher, Connection connection)
        {
            for (int i = 0; i < numberOfCars; i++)
            {
                RequestId<CreateEntityRequest> creationRequestId = CreationRequests.CreateCarEntity(dispatcher, connection, false, "");
                dispatcher.carCreationRequestIds.Add(creationRequestId);
            }
        }

        private static void CreateBusesInList(WorkerView dispatcher, Connection connection)
        {
            foreach (string busVehicleId in busVehicleIds)
            {
                RequestId<CreateEntityRequest> creationRequestId = CreationRequests.CreateCarEntity(dispatcher, connection, true, busVehicleId);
                dispatcher.carCreationRequestIds.Add(creationRequestId);
                requestIdToBusVehicleIdDict.Add(creationRequestId, busVehicleId);
            }
        }

        private static void MoveCars(Connection connection, WorkerView dispatcher)
        {
            Random random = new Random();
            foreach (EntityId entityId in dispatcher.carsInAuthority)
            {

                Coordinates carPos;
                if (!dispatcher.carPositions.TryGetValue(entityId, out carPos))
                    ClassConnection.SendLogMessage(LogLevel.Error, LoggerName, "No car position");
                ulong currentNodeId;
                if (!dispatcher.carNodeIds.TryGetValue(entityId, out currentNodeId))
                    ClassConnection.SendLogMessage(LogLevel.Error, LoggerName, "No car node");
                ulong prevNodeId;
                if (!dispatcher.prevCarNodeIds.TryGetValue(entityId, out prevNodeId))
                    ClassConnection.SendLogMessage(LogLevel.Error, LoggerName, "No prev car node");
                ulong currentRoadId;
                if (!dispatcher.carRoadIds.TryGetValue(entityId, out currentRoadId))
                    ClassConnection.SendLogMessage(LogLevel.Error, LoggerName, "No car road id");
                ulong newCurrentNodeId = currentNodeId;//carNodeIds[i];
                ulong newPrevNodeId = prevNodeId;
                ulong newCurrentRoadId = currentRoadId;


                List<Coordinates> pathCoords = new List<Coordinates>(new Coordinates[] { MapReader.nodes[currentNodeId].coords });
                List<ulong> pathNodes = new List<ulong>(new ulong[] { currentNodeId });
                double newPathSegmentLength = 0;
                double pathLength = 0;
                bool pastFirstNode = false;
                ulong nextNodeId = currentNodeId;
                OsmNode nextNode = MapReader.nodes[nextNodeId];
                bool reachedSpeedLimit = false;

                do {
                    OsmNode currentCarNode;
                    if (!MapReader.nodes.TryGetValue(pathNodes.Last(), out currentCarNode))
                    {
                        ClassConnection.SendLogMessage(LogLevel.Info, LoggerName, "couldn't find current car node");
                    }

                    // We need to pass the current node we're aiming for before proceeding to the next
                    if (pastFirstNode)
                    {
                        do
                        {
                            nextNodeId = currentCarNode.adjacentNodes[random.Next(currentCarNode.adjacentNodes.Count)];
                            if (!MapReader.nodes.ContainsKey(nextNodeId))
                            {
                                ClassConnection.SendLogMessage(LogLevel.Info, LoggerName, "couldn't find adjacent node");
                            }
                            if (!MapReader.roadNodes.Contains(nextNodeId))
                            {
                                ClassConnection.SendLogMessage(LogLevel.Info, LoggerName, "adjacent node is not in road nodes");
                            }
                        }
                        while (nextNodeId == prevNodeId && currentCarNode.adjacentNodes.Count > 1);
                        newPrevNodeId = pathNodes.Last();
                        nextNode = MapReader.nodes[nextNodeId];
                        pathCoords.Add(nextNode.coords);
                        pathNodes.Add(nextNodeId);
                    }

                    newPathSegmentLength = Coords.Dist(carPos, pathCoords.Last());
                    OsmWay currentRoadWay = MapReader.ways[currentRoadId];
                    double maxPathLength = MilesPerHoursTo10metersPerTimeInterval(currentRoadWay.SpeedLimit);
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
                newCurrentNodeId = nextNodeId;
                if (!nextNode.waysOn.Contains(currentRoadId))
                {
                    newCurrentRoadId = nextNode.waysOn.First();
                }
                var positionComponentUpdate = Position.Update.FromInitialData(new PositionData(carPos));
                var carComponentUpdate = Car.Update.FromInitialData(new CarData(newCurrentNodeId, newPrevNodeId, newCurrentRoadId));
                ClassConnection.SendComponentUpdate(Position.Metaclass, entityId, positionComponentUpdate);
                ClassConnection.SendComponentUpdate(Car.Metaclass, entityId, carComponentUpdate);
            }
        }

        public static double MilesPerHoursTo10metersPerTimeInterval(int mph){
            return ((mph * 1.60934) / (60*60)) * 100 * upateInterval; // But is this actually right?
        }
    }

}