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
        public const double upateInterval = 2;
        private const int numberOfCars = 4000;

        // SpatialOS necesities
        private const string WorkerType = "Managed";
        public const string StaticLogName = "Startup - Static";
        private const int ErrorExitStatus = 1;
        private const uint GetOpListTimeoutInMilliseconds = 5000;

        public static bool staticVarsInitialised = false;
        public static Connection StaticConnection;
        private static bool ClassIsConnected;

        // My utility variables
        public static MapReader MapReader;
        private static List<EntityId> busStopEntityIds = new List<EntityId>();

        private static Map<RequestId<CreateEntityRequest>, string> requestIdToBusVehicleIdDict = new Map<RequestId<CreateEntityRequest>, string>();
        private static Map<string, EntityId> busVehicleIdToEntityIdDict = new Map<string, EntityId>();
        public static System.Collections.Generic.HashSet<string> busVehicleIds = new System.Collections.Generic.HashSet<string>();

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
                Network = { ConnectionType = NetworkConnectionType.Tcp }
            };

            ServicePointManager.ServerCertificateValidationCallback += (o, cert, chain, errors) => true;

            using (var connection = ConnectWithReceptionist(args[1], Convert.ToUInt16(args[2]), args[3], connectionParameters))
            {
                WorkerView dispatcher = new WorkerView(connection);
                var isConnected = true;
                ClassIsConnected = isConnected;
                string workerId = connection.GetWorkerId();

                if(!staticVarsInitialised)
                {
                    StaticConnection = connection;
                    MapReader = InitialiseWorld.ReadMapFile("south_west_london_map.osm");
                    staticVarsInitialised = true;
                }

                if (workerId == "simulation0")
                {
                    InitialiseWorld.createOsmNodes(MapReader, dispatcher, connection);
                    InitialiseWorld.createBusStops(MapReader, dispatcher, connection);
                    CreateCars(dispatcher, connection);
                }

                dispatcher.OnDisconnect(op =>
                {
                    Console.Error.WriteLine("[disconnect] " + op.Reason);
                    isConnected = false;
                    ClassIsConnected = isConnected;
                });

                Stopwatch initalBusUpdateTimer = new Stopwatch();
                initalBusUpdateTimer.Start();
                Stopwatch busUpdateTimer = new Stopwatch();
                busUpdateTimer.Start();
                Stopwatch timer = new Stopwatch();
                timer.Start();

                while (isConnected)
                {
                    if(initalBusUpdateTimer.Elapsed.TotalSeconds >= 20 || busUpdateTimer.Elapsed.TotalSeconds >= 120)
                    {
                        initalBusUpdateTimer.Reset();
                        System.Collections.Generic.HashSet<string> newBusVehicleListIds = new System.Collections.Generic.HashSet<string>(UpdateBusesList(dispatcher));
                        CreateNewBusesInList(dispatcher, connection, newBusVehicleListIds);
                        busVehicleIds.Concat(newBusVehicleListIds);
                        connection.SendLogMessage(LogLevel.Info, workerId, "Buses on map count: " + busVehicleIds.Count);
                        busUpdateTimer.Restart();
                    }

                    if(timer.Elapsed.TotalSeconds >= upateInterval)
                    {
                        Movement.MoveCars(connection, dispatcher, MapReader);
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

        private static List<string> UpdateBusesList(WorkerView dispatcher)
        {            
            StaticConnection.SendLogMessage(LogLevel.Info, StaticLogName, "Updating bus list - stop list length " + dispatcher.busStopAtcoCodes.Count);
            List<string> newBusVehicleIds = new List<string>();
            foreach (string atcoCode in dispatcher.busStopAtcoCodes.Values)
            {
                string json = string.Empty;
                string app_id = "d95991f2";
                string app_key = "db59c31bd87e57bbe3512137ca40a450";
                string url = @"https://api.tfl.gov.uk/StopPoint/"+atcoCode+@"/Arrivals?app_id="+app_id+@"&app_key="+app_key;

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
                        // Only create a bus if it is at most 5 minutes from at least 1 station
                        if(bus.timeToStation < 300 && dispatcher.busStopAtcoCodes.ContainsValue((string)bus.naptanId))
                        {
                            string vehicleId = bus.vehicleId;
                            newBusVehicleIds.Add(vehicleId);
                        }
                    }
                }
                catch(WebException ex)
                {
                    StaticConnection.SendLogMessage(LogLevel.Info, StaticLogName, "web exception - status " + ex.Status);
                }
            }
            return newBusVehicleIds;
        }

        public static void EntityCreateCallback(CreateEntityResponseOp response)
        {
            if (requestIdToBusVehicleIdDict.ContainsKey(response.RequestId))
            {
                if (response.EntityId.HasValue)
                {
                    EntityId entityId = response.EntityId.Value;
                    string busVehicleId = requestIdToBusVehicleIdDict[response.RequestId];
                    busVehicleIdToEntityIdDict[busVehicleId] = entityId;
                    StaticConnection.SendLogMessage(LogLevel.Info, StaticLogName, "Is A Bus");
                }
                else
                    StaticConnection.SendLogMessage(LogLevel.Info, StaticLogName, "Bus was not created");
            }
        }

        public static bool BusOnMap(string busVehicleId)
        {
            string json = string.Empty;
            string app_id = "d95991f2";
            string app_key = "db59c31bd87e57bbe3512137ca40a450";
            string url = @"https://api.tfl.gov.uk/Vehicle/" + busVehicleId + @"/Arrivals?app_id=" + app_id + @"&app_key=" + app_key;
            StaticConnection.SendLogMessage(LogLevel.Info, StaticLogName, "sending request to: " + url);

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
                List<string> nextStops = new List<string>();
                Map<string, string> estimatedArrivals = new Map<string, string>();
                foreach (var arrival in arrivals)
                {
                    string expectedArrival = (string)arrival.expectedArrival;
                    estimatedArrivals[(string)arrival.naptanId] = expectedArrival;

                    if (DateTime.Parse(expectedArrival, System.Globalization.CultureInfo.InvariantCulture).CompareTo(DateTime.UtcNow) > 0)
                    {
                        // This could be optimised
                        if (!nextStops.Contains((string)arrival.naptanId))
                            nextStops.Insert(nextStops.Count, (string)arrival.naptanId);
                    }
                }

                return MapReader.busStops.ContainsKey(nextStops.First());
            }
            catch (WebException ex)
            {
                StaticConnection.SendLogMessage(LogLevel.Info, StaticLogName, "web exception - status " + ex.Status);
            }
            return false;
        }

        public static bool UpdateBusTimes(Connection connection, WorkerView dispatcher, EntityId busEntityId)
        {
            string busVehicleId;
            if(!dispatcher.busesVehicleIds.TryGetValue(busEntityId, out busVehicleId))
            {
                StaticConnection.SendLogMessage(LogLevel.Error, StaticLogName, "Couldn't find busVehicleId for entity " + busEntityId);
                return false;
            }

            string json = string.Empty;
            string app_id = "d95991f2";
            string app_key = "db59c31bd87e57bbe3512137ca40a450";
            string url = @"https://api.tfl.gov.uk/Vehicle/" + busVehicleId + @"/Arrivals?app_id=" + app_id + @"&app_key=" + app_key;
            StaticConnection.SendLogMessage(LogLevel.Info, StaticLogName, "sending request to: " + url);

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
                List<string> nextStops = new List<string>();
                dispatcher.busesNextStops.TryGetValue(busEntityId, out nextStops);
                Map<string, string> estimatedArrivals = new Map<string, string>();
                foreach(var arrival in arrivals)
                {
                    if (!estimatedArrivals.ContainsKey((string)arrival.naptanId))
                    {
                        string expectedArrival = (string)arrival.expectedArrival;
                        estimatedArrivals.Add(new System.Collections.Generic.KeyValuePair<string, string>((string)arrival.naptanId, expectedArrival));

                        if (DateTime.Parse(expectedArrival, System.Globalization.CultureInfo.InvariantCulture).CompareTo(DateTime.UtcNow) > 0)
                        {
                            // This could be optimised
                            if (!nextStops.Contains((string)arrival.naptanId))
                                nextStops.Insert(nextStops.Count, (string)arrival.naptanId);
                        }
                    }
                }
                
                if (!MapReader.busStops.ContainsKey(nextStops.First()))
                {
                    connection.SendDeleteEntityRequest(busEntityId, new Option<uint>());
                    StaticConnection.SendLogMessage(LogLevel.Info, StaticLogName, "Bus stop " + nextStops.First() + " was not found on map, DELETING entity " + busEntityId);
                    string vehicleId;
                    if (dispatcher.busesVehicleIds.TryGetValue(busEntityId, out vehicleId))
                        Startup.busVehicleIds.Remove(vehicleId);
                }

                    var update = Bus.Update.FromInitialData(new BusData(busVehicleId, nextStops, estimatedArrivals));
                StaticConnection.SendComponentUpdate(Bus.Metaclass, busEntityId, update);
            }
            catch (WebException ex)
            {
                StaticConnection.SendLogMessage(LogLevel.Info, StaticLogName, "web exception - status " + ex.Status);
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
            connection.SendLogMessage(LogLevel.Info, connection.GetWorkerId(), "Successfully connected using the Receptionist");
            return connection;
        }

        private static void CreateCars(WorkerView dispatcher, Connection connection)
        {
            for (int i = 0; i < numberOfCars; i++)
            {
                RequestId<CreateEntityRequest> creationRequestId = CreationRequests.CreateCarEntity(dispatcher, connection);
                dispatcher.carCreationRequestIds.Add(creationRequestId);
            }
        }

        private static void CreateNewBusesInList(WorkerView dispatcher, Connection connection, System.Collections.Generic.HashSet<string> newBusVehicleIds)
        {
            foreach (string busVehicleId in newBusVehicleIds)
            {
                if (!busVehicleIds.Contains(busVehicleId))
                {
                    if (BusOnMap(busVehicleId))
                    {
                        RequestId<CreateEntityRequest> creationRequestId = CreationRequests.CreateBusEntity(dispatcher, connection, busVehicleId);
                        dispatcher.carCreationRequestIds.Add(creationRequestId);
                        requestIdToBusVehicleIdDict.Add(creationRequestId, busVehicleId);
                    }
                }
            }
        }

    }

}