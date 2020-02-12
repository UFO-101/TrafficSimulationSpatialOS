using System;
using System.Collections.Generic;
using System.Xml;
using System.Linq;

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
    /// An OSM object that describes an arrangement of OsmNodes into a shape or road.
    /// </summary>
    public class OsmWay : BaseOsm
    {
        string[] not_proper_driving_road_values = {"pedestrian", "bus_guideway", "escape", "raceway", "footway", "bridleway", "steps", "corridor", "path", "cycleway"};
        /// <summary>
        /// Way ID.
        /// </summary>
        public ulong ID { get; private set; }

        /// <summary>
        /// True if visible.
        /// </summary>
        public bool Visible { get; private set; }

        /// <summary>
        /// List of node IDs.
        /// </summary>
        public List<ulong> NodeIDs { get; private set; }

        /// <summary>
        /// True if the way is a boundary.
        /// </summary>
        public bool IsBoundary { get; private set; }

        /// <summary>
        /// True if the way is a building.
        /// </summary>
        public bool IsBuilding { get; private set; }

        /// <summary>
        /// True if the way is a road.
        /// </summary>
        public bool IsRoad { get; private set; }

        /// <summary>
        /// Height of the structure.
        /// </summary>
        public float Height { get; private set; }

        /// <summary>
        /// The name of the object.
        /// </summary>
        public string Name { get; private set; }

        /// <summary>
        /// The number of lanes on the road. Default is 1 for contra-flow
        /// </summary>
        public int Lanes { get; private set; }

        public int SpeedLimit { get; private set; }

        /// <summary>
        /// Constructor.
        /// </summary>
        /// <param name="node"></param>
        public OsmWay(XmlNode node)
        {
            NodeIDs = new List<ulong>();
            Height = 3.0f; // Default height for structures is 1 story (approx. 3m)
            Lanes = 1;      // Number of lanes either side of the divide 
            Name = "";
            SpeedLimit = 25;

            // Get the data from the attributes
            ID = GetAttribute<ulong>("id", node.Attributes);
            Visible = GetAttribute<bool>("visible", node.Attributes);

            // Get the nodes
            XmlNodeList nds = node.SelectNodes("nd");
            foreach(XmlNode n in nds)
            {
                ulong refNo = GetAttribute<ulong>("ref", n.Attributes);                
                NodeIDs.Add(refNo);
            }

            if (NodeIDs.Count > 1)
            {
                IsBoundary = NodeIDs[0] == NodeIDs[NodeIDs.Count - 1];
            }

            // Read the tags
            XmlNodeList tags = node.SelectNodes("tag");
            bool highway = false;
            bool serviceRoad = false;
            bool disqualifiedAsRoad = false;
            bool hasMaxspeed = false;
            bool hasName = false;
            foreach (XmlNode t in tags)
            {
                string key = GetAttribute<string>("k", t.Attributes);
                if (key == "building:levels")
                {
                    Height = 3.0f * GetAttribute<float>("v", t.Attributes);
                }
                else if (key == "height")
                {
                    Height = 0.3048f * GetAttribute<float>("v", t.Attributes);
                }
                else if (key == "building")
                {
                    IsBuilding = true;
                }
                else if (key == "highway")
                {
                    highway = true;
                    if(not_proper_driving_road_values.Contains(GetAttribute<string>("v", t.Attributes))){
                        disqualifiedAsRoad = true;
                    } else if (GetAttribute<string>("v", t.Attributes) == "service"){
                        serviceRoad = true;
                    }
                }
                else if (key=="lanes")
                {
                    Lanes = GetAttribute<int>("v", t.Attributes);
                }
                else if (key=="name")
                {
                    hasName = true;
                    Name = GetAttribute<string>("v", t.Attributes);
                }
                else if (key=="access" && GetAttribute<string>("v", t.Attributes) == "no")
                {
                    disqualifiedAsRoad = true;                    
                }
                else if (key=="service")
                {
                    disqualifiedAsRoad = true;                    
                }
                else if (key=="maxspeed")
                {
                    hasMaxspeed = true;                    
                    string maxSpeedStr = GetAttribute<string>("v", t.Attributes);
                    char[] splitChars = {' '};
                    string[] strArr = maxSpeedStr.Split(splitChars);
                    SpeedLimit = Int32.Parse(strArr[0]);
                }

            }
            if(highway && !disqualifiedAsRoad && (!serviceRoad || hasMaxspeed || hasName)){
                IsRoad = true;
            }
        }
    }
}