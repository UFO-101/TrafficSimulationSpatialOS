using System;
using System.Reflection;
using System.Linq;

using Improbable;
using Improbable.Worker;
using Improbable.Collections;

using System.Diagnostics;

using OpenStreetMap;
using Mapandcars;

namespace Managed
{
    internal class Startup
    {
        private const string WorkerType = "Managed";

        private const string LoggerName = "Startup.cs";

        private const int ErrorExitStatus = 1;

        private const uint GetOpListTimeoutInMilliseconds = 100;

        private static Connection ClassConnection;
        private static Dispatcher ClassDispatcher;
        private static bool ClassIsConnected;

        private static List<EntityId> carEntityIds = new List<EntityId>();
        private static List<Coordinates> carPositions = new List<Coordinates>();

        private static MapReader mapReader = new MapReader();
        //private static List<int> carNodeIndices = new List<int>();
        private static List<ulong> prevCarNodeIds = new List<ulong>();
        private static List<ulong> carNodeIds = new List<ulong>();
        private static List<ulong> roadNodeIds = new List<ulong>();
        private static Map<RequestId<CreateEntityRequest>, ulong> requestIdToNodeIdDict = new Map<RequestId<CreateEntityRequest>, ulong>();
        private static Map<ulong, EntityId> nodeIdToEntityIdDict = new Map<ulong, EntityId>();

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

            using (var connection = ConnectWithReceptionist(args[1], Convert.ToUInt16(args[2]), args[3], connectionParameters))
            {
                var dispatcher = new Dispatcher();
                var isConnected = true;
                ClassConnection = connection;
                ClassDispatcher = dispatcher;
                ClassIsConnected = isConnected;

                //MapReader mapReader = new MapReader();
                try{
                    string mapFilePath = System.AppDomain.CurrentDomain.BaseDirectory + "warwick_uni_map.osm";//"sheen_map.osm";//
                    ClassConnection.SendLogMessage(LogLevel.Info, LoggerName, "map file path: " + mapFilePath);

                    mapReader.Read(mapFilePath);
                    ClassConnection.SendLogMessage(LogLevel.Info, LoggerName, "Node list length: " + mapReader.nodes.Count);

                    dispatcher.OnCreateEntityResponse(EntityCreateCallback);

                    foreach(OpenStreetMap.OsmWay way in mapReader.ways.Values.ToList()){
                        if(way.IsRoad){
                            foreach(ulong nodeId in way.NodeIDs){
                                OsmNode thisNode;
                                if(mapReader.nodes.TryGetValue(nodeId, out thisNode))
                                {
                                    if(thisNode.Id != nodeId){
                                        ClassConnection.SendLogMessage(LogLevel.Info, LoggerName, "Node ids don't match!");
                                    }
                                    if(!roadNodeIds.Contains(nodeId)){
                                        roadNodeIds.Add(nodeId);
                                        RequestId<CreateEntityRequest> roadNodeRequestID = CreateOsmNodeEntity(dispatcher, connection, "Road Node", thisNode.coords);
                                        requestIdToNodeIdDict.Add(roadNodeRequestID, nodeId);
                                    }
                                }
                            }
                        }
                        if(way.IsBuilding){

                        }
                    }
                }
                catch(System.IO.FileNotFoundException)
                {
                    ClassConnection.SendLogMessage(LogLevel.Info, LoggerName, "Can't read map file");
                }

                for(int i = 0; i < 100; i++)
                {
                    CreateCarEntity(dispatcher, connection);
                }       

                dispatcher.OnDisconnect(op =>
                {
                    Console.Error.WriteLine("[disconnect] " + op.Reason);
                    isConnected = false;
                    ClassIsConnected = isConnected;
                });

                EntityId id;
                EntityId previousRoadNodeID = new EntityId(232242);
                Coordinates pos, newPos;//, newNodePos;
                //double xRatio, zRatio;
                Random random = new Random();
                bool firstIteration = true;

                Stopwatch timer = new Stopwatch();
                timer.Start();

                while (isConnected)
                {                    
                    if(timer.Elapsed.TotalSeconds >= 0.05)
                    {
                        for(int i = 0; i < carEntityIds.Count; i++)
                        {
                            if(!firstIteration){
                                var reverseUpdate = Improbable.Metadata.Update.FromInitialData(new MetadataData("Road Node"));
                                ClassConnection.SendComponentUpdate(Improbable.Metadata.Metaclass, previousRoadNodeID, reverseUpdate);
                            }

                            id = carEntityIds[i];
                            OsmNode currentCarNode;
                            if(!mapReader.nodes.TryGetValue(carNodeIds[i], out currentCarNode)) {
                                ClassConnection.SendLogMessage(LogLevel.Info, LoggerName, "couldn't find current car node");
                            }
                            //pos = carPositions[i];

                            OsmNode newNode;
                            bool newNodeInRoadNodes = false;
                            do{
                                ulong adjacentNodeId = currentCarNode.adjacentNodes[random.Next(currentCarNode.adjacentNodes.Count)];
                                if(!mapReader.nodes.TryGetValue(adjacentNodeId, out newNode)){
                                    ClassConnection.SendLogMessage(LogLevel.Info, LoggerName, "couldn't find adjacent node");
                                }
                                if(!roadNodeIds.Contains(adjacentNodeId)){
                                    ClassConnection.SendLogMessage(LogLevel.Info, LoggerName, "adjacent node is not in road nodes");
                                }                                
                                newNodeInRoadNodes = roadNodeIds.Contains(adjacentNodeId);
                            }
                            while(newNode.Id == prevCarNodeIds[i] && currentCarNode.adjacentNodes.Count > 1);
                            prevCarNodeIds[i] = carNodeIds[i];
                            carNodeIds[i] = newNode.Id;
                            
                            newPos = newNode.coords;                            
                            carPositions[i] = newPos;
                            var update = Improbable.Position.Update.FromInitialData(new PositionData(newPos));
                            ClassConnection.SendComponentUpdate(Improbable.Position.Metaclass, id, update);

                            // EntityId roadNodeEntityID;
                            // if(nodeIdToEntityIdDict.TryGetValue(newNode.Id, out roadNodeEntityID)) {
                            //     var roadNodeUpdate = Improbable.Metadata.Update.FromInitialData(new MetadataData("Current Node"));
                            //     ClassConnection.SendComponentUpdate(Improbable.Metadata.Metaclass, roadNodeEntityID, roadNodeUpdate);
                            //     previousRoadNodeID = roadNodeEntityID;
                            // } else {
                            //     connection.SendLogMessage(LogLevel.Info, LoggerName, "Couldn't get road node entity id");
                            // }
                        }
                        timer.Restart();
                        firstIteration = false;
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

        private static void PrintUsage()
        {
            Console.WriteLine("Usage: mono Managed.exe receptionist <hostname> <port> <worker_id>");
            Console.WriteLine("Connects to SpatialOS");
            Console.WriteLine("    <hostname>      - hostname of the receptionist to connect to.");
            Console.WriteLine("    <port>          - port to use");
            Console.WriteLine("    <worker_id>     - name of the worker assigned by SpatialOS.");
        }

        private static Connection ConnectWithReceptionist(string hostname, ushort port,
            string workerId, ConnectionParameters connectionParameters)
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

        private static void CreateCarEntity(Dispatcher dispatcher, Connection connection)
        {
            const string entityType = "Car";            
            var entity = new Entity();

            // This requirement set matches any worker with the attribute "simulation".
            var basicWorkerRequirementSet = new WorkerRequirementSet(
                new List<WorkerAttributeSet> {new WorkerAttributeSet(new List<string> {"simulation"})}
            );  
            
            // Give authority over Position and EntityAcl to any physics worker, and over PlayerControls to the caller worker.
            var writeAcl = new Map<uint, WorkerRequirementSet>
            {
                {Position.ComponentId, basicWorkerRequirementSet},
                {EntityAcl.ComponentId, basicWorkerRequirementSet},
            };
            
            entity.Add(EntityAcl.Metaclass,
                new EntityAclData( /* read */ basicWorkerRequirementSet, /* write */ writeAcl));

            entity.Add(Persistence.Metaclass, new PersistenceData());
            entity.Add(Metadata.Metaclass, new MetadataData(entityType));
            entity.Add(Position.Metaclass, new PositionData(new Coordinates(1, 2, 3)));
            entity.Add(Car.Metaclass, new CarData(1.0f, new Vector3f(1.0f, 0.0f, 1.0f)));
            
            RequestId<CreateEntityRequest> creationRequestId = connection.SendCreateEntityRequest(entity, new Option<EntityId>(), new Option<uint>());
            carCreationRequestIds.Add(creationRequestId);
        }

        private static RequestId<CreateEntityRequest> CreateOsmNodeEntity(Dispatcher dispatcher, Connection connection, string name, Coordinates coords)
        {
            string entityType = name;
            var entity = new Entity();
            var basicWorkerRequirementSet = new WorkerRequirementSet(
                new List<WorkerAttributeSet> {new WorkerAttributeSet(new List<string> {"simulation"})}
            );            
            var writeAcl = new Map<uint, WorkerRequirementSet>{};            
            entity.Add(EntityAcl.Metaclass, new EntityAclData(basicWorkerRequirementSet, writeAcl));
            entity.Add(Persistence.Metaclass, new PersistenceData());
            entity.Add(Metadata.Metaclass, new MetadataData(entityType));
            entity.Add(Position.Metaclass, new PositionData(coords));
            return connection.SendCreateEntityRequest(entity, new Option<EntityId>(), new Option<uint>());
        }

        private static void CreateOsmWay(Dispatcher dispatcher, Connection connection, string name, Coordinates coords, List<EntityId> entityIds)
        {
            string entityType = name;
            var entity = new Entity();
            var basicWorkerRequirementSet = new WorkerRequirementSet(
                new List<WorkerAttributeSet> {new WorkerAttributeSet(new List<string> {"simulation"})}
            );            
            var writeAcl = new Map<uint, WorkerRequirementSet>{};            
            entity.Add(EntityAcl.Metaclass, new EntityAclData(basicWorkerRequirementSet, writeAcl));
            entity.Add(Persistence.Metaclass, new PersistenceData());
            entity.Add(Metadata.Metaclass, new MetadataData(entityType));
            entity.Add(Position.Metaclass, new PositionData(coords));
            entity.Add(Mapandcars.OsmWay.Metaclass, new OsmWayData(entityIds));
            connection.SendCreateEntityRequest(entity, new Option<EntityId>(), new Option<uint>());
        }

        private static void EntityCreateCallback(CreateEntityResponseOp response) {
            if(carCreationRequestIds.Contains(response.RequestId)) {
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
                    } else {
                        ClassConnection.SendLogMessage(LogLevel.Info, LoggerName, "Couldn't find road node in dictionary.");
                    }
                }
                else {
                    ClassConnection.SendLogMessage(LogLevel.Info, LoggerName, "Car was not created");
                }
            }
            else if(requestIdToNodeIdDict.ContainsKey(response.RequestId)) {
                if(response.EntityId.HasValue) {
                    EntityId entityId = response.EntityId.Value;                            

                    ulong roadNodeId;
                    if(requestIdToNodeIdDict.TryGetValue(response.RequestId, out roadNodeId)){
                        nodeIdToEntityIdDict.Add(roadNodeId, entityId);
                    } else {
                        ClassConnection.SendLogMessage(LogLevel.Info, LoggerName, "Couldn't get road node");
                    }
                }
                else {
                    ClassConnection.SendLogMessage(LogLevel.Info, LoggerName, "Road node was not created");
                }
            }
        }

        // private static void GetCarsCallback(EntityQueryResponseOp response) {
        //     foreach(var entityKV in response.Result)entityType
        //         Improbable.Worker.Entity entity = entityKV.Value;
        //         Improbable.Worker.EntityId entityId = entityKV.Key;
        //         if(entity.Get<Improbable.Position>().HasValue)
        //         {
        //             Improbable.Position currentPosition = entity.Get<Improbable.Position>().Value.Value;
        //             var update = Improbable.Position.Update.FromInitialData(new Improbable.PositionData(new Improbable.Coordinates(7, 7, 7)));
        //             ClassConnection.SendComponentUpdate(Improbable.Position.Metaclass, entityId, update);
        //         }
        //     }            
        // }
    }
}