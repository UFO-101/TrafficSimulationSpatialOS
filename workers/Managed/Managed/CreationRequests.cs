using System;
using Improbable;
using Improbable.Worker;
using Improbable.Collections;
using Mapandcars;
using OpenStreetMap;

namespace Managed
{
    internal class CreationRequests
    {
        public static RequestId<CreateEntityRequest> CreateCarEntity(Dispatcher dispatcher, Connection connection)
        {
            return CreateVehicleEntity(dispatcher, connection, false, "");
        }

        public static RequestId<CreateEntityRequest> CreateBusEntity(Dispatcher dispatcher, Connection connection, string busVehicleId)
        {
            return CreateVehicleEntity(dispatcher, connection, true, busVehicleId);
        }

        private static RequestId<CreateEntityRequest> CreateVehicleEntity(Dispatcher dispatcher, Connection connection, bool bus, string busVehicleId)
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
                    {Car.ComponentId, basicWorkerRequirementSet},
                    {EntityAcl.ComponentId, basicWorkerRequirementSet},
                    {Bus.ComponentId, basicWorkerRequirementSet}
                };

            entity.Add(EntityAcl.Metaclass,
                new EntityAclData( /* read */ basicWorkerRequirementSet, /* write */ writeAcl));

            entity.Add(Persistence.Metaclass, new PersistenceData());
            entity.Add(Metadata.Metaclass, new MetadataData(entityType));
            entity.Add(Position.Metaclass, new PositionData(new Coordinates(0, -99.99, 0)));
            entity.Add(Car.Metaclass, new CarData(99999, 99999, 99999));
            if (bus)
                entity.Add(Bus.Metaclass, new BusData(busVehicleId, new List<string>(), new Map<string, string>()));

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
            entity.Add(BusStop.Metaclass, new BusStopData(atcoCode, MapReader.originX, MapReader.originY, MapReader.offsetX, MapReader.offsetY));
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