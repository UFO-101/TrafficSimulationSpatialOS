using System;
//using System.Reflection;
using Improbable.Worker;
//using ThinkGeo.MapSuite.WinForms;

//using System.Drawing.Drawing2D;
//using System.Diagnostics;
//using System.Windows.Forms;
using System.Windows.Forms;
using Improbable.Collections;
using Improbable.Worker.Query;
using Mapandcars;
using Improbable;
using System.Reflection;
using System.Diagnostics;
using System.Net;
using ThinkGeo.MapSuite.Shapes;

namespace External
{
    internal class Startup
    {
        private const string WorkerType = "External";

        public const string LoggerName = "Startup.cs";

        public const int ErrorExitStatus = 1;

        public const uint GetOpListTimeoutInMilliseconds = 100;

        public static bool useLocator;
        public static string[] args;
        public static ConnectionParameters connectionParameters;

        public static double originX = 0;
        public static double originY = 0;
        public static ulong offsetX = 100;
        public static ulong offsetY = 50;

        public static Map<EntityId, PointShape> busPoints = new Map<EntityId, PointShape>();

        [STAThread]
        private static int Main(string[] args)
        {
            ServicePointManager.ServerCertificateValidationCallback += (o, cert, chain, errors) => true;

            #region Check arguments

            if (args.Length < 1)
            {
                Console.WriteLine("Error1");
                PrintUsage();
                return ErrorExitStatus;
            }

            if (args[0] != "receptionist" && args[0] != "locator")
            {
                Console.WriteLine("Error2");
                PrintUsage();
                return ErrorExitStatus;
            }

            useLocator = (args[0] == "locator");
            Startup.args = args;

            if (useLocator && args.Length != 5 || !useLocator && args.Length != 4)
            {
                Console.WriteLine("Error3");
                Console.WriteLine("args length: " + args.Length);
                int count = 0;
                foreach (string arg in args)
                {
                    Console.WriteLine("arg " + count + ": " + arg);
                    count++;
                }
                PrintUsage();
                return ErrorExitStatus;
            }

            #endregion

            // Avoid missing component errors because no components are directly used in this project
            // and the GeneratedCode assembly is not loaded but it should be
            Assembly.Load("GeneratedCode");

            connectionParameters = new ConnectionParameters
            {
                WorkerType = WorkerType,
                Network =
                {
                    ConnectionType = NetworkConnectionType.Tcp,

                    // Local clients connecting to a local deployment shouldn't use external IP
                    // Clients connecting to a cloud deployment using the locator should
                    // Consider exposing this as a command-line option if you have an advanced configuration
                    // See table: https://docs.improbable.io/reference/11.0/workers/csharp/using#connecting-to-spatialos
                    UseExternalIp = useLocator
                }
            };

            Connection connection = Startup.useLocator
            ? ConnectClientWithLocator(args[1], args[2], args[3], args[4], Startup.connectionParameters)
            : ConnectClientWithReceptionist(args[1], Convert.ToUInt16(args[2]), args[3], Startup.connectionParameters);

            Dispatcher dispatcher = new Dispatcher();

            dispatcher.OnDisconnect(op =>
            {
                Console.Error.WriteLine("[disconnect] " + op.Reason);
            });
            dispatcher.OnEntityQueryResponse(EntityQueryCallback);

            SendQuery(new object(), new EventArgs(), connection, dispatcher, 1006);
            
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new TestForm(connection, dispatcher));
            
            // This means we forcefully disconnected

            return 0;
        }

        private static void EntityQueryCallback(EntityQueryResponseOp op)
        {
            busPoints.Clear();
            foreach (System.Collections.Generic.KeyValuePair<EntityId, Entity> KVPair in op.Result)
            {
                BusStopData? busStopData = KVPair.Value.Get(BusStop.Metaclass);
                if (busStopData.HasValue)
                {
                    originX = busStopData.Value.originX;
                    originY = busStopData.Value.originY;
                    offsetX = busStopData.Value.offsetX;
                    offsetY = busStopData.Value.offsetY;
                }

                PositionData? posData = KVPair.Value.Get(Position.Metaclass);
                if (posData.HasValue && !busStopData.HasValue) // Don't update the positions if it's bus stops
                {
                    Coordinates coords = posData.Value.coords;
                    PointShape point = CoordsToPoint(coords);
                    if (point != null)
                    {
                        busPoints.Add(KVPair.Key, point);
                    }
                }
            }
        }

        public static void SendQuery(object sender, EventArgs e, Connection connection, Dispatcher dispatcher, uint componentType)
        {
            connection.SendLogMessage(LogLevel.Info, LoggerName, "Hello this is the external worker sending a query");
            EntityQuery entityQuery = new EntityQuery();
            ComponentConstraint componentConstraint = new ComponentConstraint(componentType);
            entityQuery.Constraint = componentConstraint;
            entityQuery.ResultType = new SnapshotResultType();
            RequestId<EntityQueryRequest> entityQueryRequestId = connection.SendEntityQueryRequest(entityQuery, new Option<uint>());
        }

        public static PointShape CoordsToPoint(Coordinates position)
        {
            if (originX == 0 || originY == 0)
                return null;

            double lon = MercatorProjection.xToLon((position.x + originX + offsetX) * 10.0);
            double lat = MercatorProjection.yToLat((position.z + originY - offsetY) * 10.0);
            return WebMercator.LatLonToMeters(lat, lon);
        }

        public static void ProcessOpList(object sender, EventArgs e, Connection connection, Dispatcher dispatcher)
        {
            using (var opList = connection.GetOpList(GetOpListTimeoutInMilliseconds))
            {
                dispatcher.Process(opList);
            }
        }

        private static void PrintUsage()
        {
            Console.WriteLine("Usage: mono External.exe receptionist <hostname> <port> <worker_id>");
            Console.WriteLine("       mono External.exe locator <hostname> <project_name> <deployment_id> <login_token>");
            Console.WriteLine("Connects to SpatialOS");
            Console.WriteLine("    <hostname>      - hostname of the receptionist or locator to connect to.");
            Console.WriteLine("    <port>          - port to use if connecting through the receptionist.");
            Console.WriteLine("    <worker_id>     - name of the worker assigned by SpatialOS.");
            Console.WriteLine("    <project_name>  - name of the project to run.");
            Console.WriteLine("    <deployment_id> - name of the cloud deployment to run.");
            Console.WriteLine("    <login_token>   - token to use when connecting through the locator.");
        }

        public static Connection ConnectClientWithLocator(string hostname, string projectName, string deploymentId,
            string loginToken, ConnectionParameters connectionParameters)
        {
            Connection connection;
            connectionParameters.Network.UseExternalIp = true;

            var locatorParameters = new LocatorParameters
            {
                ProjectName = projectName,
                CredentialsType = LocatorCredentialsType.LoginToken,
                LoginToken = {Token = loginToken}
            };

            var locator = new Locator(hostname, locatorParameters);

            using (var future = locator.ConnectAsync(deploymentId, connectionParameters, QueueCallback))
            {
                connection = future.Get();
            }

            connection.SendLogMessage(LogLevel.Info, LoggerName, "Successfully connected using the Locator");

            return connection;
        }

        private static bool QueueCallback(QueueStatus queueStatus)
        {
            if (!string.IsNullOrEmpty(queueStatus.Error))
            {
                Console.Error.WriteLine("Error while queueing: " + queueStatus.Error);
                Environment.Exit(ErrorExitStatus);
            }
            Console.WriteLine("Worker of type '" + WorkerType + "' connecting through locator: queueing.");
            return true;
        }

        public static Connection ConnectClientWithReceptionist(string hostname, ushort port,
            string workerId, ConnectionParameters connectionParameters)
        {
            Connection connection;

            using (var future = Connection.ConnectAsync(hostname, port, workerId, connectionParameters))
            {
                connection = future.Get();
            }

            connection.SendLogMessage(LogLevel.Info, LoggerName, "Successfully connected using the Receptionist");

            return connection;
        }
    }
}