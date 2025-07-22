using System;

namespace DEMBuilder.Services.Projection
{
    [System.Diagnostics.DebuggerDisplay("{ToString()}")]
    public struct vec2
    {
        public double easting; //easting
        public double northing; //northing

        public vec2(double easting, double northing)
        {
            this.easting = easting;
            this.northing = northing;
        }

        public vec2(vec2 v)
        {
            easting = v.easting;
            northing = v.northing;
        }

        public static vec2 operator -(vec2 lhs, vec2 rhs)
        {
            return new vec2(lhs.easting - rhs.easting, lhs.northing - rhs.northing);
        }

        public double HeadingXZ()
        {
            return Math.Atan2(easting, northing);
        }

        public vec2 Normalize()
        {
            double length = GetLength();
            if (Math.Abs(length) < 0.000000000001)
            {
                throw new DivideByZeroException("Trying to normalize a vector with length of zero.");
            }

            return new vec2(easting /= length, northing /= length);
        }

        public double GetLength()
        {
            return Math.Sqrt((easting * easting) + (northing * northing));
        }

        public double GetLengthSquared()
        {
            return (easting * easting) + (northing * northing);
        }

        public static vec2 operator *(vec2 self, double s)
        {
            return new vec2(self.easting * s, self.northing * s);
        }

        public static vec2 operator +(vec2 lhs, vec2 rhs)
        {
            return new vec2(lhs.easting + rhs.easting, lhs.northing + rhs.northing);
        }

        public override string ToString()
        {
            return "east:" + easting.ToString("0.000") + ", north:" + northing.ToString("0.000");
        }
    }
}
