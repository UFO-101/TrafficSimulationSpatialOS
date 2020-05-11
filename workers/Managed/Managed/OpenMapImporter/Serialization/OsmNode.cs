using System.Xml;
using Improbable;
using System.Collections.Generic;

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
    /// <summary>
    /// OSM node.
    /// </summary>
    public class OsmNode : BaseOsm
    {
        /// <summary>
        /// Node ID.
        /// </summary>
        public ulong Id { get; private set; }

        /// <summary>
        /// Latitude position of the node.
        /// </summary>
        public float Latitude { get; private set; }

        /// <summary>
        /// Longitude position of the node.
        /// </summary>
        public float Longitude { get; private set; }

        /// <summary>
        /// Unity unit X-co-ordinate.
        /// </summary>
        public double X { get; private set; }

        /// <summary>
        /// Unity unit Y-co-ordinate.
        /// </summary>
        public double Y { get; private set; }

        public Coordinates coords { get; private set; }

        public List<ulong> adjacentNodes { get; private set; } = new List<ulong>();

        public List<ulong> waysOn { get; private set; } = new List<ulong>();

        public bool isBusStop {get; private set; } = false;
        public string actoCode {get; private set; }= "";

        // /// <summary>
        // /// Implicit conversion between OsmNode and Vector3.
        // /// </summary>
        // /// <param name="node">OsmNode instance</param>
        // public static implicit operator Coordinates (OsmNode node)
        // {
        //     return new Coordinates(node.X, node.Y, 0);
        // }
        
        /// <summary>
        /// Constructor.
        /// </summary>
        /// <param name="node">Xml node</param>
        public OsmNode(XmlNode node, OsmNode firstNode)
        {
            // Get the attribute values
            Id = GetAttribute<ulong>("id", node.Attributes);
            Latitude = GetAttribute<float>("lat", node.Attributes);
            Longitude = GetAttribute<float>("lon", node.Attributes);

            // Calculate the position in Unity units
            X = MercatorProjection.lonToX(Longitude) * 0.1f;
            Y = MercatorProjection.latToY(Latitude) * 0.1f;
            
            if(firstNode == null)
                coords = new Coordinates(MapReader.offsetX,0, MapReader.offsetY);
            else
                coords = new Coordinates((X - firstNode.X) + MapReader.offsetX, 0, (Y - firstNode.Y) + MapReader.offsetY);

            XmlNodeList tags = node.SelectNodes("tag");
            foreach (XmlNode t in tags)
            {
                string key = GetAttribute<string>("k", t.Attributes);
                if (key == "highway") {
                    string value = GetAttribute<string>("v", t.Attributes);
                    if(value == "bus_stop"){
                        isBusStop = true;                        
                    }
                } else if (key == "naptan:AtcoCode") {
                    string value = GetAttribute<string>("v", t.Attributes);
                    actoCode = value;
                }
            }
            if(isBusStop && actoCode == ""){
                //throw new System.Exception("Is bus stop but no actoCode: node " + Id);
                isBusStop = false;
            }
        }

        public void addAdjacentNode(ulong adjacentNodeId)
        {
            adjacentNodes.Add(adjacentNodeId);
        }

        public void addWayOn(ulong wayOnId)
        {
            waysOn.Add(wayOnId);
        }
    }
}