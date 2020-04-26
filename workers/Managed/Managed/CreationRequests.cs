using System;
using Improbable;
using Improbable.Worker;
using Improbable.Collections;
using Mapandcars;
using System.Linq;
using OpenStreetMap;

namespace Managed
{
    internal class CreationRequests
    {
        public static RequestId<CreateEntityRequest> CreateCarEntity(Dispatcher dispatcher, Connection connection, bool bus, string busVehicleId)
        {
            string entityType = bus ? "Bus" : "Car";
            var entity = new Entity();

            // This requirement set matches any worker with the attribute "simulation".
            var basicWorkerRequirementSet = new WorkerRequirementSet(
                new List<WorkerAttributeSet> { new WorkerAttributeSet(new List<string> { "simulation" }) }
            );

            // Give authority over Position and EntityAcl to any physics worker, and over PlayerControls to the caller worker.
            var writeAcl = new Map<uint, WorkerRequirementSet>
                {
                    {Position.ComponentId, basicWorkerRequirementSet},
                    {EntityAcl.ComponentId, basicWorkerRequirementSet},
                    {Bus.ComponentId, basicWorkerRequirementSet}
                };

            entity.Add(EntityAcl.Metaclass,
                new EntityAclData( /* read */ basicWorkerRequirementSet, /* write */ writeAcl));

            entity.Add(Persistence.Metaclass, new PersistenceData());
            entity.Add(Metadata.Metaclass, new MetadataData(entityType));

            //Random random = new Random();
            //ulong[] roadNodesArray = Startup.MapReader.roadNodes.ToArray();
            //ulong roadNodeId = roadNodesArray[random.Next(0, roadNodesArray.Length)];
            //OsmNode roadNode = Startup.MapReader.nodes[roadNodeId];
            //Coordinates startCoords = roadNode.coords;
            //ulong roadId = roadNode.waysOn.First();

            entity.Add(Position.Metaclass, new PositionData(new Coordinates(0, -99.99, 0)));
            entity.Add(Car.Metaclass, new CarData());
            if (bus)
                entity.Add(Bus.Metaclass, new BusData(busVehicleId, new List<string>(), new List<uint>()));

            return connection.SendCreateEntityRequest(entity, new Option<EntityId>(), new Option<uint>());
        }

        public static RequestId<CreateEntityRequest> CreateOsmNodeEntity(Dispatcher dispatcher, Connection connection, string name, Coordinates coords)
        {
            string entityType = name;
            var entity = new Entity();
            var basicWorkerRequirementSet = new WorkerRequirementSet(
                new List<WorkerAttributeSet> { new WorkerAttributeSet(new List<string> { "simulation" }) }
            );
            var writeAcl = new Map<uint, WorkerRequirementSet> { };
            entity.Add(EntityAcl.Metaclass, new EntityAclData(basicWorkerRequirementSet, writeAcl));
            entity.Add(Persistence.Metaclass, new PersistenceData());
            entity.Add(Metadata.Metaclass, new MetadataData(entityType));
            entity.Add(Position.Metaclass, new PositionData(coords));
            return connection.SendCreateEntityRequest(entity, new Option<EntityId>(), new Option<uint>());
        }

        public static RequestId<CreateEntityRequest> CreateBusStopEntity(Dispatcher dispatcher, Connection connection, string name, Coordinates coords, String atcoCode)
        {
            string entityType = name;
            var entity = new Entity();
            var basicWorkerRequirementSet = new WorkerRequirementSet(
                new List<WorkerAttributeSet> { new WorkerAttributeSet(new List<string> { "simulation" }) }
            );
            var writeAcl = new Map<uint, WorkerRequirementSet>
                {
                    {BusStop.ComponentId, basicWorkerRequirementSet},
                    {EntityAcl.ComponentId, basicWorkerRequirementSet},
                };
            entity.Add(EntityAcl.Metaclass, new EntityAclData(basicWorkerRequirementSet, writeAcl));
            entity.Add(Persistence.Metaclass, new PersistenceData());
            entity.Add(Metadata.Metaclass, new MetadataData(entityType));
            entity.Add(Position.Metaclass, new PositionData(coords));
            entity.Add(BusStop.Metaclass, new BusStopData(atcoCode));
            return connection.SendCreateEntityRequest(entity, new Option<EntityId>(), new Option<uint>());
        }

        public static void CreateOsmWay(Dispatcher dispatcher, Connection connection, string name, Coordinates coords, List<EntityId> entityIds)
        {
            string entityType = name;
            var entity = new Entity();
            var basicWorkerRequirementSet = new WorkerRequirementSet(
                new List<WorkerAttributeSet> { new WorkerAttributeSet(new List<string> { "simulation" }) }
            );
            var writeAcl = new Map<uint, WorkerRequirementSet> { };
            entity.Add(EntityAcl.Metaclass, new EntityAclData(basicWorkerRequirementSet, writeAcl));
            entity.Add(Persistence.Metaclass, new PersistenceData());
            entity.Add(Metadata.Metaclass, new MetadataData(entityType));
            entity.Add(Position.Metaclass, new PositionData(coords));
            entity.Add(OsmRoad.Metaclass, new OsmRoadData(entityIds));
            connection.SendCreateEntityRequest(entity, new Option<EntityId>(), new Option<uint>());
        }
    }
}