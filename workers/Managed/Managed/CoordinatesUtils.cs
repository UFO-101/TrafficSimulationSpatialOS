using System;
using Improbable;

namespace Managed
{
    // All coordinates are assumed to be 2D!
    internal class Coords
    {
        public static double Dist(Coordinates coords1, Coordinates coords2) {
            return Math.Sqrt(Math.Pow(coords1.x - coords2.x, 2) + Math.Pow(coords1.z - coords2.z, 2));
        }

        public static double Length(Coordinates coords1) {
            return Math.Sqrt(Math.Pow(coords1.x, 2) + Math.Pow(coords1.z, 2));
        }

        public static Coordinates Add(Coordinates coords1, Coordinates coords2) {
            return new Coordinates(coords1.x + coords2.x, 0, coords1.z + coords2.z);
        }

        public static Coordinates Subtract(Coordinates coords1, Coordinates coords2) {
            return new Coordinates(coords1.x - coords2.x, 0, coords1.z - coords2.z);
        }

        public static Coordinates Normalise(Coordinates coords1) {
            double len = Length(coords1);
            return new Coordinates(coords1.x / len, 0, coords1.z / len);
        }

        public static Coordinates Scale(Coordinates coords1, double scalar) {
            return new Coordinates(coords1.x * scalar, 0, coords1.z * scalar);
        }

        public static Coordinates ScaleToLength(Coordinates coords1, double len) {
            return Scale(Normalise(coords1), len);
        }

    }
}