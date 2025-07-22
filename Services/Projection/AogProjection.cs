using System;

namespace DEMBuilder.Services.Projection
{
    public class AogProjection
    {
        private double _latStart;
        private double _lonStart;
        private double _mPerDegreeLat;

        public void SetReferencePoint(double latitude, double longitude)
        {
            _latStart = latitude;
            _lonStart = longitude;

            // Calculation from AgOpenGPS's CNMEA.SetLocalMetersPerDegree
            _mPerDegreeLat = 111132.92 - 559.82 * Math.Cos(2.0 * _latStart * 0.01745329251994329576923690766743) 
                           + 1.175 * Math.Cos(4.0 * _latStart * 0.01745329251994329576923690766743) 
                           - 0.0023 * Math.Cos(6.0 * _latStart * 0.01745329251994329576923690766743);
        }

        public vec2 ToEastingNorthing(double latitude, double longitude)
        {
            // Calculation from AgOpenGPS's CNMEA.ConvertWGS84ToLocal
            var rad = latitude * 0.01745329251994329576923690766743;
            double mPerDegreeLon = 111412.84 * Math.Cos(rad) - 93.5 * Math.Cos(3.0 * rad) + 0.118 * Math.Cos(5.0 * rad);

            double northing = (latitude - _latStart) * _mPerDegreeLat;
            double easting = (longitude - _lonStart) * mPerDegreeLon;

            return new vec2(easting, northing);
        }
    }
}
