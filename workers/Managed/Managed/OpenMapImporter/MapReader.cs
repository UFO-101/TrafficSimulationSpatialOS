﻿using System.Collections.Generic;
using System.IO;
using System.Xml;

/*
    Copyright (c) 2017 Sloan Kelly

    Permission is hereby granted, free of charge, to any person obtaining a copy
    of this software and associated documentation files (the "Software"), to deal
    in the Software without restriction, including without limitation the rights
    to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
    copies of the Software, and to permit persons to whom the Software is
    furnished to do so, subject to the following conditions:

    The above copyright notice and this permission notice shall be included in all
    copies or substantial portions of the Software.

    THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
    IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
    FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
    AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
    LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
    OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
    SOFTWARE.
*/

namespace OpenStreetMap
{
    public class MapReader
    {
        public Dictionary<ulong, OsmNode> nodes;
        public Dictionary<ulong, OsmWay> ways;
        public Dictionary<string, ulong> busStops;
        public Dictionary<ulong, ulong> nearestRoadNodesToBusStops;
        public HashSet<ulong> roadNodes;

        public OsmBounds bounds;
        
        public static double originX;
        public static double originY;
        public static ulong offsetX = 500;
        public static ulong offsetY = 300;

        /// <summary>
        /// Load the OpenMap data resource file.
        /// </summary>
        /// <param name="resourceFile">Path to the resource file. The file must exist.</param>
        public void Read(string resourceFile)
        {
            nodes = new Dictionary<ulong, OsmNode>();
            ways = new Dictionary<ulong, OsmWay>();
            busStops = new Dictionary<string, ulong>();
            nearestRoadNodesToBusStops = new Dictionary<ulong, ulong>();
            roadNodes = new HashSet<ulong>();


            var xmlText = File.ReadAllText(resourceFile);//MapFile.mapdata;//

            XmlDocument doc = new XmlDocument();
            doc.LoadXml(xmlText);

            SetBounds(doc.SelectSingleNode("/osm/bounds"));
            GetNodes(doc.SelectNodes("/osm/node"));
            GetWays(doc.SelectNodes("/osm/way"));
            setClosestNodes();

            float minx = (float)MercatorProjection.lonToX(bounds.MinLon);
            float maxx = (float)MercatorProjection.lonToX(bounds.MaxLon);
            float miny = (float)MercatorProjection.latToY(bounds.MinLat);
            float maxy = (float)MercatorProjection.latToY(bounds.MaxLat);
        }

        void GetNodes(XmlNodeList xmlNodeList)
        {
            OsmNode firstNode = null;
            foreach (XmlNode n in xmlNodeList)
            {
                OsmNode node = new OsmNode(n, firstNode);
                if (firstNode == null)
                {
                    firstNode = node;
                    originX = node.X;
                    originY = node.Y;
                }
                nodes[node.Id] = node;

                if(node.isBusStop)
                    busStops[node.actoCode] = node.Id;
            }
        }


        void GetWays(XmlNodeList xmlNodeList)
        {
            foreach (XmlNode node in xmlNodeList)
            {
                OsmWay way = new OsmWay(node);
                ways[way.ID] = way;

                ulong prevNode = 0;
                bool setPrevNode = false;
                foreach(ulong nodeID in way.NodeIDs){
                    if(way.IsRoad){
                        nodes[nodeID].addWayOn(way.ID);
                        roadNodes.Add(nodeID);
                        if(setPrevNode)
                        {
                            nodes[prevNode].addAdjacentNode(nodeID);
                            nodes[nodeID].addAdjacentNode(prevNode);
                        }
                        prevNode = nodeID;
                        setPrevNode = true;
                    }
                }
            }
        }


        void setClosestNodes()
        {
            foreach(ulong busStopId in busStops.Values)
            {
                double shortestDistance = 9999999;
                ulong bestNodeId = 0;
                OsmNode busStopNode = nodes[busStopId];
                foreach(ulong roadNodeId in roadNodes)
                {
                    OsmNode roadNode = nodes[roadNodeId];
                    double distance = Managed.Coords.Dist(busStopNode.coords, roadNode.coords);
                    if (distance < shortestDistance)
                    {
                        shortestDistance = distance;
                        bestNodeId = roadNodeId;
                    }
                }
                nearestRoadNodesToBusStops[busStopId] = bestNodeId;
            }
        }


        void SetBounds(XmlNode xmlNode)
        {
            bounds = new OsmBounds(xmlNode);
        }
    }
}
