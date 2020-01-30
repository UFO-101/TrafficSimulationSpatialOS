﻿using System;
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
    /// <summary>
    /// Base Open Street Map (OSM) data node.
    /// </summary>
    public class BaseOsm
    {
        /// <summary>
        /// Get an attribute's value from the collection using the given 'attrName'. 
        /// </summary>
        /// <typeparam name="T">Data type</typeparam>
        /// <param name="attrName">Name of the attribute</param>
        /// <param name="attributes">Node's attribute collection</param>
        /// <returns>The value of the attribute converted to the required type</returns>
        protected T GetAttribute<T>(string attrName, XmlAttributeCollection attributes)
        {
            // TODO: We are going to assume 'attrName' exists in the collection
            string strValue = attributes[attrName].Value;
            return (T)Convert.ChangeType(strValue, typeof(T));
        }
    }
}