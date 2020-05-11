/*===========================================
    Backgrounds for this sample are powered by ThinkGeo Cloud Maps and require
    a Client ID and Secret. These were sent to you via email when you signed up
    with ThinkGeo, or you can register now at https://cloud.thinkgeo.com.
===========================================*/

using Improbable;
using Improbable.Collections;
using Improbable.Worker;
using Improbable.Worker.Query;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using ThinkGeo.MapSuite;
using ThinkGeo.MapSuite.Drawing;
using ThinkGeo.MapSuite.Layers;
using ThinkGeo.MapSuite.Shapes;
using ThinkGeo.MapSuite.Styles;
using ThinkGeo.MapSuite.WinForms;


namespace External
{
    public partial class TestForm : Form
    {
        private PointShape[] carGPSPositions;
        private int carGPSPositionIndexer = -1;
        private Timer timer1;
        private Timer timer2;
        private Timer timer3;
        private Map<EntityId, PointShape> previousLatLong = new Map<EntityId, PointShape>();

        public TestForm(Connection connection, Dispatcher dispatcher)
        {
            InitializeComponent();
            carGPSPositions = ReadCarGPSPositions();
            timer1 = new Timer();
            timer1.Interval = 2000;
            timer1.Tick += new EventHandler(Timer_Tick);
            timer2 = new Timer();
            timer2.Interval = 2000;
            timer2.Tick += (object s, EventArgs a) => Startup.SendQuery(s, a, connection, dispatcher, 1005);
            timer3 = new Timer();
            timer3.Interval = 200;
            timer3.Tick += (object s, EventArgs a) => Startup.ProcessOpList(s, a, connection, dispatcher);
        }

        private void TestForm_Load(object sender, EventArgs e)
        {
            winformsMap1.MapUnit = GeographyUnit.Meter;
            winformsMap1.ZoomLevelSet = new ThinkGeoCloudMapsZoomLevelSet();
            winformsMap1.CurrentExtent = new RectangleShape(-31388.09, 6704526.48, -28099.15, 6703074.15);
            winformsMap1.BackgroundOverlay.BackgroundBrush = new GeoSolidBrush(GeoColor.FromArgb(255, 198, 255, 255));

            // Please input your ThinkGeo Cloud Client ID / Client Secret to enable the background map. 
            ThinkGeoCloudRasterMapsOverlay thinkGeoCloudMapsOverlay = new ThinkGeoCloudRasterMapsOverlay("LzA6gdx9FFhH6kQD1HxnA6FDmcM_0zDA55Q8--whiUY~", "So8ndg18JxcYR6xXY6KDaFbRGCbNqLO9exm4ixeokRbzKWIJ99smoA~~");
            winformsMap1.Overlays.Add(thinkGeoCloudMapsOverlay);

            //InMemoryFeatureLayer for vehicle.
            InMemoryFeatureLayer carLayer = new InMemoryFeatureLayer();
            carLayer.ZoomLevelSet.ZoomLevel01.DefaultPointStyle.PointType = PointType.Bitmap;
            carLayer.ZoomLevelSet.ZoomLevel01.DefaultPointStyle.Image = new GeoImage(@"../../data/Sedan.png");
            carLayer.ZoomLevelSet.ZoomLevel01.DefaultPointStyle.RotationAngle = 45;
            carLayer.ZoomLevelSet.ZoomLevel01.ApplyUntilZoomLevel = ApplyUntilZoomLevel.Level20;

            LayerOverlay vehicleOverlay = new LayerOverlay();
            vehicleOverlay.Layers.Add("CarLayer", carLayer);
            winformsMap1.Overlays.Add("VehicleOverlay", vehicleOverlay);

            winformsMap1.Refresh();

            timer1.Start();
            timer2.Start();
            timer3.Start();
        }

        void Timer_Tick(object sender, EventArgs e)
        {
            if (Startup.busPoints == null)
                return;

            LayerOverlay vehicleOverlay = (LayerOverlay)winformsMap1.Overlays["VehicleOverlay"];
            InMemoryFeatureLayer carLayer = vehicleOverlay.Layers["CarLayer"] as InMemoryFeatureLayer;
            carLayer.Open();
            carLayer.EditTools.BeginTransaction();

            //Gets the next GPS info 
            List<string> busEntityStrings = new List<string>();
            foreach(System.Collections.Generic.KeyValuePair<EntityId, PointShape> KVPair in Startup.busPoints)
            {
                EntityId entityId = KVPair.Key;
                string entityString = entityId.ToString();
                busEntityStrings.Add(entityString);
                PointShape carNextPosition = KVPair.Value;
                double lng = carNextPosition.X;
                double lat = carNextPosition.Y;

                PointShape prevLatLong;
                if (previousLatLong.ContainsKey(entityId))
                    prevLatLong = previousLatLong[entityId];
                else
                    prevLatLong = new PointShape(lat, lng);

                if (carLayer.InternalFeatures.Contains(entityString))
                {
                    PointShape pointShape = carLayer.InternalFeatures[entityString].GetShape() as PointShape;

                    //Gets the angle based on the current GPS position and the previous one to get the direction of the vehicle.
                    double angle = GetAngleFromTwoVertices(new Vertex(prevLatLong.Y, prevLatLong.X), new Vertex(lng, lat));
                    carLayer.ZoomLevelSet.ZoomLevel01.DefaultPointStyle.RotationAngle = 90 - (float)angle;

                    pointShape.X = lng;
                    pointShape.Y = lat;
                    pointShape.Id = entityString;
                    carLayer.EditTools.Update(pointShape);
                }
                else
                {
                    carLayer.EditTools.Add(new Feature(new PointShape(lng, lat) { Id = entityString }));
                }
                previousLatLong[entityId] = new PointShape(lat, lng);
            }
            foreach(string id in carLayer.InternalFeatures.GetKeys())
            {
                if (!busEntityStrings.Contains(id))
                {
                    PointShape pointShape = carLayer.InternalFeatures[id].GetShape() as PointShape;
                    pointShape.X = 0;
                    pointShape.Y = 0;
                    pointShape.Id = id;
                    carLayer.EditTools.Update(pointShape);
                }
            }
            carLayer.EditTools.CommitTransaction();
            carLayer.Close();
            winformsMap1.Refresh(vehicleOverlay);
            
        }

        private void WinformsMap1_MouseMove(object sender, MouseEventArgs e)
        {
            //Displays the X and Y in screen coordinates.
            statusStrip1.Items["toolStripStatusLabelScreen"].Text = "X:" + e.X + " Y:" + e.Y;

            //Gets the PointShape in world coordinates from screen coordinates.
            PointShape pointShape = ExtentHelper.ToWorldCoordinate(winformsMap1.CurrentExtent, new ScreenPointF(e.X, e.Y), winformsMap1.Width, winformsMap1.Height);

            //Displays world coordinates.
            statusStrip1.Items["toolStripStatusLabelWorld"].Text = "(world) X:" + Math.Round(pointShape.X, 4) + " Y:" + Math.Round(pointShape.Y, 4);
        }

        private void btnClose_Click(object sender, EventArgs e)
        {
            this.Close();
        }


        //We assume that the angle is based on a third point that is on top of b on the same x axis.
        private double GetAngleFromTwoVertices(Vertex b, Vertex c)
        {
            double alpha = 0;
            double tangentAlpha = (c.Y - b.Y) / (c.X - b.X);
            double Peta = Math.Atan(tangentAlpha);

            if (c.X > b.X)
            {
                alpha = 90 - (Peta * (180 / Math.PI));
            }
            else if (c.X < b.X)
            {
                alpha = 270 - (Peta * (180 / Math.PI));
            }
            else
            {
                if (c.Y > b.Y) alpha = 0;
                if (c.Y < b.Y) alpha = 180;
            }
            return alpha;
        }

        private PointShape GetNextCarPosition()
        {
            carGPSPositionIndexer++;
            carGPSPositionIndexer %= carGPSPositions.Length;
            var result = carGPSPositions[carGPSPositionIndexer];
            return result;
        }

        private PointShape[] ReadCarGPSPositions()
        {
            var positionTexts = File.ReadAllLines(@"../../data/GPSinfo.txt");
            var pointList = new System.Collections.Generic.List<PointShape>(positionTexts.Length);
            foreach (var positionText in positionTexts)
            {
                string[] strSplit = positionText.Split(',');
                double lng;
                double lat;
                if (strSplit.Length == 2 && double.TryParse(strSplit[0], out lng) && double.TryParse(strSplit[1], out lat))
                {
                    pointList.Add(new PointShape(lng, lat));
                }
            }
            return pointList.ToArray();
        }

        private void toolStripStatusLabelScreen_Click(object sender, EventArgs e)
        {

        }
    }
}
