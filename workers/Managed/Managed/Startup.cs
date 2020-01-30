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

        private static List<OsmNode> prevCarNodes = new List<OsmNode>();
        private static List<OsmNode> carNodes = new List<OsmNode>();
        private static List<int> carNodeIndices = new List<int>();
        private static List<OsmNode> roadNodes = new List<OsmNode>();

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

                MapReader mapReader = new MapReader();
                try{
                    string mapFilePath = System.AppDomain.CurrentDomain.BaseDirectory + "sheen_map.osm";//Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                    ClassConnection.SendLogMessage(LogLevel.Info, LoggerName, "map file path: " + mapFilePath);

                    mapReader.Read(mapFilePath);
                    ClassConnection.SendLogMessage(LogLevel.Info, LoggerName, "Node list length: " + mapReader.nodes.Count);

                    foreach(OpenStreetMap.OsmWay way in mapReader.ways.Values.ToList()){
                        if(way.IsRoad){
                            foreach(ulong nodeID in way.NodeIDs){
                                OsmNode thisNode;
                                if(mapReader.nodes.TryGetValue(nodeID, out thisNode))
                                {
                                    roadNodes.Add(thisNode);
                                    CreateOsmNodeEntity(dispatcher, connection, "Road Node", thisNode.coords);                                    
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

                dispatcher.OnCreateEntityResponse(CreateCarCallback);
                for(int i = 0; i < 10; i++)
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
                Coordinates pos, newPos;//, newNodePos;
                //double xRatio, zRatio;
                Random random = new Random();

                Stopwatch timer = new Stopwatch();
                timer.Start();

                while (isConnected)
                {                    
                    if(timer.Elapsed.TotalSeconds >= 0.25)
                    {
                        for(int i = 0; i < carEntityIds.Count; i++)
                        {
                            id = carEntityIds[i];
                            //pos = carPositions[i];

                            OsmNode newNode;
                            do{
                                newNode = mapReader.nodes[carNodes[i].adjacentNodes[random.Next(carNodes[i].adjacentNodes.Count)]];
                                // ClassConnection.SendLogMessage(LogLevel.Info, LoggerName, "newNode ID: " + newNode.ID);
                                // ClassConnection.SendLogMessage(LogLevel.Info, LoggerName, "prevcarnode ID: " + prevCarNodes[i].ID);
                            }
                            while(newNode.ID == prevCarNodes[i].ID && carNodes[i].adjacentNodes.Count > 1);
                            prevCarNodes[i] = carNodes[i];
                            carNodes[i] = newNode;
                            
                            newPos = newNode.coords;
                            // int newIndex = (carNodeIndices[i] + 1) % roadNodes.Count;
                            // newNodePos = roadNodes[newIndex].coords;
                            // double totalDifference = Math.Abs(newNodePos.x - pos.x) + Math.Abs(newNodePos.z - pos.z);
                            // while (totalDifference > 20 || totalDifference < 1)
                            // {
                            //     newIndex = (newIndex + 1) % roadNodes.Count;
                            //     newNodePos = roadNodes[newIndex].coords;
                            //     totalDifference = Math.Abs(newNodePos.x - pos.x) + Math.Abs(newNodePos.z - pos.z);
                            // }

                            // if(totalDifference > 3)
                            // {
                            //     xRatio = (newNodePos.x - pos.x) / totalDifference;
                            //     zRatio = (newNodePos.z - pos.z) / totalDifference;
                            //     newPos = new Coordinates(pos.x + 1.5 * (xRatio), 0, pos.z + 1.5 * (zRatio));
                            // }
                            // else
                            // {
                            //     carNodes[i] = roadNodes[newIndex];
                            //     newPos = roadNodes[newIndex].coords;//new Coordinates(pos.x + random.NextDouble() * (4) - 2, pos.y + random.NextDouble() * (4) - 2, pos.z + random.NextDouble() * (4) - 2);
                            //     carNodeIndices[i] = newIndex;
                            // }
                            carPositions[i] = newPos;
                            var update = Improbable.Position.Update.FromInitialData(new PositionData(newPos));
                            ClassConnection.SendComponentUpdate(Improbable.Position.Metaclass, id, update);
                        }
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

        private static void CreateOsmNodeEntity(Dispatcher dispatcher, Connection connection, string name, Coordinates coords)
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
            connection.SendCreateEntityRequest(entity, new Option<EntityId>(), new Option<uint>());
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

        private static void CreateCarCallback(CreateEntityResponseOp response) {
            if(carCreationRequestIds.Contains(response.RequestId)) {
                if(response.EntityId.HasValue) {
                    EntityId entityId = response.EntityId.Value;                            

                    carEntityIds.Add(entityId);

                    Random random = new Random();
                    int index = random.Next(0, roadNodes.Count);
                    carNodeIndices.Add(index);

                    carNodes.Add(roadNodes[index]);
                    prevCarNodes.Add(roadNodes[index]);
                    carPositions.Add(roadNodes[index].coords);
                    
                    //EntityIdConstraint entityIdConstraint = new EntityIdConstraint(entityId);
                    // Improbable.Worker.Query.ComponentConstraint componentConstraint = new Improbable.Worker.Query.ComponentConstraint(Mapandcars.Car.ComponentId);
                    // Improbable.Worker.Query.SnapshotResultType snapshotResult = new Improbable.Worker.Query.SnapshotResultType();
                    // Improbable.Worker.Query.EntityQuery entityQuery = new Improbable.Worker.Query.EntityQuery();
                    // entityQuery.Constraint = componentConstraint;
                    // entityQuery.ResultType = snapshotResult;
                    // RequestId<EntityQueryRequest> requestId = ClassConnection.SendEntityQueryRequest(entityQuery, new Improbable.Collections.Option<uint>(1000));        
                }
                else {
                    ClassConnection.SendLogMessage(LogLevel.Info, LoggerName, "Car was not created");
                }
            }
        }

        // private static void GetCarsCallback(EntityQueryResponseOp response) {
        //     foreach(var entityKV in response.Result)
        //     {
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