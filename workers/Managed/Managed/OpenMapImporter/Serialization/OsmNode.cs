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
        public ulong ID { get; private set; }

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
        public float X { get; private set; }

        /// <summary>
        /// Unity unit Y-co-ordinate.
        /// </summary>
        public float Y { get; private set; }

        public Coordinates coords { get; private set; }

        public List<ulong> adjacentNodes { get; private set; } = new List<ulong>();

        public List<ulong> waysOn { get; private set; } = new List<ulong>();

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
            ID = GetAttribute<ulong>("id", node.Attributes);
            Latitude = GetAttribute<float>("lat", node.Attributes);
            Longitude = GetAttribute<float>("lon", node.Attributes);

            // Calculate the position in Unity units
            X = (float)MercatorProjection.lonToX(Longitude) * 0.1f;
            Y = (float)MercatorProjection.latToY(Latitude) * 0.1f;

            if(firstNode == null)
                coords = new Coordinates(0,0,0);
            else
                coords = new Coordinates(X - firstNode.X, 0, Y - firstNode.Y);
        }

        public void addAdjacentNode(ulong adjacentNodeID)
        {
            adjacentNodes.Add(adjacentNodeID);
        }

        public void addWayOn(ulong wayOnID)
        {
            waysOn.Add(wayOnID);
        }
    }
}